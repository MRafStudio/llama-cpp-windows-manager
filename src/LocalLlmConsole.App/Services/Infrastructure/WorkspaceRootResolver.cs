namespace LocalLlmConsole.Services;

/// <summary>
/// Workspace — всегда каталог, из которого запущен LlamaCppWindowsManager.exe.
/// Переменные окружения и %LocalAppData% игнорируются: несколько копий
/// (GPU/CPU из разных папок) не должны схлопываться в один путь.
/// </summary>
public static class WorkspaceRootResolver
{
    public static string Resolve()
        => Resolve(Environment.ProcessPath);

    public static string Resolve(string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var full = Path.GetFullPath(executablePath);
            if (File.Exists(full) || Path.HasExtension(full))
            {
                var directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(directory))
                    return Path.GetFullPath(directory);
            }

            if (Directory.Exists(full))
                return full;
        }

        var baseDirectory = AppContext.BaseDirectory;
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.GetFullPath(Directory.GetCurrentDirectory())
            : Path.GetFullPath(baseDirectory);
    }
}
