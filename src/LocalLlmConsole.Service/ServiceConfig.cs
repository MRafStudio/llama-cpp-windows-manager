namespace LocalLlmConsole.Service;

/// <summary>
/// Конфигурация запуска llama-server для Windows-службы.
/// Файл пишется WPF-приложением перед установкой/запуском службы
/// в <c>data/state/service-config.json</c> (рядом с exe службы).
/// </summary>
public sealed record ServiceConfig(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string ModelId = "",
    string ProfileId = "",
    string RuntimeId = "")
{
    public const string FileName = "service-config.json";

    public static string DefaultPath
        => Path.Combine(AppContext.BaseDirectory, "data", "state", FileName);
}
