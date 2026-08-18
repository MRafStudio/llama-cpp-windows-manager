namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    private readonly string _workspaceRoot;

    public AppServiceFactory(string workspaceRoot)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public string DatabasePath => Path.Combine(_workspaceRoot, "state", "local-llm-console.db");

    public string LogRoot => Path.Combine(_workspaceRoot, "logs");

    public RuntimeLaunchOptionDiscoveryService CreateRuntimeLaunchOptionDiscoveryService(IProcessRunner processRunner)
        => new(processRunner, new RuntimeLaunchOptionDiagnosticsService(
            Path.Combine(_workspaceRoot, "diagnostics", "runtime-options")));

    public LlamaServiceController CreateLlamaServiceController(IProcessRunner processRunner)
    {
        var llamaServerPath = Path.Combine(_workspaceRoot, "runtimes", "llama-server.exe");
        var logPath = Path.Combine(_workspaceRoot, "logs", "llama-service.log");
        return new LlamaServiceController(processRunner, _workspaceRoot, llamaServerPath, logPath);
    }

    public MainWindowInfrastructureServices CreateMainWindowInfrastructureServices()
    {
        var processRunner = CreateProcessRunner();
        return new(
            CreateAppUpdateService(),
            CreateLoadedModelSessionManager(processRunner),
            processRunner,
            CreateWindowsEnvironmentService(),
            CreateWslEnvironmentService(),
            CreateRuntimeProbeClient(),
            CreateRuntimeMetricsClient(),
            CreateRuntimePackageClient());
    }
}
