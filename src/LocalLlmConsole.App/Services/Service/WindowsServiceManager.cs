using System.ServiceProcess;

namespace LocalLlmConsole.Services;

/// <summary>
/// Управление Windows-службой llama-server через штатный
/// <see cref="System.ServiceProcess.ServiceController"/> (без сторонних обёрток).
/// </summary>
public sealed class WindowsServiceManager
{
    public const string ServiceName = "llama-cpp-server";

    public bool IsInstalled()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            _ = controller.Status; // бросает InvalidOperationException, если службы нет
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public LlamaServiceStatus GetStatus()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => LlamaServiceStatus.Running,
                ServiceControllerStatus.Stopped => LlamaServiceStatus.Stopped,
                ServiceControllerStatus.StartPending => LlamaServiceStatus.Starting,
                ServiceControllerStatus.StopPending => LlamaServiceStatus.Stopping,
                _ => LlamaServiceStatus.Unknown
            };
        }
        catch
        {
            return LlamaServiceStatus.Unknown;
        }
    }

    public LlamaServiceOperationResult Start()
    {
        if (!IsAdministrator())
            return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для управления службой. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Stopped);

        if (!IsInstalled())
            return new LlamaServiceOperationResult(false, "Служба не установлена. Сначала нажмите 'Установить службу'.", LlamaServiceStatus.Stopped);

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status == ServiceControllerStatus.Running)
                return new LlamaServiceOperationResult(true, "Служба уже запущена.", LlamaServiceStatus.Running);

            controller.Start();
            // Служба стартует почти мгновенно (SCM); при падении не ждём 30 секунд.
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
            return new LlamaServiceOperationResult(true, $"Служба {ServiceName} запущена.", LlamaServiceStatus.Running);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return new LlamaServiceOperationResult(false, "Доступ запрещён. Требуется перезапуск от имени администратора.", LlamaServiceStatus.Stopped);
        }
        catch (Exception ex)
        {
            // Служба не запустилась — статус должен быть Stopped, а не Starting
            return new LlamaServiceOperationResult(false, $"Ошибка запуска службы: {ex.Message}", LlamaServiceStatus.Stopped);
        }
    }

    public LlamaServiceOperationResult Stop()
    {
        if (!IsAdministrator())
            return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для управления службой. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Running);

        if (!IsInstalled())
            return new LlamaServiceOperationResult(true, "Служба не установлена.", LlamaServiceStatus.Stopped);

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status == ServiceControllerStatus.Stopped)
                return new LlamaServiceOperationResult(true, "Служба уже остановлена.", LlamaServiceStatus.Stopped);

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
            return new LlamaServiceOperationResult(true, $"Служба {ServiceName} остановлена.", LlamaServiceStatus.Stopped);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return new LlamaServiceOperationResult(false, "Доступ запрещён. Требуется перезапуск от имени администратора.", LlamaServiceStatus.Running);
        }
        catch (Exception ex)
        {
            return new LlamaServiceOperationResult(false, $"Ошибка остановки службы: {ex.Message}", LlamaServiceStatus.Running);
        }
    }

    public LlamaServiceOperationResult Restart()
    {
        if (!IsAdministrator())
            return new LlamaServiceOperationResult(false, "Требуется запуск от имени администратора для управления службой. Перезапустите приложение с правами администратора.", LlamaServiceStatus.Running);

        if (!IsInstalled())
            return new LlamaServiceOperationResult(false, "Служба не установлена. Сначала нажмите 'Установить службу'.", LlamaServiceStatus.Stopped);

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            return new LlamaServiceOperationResult(true, $"Служба {ServiceName} перезапущена.", LlamaServiceStatus.Running);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return new LlamaServiceOperationResult(false, "Доступ запрещён. Требуется перезапуск от имени администратора.", LlamaServiceStatus.Running);
        }
        catch (Exception ex)
        {
            return new LlamaServiceOperationResult(false, $"Ошибка перезапуска службы: {ex.Message}", LlamaServiceStatus.Unknown);
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
}
