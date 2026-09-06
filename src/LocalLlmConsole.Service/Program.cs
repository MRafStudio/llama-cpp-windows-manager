using System.ServiceProcess;

namespace LocalLlmConsole.Service;

internal static class Program
{
    private static int Main(string[] args)
    {
        // GUI может передать явное имя службы (--service-name) — при миграции
        // со старого фиксированного имени llama-cpp-server на путь-зависимое.
        var serviceName = ReadArgumentValue(args, "--service-name");

        if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            return WindowsServiceInstaller.Install(serviceName);

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            return WindowsServiceInstaller.Uninstall(serviceName);

        ServiceBase.Run(new ServiceBase[] { new LlamaServerWindowsService() });
        return 0;
    }

    private static string? ReadArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
