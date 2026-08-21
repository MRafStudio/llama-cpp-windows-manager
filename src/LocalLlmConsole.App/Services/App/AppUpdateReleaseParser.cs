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
        const string serviceAssetName = "LocalLlmConsole.Service.exe";
        var serviceAsset = assets
            ?.OfType<JsonObject>()
            .Select(a => (
                Name: a["name"]?.ToString() ?? "",
                Url: FirstNonBlank(a["browser_download_url"]?.ToString(), a["url"]?.ToString())))
            .FirstOrDefault(a => a.Name.Equals(serviceAssetName, StringComparison.OrdinalIgnoreCase))
            ?? ("", "");
        var serviceChecksum = SelectChecksumAsset(assets, serviceAsset.Name);
        var latest = NormalizeVersion(latestVersion);
        var current = NormalizeVersion(currentVersion);
        return new AppUpdateInfo(
            IsVersionNewer(latest, current),
            VersionLabel(currentVersion),
            VersionLabel(latestVersion),
            FirstNonBlank(release["name"]?.ToString(), VersionLabel(latestVersion)),
            release["body"]?.ToString() ?? "",
            release["html_url"]?.ToString() ?? AppUpdateService.RepositoryUrl,
            asset.Name,
            asset.Url,
            asset.Size,
            checksum.Name,
            checksum.Url,
            "",
            serviceAsset.Name,
            serviceAsset.Url,
            serviceChecksum.Name,
            serviceChecksum.Url);
    }

    public static AppUpdateInfo NoUpdateAvailable(string currentVersion, string message = "No updates are available.")
        => new(false, VersionLabel(currentVersion), VersionLabel(currentVersion), message, message, AppUpdateService.RepositoryUrl, "", "", 0);

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
