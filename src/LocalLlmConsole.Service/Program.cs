using System.ServiceProcess;

namespace LocalLlmConsole.Service;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            return WindowsServiceInstaller.Install();

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            return WindowsServiceInstaller.Uninstall();

        ServiceBase.Run(new ServiceBase[] { new LlamaServerWindowsService() });
        return 0;
    }
}
