using System.Text;
using System.Text.Json;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

/// <summary>
/// При переезде каталога установки переписывает абсолютные пути в настройках,
/// каталоге моделей/runtime, джобах, профилях и service-config.json
/// со старого WorkspaceRoot на новый (каталог exe).
/// Пути вне старого workspace не трогает.
/// </summary>
public sealed class WorkspaceRelocationService
{
    public async Task<AppSettings> ApplyAsync(StateStore store, AppSettings loaded, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loaded);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));

        var currentRoot = Path.GetFullPath(workspaceRoot);
        var oldRoot = string.IsNullOrWhiteSpace(loaded.WorkspaceRoot)
            ? currentRoot
            : Path.GetFullPath(loaded.WorkspaceRoot);

        if (string.Equals(oldRoot, currentRoot, StringComparison.OrdinalIgnoreCase))
        {
            var same = loaded with { WorkspaceRoot = currentRoot };
            if (same != loaded)
                await store.SaveAppSettingsAsync(same);
            return same;
        }

        var relocated = RelocateSettings(loaded, oldRoot, currentRoot);
        await store.SaveAppSettingsAsync(relocated);
        await RelocateCatalogAsync(store, oldRoot, currentRoot);
        RelocateServiceConfig(currentRoot, oldRoot);
        return relocated;
    }

    public static AppSettings RelocateSettings(AppSettings settings, string oldRoot, string newRoot)
        => settings with
        {
            WorkspaceRoot = Path.GetFullPath(newRoot),
            ModelsRoot = RebasePath(settings.ModelsRoot, oldRoot, newRoot),
            RuntimeRoot = RebasePath(settings.RuntimeRoot, oldRoot, newRoot),
            CacheRoot = RebasePath(settings.CacheRoot, oldRoot, newRoot)
        };

    public static string RebasePath(string path, string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
            return path;

        try
        {
            if (!Path.IsPathRooted(path))
                return path;

            var full = Path.GetFullPath(path);
            var oldFull = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(oldFull, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(newRoot);

            if (full.StartsWith(oldFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(oldFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full[oldFull.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(newRoot, relative));
            }
        }
        catch
        {
            return path;
        }

        return path;
    }

    public static string RebaseText(string text, string oldRoot, string newRoot)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
            return text;

        var pathRebased = RebasePath(text, oldRoot, newRoot);
        if (!string.Equals(pathRebased, text, StringComparison.Ordinal))
            return pathRebased;

        var oldFull = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var newFull = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = text;
        result = ReplaceRootBounded(result, oldFull.Replace("\\", "\\\\"), newFull.Replace("\\", "\\\\"));
        result = ReplaceRootBounded(result, oldFull.Replace('\\', '/'), newFull.Replace('\\', '/'));
        result = ReplaceRootBounded(result, oldFull, newFull);
        return result;
    }

    internal static string ReplaceRootBounded(string text, string from, string to)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(from))
            return text;

        var builder = new StringBuilder(text.Length);
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(from, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                builder.Append(text, start, text.Length - start);
                return builder.ToString();
            }

            var after = index + from.Length;
            var boundary = after >= text.Length || IsRootBoundary(text[after]);
            builder.Append(text, start, index - start);
            builder.Append(boundary ? to : text.AsSpan(index, from.Length));
            start = after;
        }
    }

    private static bool IsRootBoundary(char ch)
        => ch is '\\' or '/' or '"' or '\'' or ' ' or '\t' or '\r' or '\n' or ',' or '}' or ']' or ':';

    private static async Task RelocateCatalogAsync(StateStore store, string oldRoot, string newRoot)
    {
        foreach (var model in await store.ListModelsAsync())
        {
            var path = RebasePath(model.ModelPath, oldRoot, newRoot);
            var metadata = RebaseText(model.MetadataJson, oldRoot, newRoot);
            if (string.Equals(path, model.ModelPath, StringComparison.Ordinal)
                && string.Equals(metadata, model.MetadataJson, StringComparison.Ordinal))
                continue;
            await store.UpsertModelAsync(model with { ModelPath = path, MetadataJson = metadata });
        }

        foreach (var runtime in await store.ListRuntimesAsync())
        {
            var path = RebasePath(runtime.ExecutablePath, oldRoot, newRoot);
            var metadata = RebaseText(runtime.MetadataJson, oldRoot, newRoot);
            if (string.Equals(path, runtime.ExecutablePath, StringComparison.Ordinal)
                && string.Equals(metadata, runtime.MetadataJson, StringComparison.Ordinal))
                continue;
            await store.UpsertRuntimeAsync(runtime with { ExecutablePath = path, MetadataJson = metadata });
        }

        foreach (var job in await store.ListJobsAsync())
        {
            var logPath = RebasePath(job.LogPath, oldRoot, newRoot);
            var payload = RebaseText(job.PayloadJson, oldRoot, newRoot);
            if (string.Equals(logPath, job.LogPath, StringComparison.Ordinal)
                && string.Equals(payload, job.PayloadJson, StringComparison.Ordinal))
                continue;
            await store.UpsertJobAsync(job with { LogPath = logPath, PayloadJson = payload });
        }

        foreach (var profile in await store.ListNamedModelLaunchProfilesAsync())
        {
            var settings = profile.Settings with
            {
                VisionProjectorPath = RebasePath(profile.Settings.VisionProjectorPath, oldRoot, newRoot),
                SpecDraftModelPath = RebasePath(profile.Settings.SpecDraftModelPath, oldRoot, newRoot),
                MtpHeadPath = RebasePath(profile.Settings.MtpHeadPath, oldRoot, newRoot)
            };
            if (settings == profile.Settings)
                continue;
            await store.SaveNamedModelLaunchProfileAsync(profile with { Settings = settings });
        }
    }

    private static void RelocateServiceConfig(string workspaceRoot, string oldRoot)
    {
        var path = Path.Combine(workspaceRoot, "state", "service-config.json");
        if (!File.Exists(path))
            return;

        try
        {
            var original = File.ReadAllText(path);
            var rebased = RebaseText(original, oldRoot, workspaceRoot);
            if (string.Equals(original, rebased, StringComparison.Ordinal))
                return;

            JsonDocument.Parse(rebased).Dispose();
            File.WriteAllText(path, rebased);
        }
        catch
        {
            // Конфиг службы не должен валить старт GUI.
        }
    }
}
