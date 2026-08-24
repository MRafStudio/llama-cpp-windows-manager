using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
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
        public string AppAssetUrl = "";
        public string AppSha256Url = "";
        public string ServiceAssetUrl = "";
        public string ServiceSha256Url = "";
        public bool ShowHelp;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60) };
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

            if (string.IsNullOrWhiteSpace(options.AppAssetUrl) || string.IsNullOrWhiteSpace(options.TargetDir))
            {
                Log("Ошибка: не указаны --app-asset-url или --target-dir.");
                PrintHelp();
                return 3;
            }

            // 1. Повышение прав: нужно только если указана служба.
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

            // 2. Ждём закрытия GUI максимум 10 секунд, потом KILL.
            WaitForParentExit(options.ParentPid);

            var tempDir = Path.Combine(Path.GetTempPath(), "LlamaUpdater", options.Version);
            Directory.CreateDirectory(tempDir);

            // 3. Загрузка + проверка SHA-256 (уже скачано и совпадает — не качаем).
            var appTemp = DownloadVerifiedAsync(
                options.AppAssetUrl, options.AppSha256Url,
                Path.Combine(tempDir, "LlamaCppWindowsManager.exe"), "приложение").GetAwaiter().GetResult();

            string? serviceTemp = null;
            if (!string.IsNullOrWhiteSpace(options.ServiceAssetUrl) && !string.IsNullOrWhiteSpace(options.ServiceSha256Url))
            {
                serviceTemp = DownloadVerifiedAsync(
                    options.ServiceAssetUrl, options.ServiceSha256Url,
                    Path.Combine(tempDir, "LocalLlmConsole.Service.exe"), "служба").GetAwaiter().GetResult();
            }

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
                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    WorkingDirectory = options.TargetDir,
                    UseShellExecute = false
                });
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
                case "--app-asset-url" when i + 1 < args.Length: o.AppAssetUrl = args[++i]; break;
                case "--app-sha256-url" when i + 1 < args.Length: o.AppSha256Url = args[++i]; break;
                case "--service-asset-url" when i + 1 < args.Length: o.ServiceAssetUrl = args[++i]; break;
                case "--service-sha256-url" when i + 1 < args.Length: o.ServiceSha256Url = args[++i]; break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LocalLlmConsole.Updater — автономное обновление приложения и службы.
            Параметры:
              --version <v>              версия обновления (для каталога temp)
              --target-dir <path>        каталог установки (где лежит LlamaCppWindowsManager.exe)
              --service-name <name>      имя службы (если установлена; требует UAC)
              --parent-pid <pid>         PID приложения, которое нужно дождаться/убить
              --app-asset-url <url>      URL скачиваемого exe приложения
              --app-sha256-url <url>     URL файла .sha256 приложения
              --service-asset-url <url>  URL скачиваемого exe службы (необязательно)
              --service-sha256-url <url> URL файла .sha256 службы (необязательно)
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

    private static async Task<string> DownloadVerifiedAsync(string assetUrl, string sha256Url, string targetPath, string what)
    {
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            var expected = await FetchSha256Async(sha256Url);
            if (expected != null && Sha256Of(targetPath).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                Log($"{what}: уже скачано в temp, хэш совпадает — не качаю заново.");
                return targetPath;
            }
        }

        Log($"{what}: скачиваю {assetUrl}...");
        var bytes = await Http.GetByteArrayAsync(assetUrl);
        await File.WriteAllBytesAsync(targetPath, bytes);
        Log($"{what}: скачано {bytes.Length / 1024 / 1024} МБ.");

        var expectedHash = await FetchSha256Async(sha256Url);
        if (expectedHash == null) throw new InvalidOperationException($"Не удалось получить .sha256 для {what}.");
        var actualHash = Sha256Of(targetPath);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SHA-256 не совпадает для {what}: ожидалось {expectedHash}, получено {actualHash}. Обновление отменено.");

        Log($"{what}: SHA-256 подтверждён.");
        return targetPath;
    }

    private static async Task<string?> FetchSha256Async(string sha256Url)
    {
        try
        {
            var text = await Http.GetStringAsync(sha256Url);
            var match = System.Text.RegularExpressions.Regex.Match(text, @"[0-9a-fA-F]{64}");
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
