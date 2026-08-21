namespace LocalLlmConsole.ViewModels;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;

/// <summary>
/// ViewModel для страницы управления службой llama-server.
/// Работает через собственные классы управления (WindowsServiceManager,
/// WindowsServiceInstallerClient, ServiceLaunchConfigWriter) — без вторжения
/// в авторскую логику запуска моделей.
/// </summary>
public sealed class LlamaServiceViewModel : INotifyPropertyChanged
{
    private readonly WindowsServiceManager _serviceManager;
    private readonly WindowsServiceInstallerClient _installer;
    private readonly ServiceLaunchConfigWriter _configWriter;
    private readonly Func<Task<IReadOnlyList<LocalLlmConsole.Models.ModelRecord>>> _loadModels;
    private readonly Func<LocalLlmConsole.Models.ModelRecord, Task<IReadOnlyList<LocalLlmConsole.Models.NamedModelLaunchProfile>>> _loadProfiles;
    private readonly Func<Task<IReadOnlyList<LocalLlmConsole.Models.RuntimeRecord>>> _loadRuntimes;
    private readonly Func<LocalLlmConsole.Models.ModelRecord, string, LocalLlmConsole.Models.RuntimeRecord, IReadOnlyList<string>> _buildLaunchArgs;

    private IReadOnlyList<LocalLlmConsole.Models.ModelRecord> _models = [];
    private LocalLlmConsole.Models.ModelRecord? _selectedModel;
    private IReadOnlyList<LocalLlmConsole.Models.NamedModelLaunchProfile> _profiles = [];
    private LocalLlmConsole.Models.NamedModelLaunchProfile? _selectedProfile;
    private IReadOnlyList<LocalLlmConsole.Models.RuntimeRecord> _runtimes = [];
    private LocalLlmConsole.Models.RuntimeRecord? _selectedRuntime;

    private LlamaServiceStatus _status;
    private string _statusText = string.Empty;
    private string _processIdText = string.Empty;
    private string _lastMessage = string.Empty;
    private bool _hasError;
    private string _llamaServerPathText = string.Empty;
    private bool _isLoading;
    private bool _isElevated;
    private bool _isInstalled;

    public LlamaServiceStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanRestart));
                OnPropertyChanged(nameof(ServiceStatus));
                OnPropertyChanged(nameof(ServiceStatusColor));
                UpdateButtons();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Текст статуса службы: установлена/не установлена, запущена/остановлена.
    /// </summary>
    public string ServiceStatus
    {
        get
        {
            if (!_isInstalled) return Loc.T("Service.Status.NotInstalled");
            return _status switch
            {
                LlamaServiceStatus.Running => Loc.T("Service.Status.Running"),
                LlamaServiceStatus.Starting => Loc.T("Service.Status.Starting"),
                LlamaServiceStatus.Stopping => Loc.T("Service.Status.Stopping"),
                LlamaServiceStatus.Stopped => Loc.T("Service.Status.Stopped"),
                _ => Loc.T("Service.Status.Unknown")
            };
        }
    }

    /// <summary>
    /// Цвет статуса: зелёный — работает, жёлтый — переходные состояния,
    /// красный — не установлена/ошибка, серый — остановлена.
    /// </summary>
    public System.Windows.Media.Brush ServiceStatusColor
    {
        get
        {
            if (!_isInstalled) return ThemeBrush("Danger");
            return _status switch
            {
                LlamaServiceStatus.Running => ThemeBrush("Success"),
                LlamaServiceStatus.Starting or LlamaServiceStatus.Stopping => ThemeBrush("Warning"),
                LlamaServiceStatus.Stopped => ThemeBrush("TextMuted"),
                _ => ThemeBrush("TextMuted")
            };
        }
    }

    /// <summary>
    /// Последнее сообщение операции (результат установки/удаления/запуска,
    /// ошибка, рекомендация).
    /// </summary>
    public string LastMessage
    {
        get => _lastMessage;
        private set
        {
            if (_lastMessage != value)
            {
                _lastMessage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Имеется ли ошибка в последнем сообщении (красный цвет сообщения).
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError != value)
            {
                _hasError = value;
                OnPropertyChanged();
            }
        }
    }

    private void SetError(string message)
    {
        StatusText = message;
        LastMessage = message;
        HasError = true;
    }

    private static System.Windows.Media.Brush ThemeBrush(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
           ?? System.Windows.Media.Brushes.Gray;

    public string ProcessIdText
    {
        get => _processIdText;
        private set
        {
            if (_processIdText != value)
            {
                _processIdText = value;
                OnPropertyChanged();
            }
        }
    }

    public string LlamaServerPathText
    {
        get => _llamaServerPathText;
        private set
        {
            if (_llamaServerPathText != value)
            {
                _llamaServerPathText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanRestart));
            }
        }
    }

    public bool IsElevated
    {
        get => _isElevated;
        private set
        {
            if (_isElevated != value)
            {
                _isElevated = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Установлена ли Windows-служба.
    /// </summary>
    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanRestart));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(ServiceStatus));
                OnPropertyChanged(nameof(ServiceStatusColor));
            }
        }
    }

    /// <summary>
    /// Отображаемое имя установленной службы (видно, из какого каталога она установлена).
    /// Поле всегда зарезервировано под заголовком страницы; пустое, если службы нет.
    /// </summary>
    public string ServiceDisplayName { get; private set; } = "";

    private void UpdateServiceDisplayName()
    {
        ServiceDisplayName = _isInstalled ? _serviceManager.GetDisplayName() : "";
        OnPropertyChanged(nameof(ServiceDisplayName));
    }

    /// <summary>
    /// Доступна ли установка службы (нет активной операции, служба не установлена).
    /// </summary>
    public bool CanInstall => !_isLoading && !_isInstalled;

    /// <summary>
    /// Доступно ли удаление службы (нет активной операции, служба установлена).
    /// </summary>
    public bool CanUninstall => !_isLoading && _isInstalled;

    /// <summary>
    /// Доступен ли запуск службы.
    /// </summary>
    public bool CanStart => !_isLoading && _isInstalled && _status != LlamaServiceStatus.Running;

    /// <summary>
    /// Доступна ли остановка службы.
    /// </summary>
    public bool CanStop => !_isLoading && _isInstalled && _status == LlamaServiceStatus.Running;

    /// <summary>
    /// Доступен ли перезапуск службы.
    /// </summary>
    public bool CanRestart => !_isLoading && _isInstalled && _status == LlamaServiceStatus.Running;

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ElevateCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }

    /// <summary>Установленные модели (для выпадающего списка).</summary>
    public IReadOnlyList<LocalLlmConsole.Models.ModelRecord> Models
    {
        get => _models;
        private set { _models = value; OnPropertyChanged(); }
    }

    /// <summary>Выбранная модель службы.</summary>
    public LocalLlmConsole.Models.ModelRecord? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!ReferenceEquals(_selectedModel, value))
            {
                _selectedModel = value;
                OnPropertyChanged();
                _ = RefreshProfilesAsync();
                SaveSelection();
            }
        }
    }

    /// <summary>Профили запуска выбранной модели.</summary>
    public IReadOnlyList<LocalLlmConsole.Models.NamedModelLaunchProfile> Profiles
    {
        get => _profiles;
        private set { _profiles = value; OnPropertyChanged(); }
    }

    /// <summary>Выбранный профиль запуска.</summary>
    public LocalLlmConsole.Models.NamedModelLaunchProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!ReferenceEquals(_selectedProfile, value))
            {
                _selectedProfile = value;
                OnPropertyChanged();
                SaveSelection();
            }
        }
    }

    /// <summary>Установленные среды выполнения (для выпадающего списка).</summary>
    public IReadOnlyList<LocalLlmConsole.Models.RuntimeRecord> Runtimes
    {
        get => _runtimes;
        private set { _runtimes = value; OnPropertyChanged(); }
    }

    /// <summary>Выбранная среда выполнения.</summary>
    public LocalLlmConsole.Models.RuntimeRecord? SelectedRuntime
    {
        get => _selectedRuntime;
        set
        {
            if (!ReferenceEquals(_selectedRuntime, value))
            {
                _selectedRuntime = value;
                OnPropertyChanged();
                SaveSelection();
            }
        }
    }

    public LlamaServiceViewModel(
        WindowsServiceManager serviceManager,
        WindowsServiceInstallerClient installer,
        ServiceLaunchConfigWriter configWriter,
        Action<string> setStatus,
        Func<string, Task> setStatusAsync,
        Func<Task<IReadOnlyList<LocalLlmConsole.Models.ModelRecord>>> loadModels,
        Func<LocalLlmConsole.Models.ModelRecord, Task<IReadOnlyList<LocalLlmConsole.Models.NamedModelLaunchProfile>>> loadProfiles,
        Func<Task<IReadOnlyList<LocalLlmConsole.Models.RuntimeRecord>>> loadRuntimes,
        Func<LocalLlmConsole.Models.ModelRecord, string, LocalLlmConsole.Models.RuntimeRecord, IReadOnlyList<string>> buildLaunchArgs,
        Action? onServiceStarting = null)
    {
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _setStatusAsync = setStatusAsync ?? throw new ArgumentNullException(nameof(setStatusAsync));
        _loadModels = loadModels ?? throw new ArgumentNullException(nameof(loadModels));
        _loadProfiles = loadProfiles ?? throw new ArgumentNullException(nameof(loadProfiles));
        _loadRuntimes = loadRuntimes ?? throw new ArgumentNullException(nameof(loadRuntimes));
        _buildLaunchArgs = buildLaunchArgs ?? throw new ArgumentNullException(nameof(buildLaunchArgs));
        _onServiceStarting = onServiceStarting;

        StartCommand = new RelayCommand(ExecuteStart, CanExecuteStart);
        StopCommand = new RelayCommand(ExecuteStop, CanExecuteStop);
        RestartCommand = new RelayCommand(ExecuteRestart, CanExecuteRestart);
        RefreshCommand = new RelayCommand(ExecuteRefresh, _ => true);
        ElevateCommand = new RelayCommand(ExecuteElevate, _ => true);
        InstallCommand = new RelayCommand(ExecuteInstall, CanExecuteInstall);
        UninstallCommand = new RelayCommand(ExecuteUninstall, CanExecuteUninstall);

        IsElevated = IsAdministrator();

        _ = LoadStatusAsync();
        _ = LoadSelectionsAsync();

        // Периодический опрос статуса службы: если службу остановили/запустили
        // вне GUI (например, из панели Windows), кнопки и статус обновятся сами.
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusSilentlyAsync();
        _statusTimer.Start();
    }

    private readonly DispatcherTimer _statusTimer;

    /// <summary>
    /// Тихий опрос статуса службы (без спиннера): обновляет кнопки и статус,
    /// если состояние службы изменилось извне.
    /// </summary>
    private async Task RefreshStatusSilentlyAsync()
    {
        if (_isLoading || !_isInstalled) return;
        try
        {
            var status = _serviceManager.GetStatus();
            if (status != _status)
            {
                Status = status;
                StatusText = status switch
                {
                    LlamaServiceStatus.Running => Loc.T("Service.Status.Running"),
                    LlamaServiceStatus.Stopped => Loc.T("Service.Status.Stopped"),
                    _ => Loc.T("Service.Status.Unknown")
                };
                ProcessIdText = status == LlamaServiceStatus.Running ? $"PID: {GetLlamaServerPid()}" : "PID: —";
            }
        }
        catch
        {
            // Опрос не критичен — следующее срабатывание повторит попытку.
        }
    }

    private readonly Action<string> _setStatus;
    private readonly Func<string, Task> _setStatusAsync;
    private readonly Action? _onServiceStarting;

    private async Task LoadStatusAsync()
    {
        IsLoading = true;
        try
        {
            IsInstalled = _serviceManager.IsInstalled();
            UpdateServiceDisplayName();

            if (!IsInstalled)
            {
                StatusText = Loc.T("Service.Status.NotInstalled");
                LastMessage = Loc.T("Service.Hint.NotInstalled");
                HasError = false;
                Status = LlamaServiceStatus.Stopped;
                return;
            }

            var status = _serviceManager.GetStatus();
            Status = status;
            StatusText = status switch
            {
                LlamaServiceStatus.Running => Loc.T("Service.Status.Running"),
                LlamaServiceStatus.Stopped => Loc.T("Service.Status.Stopped"),
                _ => Loc.T("Service.Status.Unknown")
            };
            LastMessage = Loc.T("Service.Hint.Installed");
            HasError = false;
            ProcessIdText = status == LlamaServiceStatus.Running ? $"PID: {GetLlamaServerPid()}" : "PID: —";
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateButtons() => CommandManager.InvalidateRequerySuggested();

    /// <summary>
    /// Загружает установленные модели и среды выполнения, восстанавливает
    /// сохранённый в конфиге службы выбор (модель/профиль/runtime).
    /// </summary>
    private async Task LoadSelectionsAsync()
    {
        try
        {
            Models = await _loadModels();
            Runtimes = await _loadRuntimes();

            var saved = _configWriter.Read();
            SelectedModel = saved is not null && Models.FirstOrDefault(m => m.Id == saved.ModelId) is { } savedModel
                ? savedModel
                : Models.FirstOrDefault();
            SelectedRuntime = saved is not null && Runtimes.FirstOrDefault(r => r.Id == saved.RuntimeId) is { } savedRuntime
                ? savedRuntime
                : Runtimes.FirstOrDefault();
            // Профили загружаются в RefreshProfilesAsync (вызывается из setter SelectedModel)
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Загружает профили запуска выбранной модели, восстанавливает сохранённый профиль.
    /// </summary>
    private async Task RefreshProfilesAsync()
    {
        var model = SelectedModel;
        if (model is null)
        {
            Profiles = [];
            SelectedProfile = null;
            return;
        }

        try
        {
            var profiles = await _loadProfiles(model);
            Profiles = profiles;
            var savedId = _configWriter.Read()?.ProfileId;
            SelectedProfile = profiles.FirstOrDefault(p => p.Id == savedId)
                              ?? profiles.FirstOrDefault(p => p.IsDefault)
                              ?? profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Сохраняет выбранные модель/профиль/runtime в конфиг службы (без аргументов —
    /// они перезапишутся при запуске). Вызывается при изменении любого из списков.
    /// </summary>
    private void SaveSelection()
    {
        try
        {
            var executable = SelectedRuntime?.ExecutablePath ?? FindLlamaServerPath();
            _configWriter.Write(executable, [],
                SelectedModel?.Id ?? "",
                SelectedProfile?.Id ?? "",
                SelectedRuntime?.Id ?? "");
        }
        catch
        {
            // Сохранение выбора — не критично при ошибке
        }
    }

    private void ExecuteStart(object? param)
    {
        if (IsLoading || Status == LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = Loc.T("Service.Status.Starting");
        try
        {
            // Конфиг службы не записан — запускать нельзя (служба упадёт)
            if (!WriteLaunchConfigIfNeeded())
            {
                IsLoading = false;
                return;
            }
            // Служба должна занять порт шлюза: освобождаем его ДО старта,
            // иначе llama-server упадёт с "couldn't bind" (порт занят GUI-шлюзом).
            _onServiceStarting?.Invoke();
            var result = _serviceManager.Start();
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExecuteStart(object? param) => CanStart;

    private void ExecuteStop(object? param)
    {
        if (IsLoading || Status != LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = Loc.T("Service.Status.Stopping");
        try
        {
            var result = _serviceManager.Stop();
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExecuteStop(object? param) => CanStop;

    private void ExecuteRestart(object? param)
    {
        if (IsLoading || Status != LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = Loc.T("Service.Status.Restarting");
        try
        {
            WriteLaunchConfigIfNeeded();
            var result = _serviceManager.Restart();
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExecuteRestart(object? param) => CanRestart;

    private void ExecuteRefresh(object? param) => _ = LoadStatusAsync();

    private void ExecuteInstall(object? param)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = Loc.T("Service.Status.Installing");
        try
        {
            // Установка не требует параметров модели — служба просто создаётся.
            // Параметры понадобятся при запуске (WriteLaunchConfigIfNeeded в ExecuteStart).
            var result = _installer.Install();
            ApplyResult(result);
            // Обновляем состояние установки, чтобы кнопки переключились
            IsInstalled = _serviceManager.IsInstalled();
            UpdateServiceDisplayName();
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExecuteInstall(object? param) => CanInstall;

    private void ExecuteUninstall(object? param)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = Loc.T("Service.Status.Uninstalling");
        try
        {
            var result = _installer.Uninstall();
            ApplyResult(result);
            // Обновляем состояние установки, чтобы кнопки переключились
            IsInstalled = _serviceManager.IsInstalled();
            UpdateServiceDisplayName();
        }
        catch (Exception ex)
        {
            SetError($"{Loc.T("Service.Status.Error")}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExecuteUninstall(object? param) => CanUninstall;

    /// <summary>
    /// Собирает параметры запуска из выбранных на странице службы модели,
    /// профиля и среды выполнения, пишет конфиг службы.
    /// Возвращает true, если конфиг записан. При неудаче выводит ошибку
    /// и возвращает false — операцию (запуск) продолжать нельзя.
    /// </summary>
    private bool WriteLaunchConfigIfNeeded()
    {
        var model = SelectedModel;
        if (model is null)
        {
            SetError($"{Loc.T("Service.Status.Error")}: не выбрана модель. Выберите модель в списке на странице службы.");
            return false;
        }

        var profile = SelectedProfile;
        if (profile is null)
        {
            SetError($"{Loc.T("Service.Status.Error")}: не выбран профиль запуска. Выберите профиль для модели {model.Name}.");
            return false;
        }

        var runtime = SelectedRuntime;
        if (runtime is null)
        {
            SetError($"{Loc.T("Service.Status.Error")}: не выбрана среда выполнения. Выберите среду в списке на странице службы.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(runtime.ExecutablePath) || !File.Exists(runtime.ExecutablePath))
        {
            SetError($"{Loc.T("Service.Status.Error")}: llama-server не найден в выбранной среде выполнения: {runtime.ExecutablePath}");
            return false;
        }

        var args = _buildLaunchArgs(model, profile.Id, runtime);
        if (args is null || args.Count == 0)
        {
            SetError($"{Loc.T("Service.Status.Error")}: не удалось получить параметры запуска для модели {model.Name} с профилем {profile.Name}.");
            return false;
        }

        _configWriter.Write(runtime.ExecutablePath, args, model.Id, profile.Id, runtime.Id);
        return true;
    }

    private string FindLlamaServerPath()
    {
        var runtimesRoot = Path.Combine(AppContext.BaseDirectory, "data", "runtimes");
        if (!Directory.Exists(runtimesRoot)) return "";

        try
        {
            var candidate = Directory.EnumerateFiles(runtimesRoot, "llama-server.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return candidate ?? "";
        }
        catch
        {
            return "";
        }
    }

    private int GetLlamaServerPid()
    {
        try
        {
            return Process.GetProcessesByName("llama-server")
                .Select(p => p.Id)
                .FirstOrDefault();
        }
        catch
        {
            return 0;
        }
    }

    private void ApplyResult(LlamaServiceOperationResult result)
    {
        // При ошибке операции статус должен отражать реальное состояние,
        // а не зависать в переходном (Запуск/Остановка).
        if (result.Status != LlamaServiceStatus.Unknown)
            Status = result.Status;
        else if (!result.Success)
            Status = LlamaServiceStatus.Stopped;

        StatusText = result.Message;
        LastMessage = result.Message;
        HasError = !result.Success;

        if (!result.Success && result.Message.Contains("администратора", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"⚠️ {result.Message}";
            Status = LlamaServiceStatus.Stopped;

            // Предлагаем перезапустить приложение с правами администратора
            var answer = ThemedMessageBox.Show(
                Loc.T("Service.ElevationPrompt"),
                Loc.T("Service.PageTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
                ExecuteElevate(null);
            return;
        }

        ProcessIdText = result.Status == LlamaServiceStatus.Running && GetLlamaServerPid() > 0
            ? $"PID: {GetLlamaServerPid()}"
            : "PID: —";
    }

    private static void ExecuteElevate(object? param)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            };
            // Новый процесс стартует с флагом, который пропускает проверку
            // single-instance мутекса (текущий процесс ещё держит его при завершении).
            psi.ArgumentList.Add("--elevated-restart");

            System.Diagnostics.Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Ошибка повышения прав: {ex.Message}");
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

/// <summary>
/// Простая реализация ICommand для WPF.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
