using System.Diagnostics;

namespace LocalLlmConsole.Service;

/// <summary>
/// Самоустановка/удаление Windows-службы через штатную утилиту sc.exe.
/// Вызывается: LocalLlmConsole.Service.exe --install | --uninstall
/// </summary>
public static class WindowsServiceInstaller
{
    /// <summary>
    /// Устанавливает службу. Если serviceName не задан — имя вычисляется
    /// из каталога установки (ServiceIdentity). При миграции со старого имени
    /// (llama-cpp-server) GUI передаёт --service-name явно, чтобы удалить
    /// именно старую службу.
    /// </summary>
    public static int Install(string? serviceName = null)
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "LocalLlmConsole.Service.exe");
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Не найден исполняемый файл службы: {exePath}");
            return 1;
        }

        var name = string.IsNullOrWhiteSpace(serviceName)
            ? LlamaServerWindowsService.Name
            : serviceName;

        var create = RunSc("create", name,
            "binPath=", exePath,
            "start=", "auto",
            "DisplayName=", LlamaServerWindowsService.BuildDisplayName());
        if (create != 0)
        {
            Console.Error.WriteLine("Не удалось создать службу. Возможно, она уже установлена.");
            return create;
        }

        RunSc("description", name,
            LlamaServerWindowsService.BuildDescription());

        Console.WriteLine($"Служба {name} установлена.");
        return 0;
    }

    public static int Uninstall(string? serviceName = null)
    {
        var name = string.IsNullOrWhiteSpace(serviceName)
            ? LlamaServerWindowsService.Name
            : serviceName;

        RunSc("stop", name);
        var delete = RunSc("delete", name);
        if (delete != 0)
        {
            Console.Error.WriteLine("Не удалось удалить службу. Возможно, она не установлена.");
            return delete;
        }

        Console.WriteLine($"Служба {name} удалена.");
        return 0;
    }

    private static int RunSc(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null) return -1;
        _ = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000)) return -1;
        return process.ExitCode;
    }
}
