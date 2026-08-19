using System.Text.Json;

namespace LocalLlmConsole.Services;

/// <summary>
/// Пишет конфигурацию запуска llama-server для Windows-службы
/// в <c>data/state/service-config.json</c> (рядом с exe службы).
/// Служба (LocalLlmConsole.Service) читает этот файл при старте.
/// </summary>
public sealed class ServiceLaunchConfigWriter
{
    private readonly string _workspaceRoot;

    public ServiceLaunchConfigWriter(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
    }

    public string ConfigPath
        => Path.Combine(_workspaceRoot, "state", "service-config.json");

    /// <summary>
    /// Записывает конфиг с исполняемым файлом llama-server, аргументами запуска
    /// и идентификаторами выбранных модели/профиля/среды выполнения.
    /// </summary>
    public void Write(string executablePath, IReadOnlyList<string> arguments, string modelId = "", string profileId = "", string runtimeId = "")
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var config = new ServiceLaunchConfig(executablePath, arguments, modelId, profileId, runtimeId);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// Читает сохранённый конфиг (выбор модели/профиля/runtime) или null, если файла нет.
    /// </summary>
    public ServiceLaunchConfig? Read()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ServiceLaunchConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    public bool Exists => File.Exists(ConfigPath);
}

/// <summary>
/// Сериализуемая модель конфигурации службы (совпадает со схемой LocalLlmConsole.Service).
/// </summary>
public sealed record ServiceLaunchConfig(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string ModelId = "",
    string ProfileId = "",
    string RuntimeId = "");
