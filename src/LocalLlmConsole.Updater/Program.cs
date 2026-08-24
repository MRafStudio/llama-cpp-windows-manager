using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace LocalLlmConsole.Updater;

/// <summary>
/// Автономный процесс обновления приложения и службы.
/// Запускается GUI (приложение сразу закрывается), делает всё сам:
/// UAC-повышение (если есть служба) → KILL родителя через 10 сек →
/// загрузка в %TEMP% → проверка SHA-256 → стоп службы → замена файлов →
/// старт службы → запуск приложения. Пишет лог в %TEMP%\LlamaUpdater\update.log.
/// </summary>
internal static class Program
{
    private const int MaxParentWaitSeconds = 10;

    private sealed class Options
    {
        public string Version = "";
        public string TargetDir = "";
        public string ServiceName = "";
        public int ParentPid;
        public string AppFile = "";        // путь к УЖЕ скачанному GUI проверенному exe
        public string ServiceFile = "";    // путь к УЖЕ скачанному проверенному exe службы (если есть)
        public bool ShowHelp;
    }

    private static string LogPath = "";
    private static int Main(string[] args)
    {
        var options = ParseArgs(args);
        LogPath = Path.Combine(Path.GetTempPath(), "LlamaUpdater", "update.log");
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Log($"Updater v2.3.2.2 started. Args: {string.Join(" ", args)}");

        try
        {
            if (options.ShowHelp) { PrintHelp(); return 0; }

            if (string.IsNullOrWhiteSpace(options.AppFile) || string.IsNullOrWhiteSpace(options.TargetDir))
            {
                Log("Ошибка: не указаны --app-file или --target-dir.");
                PrintHelp();
                return 3;
            }

            // 1. Проверяем, что скачанные GUI файлы на месте.
            var appTemp = options.AppFile;
            if (!File.Exists(appTemp) || new FileInfo(appTemp).Length == 0)
            {
                Log($"Ошибка: файл приложения не найден или пуст: {appTemp}");
                return 4;
            }
            string? serviceTemp = null;
            if (!string.IsNullOrWhiteSpace(options.ServiceFile))
            {
                if (!File.Exists(options.ServiceFile) || new FileInfo(options.ServiceFile).Length == 0)
                {
                    Log($"Ошибка: файл службы не найден или пуст: {options.ServiceFile}");
                    return 4;
                }
                serviceTemp = options.ServiceFile;
            }

            // 2. Повышение прав: нужно только если указана служба.
            if (!string.IsNullOrWhiteSpace(options.ServiceName) && !IsAdministrator())
            {
                Log($"Требуется повышение прав (служба {options.ServiceName}). Запускаю UAC...");
                if (!TryElevate(args))
                {
                    Log("UAC отклонён — обновление отменено, ничего не менялось.");
                    return 2;
                }
                Log("UAC подтверждён, elevated-копия запущена. Этот процесс завершается.");
                return 0;
            }

            // 3. Ждём закрытия GUI максимум 10 секунд, потом KILL.
            WaitForParentExit(options.ParentPid);

            // 4. Останавливаем службу (если указана и запущена).
            var serviceWasRunning = false;
            if (!string.IsNullOrWhiteSpace(options.ServiceName))
            {
                serviceWasRunning = StopService(options.ServiceName);
            }

            // 5. Заменяем файлы (App + Service).
            ReplaceFile(appTemp, Path.Combine(options.TargetDir, "LlamaCppWindowsManager.exe"), "приложение");
            if (serviceTemp != null)
            {
                ReplaceFile(serviceTemp, Path.Combine(options.TargetDir, "LocalLlmConsole.Service.exe"), "служба");
            }

            // 6. Запускаем службу (если она была запущена до обновления).
            if (serviceWasRunning)
            {
                StartService(options.ServiceName);
            }

            // 7. Запускаем приложение.
            var appPath = Path.Combine(options.TargetDir, "LlamaCppWindowsManager.exe");
            if (File.Exists(appPath))
            {
                Log($"Запускаю приложение: {appPath}");
                // Запуск через explorer.exe: (1) снимает админ-токен (приложение не
                // должно работать от имени администратора), (2) окно появляется на
                // переднем плане как при обычном запуске пользователем.
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{appPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"Запуск через explorer не удался ({ex.Message}) — запускаю напрямую.");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = appPath,
                        WorkingDirectory = options.TargetDir,
                        UseShellExecute = true
                    });
                }
            }

            Log("Обновление завершено успешно.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"Updater FAILED: {ex}");
            return 1;
        }
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help": o.ShowHelp = true; break;
                case "--version" when i + 1 < args.Length: o.Version = args[++i]; break;
                case "--target-dir" when i + 1 < args.Length: o.TargetDir = args[++i]; break;
                case "--service-name" when i + 1 < args.Length: o.ServiceName = args[++i]; break;
                case "--parent-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid): o.ParentPid = pid; break;
                case "--app-file" when i + 1 < args.Length: o.AppFile = args[++i]; break;
                case "--service-file" when i + 1 < args.Length: o.ServiceFile = args[++i]; break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LocalLlmConsole.Updater — автономное применение скачанного обновления.
            Параметры:
              --version <v>              версия обновления (информационно)
              --target-dir <path>        каталог установки (где лежит LlamaCppWindowsManager.exe)
              --service-name <name>      имя службы (если установлена; требует UAC)
              --parent-pid <pid>         PID приложения, которое нужно дождаться/убить
              --app-file <path>          путь к скачанному и проверенному exe приложения
              --service-file <path>      путь к скачанному и проверенному exe службы (необязательно)
            """);
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryElevate(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
            };
            return Process.Start(psi) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void WaitForParentExit(int parentPid)
    {
        if (parentPid <= 0) return;
        var deadline = DateTime.UtcNow.AddSeconds(MaxParentWaitSeconds);
        Process? parent = null;
        try { parent = Process.GetProcessById(parentPid); } catch { /* уже закрыт */ }

        while (parent != null && DateTime.UtcNow < deadline)
        {
            parent.Refresh();
            if (parent.HasExited) { Log("GUI закрылся сам — ждать не нужно."); return; }
            Thread.Sleep(250);
        }

        if (parent != null)
        {
            try
            {
                parent.Refresh();
                if (!parent.HasExited)
                {
                    Log($"GUI не закрылся за {MaxParentWaitSeconds} сек — принудительно KILL PID {parentPid}.");
                    parent.Kill(entireProcessTree: true);
                    parent.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Log($"Не удалось убить процесс {parentPid}: {ex.Message}");
            }
        }
    }

    private static bool StopService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending)
            {
                Log($"Служба {serviceName} уже остановлена.");
                return false;
            }
            Log($"Останавливаю службу {serviceName}...");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            Log($"Служба {serviceName} остановлена.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Не удалось остановить службу {serviceName}: {ex.Message}");
            return false;
        }
    }

    private static void StartService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running) return;
            Log($"Запускаю службу {serviceName}...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Log($"Служба {serviceName} запущена.");
        }
        catch (Exception ex)
        {
            Log($"Не удалось запустить службу {serviceName}: {ex.Message}");
        }
    }

    private static void ReplaceFile(string source, string target, string what)
    {
        if (!File.Exists(target))
        {
            Log($"{what}: целевой файл отсутствует — просто копирую ({target}).");
            File.Copy(source, target, overwrite: true);
            return;
        }

        var backup = target + ".bak";
        try
        {
            Log($"{what}: заменяю {target}...");
            File.Replace(source, target, backup, ignoreMetadataErrors: true);
        }
        catch
        {
            // File.Replace не сработал (антивирус/блокировка) — копируем поверх.
            File.Copy(source, target, overwrite: true);
        }
        Log($"{what}: файл заменён.");
    }

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* лог — не критично */ }
    }
}
