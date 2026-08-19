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
        var config = LoadConfig()
            ?? throw new InvalidOperationException($"Конфигурация службы не найдена: {ServiceConfig.DefaultPath}");

        if (string.IsNullOrWhiteSpace(config.ExecutablePath) || !File.Exists(config.ExecutablePath))
            throw new InvalidOperationException($"llama-server не найден: {config.ExecutablePath}");

        var psi = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(config.ExecutablePath) ?? Environment.CurrentDirectory
        };
        foreach (var arg in config.Arguments)
            psi.ArgumentList.Add(arg);

        lock (_processLock)
        {
            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить llama-server.");
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
    }

    protected override void OnShutdown() => OnStop();

    private static ServiceConfig? LoadConfig()
    {
        try
        {
            var path = ServiceConfig.DefaultPath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ServiceConfig>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
