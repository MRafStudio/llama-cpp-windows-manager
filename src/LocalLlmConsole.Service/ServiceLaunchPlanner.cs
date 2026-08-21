using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Service;

/// <summary>
/// План запуска llama-server, собранный службой полностью автономно:
/// выбор (модель/профиль/среда) — из service-config.json (на него влияет
/// только страница «Управление службой»), параметры запуска — из профиля
/// в БД приложения (settings_json). GUI не может сломать запуск службы,
/// что бы ни делал с остальными страницами или с аргументами в файле.
/// </summary>
public sealed record ServiceLaunchPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments);

public static class ServiceLaunchPlanner
{
    /// <summary>
    /// Строит план запуска. Бросает InvalidOperationException с понятным
    /// сообщением, если выбор или параметры отсутствуют/повреждены —
    /// честная ошибка вместо запуска с левыми параметрами.
    /// </summary>
    public static ServiceLaunchPlan Build(string configPath, string databasePath)
    {
        if (!File.Exists(configPath))
            throw new InvalidOperationException(
                $"Конфигурация службы не найдена: {configPath}. Откройте страницу «Управление службой» и выберите модель, профиль запуска и среду выполнения.");

        ServiceConfig config;
        try
        {
            config = JsonSerializer.Deserialize<ServiceConfig>(File.ReadAllText(configPath))
                ?? throw new InvalidOperationException($"Конфигурация службы повреждена: {configPath}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Конфигурация службы повреждена: {configPath}. {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(config.ModelId))
            throw new InvalidOperationException(
                "В конфигурации службы не выбрана модель. Откройте страницу «Управление службой» и выберите модель, профиль запуска и среду выполнения.");

        if (!File.Exists(databasePath))
            throw new InvalidOperationException($"База данных приложения не найдена: {databasePath}");

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        // 1. Модель из БД (путь к файлу)
        var modelPath = QueryString(connection, "SELECT model_path FROM models WHERE id = $id", config.ModelId)
            ?? throw new InvalidOperationException(
                $"Модель '{config.ModelId}' не найдена в базе данных приложения. Выберите модель на странице «Управление службой».");
        if (!File.Exists(modelPath))
            throw new InvalidOperationException($"Файл модели не найден: {modelPath}");

        // 2. Профиль: выбранный, иначе default-профиль модели
        var settingsJson = QueryString(
                connection,
                "SELECT settings_json FROM model_launch_profiles WHERE id = $id AND model_id = $model",
                config.ProfileId, config.ModelId)
            ?? QueryString(
                connection,
                "SELECT settings_json FROM model_launch_profiles WHERE model_id = $model AND is_default = 1",
                modelId: config.ModelId)
            ?? throw new InvalidOperationException(
                $"Профиль запуска '{config.ProfileId}' для модели '{config.ModelId}' не найден в базе данных. Выберите профиль на странице «Управление службой».");

        // 3. Среда выполнения: выбранная, иначе путь из конфига
        var executable = !string.IsNullOrWhiteSpace(config.RuntimeId)
            ? QueryString(connection, "SELECT executable_path FROM runtimes WHERE id = $id", config.RuntimeId) ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(executable) && !string.IsNullOrWhiteSpace(config.ExecutablePath))
            executable = config.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException(
                $"llama-server не найден (среда выполнения '{config.RuntimeId}'). Выберите среду выполнения на странице «Управление службой».");

        return new ServiceLaunchPlan(executable, BuildArguments(settingsJson, modelPath));
    }

    /// <summary>
    /// Строит аргументы llama-server из параметров профиля (settings_json из БД).
    /// Жёсткие правила (порт 8101, alias) применяет вызывающий код.
    /// </summary>
    private static IReadOnlyList<string> BuildArguments(string settingsJson, string modelPath)
    {
        using var doc = JsonDocument.Parse(settingsJson);
        var root = doc.RootElement;

        var args = new List<string> { "--model", modelPath, "--host", "127.0.0.1" };

        AddInt(args, root, "ContextSize", "--ctx-size");
        AddInt(args, root, "GpuLayers", "--n-gpu-layers");
        AddInt(args, root, "ParallelSlots", "--parallel");
        AddInt(args, root, "BatchSize", "--batch-size");
        AddInt(args, root, "MicroBatchSize", "--ubatch-size");
        AddInt(args, root, "Threads", "--threads", skipNonPositive: true);
        AddString(args, root, "FlashAttention", "--flash-attn");
        AddString(args, root, "CacheTypeK", "--cache-type-k");
        AddString(args, root, "CacheTypeV", "--cache-type-v");
        AddFlagWhen(args, root, "MlockMode", "on", "--mlock");
        AddMmapFlag(args, root);
        if (IsOn(root, "ContinuousBatching")) args.Add("--cont-batching");
        AddDouble(args, root, "Temperature", "--temp");
        AddInt(args, root, "TopK", "--top-k");
        AddDouble(args, root, "TopP", "--top-p");
        AddDouble(args, root, "MinP", "--min-p");
        AddInt(args, root, "RepeatLastN", "--repeat-last-n");
        AddDouble(args, root, "RepeatPenalty", "--repeat-penalty");
        AddDouble(args, root, "PresencePenalty", "--presence-penalty");
        AddDouble(args, root, "FrequencyPenalty", "--frequency-penalty");
        AddInt(args, root, "ReasoningBudget", "--reasoning-budget", skipNonPositive: true);
        AddInt(args, root, "MaxTokens", "--n-predict", skipNonPositive: true);
        if (IsOn(root, "EnableMetrics")) args.Add("--metrics");

        // Vision-проектор (mmproj): авто-подбор в папке модели (как в приложении)
        var projector = FindVisionProjector(modelPath);
        if (projector is not null)
        {
            args.Add("--mmproj");
            args.Add(projector);
        }

        // Пользовательские параметры (уважаются кавычки)
        var custom = StringValue(root, "CustomParameters");
        if (!string.IsNullOrWhiteSpace(custom))
            args.AddRange(SplitArguments(custom));

        return args;
    }

    private static void AddInt(List<string> args, JsonElement root, string field, string flag, bool skipNonPositive = false)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.Number) return;
        if (element.TryGetInt32(out var value) && (!skipNonPositive || value > 0))
        {
            args.Add(flag);
            args.Add(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddDouble(List<string> args, JsonElement root, string field, string flag)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.Number) return;
        if (element.TryGetDouble(out var value))
        {
            args.Add(flag);
            args.Add(value.ToString("0.######", CultureInfo.InvariantCulture));
        }
    }

    private static void AddString(List<string> args, JsonElement root, string field, string flag)
    {
        var value = StringValue(root, field);
        if (string.IsNullOrWhiteSpace(value)) return;
        args.Add(flag);
        args.Add(value);
    }

    private static void AddFlagWhen(List<string> args, JsonElement root, string field, string expectedValue, string flag)
    {
        if (string.Equals(StringValue(root, field), expectedValue, StringComparison.OrdinalIgnoreCase))
            args.Add(flag);
    }

    private static void AddMmapFlag(List<string> args, JsonElement root)
    {
        var value = StringValue(root, "MmapMode");
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            args.Add("--no-mmap");
        else if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            args.Add("--mmap");
    }

    private static bool IsOn(JsonElement root, string field)
    {
        var value = StringValue(root, field);
        return string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string StringValue(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)) return "";
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "on",
            JsonValueKind.False => "off",
            _ => ""
        };
    }

    /// <summary>
    /// Авто-подбор mmproj для модели: файлы в папке модели с именем проектора,
    /// совместимые по семейству/версии (повтор логики приложения, упрощённо).
    /// </summary>
    private static string? FindVisionProjector(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        var mainName = Path.GetFileName(modelPath);
        var mainFamily = FamilyVersion(mainName);

        return Directory.EnumerateFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!LooksLikeVisionProjectorName(name)) return false;
                if (mainFamily is null) return true;
                var companionFamily = FamilyVersion(name);
                return companionFamily is null || string.Equals(mainFamily, companionFamily, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(file => Path.GetFileName(file).Contains("f16", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool LooksLikeVisionProjectorName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.Contains("mmproj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("projector", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("clip", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("visual-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("image-head", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FamilyVersion(string name)
    {
        var match = Regex.Match(
            name ?? "",
            @"(?ix)(?:^|[^a-z0-9])
              (?<family>qwen|gemma|llama|mistral|ministral|mixtral|pixtral|deepseek|glm|phi|internvl|minicpm)
              (?:[\s._-]+(?:small|large|nemo))?
              [\s._-]*(?:v|r)?(?<version>\d+(?:[._-]\d+)?)
              (?:[^0-9]|$)");
        if (!match.Success) return null;
        var version = match.Groups["version"].Value.Replace('_', '.').Replace('-', '.');
        return $"{match.Groups["family"].Value.ToLowerInvariant()}:{version}";
    }

    /// <summary>
    /// Разбивает пользовательские параметры на токены, уважая кавычки.
    /// </summary>
    private static IReadOnlyList<string> SplitArguments(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in text.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string? QueryString(SqliteConnection connection, string sql, string? id = null, string? modelId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        if (modelId is not null) command.Parameters.AddWithValue("$model", modelId);
        return command.ExecuteScalar() as string;
    }
}
