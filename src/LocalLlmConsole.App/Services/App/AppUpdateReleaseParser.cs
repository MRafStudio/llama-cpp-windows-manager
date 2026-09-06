namespace LocalLlmConsole.Services;

public static class AppUpdateReleaseParser
{
    private static readonly string[] PortableExeNames = [AppUpdateService.PortableExeName];

    public static AppUpdateInfo ParseLatestRelease(JsonObject release, string currentVersion)
    {
        var latestVersion = FirstNonBlank(
            release["tag_name"]?.ToString(),
            release["name"]?.ToString());
        if (string.IsNullOrWhiteSpace(latestVersion))
            throw new InvalidOperationException("The GitHub release has no tag name.");

        var assets = release["assets"]?.AsArray();
        var asset = SelectPortableAsset(assets);
        var checksum = SelectChecksumAsset(assets, asset.Name);
        // ZIP-архив обновления (полный: App + Service + Updater + llwmctl).
        var zipAsset = SelectZipAsset(assets);
        var zipChecksum = SelectChecksumAsset(assets, zipAsset.Name);
        const string serviceAssetName = "LocalLlmConsole.Service.exe";
        var serviceAsset = assets
            ?.OfType<JsonObject>()
            .Select(a => (
                Name: a["name"]?.ToString() ?? "",
                Url: FirstNonBlank(a["browser_download_url"]?.ToString(), a["url"]?.ToString())))
            .FirstOrDefault(a => a.Name.Equals(serviceAssetName, StringComparison.OrdinalIgnoreCase))
            ?? ("", "");
        var serviceChecksum = SelectChecksumAsset(assets, serviceAsset.Name);
        const string updaterAssetName = "LocalLlmConsole.Updater.exe";
        var updaterAsset = assets
            ?.OfType<JsonObject>()
            .Select(a => (
                Name: a["name"]?.ToString() ?? "",
                Url: FirstNonBlank(a["browser_download_url"]?.ToString(), a["url"]?.ToString())))
            .FirstOrDefault(a => a.Name.Equals(updaterAssetName, StringComparison.OrdinalIgnoreCase))
            ?? ("", "");
        var updaterChecksum = SelectChecksumAsset(assets, updaterAsset.Name);
        var latest = NormalizeVersion(latestVersion);
        var current = NormalizeVersion(currentVersion);
        // ВАЖНО: только именованные аргументы! Позиционные вызовы этого record
        // сдвигались при добавлении полей (ExpectedSha256 и т.п.) — из-за этого
        // ZipAssetUrl получал URL .sha256-файла, а чексумма уходила в fallback
        // на exe. Именованные аргументы исключают сдвиг навсегда.
        return new AppUpdateInfo(
            IsAvailable: IsVersionNewer(latest, current),
            CurrentVersion: VersionLabel(currentVersion),
            LatestVersion: VersionLabel(latestVersion),
            ReleaseName: FirstNonBlank(release["name"]?.ToString(), VersionLabel(latestVersion)),
            ReleaseNotes: release["body"]?.ToString() ?? "",
            HtmlUrl: release["html_url"]?.ToString() ?? AppUpdateService.RepositoryUrl,
            AssetName: asset.Name,
            AssetUrl: asset.Url,
            AssetSize: asset.Size,
            ChecksumAssetName: checksum.Name,
            ChecksumAssetUrl: checksum.Url,
            ExpectedSha256: "",
            ServiceAssetName: serviceAsset.Name,
            ServiceAssetUrl: serviceAsset.Url,
            ServiceChecksumAssetName: serviceChecksum.Name,
            ServiceChecksumAssetUrl: serviceChecksum.Url,
            ServiceExpectedSha256: "",
            UpdaterAssetName: updaterAsset.Name,
            UpdaterAssetUrl: updaterAsset.Url,
            UpdaterChecksumAssetName: updaterChecksum.Name,
            UpdaterChecksumAssetUrl: updaterChecksum.Url,
            UpdaterExpectedSha256: "",
            ZipAssetName: zipAsset.Name,
            ZipAssetUrl: zipAsset.Url,
            ZipChecksumAssetName: zipChecksum.Name,
            ZipChecksumAssetUrl: zipChecksum.Url,
            ZipExpectedSha256: "");
    }

    public static AppUpdateInfo NoUpdateAvailable(string currentVersion, string message = "No updates are available.")
        => new(
            IsAvailable: false,
            CurrentVersion: VersionLabel(currentVersion),
            LatestVersion: VersionLabel(currentVersion),
            ReleaseName: message,
            ReleaseNotes: message,
            HtmlUrl: AppUpdateService.RepositoryUrl,
            AssetName: "",
            AssetUrl: "",
            AssetSize: 0);

    public static bool IsPortableExeName(string name)
        => PortableExeNames.Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static (string Name, string Url, long Size) SelectPortableAsset(JsonArray? assets)
    {
        if (assets is null) return ("", "", 0);
        var candidates = assets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString()),
                Size: JsonLong(asset["size"])))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.Url))
            .ToList();

        return PortableExeNames
            .Select(name => candidates.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(asset => !string.IsNullOrWhiteSpace(asset.Name))
            is var exact && !string.IsNullOrWhiteSpace(exact.Name) ? exact
            : candidates.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                is var zip && !string.IsNullOrWhiteSpace(zip.Name) ? zip
            : candidates.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ZIP-архив обновления (например, LlamaCppWindowsManager-win-x64.zip).
    /// Содержит полный набор: приложение, службу, Updater, llwmctl.
    /// </summary>
    private static (string Name, string Url) SelectZipAsset(JsonArray? assets)
    {
        if (assets is null) return ("", "");
        return assets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString())))
            .FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                     && asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase));
    }

    private static (string Name, string Url) SelectChecksumAsset(JsonArray? assets, string assetName)
    {
        if (assets is null || string.IsNullOrWhiteSpace(assetName)) return ("", "");
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            assetName + ".sha256",
            assetName + ".sha256.txt",
            Path.ChangeExtension(assetName, ".sha256")
        };
        return assets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString())))
            .FirstOrDefault(asset => expectedNames.Contains(asset.Name));
    }

    private static Version NormalizeVersion(string value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        var prerelease = text.IndexOfAny(['-', '+']);
        if (prerelease >= 0) text = text[..prerelease];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) text += ".0.0";
        else if (parts.Length == 2) text += ".0";
        return Version.TryParse(text, out var version) ? version : new Version(0, 0, 0);
    }

    private static bool IsVersionNewer(Version latest, Version current) => latest.CompareTo(current) > 0;

    private static string VersionLabel(string value)
    {
        var text = (value ?? "").Trim();
        return text.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? text : $"v{text}";
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static long JsonLong(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;
}
