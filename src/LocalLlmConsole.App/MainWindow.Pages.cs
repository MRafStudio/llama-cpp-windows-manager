using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private void ShowOverview()
        => ShowOverview(refresh: true);

    private void ShowOverview(bool refresh)
    {
        _pageControllers.Overview.CancelPendingSelections();
        SetPage("Overview", Loc.T("PageSubtitle.Overview"));
        var overview = OverviewPageFactory.Create(new OverviewPageRequest(
            _viewModel,
            _pageControllers.Overview.Build(),
            SetRuntimeMetricsGridColumnSizing));

        _overviewPage.Apply(overview);
        _overviewPage.ApplyUiPreferences(_settings);
        _runtimeDashboardPage.Apply(overview);

        PageHost.Content = overview.Root;
        if (refresh)
        {
            RunBackground(RefreshOverviewAsync, "Overview refresh failed");
            RunBackground(RefreshOverviewModelSelectorAsync, "Overview model refresh failed");
            RunBackground(RefreshRuntimeMetricsAsync, "Runtime metrics refresh failed");
            StartRuntimeDashboardRefreshTimer();
        }
    }

    private void ShowModels()
    {
        SetPage("Models", Loc.T("PageSubtitle.Models"));

        var modelsPage = ModelsPageFactory.Create(new ModelsPageRequest(
            _viewModel,
            _settings.ModelsRoot,
            CreateLaunchSettingsPanel(),
            _pageControllers.Models.Build()));

        _modelsPage.Apply(modelsPage);
        _modelsPage.ApplyUiPreferences(_settings);
        ConfigureHfSearchGrid();
        PageHost.Content = modelsPage.Root;
        RunBackground(RefreshModelsAsync, "Models refresh failed");
    }

    private void ShowRuntimes()
    {
        SetPage("Runtimes", Loc.T("PageSubtitle.Runtimes"));
        var runtimesPage = RuntimesPageFactory.Create(new RuntimesPageRequest(
            _viewModel,
            _settings.RuntimeRoot,
            _settings.CudaPackagePreference,
            _pageControllers.Runtimes.Build()));

        _runtimesPage.Apply(runtimesPage);
        PageHost.Content = runtimesPage.Root;
        RunBackground(DetectAndRefreshRuntimesAsync, "Runtime refresh failed");
    }

    private async Task ScanModelsFolderAsync()
    {
        await RunAsync("Scanning models...", async () =>
        {
            var catalog = ModelServices.Catalog;
            Require(catalog);
            await catalog!.ScanAsync(_settings.ModelsRoot);
            await RefreshModelsAsync();
            await RefreshOverviewAsync();
        });
    }

    private void ShowService()
    {
        SetPage("Service", Loc.T("PageSubtitle.Service"));

        // Создаём ViewModel если ещё не создана
        if (_serviceViewModel is null)
        {
            var setStatus = (string text) => _viewModel.SetStatus(text);
            var setStatusAsync = (string text) => Task.Run(() => { _viewModel.SetStatus(text); return Task.CompletedTask; });

            var serviceManager = new WindowsServiceManager();
            var installer = new WindowsServiceInstallerClient();
            var configWriter = new ServiceLaunchConfigWriter(_workspaceRoot);

            _serviceViewModel = new LlamaServiceViewModel(
                serviceManager,
                installer,
                configWriter,
                setStatus,
                setStatusAsync,
                CollectServiceLaunchArgs);
        }

        // Привязываем ViewModel к MainWindowViewModel для данных
        _viewModel.LlamaService = _serviceViewModel;

        var servicePage = Ui.Pages.Service.ServicePageFactory.Create(new Ui.Pages.Service.ServicePageRequest(_serviceViewModel));
        PageHost.Content = servicePage.Content;
    }

    /// <summary>
    /// Собирает актуальные аргументы запуска llama-server для выбранной модели
    /// (путь к модели, порт, контекст, кэши и т.д.) — как при запуске из GUI.
    /// </summary>
    private IReadOnlyList<string> CollectServiceLaunchArgs()
    {
        try
        {
            var model = SelectedModel();
            if (model is null) return Array.Empty<string>();

            var runtime = SelectedRuntime();
            if (runtime is null) return Array.Empty<string>();

            // Штатный сервис: строит финальные параметры запуска модели
            // (профиль + дефолты) — как при запуске из GUI.
            var viewState = ModelServices.ModelLaunchSettingsWorkflow
                .BuildAsync(model, _settings, CancellationToken.None, SelectedModelLaunchProfileId())
                .GetAwaiter().GetResult();
            var appSettings = viewState.LaunchSettings;

            var extra = new List<string>();
            if (appSettings.EnableMetrics) extra.Add("--metrics");
            extra.AddRange(CustomLaunchParameterParser.Parse(appSettings.CustomParameters));

            var context = new RuntimeLaunchRequestContext(
                runtime.Mode,
                runtime.Backend,
                runtime.ExecutablePath,
                model.ModelPath,
                "127.0.0.1",
                AllowNetworkAccess: false,
                appSettings.VisionProjectorPath,
                VisionProjectorEmbedded: string.Equals(appSettings.VisionMode, "on", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(appSettings.VisionProjectorPath),
                DraftModelPath: appSettings.SpecDraftModelPath,
                MtpHeadPath: LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(appSettings.SpeculativeType) ? appSettings.MtpHeadPath : "",
                ExtraArguments: extra);

            var request = RuntimeLaunchRequestFactory.Create(appSettings, context);
            return RuntimeAdapter.BuildArgs(request);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void RefreshCurrentPage()
    {
        switch (_viewModel.CurrentPage)
        {
            case "Overview": ShowOverview(); break;
            case "Models": ShowModels(); break;
            case "Runtimes": ShowRuntimes(); break;
            case "Settings": ShowSettings(); break;
            case "Lifetime": ShowLifetime(); break;
            case "Logs": ShowLogs(); break;
            case "Windows": ShowWindows(); break;
            case "WSL Linux": ShowWslLinux(); break;
            case "Updates": ShowUpdates(); break;
            case "Service": ShowService(); break;
            case "Help": ShowHelp(); break;
        }
    }
}
