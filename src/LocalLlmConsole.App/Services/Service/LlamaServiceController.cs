namespace LocalLlmConsole.Services;

/// <summary>
/// Результат операции управления службой.
/// </summary>
public sealed record LlamaServiceOperationResult(
    bool Success,
    string Message,
    LlamaServiceStatus Status = LlamaServiceStatus.Unknown);

/// <summary>
/// Статус службы.
/// </summary>
public enum LlamaServiceStatus
{
    /// <summary>
    /// Статус неизвестен (не удалось определить).
    /// </summary>
    Unknown,
    /// <summary>
    /// Служба остановлена.
    /// </summary>
    Stopped,
    /// <summary>
    /// Служба запущена и работает.
    /// </summary>
    Running,
    /// <summary>
    /// Служба в процессе запуска.
    /// </summary>
    Starting,
    /// <summary>
    /// Служба в процессе остановки.
    /// </summary>
    Stopping
}

/// <summary>
/// Контроллер управления службой llama-server.
/// Обеспечивает запуск, остановку и мониторинг llama-server как Windows-службы.
/// </summary>
public sealed class LlamaServiceController : IDisposable
{
    private const string ServiceName = "llama-cpp-server";
    private const string ServiceDisplayName = "llama.cpp Server Service";
    private const string ServiceDescription = "llama.cpp server running as a Windows service. Manages model serving independently of the desktop application.";

    private readonly IProcessRunner _processRunner;
    private readonly string _workspaceRoot;
    private readonly string _llamaServerPath;
    private readonly string _logPath;

    private System.Timers.Timer? _healthCheckTimer;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private volatile bool _isRunning = false;
    private volatile bool _disposed = false;
    private readonly object _lock = new object();

    /// <summary>
    /// Делегат события изменения статуса службы.
    /// </summary>
    public event Action<LlamaServiceStatus, string>? StatusChanged;

    /// <summary>
    /// Текущий статус службы.
    /// </summary>
    public LlamaServiceStatus CurrentStatus
    {
        get
        {
            lock (_lock)
                return _isRunning ? LlamaServiceStatus.Running : LlamaServiceStatus.Stopped;
        }
    }

    /// <summary>
    /// PID запущенного процесса llama-server.
    /// </summary>
    public int CurrentProcessId { get; private set; }

    public LlamaServiceController(
        IProcessRunner processRunner,
        string workspaceRoot,
        string llamaServerPath,
        string logPath)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
        _llamaServerPath = llamaServerPath ?? throw new ArgumentNullException(nameof(llamaServerPath));
        _logPath = logPath ?? throw new ArgumentNullException(nameof(logPath));

        // Настройка health-check таймера
        _healthCheckTimer = new System.Timers.Timer(10000); // Каждые 10 секунд
        _healthCheckTimer.AutoReset = true;
        _healthCheckTimer.Elapsed += OnHealthCheckElapsed;
    }

    /// <summary>
    /// Запуск службы.
    /// </summary>
    public LlamaServiceOperationResult Start()
    {
        lock (_lock)
        {
            if (_disposed)
                return new LlamaServiceOperationResult(false, "Контроллер был уничтожен.");

            // Проверяем, не запущена ли уже служба
            if (_isRunning)
            {
                return new LlamaServiceOperationResult(false, "Служба уже запущена.", LlamaServiceStatus.Running);
            }

            // Проверяем, не запущен ли llama-server другим процессом
            if (IsLlamaServerProcessRunning())
            {
                _isRunning = true;
                CurrentProcessId = GetLlamaServerPid();
                return new LlamaServiceOperationResult(true, "llama-server уже запущен (обнаружен существующий процесс).", LlamaServiceStatus.Running);
            }

            // Проверяем права администратора
            if (!IsAdministrator())
            {
                // Возвращаем специальный код, чтобы GUI мог предложить перезапуск
                return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для управления службой. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Stopped);
            }

            // Запускаем llama-server
            try
            {
                StartLlamaServer();
                _isRunning = true;
                _healthCheckTimer?.Start();

                StatusChanged?.Invoke(LlamaServiceStatus.Running, $"llama-server запущен (PID: {CurrentProcessId})");
                return new LlamaServiceOperationResult(true, $"llama-server запущен (PID: {CurrentProcessId}).", LlamaServiceStatus.Running);
            }
            catch (InvalidOperationException ex)
            {
                return new LlamaServiceOperationResult(false, $"Ошибка запуска: {ex.Message}", LlamaServiceStatus.Stopped);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return new LlamaServiceOperationResult(false, "Доступ запрещён. Требуется перезапуск от имени администратора.", LlamaServiceStatus.Stopped);
            }
            catch (Exception ex)
            {
                return new LlamaServiceOperationResult(false, $"Неожиданная ошибка: {ex.Message}", LlamaServiceStatus.Stopped);
            }
        }
    }

    /// <summary>
    /// Остановка службы.
    /// </summary>
    public LlamaServiceOperationResult Stop()
    {
        lock (_lock)
        {
            if (_disposed)
                return new LlamaServiceOperationResult(false, "Контроллер был уничтожен.");

            if (!_isRunning)
            {
                return new LlamaServiceOperationResult(true, "Служба уже остановлена.", LlamaServiceStatus.Stopped);
            }

            try
            {
                StopLlamaServer();
                _isRunning = false;
                _healthCheckTimer?.Stop();
                CurrentProcessId = 0;

                StatusChanged?.Invoke(LlamaServiceStatus.Stopped, "llama-server остановлен.");
                return new LlamaServiceOperationResult(true, "llama-server остановлен.", LlamaServiceStatus.Stopped);
            }
            catch (Exception ex)
            {
                return new LlamaServiceOperationResult(false, $"Ошибка остановки: {ex.Message}", LlamaServiceStatus.Running);
            }
        }
    }

    /// <summary>
    /// Перезапуск службы (стоп + старт).
    /// </summary>
    public LlamaServiceOperationResult Restart()
    {
        lock (_lock)
        {
            if (_disposed)
                return new LlamaServiceOperationResult(false, "Контроллер был уничтожен.");

            // Сначала останавливаем
            var stopResult = Stop();
            if (!stopResult.Success)
            {
                return new LlamaServiceOperationResult(false, $"Ошибка при остановке: {stopResult.Message}", CurrentStatus);
            }

            // Небольшая задержка для очистки ресурсов
            System.Threading.Thread.Sleep(500);

            // Запускаем заново
            var startResult = Start();
            return startResult;
        }
    }

    /// <summary>
    /// Проверка статуса службы.
    /// </summary>
    public LlamaServiceOperationResult CheckStatus()
    {
        if (_disposed)
            return new LlamaServiceOperationResult(false, "Контроллер был уничтожен.");

        try
        {
            var isRunning = IsLlamaServerProcessRunning();

            if (isRunning)
            {
                _isRunning = true;
                CurrentProcessId = GetLlamaServerPid();
                return new LlamaServiceOperationResult(true, $"llama-server запущен (PID: {CurrentProcessId}).", LlamaServiceStatus.Running);
            }
            else
            {
                _isRunning = false;
                CurrentProcessId = 0;
                return new LlamaServiceOperationResult(true, "llama-server остановлен.", LlamaServiceStatus.Stopped);
            }
        }
        catch (Exception ex)
        {
            return new LlamaServiceOperationResult(false, $"Ошибка проверки статуса: {ex.Message}", LlamaServiceStatus.Unknown);
        }
    }

    /// <summary>
    /// Запуск llama-server.
    /// </summary>
    private void StartLlamaServer()
    {
        if (!File.Exists(_llamaServerPath))
            throw new FileNotFoundException($"llama-server не найден: {_llamaServerPath}");

        // Создаём папку для логов
        var logDir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(logDir))
            Directory.CreateDirectory(logDir);

        // Запускаем процесс
        var psi = new ProcessStartInfo
        {
            FileName = _llamaServerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_llamaServerPath) ?? Environment.CurrentDirectory
        };

        // Читаем параметры из конфига (будет переопределено при загрузке модели)
        // Пока запускаем с минимальными параметрами
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add("8081");

        // Запускаем
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                LogOutput(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                LogError(e.Data);
            }
        };

        process.Exited += (_, _) =>
        {
            lock (_lock)
            {
                if (!_disposed && _isRunning)
                {
                    // Процесс завершился — служба пытается перезапустить
                    var exitCode = process.ExitCode;
                    StatusChanged?.Invoke(LlamaServiceStatus.Stopped, $"llama-server завершился с кодом {exitCode}. Перезапуск...");
                    _isRunning = false;

                    // Пытаемся перезапустить через 2 секунды
                    Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        if (!_disposed && !_isRunning)
                        {
                            Start();
                        }
                    });
                }
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить llama-server.");

        CurrentProcessId = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// Остановка llama-server.
    /// </summary>
    private void StopLlamaServer()
    {
        try
        {
            if (CurrentProcessId > 0)
            {
                var process = Process.GetProcessById(CurrentProcessId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Не удалось остановить llama-server: {ex.Message}");
        }

        // Также убиваем все оставшиеся процессы llama-server
        try
        {
            foreach (var proc in Process.GetProcessesByName("llama-server"))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1000);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Проверка, запущен ли llama-server.
    /// </summary>
    private bool IsLlamaServerProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("llama-server").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Получение PID запущенного llama-server.
    /// </summary>
    private int GetLlamaServerPid()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("llama-server"))
            {
                return proc.Id;
            }
        }
        catch { }
        return 0;
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

    /// <summary>
    /// Периодическая проверка здоровья (health check).
    /// </summary>
    private void OnHealthCheckElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var isRunning = IsLlamaServerProcessRunning();

            if (!isRunning && _isRunning)
            {
                // Процесс упал — пытаемся перезапустить
                StatusChanged?.Invoke(LlamaServiceStatus.Stopped, "llama-server остановлен. Перезапуск...");
                _isRunning = false;

                // Перезапускаем через 2 секунды
                Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    if (!_disposed && !_isRunning)
                    {
                        Start();
                    }
                });
            }
            else if (isRunning && !_isRunning)
            {
                // Процесс найден, но мы думали что остановлен
                _isRunning = true;
                CurrentProcessId = GetLlamaServerPid();
                StatusChanged?.Invoke(LlamaServiceStatus.Running, $"llama-server найден (PID: {CurrentProcessId}).");
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Ошибка health check: {ex.Message}");
        }
    }

    /// <summary>
    /// Логирование вывода llama-server.
    /// </summary>
    private void LogOutput(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{timestamp}] OUT: {message}{Environment.NewLine}";
            File.AppendAllText(_logPath, line, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>
    /// Логирование ошибок llama-server.
    /// </summary>
    private void LogError(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{timestamp}] ERR: {message}{Environment.NewLine}";
            File.AppendAllText(_logPath, line, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>
    /// Освобождение ресурсов.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        // Останавливаем процесс при уничтожении контроллера
        Stop();
    }
}
