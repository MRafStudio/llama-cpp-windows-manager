namespace LocalLlmConsole.ViewModels;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LocalLlmConsole.Services;

/// <summary>
/// ViewModel для страницы управления службой llama-server.
/// </summary>
public sealed class LlamaServiceViewModel : INotifyPropertyChanged
{
    private readonly LlamaServiceController _controller;
    private readonly Action<string> _setStatus;
    private readonly Func<string, Task> _setStatusAsync;

    private LlamaServiceStatus _status;
    private string _statusText = string.Empty;
    private string _processIdText = string.Empty;
    private string _llamaServerPathText = string.Empty;
    private bool _isLoading;
    private bool _isElevated;

    /// <summary>
    /// Статус службы.
    /// </summary>
    public LlamaServiceStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                UpdateButtons();
            }
        }
    }

    /// <summary>
    /// Текстовое представление статуса.
    /// </summary>
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
    /// Текст с PID процесса.
    /// </summary>
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

    /// <summary>
    /// Путь к llama-server.
    /// </summary>
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

    /// <summary>
    /// Загружается ли статус.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Запущено ли приложение с правами администратора.
    /// </summary>
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
    /// Команда запуска службы.
    /// </summary>
    public ICommand StartCommand { get; }

    /// <summary>
    /// Команда остановки службы.
    /// </summary>
    public ICommand StopCommand { get; }

    /// <summary>
    /// Команда перезапуска службы.
    /// </summary>
    public ICommand RestartCommand { get; }

    /// <summary>
    /// Команда обновления статуса.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Команда перезапуска приложения с правами администратора.
    /// </summary>
    public ICommand ElevateCommand { get; }

    public LlamaServiceViewModel(
        LlamaServiceController controller,
        Action<string> setStatus,
        Func<string, Task> setStatusAsync)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _setStatusAsync = setStatusAsync ?? throw new ArgumentNullException(nameof(setStatusAsync));

        // Инициализация команд
        StartCommand = new RelayCommand(ExecuteStart, CanExecuteStart);
        StopCommand = new RelayCommand(ExecuteStop, CanExecuteStop);
        RestartCommand = new RelayCommand(ExecuteRestart, CanExecuteRestart);
        RefreshCommand = new RelayCommand(ExecuteRefresh, _ => true);
        ElevateCommand = new RelayCommand(ExecuteElevate, _ => true);

        // Подписка на события
        _controller.StatusChanged += OnStatusChanged;

        // Проверка прав
        IsElevated = IsAdministrator();

        // Загрузка начального статуса
        _ = LoadStatusAsync();
    }

    /// <summary>
    /// Загрузка начального статуса.
    /// </summary>
    private async Task LoadStatusAsync()
    {
        IsLoading = true;
        try
        {
            var result = _controller.CheckStatus();
            if (result.Success)
            {
                UpdateFromResult(result);
            }
            else
            {
                StatusText = $"Ошибка: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Обработчик события изменения статуса.
    /// </summary>
    private void OnStatusChanged(LlamaServiceStatus status, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Status = status;
            StatusText = message;
            ProcessIdText = _controller.CurrentProcessId > 0
                ? $"PID: {_controller.CurrentProcessId}"
                : "PID: —";
        });
    }

    /// <summary>
    /// Обновление UI из результата операции.
    /// </summary>
    private void UpdateFromResult(LlamaServiceOperationResult result)
    {
        Status = result.Status;
        StatusText = result.Message;
        ProcessIdText = _controller.CurrentProcessId > 0
            ? $"PID: {_controller.CurrentProcessId}"
            : "PID: —";
    }

    /// <summary>
    /// Обновление состояния кнопок.
    /// </summary>
    private void UpdateButtons()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Выполнение команды запуска.
    /// </summary>
    private void ExecuteStart(object? param)
    {
        if (IsLoading || Status == LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = "Запуск службы...";

        try
        {
            var result = _controller.Start();
            UpdateFromResult(result);

            if (!result.Success && result.Message.Contains("администратора", StringComparison.OrdinalIgnoreCase))
            {
                // Нужно предложить перезапуск с правами администратора
                StatusText = $"⚠️ {result.Message}";
                Status = LlamaServiceStatus.Stopped;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка запуска: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Проверка возможности запуска.
    /// </summary>
    private bool CanExecuteStart(object? param) => !IsLoading && Status != LlamaServiceStatus.Running;

    /// <summary>
    /// Выполнение команды остановки.
    /// </summary>
    private void ExecuteStop(object? param)
    {
        if (IsLoading || Status != LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = "Остановка службы...";
        try
        {
            var result = _controller.Stop();
            UpdateFromResult(result);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка остановки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Проверка возможности остановки.
    /// </summary>
    private bool CanExecuteStop(object? param) => !IsLoading && Status == LlamaServiceStatus.Running;

    /// <summary>
    /// Выполнение команды перезапуска.
    /// </summary>
    private void ExecuteRestart(object? param)
    {
        if (IsLoading || Status != LlamaServiceStatus.Running) return;

        IsLoading = true;
        StatusText = "Перезапуск службы...";
        try
        {
            var result = _controller.Restart();
            UpdateFromResult(result);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка перезапуска: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Проверка возможности перезапуска.
    /// </summary>
    private bool CanExecuteRestart(object? param) => !IsLoading && Status == LlamaServiceStatus.Running;

    /// <summary>
    /// Выполнение команды обновления статуса.
    /// </summary>
    private void ExecuteRefresh(object? param)
    {
        _ = LoadStatusAsync();
    }

    /// <summary>
    /// Выполнение команды повышения прав.
    /// </summary>
    private static void ExecuteElevate(object? param)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            System.Diagnostics.Process.Start(psi);
            // Закрываем текущий процесс
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Ошибка повышения прав: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверка, запущен ли процесс от имени администратора.
    /// </summary>
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

    public event EventHandler? CanExecuteChanged;
}
