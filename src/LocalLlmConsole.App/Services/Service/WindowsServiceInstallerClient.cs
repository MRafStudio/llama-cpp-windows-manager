using System.Diagnostics;

namespace LocalLlmConsole.Services;

/// <summary>
/// Установка/удаление Windows-службы llama-server через наш сервисный exe
/// (LocalLlmConsole.Service.exe --install / --uninstall), который использует
/// штатную утилиту sc.exe. Никаких сторонних обёрток.
/// </summary>
public sealed class WindowsServiceInstallerClient
{
    public const string ServiceExeFileName = "LocalLlmConsole.Service.exe";

    public LlamaServiceOperationResult Install()
    {
        if (!IsAdministrator())
            return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для установки службы. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Stopped);

        var (exitCode, error) = RunServiceExe("--install");
        return exitCode == 0
            ? new LlamaServiceOperationResult(true, $"Служба {WindowsServiceManager.ServiceName} установлена.", LlamaServiceStatus.Stopped)
            : new LlamaServiceOperationResult(false, $"Ошибка установки службы: {error.Trim()}");
    }

    public LlamaServiceOperationResult Uninstall()
    {
        if (!IsAdministrator())
            return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для удаления службы. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Stopped);

        var (exitCode, error) = RunServiceExe("--uninstall");
        return exitCode == 0
            ? new LlamaServiceOperationResult(true, $"Служба {WindowsServiceManager.ServiceName} удалена.", LlamaServiceStatus.Stopped)
            : new LlamaServiceOperationResult(false, $"Ошибка удаления службы: {error.Trim()}");
    }

    private static (int ExitCode, string Error) RunServiceExe(string argument)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, ServiceExeFileName);
        if (!File.Exists(exePath))
            return (1, $"Не найден исполняемый файл службы: {exePath}. Пересоберите решение.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi);
        if (process is null) return (1, "Не удалось запустить установщик службы.");
        _ = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15000)) return (1, "Таймаут установщика службы.");
        return (process.ExitCode, error);
    }

    private static bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
