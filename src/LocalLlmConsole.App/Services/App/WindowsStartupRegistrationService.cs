namespace LocalLlmConsole.Services;

using System.Globalization;

public sealed record WindowsStartupRegistrationResult(
    bool Success,
    string StatusMessage);

public sealed class WindowsStartupRegistrationService
{
    public const string StartupValueName = "LlamaCppWindowsManager";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Func<string?> _readStartupCommand;
    private readonly Action<string> _writeStartupCommand;
    private readonly Action _deleteStartupCommand;
    private readonly Func<string> _executablePath;

    // Маппинг системных локалей на доступные языки приложения
    private static readonly Dictionary<string, string> _cultureToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ru", "ru" }, { "uk", "ru" }, { "be", "ru" }, // Славянские → русский
        { "en", "en" }, { "de", "de" }, { "fr", "fr" },
        { "es", "es" }, { "pt", "pt" }, { "it", "it" },
        { "ja", "ja" }, { "ko", "ko" }, { "zh", "zh" },
        { "tr", "tr" }, { "fa", "fa" }, { "pl", "pl" },
        { "nl", "nl" }, { "vi", "vi" }, { "ar", "ar" },
        { "hi", "hi" }, { "id", "id" }, { "sv", "sv" },
        { "bg", "bg" }, { "cs", "cs" }
    };

    public WindowsStartupRegistrationService()
        : this(
            ReadRegistryStartupCommand,
            WriteRegistryStartupCommand,
            DeleteRegistryStartupCommand,
            CurrentExecutablePath)
    {
    }

    public WindowsStartupRegistrationService(
        Func<string?> readStartupCommand,
        Action<string> writeStartupCommand,
        Action deleteStartupCommand,
        Func<string> executablePath)
    {
        _readStartupCommand = readStartupCommand ?? throw new ArgumentNullException(nameof(readStartupCommand));
        _writeStartupCommand = writeStartupCommand ?? throw new ArgumentNullException(nameof(writeStartupCommand));
        _deleteStartupCommand = deleteStartupCommand ?? throw new ArgumentNullException(nameof(deleteStartupCommand));
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    public AppSettings Reconcile(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var updated = settings with { StartWithWindows = IsEnabled() };

            // Если язык явно не установлен (дефолтный "en"), попробуем определить системный
            if (!string.IsNullOrWhiteSpace(settings.UiCulture) &&
                string.Equals(settings.UiCulture, "en", StringComparison.OrdinalIgnoreCase))
            {
                var systemLang = ResolveSystemLanguage();
                if (!string.Equals(systemLang, "en", StringComparison.OrdinalIgnoreCase))
                {
                    updated = updated with { UiCulture = systemLang };
                }
            }

            return updated;
        }
        catch
        {
            return settings;
        }
    }

    /// <summary>Определяет язык системы и маппит на доступный язык приложения.</summary>
    private static string ResolveSystemLanguage()
    {
        try
        {
            // 1. Текущий UI культуры Windows
            var culture = CultureInfo.CurrentUICulture;
            var cultureName = culture.Name; // "ru-RU", "en-US", etc.
            var twoLetter = culture.TwoLetterISOLanguageName.ToLowerInvariant(); // "ru", "en", etc.

            // Сначала ищем по полному имени, потом по двухбуквенному коду
            if (_cultureToLanguage.TryGetValue(cultureName, out var lang))
                return lang;
            if (_cultureToLanguage.TryGetValue(twoLetter, out lang))
                return lang;

            // Для китайского: проверяем регион
            if (twoLetter == "zh")
            {
                var region = culture.TwoLetterISOLanguageName;
                if (cultureName.Contains("CN") || cultureName.Contains("TW") || cultureName.Contains("HK"))
                    return "zh";
            }

            // fallback — English
            return "en";
        }
        catch
        {
            return "en";
        }
    }

    public bool IsEnabled()
        => !string.IsNullOrWhiteSpace(_readStartupCommand());

    public WindowsStartupRegistrationResult Apply(bool startWithWindows)
    {
        try
        {
            if (startWithWindows)
                _writeStartupCommand(StartupCommand());
            else
                _deleteStartupCommand();

            return new WindowsStartupRegistrationResult(true, "");
        }
        catch (Exception ex)
        {
            return new WindowsStartupRegistrationResult(
                false,
                $"Start with Windows could not be updated: {ex.Message}");
        }
    }

    public string StartupCommand()
    {
        var path = _executablePath();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Application executable path could not be resolved.");

        return StartupCommandForExecutable(path);
    }

    public static string StartupCommandForExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is required.", nameof(executablePath));

        return $"\"{executablePath.Replace("\"", "", StringComparison.Ordinal)}\"";
    }

    private static string? ReadRegistryStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(StartupValueName) as string;
    }

    private static void WriteRegistryStartupCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup registry key could not be opened.");
        key.SetValue(StartupValueName, command, RegistryValueKind.String);
    }

    private static void DeleteRegistryStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(StartupValueName, throwOnMissingValue: false);
    }

    private static string CurrentExecutablePath()
        => Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
}
