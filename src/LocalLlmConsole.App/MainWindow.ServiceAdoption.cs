using System.Text.Json;
using System.Windows;

namespace LocalLlmConsole;

/// <summary>
/// Адаптация старта под Windows-службу llama-cpp-server.
/// Служба — постоянный шлюз локальных LLM: она держит llama-server
/// на едином порту 8101 (красивое имя модели формирует сама из имени файла).
/// Если служба запущена — GUI не поднимает собственный шлюз (не занимает 8101),
/// а регистрирует модель службы как уже загруженную сессию: страница «Обзор»
/// показывает шлюз и модель, кнопка «Загрузить» становится неактивной.
/// Если службы нет — GUI стартует стандартным загрузчиком, как раньше.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// True, если шлюзом управляет служба (служба установлена и запущена) —
    /// тогда собственный шлюз GUI поднимать нельзя: порт 8101 занят службой.
    /// </summary>
    private bool ShouldUseServiceGateway() => IsServiceGatewayRunning();

    /// <summary>
    /// Освобождает порт шлюза и GUI-модели: служба — единственный владелец.
    /// Вызывается перед стартом службы.
    /// </summary>
    private async Task ReleaseGatewayPortForServiceAsync()
    {
        try
        {
            if (_gateway is not null)
            {
                await StopModelGatewayAsync();
                UpdateGatewayStatusText();
            }

            // Служба становится единственным владельцем моделей:
            // GUI-сессии (реальные процессы) останавливаем, чтобы не было
            // дубля на порту профиля и лишней нагрузки на VRAM.
            var ownedSessions = _sessions.Snapshots()
                .Where(session => session.IsRunning && session.ProcessId > 0)
                .ToList();
            foreach (var session in ownedSessions)
                await _sessions.StopAsync(session.SessionId, "Service started: GUI runtime released.");
            if (ownedSessions.Count > 0)
                RefreshOverviewSessionRows();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not release gateway port for service: {ex}");
        }
    }

    private bool IsServiceGatewayRunning()
    {
        var serviceManager = new Services.WindowsServiceManager();
        return serviceManager.GetStatus() == Services.LlamaServiceStatus.Running;
    }

    /// <summary>
    /// Регистрирует модель, которой управляет служба, как уже загруженную сессию:
    /// страница «Обзор» показывает её (порт 8101), кнопка «Загрузить» неактивна.
    /// Параметры берутся из БД приложения — как и у самой службы.
    /// </summary>
    private async Task AdoptServiceManagedSessionAsync()
    {
        try
        {
            var serviceManager = new Services.WindowsServiceManager();
            if (serviceManager.GetStatus() != Services.LlamaServiceStatus.Running) return;

            // Служба — владелец шлюза 8101. Если GUI уже поднял свой шлюз
            // (службу запустили после старта GUI) — освобождаем порт для службы.
            if (_gateway is not null)
            {
                await StopModelGatewayAsync();
                UpdateGatewayStatusText();
            }

            var configPath = Path.Combine(_workspaceRoot, "state", "service-config.json");
            if (!File.Exists(configPath)) return;

            ServiceLaunchConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<ServiceLaunchConfig>(
                    await File.ReadAllTextAsync(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return;
            }
            if (config is null || string.IsNullOrWhiteSpace(config.ModelId)) return;

            var models = await AppServices.ModelLookupApplication.ListAsync();
            var model = models.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, config.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model is null) return;

            if (_sessions.SessionForModel(model.Id) is { IsRunning: true }) return;

            var profiles = await ModelServices.LaunchProfiles.ListNamedAsync(model);
            var profile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
            var profileId = profile?.Id ?? $"default:{model.Id}";
            var profileName = string.IsNullOrWhiteSpace(profile?.Name) ? "Default" : profile.Name;

            var runtimes = await AppServices.StateStore.ListRuntimesAsync();
            var runtime = runtimes.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, config.RuntimeId, StringComparison.OrdinalIgnoreCase));
            if (runtime is null) return;

            // Сессия-призрак: процессом управляет служба, GUI только отображает её.
            // Порт и host — из настроек приложения (порт шлюза / доступ в LAN),
            // как их прочитает и сама служба при запуске.
            var gatewayLan = _settings.ModelAccessMode is "gateway" or "both";
            var settings = _settings with
            {
                Host = gatewayLan ? "0.0.0.0" : "127.0.0.1",
                Port = _settings.AutoLoadGatewayEnabled ? _settings.AutoLoadGatewayPort : (profile?.Settings.Port ?? _settings.AutoLoadGatewayPort),
                ModelApiKey = _settings.ModelApiKey,
                RequireApiKeyAuth = _settings.RequireApiKeyAuth
            };

            _sessions.AttachExisting(
                runtime,
                model,
                settings,
                logPath: ResolveServiceLogPath(),
                state: LlamaRuntimeState.Loaded,
                processMarker: "",
                sessionId: "",
                startedAt: DateTimeOffset.UtcNow,
                processId: 0,
                launchProfileId: profileId,
                launchProfileName: profileName);

            SetStatus(Loc.T("Status.ServiceGatewayOwnsPort"));
            RefreshOverviewSessionRows();
            await RefreshOverviewModelSelectorAsync();
            UpdateOverviewModelActions();

            // Таймер дашборда в обычном сценарии запускается вместе с моделью —
            // для сессии службы запускаем его здесь, чтобы «Журнал среды выполнения
            // в реальном времени» заполнился сразу, без перехода по страницам.
            StartRuntimeDashboardRefreshTimer();
            await RefreshRuntimeMetricsAsync();

            // Зафиксировать реальные модель и профиль службы (из service-config.json)
            // и сделать селекторы нередактируемыми: шлюзом управляет служба,
            // «Загрузить» отсюда бессмысленно.
            _overviewPage.SelectModelChoice(config.ModelId, _viewModel.Overview.ModelChoices);
            _overviewPage.SelectLaunchProfile(profileId);
            if (_overviewPage.ModelCombo is not null)
                _overviewPage.ModelCombo.IsEnabled = false;
            _overviewPage.SetLaunchProfileEnabled(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not adopt service-managed model: {ex}");
        }
    }

    private string ResolveServiceLogPath()
    {
        var logs = Path.Combine(_workspaceRoot, "logs");
        if (Directory.Exists(logs))
        {
            var latest = Directory.GetFiles(logs, "llama-server-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is not null) return latest;
        }
        return Path.Combine(logs, $"llama-server-service-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    private sealed record ServiceLaunchConfig(
        string ExecutablePath,
        IReadOnlyList<string>? Arguments,
        string ModelId = "",
        string ProfileId = "",
        string RuntimeId = "");
}
