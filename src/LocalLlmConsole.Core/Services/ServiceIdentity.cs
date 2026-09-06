namespace LocalLlmConsole.Services;

/// <summary>
/// Идентичность Windows-службы llama-server.
/// Имя службы вычисляется из каталога установки приложения, чтобы несколько
/// копий (из разных каталогов) могли работать одновременно: каждая копия
/// управляет ТОЛЬКО своей службой.
/// </summary>
public static class ServiceIdentity
{
    /// <summary>
    /// Префикс имени службы. Дальше идёт нормализованный путь каталога установки:
    /// «D:\NEURO\LlamaManager» → «llama-cpp-d_neuro_llamamanager».
    /// </summary>
    public const string ServiceNamePrefix = "llama-cpp-";

    /// <summary>
    /// Старое фиксированное имя (до перехода на путь-зависимые имена).
    /// Используется для обнаружения и миграции ранее установленных служб.
    /// </summary>
    public const string LegacyServiceName = "llama-cpp-server";

    /// <summary>
    /// Строит имя службы по каталогу установки:
    /// «D:\NEURO\LlamaManager» → «llama-cpp-d_neuro_llamamanager».
    /// Диск и каталоги приводятся к нижнему регистру и склеиваются через «_».
    /// </summary>
    public static string BuildServiceName(string installDirectory)
    {
        var normalized = string.IsNullOrWhiteSpace(installDirectory)
            ? ""
            : installDirectory.TrimEnd('\\', '/');

        var builder = new System.Text.StringBuilder(ServiceNamePrefix);
        foreach (var ch in normalized)
        {
            if (ch == '\\' || ch == '/' || ch == ' ')
            {
                builder.Append('_');
            }
            else if (ch != ':')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Отображаемое имя службы: «Llama.cpp (D:\NEURO\LlamaManager)».
    /// В скобках — ПОЛНЫЙ путь к каталогу приложения (где лежит
    /// LlamaCppWindowsManager.exe), а не родительский каталог.
    /// </summary>
    public static string BuildDisplayName(string installDirectory)
    {
        var normalized = string.IsNullOrWhiteSpace(installDirectory)
            ? ""
            : installDirectory.TrimEnd('\\', '/');
        return string.IsNullOrWhiteSpace(normalized)
            ? "Llama.cpp"
            : $"Llama.cpp ({normalized})";
    }
}
