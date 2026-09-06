using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace LocalLlmConsole.Updater;

/// <summary>
/// Автономный процесс обновления приложения и службы (по схеме: GUI качает ZIP
/// в Temp рядом с приложением и подменяет этот Updater свежим; затем запускает
/// его ОТДЕЛЬНЫМ процессом и закрывается). Здесь:
/// UAC (если есть служба) → ожидание/KILL всех LlamaCppWindowsManager.exe →
/// стоп службы → распаковка ZIP в каталог приложения (кроме самого себя) →
/// старт службы → автозапуск приложения. Пишет лог в {TargetDir}\LlamaUpdate\update.log.
/// </summary>
internal static class Program
{
    private const int MaxAppWaitSeconds = 5;
    private const int MaxFileReplaceSeconds = 60;  // сколько ждём освобождения занятого файла
    private const string AppProcessName = "LlamaCppWindowsManager";
    private const string UpdaterFileName = "LocalLlmConsole.Updater.exe";

    private sealed class Options
    {
        public string Version = "";
        public string TargetDir = "";
        public string ServiceName = "";
        public string UpdateZip = "";       // путь к скачанному GUI zip-архиву обновления (новый формат)
        public string AppFile = "";         // старый формат: путь к скачанному exe приложения
        public string ServiceFile = "";     // старый формат: путь к скачанному exe службы
        public int ParentPid;               // старый формат: PID приложения-родителя
        public bool ShowHelp;
    }

    private static string LogPath = "";

    private static string AssemblyVersion =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";

    private static int Main(string[] args)
    {
        var options = ParseArgs(args);
        var updateDir = string.IsNullOrWhiteSpace(options.TargetDir)
            ? Path.GetTempPath()
            : Path.Combine(options.TargetDir, "LlamaUpdate");
        LogPath = Path.Combine(updateDir, "update.log");
        try { Directory.CreateDirectory(updateDir); } catch { /* лог не критичен */ }
        Log($"Updater v{AssemblyVersion} started. Args: {string.Join(" ", args)}");

        try
        {
            if (options.ShowHelp) { PrintHelp(); return 0; }

            if (string.IsNullOrWhiteSpace(options.TargetDir))
            {
                Log("Ошибка: не указан --target-dir.");
                PrintHelp();
                return 3;
            }

            var useZipMode = !string.IsNullOrWhiteSpace(options.UpdateZip);
            if (!useZipMode && string.IsNullOrWhiteSpace(options.AppFile))
            {
                Log("Ошибка: не указаны --update-zip или --app-file.");
                PrintHelp();
                return 3;
            }

            // 1. Проверяем, что скачанные GUI файлы на месте.
            if (useZipMode)
            {
                if (!File.Exists(options.UpdateZip) || new FileInfo(options.UpdateZip).Length == 0)
                {
                    Log($"Ошибка: zip обновления не найден или пуст: {options.UpdateZip}");
                    return 4;
                }
            }
            else
            {
                if (!File.Exists(options.AppFile) || new FileInfo(options.AppFile).Length == 0)
                {
                    Log($"Ошибка: файл приложения не найден или пуст: {options.AppFile}");
                    return 4;
                }
                if (!string.IsNullOrWhiteSpace(options.ServiceFile)
                    && (!File.Exists(options.ServiceFile) || new FileInfo(options.ServiceFile).Length == 0))
                {
                    Log($"Ошибка: файл службы не найден или пуст: {options.ServiceFile}");
                    return 4;
                }
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

            // 3. Ждём полного закрытия приложения из этого каталога (5 сек), потом KILL.
            if (useZipMode)
            {
                KillRunningApps(options.TargetDir);
            }
            else
            {
                WaitForParentExit(options.ParentPid);
            }

            // 4. Останавливаем службу (если указана и запущена).
            var serviceWasRunning = false;
            if (!string.IsNullOrWhiteSpace(options.ServiceName))
            {
                serviceWasRunning = StopService(options.ServiceName);
            }

            // 5. Применяем обновление: zip-режим распаковывает архив (кроме себя),
            //    старый режим заменяет отдельные файлы.
            if (useZipMode)
            {
                ExtractUpdate(options.UpdateZip, options.TargetDir);
            }
            else
            {
                ReplaceFile(options.AppFile, Path.Combine(options.TargetDir, "LlamaCppWindowsManager.exe"), "приложение");
                if (!string.IsNullOrWhiteSpace(options.ServiceFile))
                {
                    ReplaceFile(options.ServiceFile, Path.Combine(options.TargetDir, "LocalLlmConsole.Service.exe"), "служба");
                }
            }

            // 6. Запускаем службу (если она была запущена до обновления).
            if (serviceWasRunning)
            {
                StartService(options.ServiceName);
            }

            // 7. Запускаем приложение (только если это настоящий exe).
            var appPath = Path.Combine(options.TargetDir, "LlamaCppWindowsManager.exe");
            if (File.Exists(appPath) && LooksLikeExecutable(appPath))
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
                case "--update-zip" when i + 1 < args.Length: o.UpdateZip = args[++i]; break;
                case "--app-file" when i + 1 < args.Length: o.AppFile = args[++i]; break;
                case "--service-file" when i + 1 < args.Length: o.ServiceFile = args[++i]; break;
                case "--parent-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid): o.ParentPid = pid; break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LocalLlmConsole.Updater — автономное применение скачанного обновления.
            Параметры (новый формат — zip):
              --version <v>              версия обновления (информационно)
              --target-dir <path>        каталог установки (где лежит LlamaCppWindowsManager.exe)
              --service-name <name>      имя службы (если установлена; требует UAC)
              --update-zip <path>        путь к скачанному и проверенному zip-архиву обновления
            Параметры (старый формат — отдельные exe):
              --app-file <path>          путь к скачанному и проверенному exe приложения
              --service-file <path>      путь к скачанному и проверенному exe службы (необязательно)
              --parent-pid <pid>         PID приложения, которое нужно дождаться/убить
            """);
    }

    /// <summary>
    /// Проверяет, что файл — настоящий PE-исполняемый (сигнатура MZ),
    /// а не текстовый мусор (защита от случайного запуска).
    /// </summary>
    private static bool LooksLikeExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 2) return false;
            var header = new byte[2];
            stream.ReadExactly(header, 0, 2);
            return header[0] == (byte)'M' && header[1] == (byte)'Z';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Старый формат: ждём закрытия родительского GUI (10 сек), потом KILL.
    /// </summary>
    private static void WaitForParentExit(int parentPid)
    {
        const int maxWaitSeconds = 10;
        if (parentPid <= 0) return;
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        Process? parent = null;
        try { parent = Process.GetProcessById(parentPid); }
        catch
        {
            Log($"GUI (PID {parentPid}) уже закрыт — ждать не нужно.");
            return;
        }

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
                    Log($"GUI не закрылся за {maxWaitSeconds} сек — принудительно KILL PID {parentPid}.");
                    parent.Kill(entireProcessTree: true);
                    parent.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Log($"Не удалось убить процесс {parentPid}: {ex.Message}");
            }
            finally
            {
                parent.Dispose();
            }
        }
    }

    /// <summary>
    /// Старый формат: заменяет один файл, ожидая освобождения занятого.
    /// </summary>
    private static void ReplaceFile(string source, string target, string what)
    {
        if (!File.Exists(target))
        {
            Log($"{what}: целевой файл отсутствует — просто копирую ({target}).");
            File.Copy(source, target, overwrite: true);
            return;
        }

        var backup = target + ".bak";
        var deadline = DateTime.UtcNow.AddSeconds(MaxFileReplaceSeconds);
        while (true)
        {
            try
            {
                Log($"{what}: заменяю {target}...");
                File.Replace(source, target, backup, ignoreMetadataErrors: true);
                Log($"{what}: файл заменён.");
                return;
            }
            catch (IOException ex)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Log($"{what}: файл так и не освободился за {MaxFileReplaceSeconds} сек: {ex.Message}");
                    throw;
                }
                Log($"{what}: файл занят ({ex.Message}) — жду освобождения...");
                Thread.Sleep(1000);
            }
            catch
            {
                // File.Replace не сработал (не IOException) — копируем поверх.
                File.Copy(source, target, overwrite: true);
                Log($"{what}: файл заменён (копированием).");
                return;
            }
        }
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
                Verb = "runas"
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            return Process.Start(psi) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ждёт полного закрытия ТОЛЬКО того экземпляра LlamaCppWindowsManager.exe,
    /// который лежит в целевом каталоге (рядом с этим Updater). Другие копии
    /// приложения из других каталогов не трогаем — их обновление не касается.
    /// Максимум <see cref="MaxAppWaitSeconds"/> сек, затем принудительно убиваем.
    /// </summary>
    private static void KillRunningApps(string targetDir)
    {
        var targetAppPath = Path.Combine(targetDir, "LlamaCppWindowsManager.exe");
        var deadline = DateTime.UtcNow.AddSeconds(MaxAppWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var ours = FindOwnedAppProcesses(targetAppPath);
            if (ours.Count == 0) { Log("Экземпляр приложения из этого каталога закрыт."); return; }
            foreach (var p in ours) p.Dispose();
            Thread.Sleep(250);
        }

        var leftovers = FindOwnedAppProcesses(targetAppPath);
        foreach (var process in leftovers)
        {
            try
            {
                Log($"Приложение из этого каталога не закрылось за {MaxAppWaitSeconds} сек — принудительно KILL PID {process.Id}.");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Log($"Не удалось убить процесс {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
        // Даём ОС освободить образ exe после убийства процессов.
        Thread.Sleep(1500);
    }

    /// <summary>
    /// Ищет процессы LlamaCppWindowsManager.exe, чей исполняемый файл
    /// находится в целевом каталоге (MainModule.FileName совпадает с путём).
    /// Процессы из других каталогов игнорируются.
    /// </summary>
    private static List<Process> FindOwnedAppProcesses(string targetAppPath)
    {
        var owned = new List<Process>();
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            try
            {
                var exePath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath)
                    && string.Equals(exePath, targetAppPath, StringComparison.OrdinalIgnoreCase))
                {
                    owned.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                // Чужой процесс (нет доступа к MainModule) — не наш, пропускаем.
                process.Dispose();
            }
        }
        return owned;
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

    /// <summary>
    /// Распаковывает zip обновления в каталог приложения, пропуская
    /// LocalLlmConsole.Updater.exe (самого себя — файл занят запущенным
    /// процессом; свежую версию Updater кладёт GUI перед запуском).
    /// Занятые файлы ждём до <see cref="MaxFileReplaceSeconds"/> сек.
    /// </summary>
    private static void ExtractUpdate(string zipPath, string targetDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // папка
            if (entry.Name.Equals(UpdaterFileName, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Пропускаю {entry.Name} (сам Updater — свежий уже положен GUI).");
                continue;
            }

            var targetPath = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            CopyEntryWithRetry(entry, targetPath);
        }
    }

    private static void CopyEntryWithRetry(ZipArchiveEntry entry, string targetPath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(MaxFileReplaceSeconds);
        while (true)
        {
            try
            {
                Log($"копирую {entry.Name} → {targetPath}...");
                entry.ExtractToFile(targetPath, overwrite: true);
                Log($"{entry.Name}: файл обновлён.");
                return;
            }
            catch (IOException ex)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Log($"{entry.Name}: файл так и не освободился за {MaxFileReplaceSeconds} сек: {ex.Message}");
                    throw;
                }
                Log($"{entry.Name}: файл занят ({ex.Message}) — жду освобождения...");
                Thread.Sleep(1000);
            }
        }
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
