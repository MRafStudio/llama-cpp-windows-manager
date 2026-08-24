using LocalLlmConsole;

namespace LocalLlmConsole.Services;

public enum AppUpdateApplicationPromptKind
{
    Information,
    Warning
}

public enum AppUpdateCheckApplicationOutcome
{
    Skipped,
    Checked,
    Failed
}

public enum AppUpdateInstallApplicationOutcome
{
    NotAvailable,
    MissingAsset,
    Declined,
    Started
}

public sealed record AppUpdateApplicationPrompt(
    string Title,
    string Message,
    AppUpdateApplicationPromptKind Kind);

public sealed record AppUpdateCheckApplicationActions(
    Func<bool> IsCheckInFlight,
    Action<bool> SetCheckInFlight,
    Func<bool, CancellationToken, Task<AppUpdateCheckWorkflowResult>> CheckLatestAsync,
    Action<AppUpdateInfo> SetLatestUpdate,
    Action RefreshNavigation,
    Func<bool> IsUpdatesPage,
    Action ShowUpdatesPage,
    Action<string> SetStatus,
    Func<AppUpdateApplicationPrompt, bool> ConfirmPrompt,
    Action<AppUpdateApplicationPrompt> NotifyPrompt,
    Func<AppUpdateInfo, bool, Task> InstallAsync);

public sealed record AppUpdateInstallApplicationRequest(
    AppUpdateInfo Update,
    bool Confirm,
    string? CurrentExecutablePath,
    int CurrentProcessId);

public sealed record AppUpdateInstallApplicationActions(
    Func<AppUpdateApplicationPrompt, bool> ConfirmPrompt,
    Action<AppUpdateApplicationPrompt> NotifyPrompt,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<AppUpdateInfo, IProgress<UpdateProgressState>, string?, int, CancellationToken, Task<string>> DownloadAndStartInstallAsync,
    Action<string> SetStatus,
    Action Close);

public sealed class AppUpdateApplicationService
{
    public async Task<AppUpdateCheckApplicationOutcome> CheckForUpdatesAsync(
        bool manual,
        AppUpdateCheckApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        Validate(actions);

        if (actions.IsCheckInFlight())
            return AppUpdateCheckApplicationOutcome.Skipped;

        actions.SetCheckInFlight(true);
        try
        {
            actions.SetStatus("Checking for app updates...");
            var result = await actions.CheckLatestAsync(manual, cancellationToken);
            actions.SetLatestUpdate(result.Update);
            actions.RefreshNavigation();
            if (actions.IsUpdatesPage())
                actions.ShowUpdatesPage();

            actions.SetStatus(result.StatusMessage);
            if (result.ShouldPromptInstall)
            {
                var prompt = new AppUpdateApplicationPrompt(
                    result.DialogTitle,
                    result.DialogMessage,
                    AppUpdateApplicationPromptKind.Information);
                if (actions.ConfirmPrompt(prompt))
                    await actions.InstallAsync(result.Update, false);
            }
            else if (result.ShouldShowNoUpdateDialog)
            {
                actions.NotifyPrompt(new AppUpdateApplicationPrompt(
                    result.DialogTitle,
                    result.DialogMessage,
                    AppUpdateApplicationPromptKind.Information));
            }

            return AppUpdateCheckApplicationOutcome.Checked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            actions.SetStatus($"Update check failed: {ex.Message}");
            if (manual)
            {
                actions.NotifyPrompt(new AppUpdateApplicationPrompt(
                    "Update check failed",
                    ex.Message,
                    AppUpdateApplicationPromptKind.Warning));
            }

            return AppUpdateCheckApplicationOutcome.Failed;
        }
        finally
        {
            actions.SetCheckInFlight(false);
        }
    }

    public async Task<AppUpdateInstallApplicationOutcome> InstallAsync(
        AppUpdateInstallApplicationRequest request,
        AppUpdateInstallApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);

        if (!request.Update.IsAvailable)
            return AppUpdateInstallApplicationOutcome.NotAvailable;

        if (string.IsNullOrWhiteSpace(request.Update.AssetUrl))
        {
            actions.NotifyPrompt(new AppUpdateApplicationPrompt(
                "Install update",
                "The latest GitHub release does not include a portable Windows app asset.",
                AppUpdateApplicationPromptKind.Warning));
            return AppUpdateInstallApplicationOutcome.MissingAsset;
        }

        if (request.Confirm)
        {
            var prompt = new AppUpdateApplicationPrompt(
                "Install update",
                $"Install {request.Update.LatestVersion} now?\n\nThe app will close, replace the executable, and restart.",
                AppUpdateApplicationPromptKind.Information);
            if (!actions.ConfirmPrompt(prompt))
                return AppUpdateInstallApplicationOutcome.Declined;
        }

        await InstallWithProgressAsync(request, actions, cancellationToken);
        return AppUpdateInstallApplicationOutcome.Started;
    }

    /// <summary>
    /// Скачивание с красивым окном прогресса; при ошибке — GUI остаётся работать.
    /// Updater запускается только после 100% успешной загрузки и проверки.
    /// </summary>
    private static async Task InstallWithProgressAsync(
        AppUpdateInstallApplicationRequest request,
        AppUpdateInstallApplicationActions actions,
        CancellationToken cancellationToken)
    {
        var window = new UpdateProgressWindow("Обновление приложения");
        try
        {
            window.Show();
            var progress = new Progress<UpdateProgressState>(state => window.SetState(state));
            var status = await actions.DownloadAndStartInstallAsync(
                request.Update,
                progress,
                request.CurrentExecutablePath,
                request.CurrentProcessId,
                cancellationToken);
            actions.SetStatus(status);
            window.Close();
            actions.Close();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            try { window.Close(); }
            catch { /* окно могло уже закрыться */ }
            actions.NotifyPrompt(new AppUpdateApplicationPrompt(
                "Update failed",
                $"Не удалось подготовить обновление: {ex.Message}\n\nПриложение продолжает работать.",
                AppUpdateApplicationPromptKind.Warning));
        }
    }

    private static void Validate(AppUpdateCheckApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.IsCheckInFlight);
        ArgumentNullException.ThrowIfNull(actions.SetCheckInFlight);
        ArgumentNullException.ThrowIfNull(actions.CheckLatestAsync);
        ArgumentNullException.ThrowIfNull(actions.SetLatestUpdate);
        ArgumentNullException.ThrowIfNull(actions.RefreshNavigation);
        ArgumentNullException.ThrowIfNull(actions.IsUpdatesPage);
        ArgumentNullException.ThrowIfNull(actions.ShowUpdatesPage);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.ConfirmPrompt);
        ArgumentNullException.ThrowIfNull(actions.NotifyPrompt);
        ArgumentNullException.ThrowIfNull(actions.InstallAsync);
    }

    private static void Validate(AppUpdateInstallApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ConfirmPrompt);
        ArgumentNullException.ThrowIfNull(actions.NotifyPrompt);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.DownloadAndStartInstallAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.Close);
    }
}
