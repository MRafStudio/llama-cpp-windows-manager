using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using System.Text.Json;

namespace LocalLlmConsole.Service;

/// <summary>
/// Windows-служба, запускающая llama-server как дочерний процесс.
/// Параметры запуска читаются из <see cref="ServiceConfig.DefaultPath"/>
/// (файл пишет WPF-приложение перед установкой/запуском службы).
/// </summary>
public sealed class LlamaServerWindowsService : ServiceBase
{
    public const string Name = "llama-cpp-server";

    /// <summary>
    /// Отображаемое имя службы: "Llama.cpp (путь к папке службы без имени файла)".
    /// </summary>
    public static string BuildDisplayName()
    {
        var baseDir = AppContext.BaseDirectory?.TrimEnd('\\', '/') ?? "";
        var folder = Path.GetDirectoryName(baseDir) ?? baseDir;
        return string.IsNullOrWhiteSpace(folder) ? "Llama.cpp" : $"Llama.cpp ({folder})";
    }

    /// <summary>
    /// Локалезависимое описание службы: русское на системах с RU-локалью,
    /// английское на остальных.
    /// </summary>
    public static string BuildDescription()
    {
        var isRussian = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName
            .Equals("ru", StringComparison.OrdinalIgnoreCase);
        return isRussian
            ? "Служба сервера моделей llama.cpp."
            : "llama.cpp model server service.";
    }

    private Process? _process;
    private readonly object _processLock = new();
    private StreamWriter? _logWriter;
    private readonly object _logWriterLock = new();

    public LlamaServerWindowsService()
    {
        this.ServiceName = Name;
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        // Служба полностью автономна: выбор (модель/профиль/среда выполнения) —
        // из service-config.json (на него влияет только страница «Управление службой»),
        // параметры запуска — из профиля в БД приложения. GUI не может сломать запуск.
        var plan = ServiceLaunchPlanner.Build(ServiceConfig.DefaultPath, DatabasePath);

        // Служба — постоянный шлюз локальных LLM: модель ВСЕГДА слушается
        // на едином порту 8101 и под красивым именем (--alias из имени файла).
        var arguments = NormalizeArguments(plan.Arguments);

        var psi = new ProcessStartInfo
        {
            FileName = plan.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(plan.ExecutablePath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        lock (_processLock)
        {
            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить llama-server.");

            // Вывод llama-server пишется в data/logs/llama-server-<timestamp>.log,
            // чтобы «Журнал среды выполнения» в приложении показывал его в реальном времени.
            var logPath = Path.Combine(AppContext.BaseDirectory, "data", "logs", $"llama-server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            _logWriter = new StreamWriter(logPath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
            _process.OutputDataReceived += (_, eventArgs) => WriteLogLine(eventArgs.Data);
            _process.ErrorDataReceived += (_, eventArgs) => WriteLogLine(eventArgs.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
    }

    private void WriteLogLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_logWriterLock)
        {
            _logWriter?.WriteLine(line);
        }
    }

    protected override void OnStop()
    {
        Process? process;
        lock (_processLock)
        {
            process = _process;
            _process = null;
        }

        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Процесс мог завершиться сам — не критично.
        }

        try
        {
            process.WaitForExit(3000);
        }
        catch
        {
            // Игнорируем таймаут ожидания.
        }

        process.Dispose();

        StreamWriter? logWriter;
        lock (_logWriterLock)
        {
            logWriter = _logWriter;
            _logWriter = null;
        }
        logWriter?.Dispose();
    }

    protected override void OnShutdown() => OnStop();

    /// <summary>
    /// Служба — постоянный шлюз локальных LLM. Гарантирует:
    /// красивое имя модели (--alias), формируемое из имени файла:
    /// разделители '-' и '_' → пробел. Порт и host выбирает планировщик
    /// из настроек приложения (порт шлюза / доступ в LAN).
    /// </summary>
    private static IReadOnlyList<string> NormalizeArguments(IReadOnlyList<string> planArguments)
    {
        var args = planArguments.ToList();

        var modelPath = ArgumentValue(args, "--model") ?? "";
        var alias = string.IsNullOrWhiteSpace(modelPath)
            ? ""
            : Path.GetFileNameWithoutExtension(modelPath).Replace('-', ' ').Replace('_', ' ');

        if (!string.IsNullOrWhiteSpace(alias))
            SetArgument(args, "--alias", alias);

        return args;
    }

    private static void SetArgument(IList<string> args, string name, string value)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
            args[i + 1] = value;
            return;
        }
        args.Add(name);
        args.Add(value);
    }

    private static string? ArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Путь к БД приложения (выбор/профили/среды выполняения) — рядом с exe службы.
    /// </summary>
    private static string DatabasePath
        => Path.Combine(AppContext.BaseDirectory, "data", "state", "local-llm-console.db");
}
