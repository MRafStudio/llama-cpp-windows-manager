namespace LocalLlmConsole.Services;

public sealed record AppUpdateCheckWorkflowResult(
    AppUpdateInfo Update,
    string StatusMessage,
    bool ShouldPromptInstall,
    bool ShouldShowNoUpdateDialog,
    string DialogTitle,
    string DialogMessage);

public sealed class AppUpdateWorkflowService
{
    private readonly AppUpdateService _updates;
    private readonly string _workspaceRoot;

    public AppUpdateWorkflowService(AppUpdateService updates, string workspaceRoot)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public async Task<AppUpdateCheckWorkflowResult> CheckLatestAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var update = await _updates.CheckLatestAsync(linked.Token);
        return DescribeCheckResult(update, manual);
    }

    public async Task<string> StageAndStartInstallAsync(
        AppUpdateInfo update,
        string? currentExecutablePath,
        int currentProcessId,
        CancellationToken cancellationToken = default)
    {
        return await DownloadAndStartInstallAsync(update, null, currentExecutablePath, currentProcessId, cancellationToken);
    }

    /// <summary>
    /// Скачивает обновление с прогрессом и запускает автономный Updater-процесс.
    /// При любой ошибке скачивания/проверки — исключение, GUI продолжает работать.
    /// </summary>
    public async Task<string> DownloadAndStartInstallAsync(
        AppUpdateInfo update,
        IProgress<UpdateProgressState>? progress,
        string? currentExecutablePath,
        int currentProcessId,
        CancellationToken cancellationToken = default)
    {
        var files = await _updates.DownloadAndVerifyAsync(update, progress, cancellationToken);
        _updates.StartUpdaterProcess(update, files, currentProcessId);
        return "Update started. Closing to install...";
    }

    public async Task<InstalledUpdateNotice?> TryConsumeInstalledNoticeAsync(CancellationToken cancellationToken = default)
        => await AppUpdateService.TryConsumeInstalledNoticeAsync(_workspaceRoot, cancellationToken);

    public static AppUpdateCheckWorkflowResult DescribeCheckResult(AppUpdateInfo update, bool manual)
    {
        if (update.IsAvailable)
        {
            return new AppUpdateCheckWorkflowResult(
                update,
                $"Update available: {update.LatestVersion}.",
                ShouldPromptInstall: manual,
                ShouldShowNoUpdateDialog: false,
                "Install update",
                $"Update {update.CurrentVersion} -> {update.LatestVersion} is available.\n\n{update.ReleaseName}\n\nInstall it now?");
        }

        return new AppUpdateCheckWorkflowResult(
            update,
            "No app updates available.",
            ShouldPromptInstall: false,
            ShouldShowNoUpdateDialog: manual,
            "Check for updates",
            $"No updates are available.\n\nCurrent version: {update.CurrentVersion}");
    }
}
