using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace amChipper.App.Services;

/// <summary>
/// Checks a JSON update manifest and opens the matching download page when a newer build is available.
/// </summary>
public sealed class UpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        "v0.1.0.0";

    public async Task<UpdateCheckResult> CheckAsync(Uri manifestUri, CancellationToken cancellationToken = default)
    {
        using var stream = await _httpClient.GetStreamAsync(manifestUri, cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            return new UpdateCheckResult(false, CurrentVersion, string.Empty, string.Empty, "Manifest did not contain a version.");

        bool updateAvailable = CompareBuildCodes(manifest.Version, CurrentVersion) > 0;
        return new UpdateCheckResult(updateAvailable, CurrentVersion, manifest.Version, manifest.DownloadUrl ?? string.Empty, manifest.ReleaseNotes ?? string.Empty);
    }

    public static void OpenDownload(UpdateCheckResult result)
    {
        if (!result.UpdateAvailable || string.IsNullOrWhiteSpace(result.DownloadUrl))
            return;

        Process.Start(new ProcessStartInfo(result.DownloadUrl) { UseShellExecute = true });
    }

    private static int CompareBuildCodes(string left, string right)
    {
        string l = ExtractComparable(left);
        string r = ExtractComparable(right);
        return string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractComparable(string version)
    {
        int marker = version.IndexOf("AMC", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? version[marker..].Replace(".", string.Empty, StringComparison.Ordinal) : version;
    }

    private sealed record UpdateManifest(string Version, string? DownloadUrl, string? ReleaseNotes);
}

/// <summary>
/// Contains the result of an update manifest check.
/// </summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string LatestVersion, string DownloadUrl, string Message);
