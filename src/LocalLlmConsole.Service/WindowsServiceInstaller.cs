using System.Diagnostics;

namespace LocalLlmConsole.Service;

/// <summary>
/// Самоустановка/удаление Windows-службы через штатную утилиту sc.exe.
/// Вызывается: LocalLlmConsole.Service.exe --install | --uninstall
/// </summary>
public static class WindowsServiceInstaller
{
    public static int Install()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "LocalLlmConsole.Service.exe");
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Не найден исполняемый файл службы: {exePath}");
            return 1;
        }

        var create = RunSc("create", LlamaServerWindowsService.Name,
            "binPath=", exePath,
            "start=", "auto",
            "DisplayName=", LlamaServerWindowsService.BuildDisplayName());
        if (create != 0)
        {
            Console.Error.WriteLine("Не удалось создать службу. Возможно, она уже установлена.");
            return create;
        }

        RunSc("description", LlamaServerWindowsService.Name,
            LlamaServerWindowsService.BuildDescription());

        Console.WriteLine($"Служба {LlamaServerWindowsService.Name} установлена.");
        return 0;
    }

    public static int Uninstall()
    {
        RunSc("stop", LlamaServerWindowsService.Name);
        var delete = RunSc("delete", LlamaServerWindowsService.Name);
        if (delete != 0)
        {
            Console.Error.WriteLine("Не удалось удалить службу. Возможно, она не установлена.");
            return delete;
        }

        Console.WriteLine($"Служба {LlamaServerWindowsService.Name} удалена.");
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
