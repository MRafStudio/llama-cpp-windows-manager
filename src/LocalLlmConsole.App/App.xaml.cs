using System.Windows;

namespace LocalLlmConsole;

public partial class App : System.Windows.Application
{
    private readonly SingleInstanceApplicationService _singleInstance = new(SingleInstanceApplicationService.AcquireMutexLease);
    private readonly DialogService _dialogs = new(ThemedMessageBox.Show);

    /// <summary>
    /// Имя mutex однокопийности вычисляется из КАТАЛОГА установки (как имя службы):
    /// «D:\NEURO\LlamaGPU» → «Local\llama.cpp-service-console-d_neuro_llamagpu».
    /// Копии из разных каталогов — разные mutex → работают одновременно.
    /// </summary>
    private static string SingleInstanceMutexName()
    {
        var serviceName = LocalLlmConsole.Services.ServiceIdentity.BuildServiceName(AppContext.BaseDirectory);
        var suffix = serviceName.StartsWith(LocalLlmConsole.Services.ServiceIdentity.ServiceNamePrefix)
            ? serviceName[LocalLlmConsole.Services.ServiceIdentity.ServiceNamePrefix.Length..]
            : "single-instance";
        return @"Local\llama.cpp-service-console-" + suffix;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--bootstrap-agent-sidecars-only", StringComparer.OrdinalIgnoreCase))
        {
            var sidecars = new AgentSidecarBootstrapService().InstallEmbedded(
                typeof(App).Assembly,
                AppContext.BaseDirectory,
                verifyBundleContents: true);
            if (sidecars.Status == AgentSidecarBootstrapStatus.Failed)
                Trace.TraceWarning($"Agent control sidecar bootstrap failed: {sidecars.Error}");
            Shutdown(AgentSidecarBootstrapService.VerificationExitCode(sidecars.Status));
            return;
        }

        if (!_singleInstance.TryAcquire(SingleInstanceMutexName())
            && !e.Args.Contains("--elevated-restart", StringComparer.OrdinalIgnoreCase))
        {
            _dialogs.Notify(null, "llama.cpp Windows Manager (ext) is already running.", "llama.cpp Windows Manager (ext)", MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var startupSidecars = new AgentSidecarBootstrapService().InstallEmbedded(typeof(App).Assembly, AppContext.BaseDirectory);
        if (startupSidecars.Status == AgentSidecarBootstrapStatus.Failed)
            Trace.TraceWarning($"Agent control sidecar bootstrap failed: {startupSidecars.Error}");

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
