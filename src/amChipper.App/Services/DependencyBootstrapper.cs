using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using amChipper.App.Services;

namespace amChipper.App.Services;

/// <summary>
/// Checks for required native dependencies on startup and downloads them
/// automatically if missing.
///
/// Currently manages:
///   • libopenmpt.dll (x64)  — downloaded from lib.openmpt.org
/// </summary>
public sealed class DependencyBootstrapper
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// lib.openmpt.org developer-package download index page.
    /// We parse it to find the latest Windows VS2022 dev zip (which contains libopenmpt.dll).
    ///
    /// NOTE: As of 0.8.x the layout changed:
    ///   /bin/ → plugins-only (Winamp/XMPlay/openmpt123) — does NOT contain libopenmpt.dll
    ///   /dev/ → development package with libopenmpt.dll, headers, and import libs
    /// The dev zip is named libopenmpt-X.Y.Z+release.dev.windows.vs2022.zip.
    /// </summary>
    private const string OpenmptIndexUrl =
        "https://lib.openmpt.org/files/libopenmpt/dev/";

    /// <summary>
    /// Fallback URL used when the index cannot be parsed.
    /// Targets the VS2022 dev package which contains libopenmpt.dll.
    /// Update this when bumping versions.
    /// </summary>
    private const string OpenmptFallbackZip =
        "https://lib.openmpt.org/files/libopenmpt/dev/libopenmpt-0.8.6+release.dev.windows.vs2022.zip";

    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    public const string DllName = "libopenmpt.dll";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<DownloadProgressArgs>? ProgressChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if a valid libopenmpt.dll is present next to the executable.
    /// "Valid" means the file exists AND it exports the expected libopenmpt C API
    /// (guards against accidentally extracting openmpt-mpg123.dll under the wrong name).
    /// Does NOT delete an invalid DLL — deletion is left to <see cref="DeleteInvalidDll"/>
    /// which is called only after the user agrees to download a replacement.
    /// </summary>
    public static bool IsLibOpenMptPresent()
    {
        string path = FindExistingLibOpenMptPath();

        if (!File.Exists(path))
        {
            AppLogger.Warning(
                $"libopenmpt.dll not found at: {Path.Combine(RuntimeDependencyResolver.LibsDirectory, DllName)} " +
                $"or {Path.Combine(AppContext.BaseDirectory, DllName)}");
            return false;
        }

        // Verify the file actually exports the core libopenmpt entry point.
        // We use NativeLibrary so this check doesn't permanently bind the DLL
        // to the P/Invoke layer if it turns out to be invalid.
        if (NativeLibrary.TryLoad(path, out nint handle))
        {
            bool valid = NativeLibrary.TryGetExport(
                handle, "openmpt_get_library_version", out _);
            NativeLibrary.Free(handle);

            if (valid)
            {
                AppLogger.Info($"libopenmpt.dll verified at: {path}");
                return true;
            }

            AppLogger.Warning(
                $"libopenmpt.dll at {path} is invalid " +
                "(missing openmpt exports — probably a helper DLL was misidentified). " +
                "Will prompt user to replace it.");
        }
        else
        {
            AppLogger.Warning(
                $"libopenmpt.dll at {path} could not be loaded " +
                "(wrong architecture or missing dependency DLL?). " +
                "Will prompt user to replace it.");
        }

        return false;
    }

    /// <summary>
    /// Deletes an invalid libopenmpt.dll so DownloadLibOpenMptAsync can place
    /// the correct one.  Called only after the user consents to the download.
    /// </summary>
    public static void DeleteInvalidDll()
    {
        foreach (string path in GetCandidateLibOpenMptPaths())
        {
            if (!File.Exists(path))
                continue;

            try
            {
                File.Delete(path);
                AppLogger.Info($"Deleted invalid DLL: {path}");
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    $"Could not delete invalid DLL (it may already be loaded): {ex.Message}\n" +
                    $"Please manually delete {path} and restart amChipper.");
            }
        }
    }

    /// <summary>
    /// Download libopenmpt.dll and place it next to the executable.
    /// Reports progress via <see cref="ProgressChanged"/> and <see cref="StatusChanged"/>.
    /// Throws on unrecoverable error.
    /// </summary>
    public async Task DownloadLibOpenMptAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("amChipper/1.0");
        http.Timeout = TimeSpan.FromMinutes(5);

        // Step 1 — resolve download URL
        RaiseStatus("Checking for latest libopenmpt release…");
        string zipUrl = await ResolveZipUrlAsync(http, ct);
        AppLogger.Info($"libopenmpt download URL: {zipUrl}");

        // Step 2 — download zip
        RaiseStatus($"Downloading {Path.GetFileName(zipUrl)}…");
        byte[] zipData = await DownloadWithProgressAsync(http, zipUrl, ct);
        AppLogger.Info($"Downloaded {zipData.Length:N0} bytes.");

        // Step 3 — validate and extract DLL from zip
        RaiseStatus("Extracting libopenmpt.dll…");

        // Quick sanity-check: a ZIP always starts with PK (0x50 0x4B).
        // If the server returned an HTML error page with a 200 status we catch
        // it here with a readable message instead of a cryptic ZipArchive error.
        if (zipData.Length < 4 || zipData[0] != 0x50 || zipData[1] != 0x4B)
        {
            string preview = System.Text.Encoding.UTF8
                .GetString(zipData, 0, Math.Min(zipData.Length, 200))
                .Replace('\n', ' ').Replace('\r', ' ');
            AppLogger.Error($"Downloaded content is not a ZIP. Preview: {preview}");
            throw new InvalidDataException(
                $"Downloaded file is not a valid ZIP (bytes: " +
                $"{zipData[0]:X2} {zipData[1]:X2} {zipData[2]:X2} {zipData[3]:X2}). " +
                $"Server may have returned an error page. " +
                $"Try downloading manually from https://lib.openmpt.org/");
        }

        Directory.CreateDirectory(RuntimeDependencyResolver.LibsDirectory);
        string destPath = Path.Combine(RuntimeDependencyResolver.LibsDirectory, DllName);
        AppLogger.Info($"Destination: {destPath}");
        ExtractDll(zipData, destPath);

        RaiseStatus($"Done! libopenmpt.dll saved to {destPath}");
        AppLogger.Info($"libopenmpt.dll extracted to: {destPath}");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Executes the ResolveZipUrlAsync operation.
    /// </summary>
    private async Task<string> ResolveZipUrlAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            string html = await http.GetStringAsync(OpenmptIndexUrl, ct);

            // As of 0.8.x the standalone libopenmpt.dll is in the VS2022 dev package
            // under /files/libopenmpt/dev/.  The .bin.windows.zip (under /bin/) is
            // plugins-only (Winamp/XMPlay/openmpt123) and does NOT contain libopenmpt.dll.
            // Match: libopenmpt-X.Y.Z+release.dev.windows.vs2022.zip
            var vs2022Matches = Regex.Matches(html,
                @"href=""(libopenmpt-[\d.]+\+release\.dev\.windows\.vs2022\.zip)""",
                RegexOptions.IgnoreCase);

            if (vs2022Matches.Count > 0)
            {
                string fileName = vs2022Matches[^1].Groups[1].Value;
                AppLogger.Info($"Found VS2022 dev package: {fileName}");
                return OpenmptIndexUrl + fileName;
            }

            AppLogger.Warning("Could not parse libopenmpt dev index — using fallback URL.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to fetch libopenmpt dev index: {ex.Message}. Using fallback URL.");
        }

        return OpenmptFallbackZip;
    }

    /// <summary>
    /// Executes the FindExistingLibOpenMptPath operation.
    /// </summary>
    private static string FindExistingLibOpenMptPath() =>
        GetCandidateLibOpenMptPaths().FirstOrDefault(File.Exists)
        ?? Path.Combine(RuntimeDependencyResolver.LibsDirectory, DllName);

    /// <summary>
    /// Executes the GetCandidateLibOpenMptPaths operation.
    /// </summary>
    private static IEnumerable<string> GetCandidateLibOpenMptPaths()
    {
        yield return Path.Combine(RuntimeDependencyResolver.LibsDirectory, DllName);
        yield return Path.Combine(AppContext.BaseDirectory, DllName);
    }

    /// <summary>
    /// Executes the DownloadWithProgressAsync operation.
    /// </summary>
    private async Task<byte[]> DownloadWithProgressAsync(
        HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream(total.HasValue ? (int)total.Value : 4 * 1024 * 1024);

        byte[] buf = new byte[81920];
        long read = 0;
        int chunk;

        while ((chunk = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, chunk);
            read += chunk;

            if (total.HasValue)
            {
                int pct = (int)(read * 100L / total.Value);
                ProgressChanged?.Invoke(this,
                    new DownloadProgressArgs(pct, read, total.Value));
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Executes the ExtractDll operation.
    /// </summary>
    private static void ExtractDll(byte[] zipData, string destPath)
    {
        using var zip = new ZipArchive(new MemoryStream(zipData), ZipArchiveMode.Read);

        // Log every entry for diagnostics — helps if the layout ever changes again.
        AppLogger.Info($"ZIP contains {zip.Entries.Count} entries:");
        foreach (var e in zip.Entries)
            AppLogger.Debug($"  [{e.Length,12:N0}] {e.FullName}");

        // ── Architecture helpers ─────────────────────────────────────────────────
        static string NormPath(string p) => p.Replace('\\', '/');
        static bool IsX64Path(string fullName)
        {
            string p = NormPath(fullName);
            return p.Contains("x86_64", StringComparison.OrdinalIgnoreCase)
                || p.Contains("/x64/", StringComparison.OrdinalIgnoreCase)
                || p.Contains("amd64", StringComparison.OrdinalIgnoreCase)
                || p.Contains("win64", StringComparison.OrdinalIgnoreCase)
                || p.Contains("64bit", StringComparison.OrdinalIgnoreCase);
        }

        // ── Find the main libopenmpt DLL ─────────────────────────────────────────
        // Name starts with "libopenmpt" to exclude companion DLLs like
        // openmpt-mpg123.dll which are handled separately below.
        var libopenmptDlls = zip.Entries
            .Where(e => e.Name.StartsWith("libopenmpt", StringComparison.OrdinalIgnoreCase)
                     && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        AppLogger.Info($"libopenmpt* DLL candidates: " +
            string.Join(", ", libopenmptDlls.Select(e => e.FullName)));

        ZipArchiveEntry? entry =
            libopenmptDlls.FirstOrDefault(e => IsX64Path(e.FullName))    // x64 folder preferred
            ?? libopenmptDlls.OrderByDescending(e => e.Length)            // largest as tiebreaker
                             .FirstOrDefault()
            ?? zip.Entries.FirstOrDefault(e =>                            // last resort: any DLL in x64 folder
                    e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && IsX64Path(e.FullName));

        if (entry is null)
        {
            // Save the full entry list to a temp file so the user can inspect it.
            string dumpPath = Path.Combine(Path.GetTempPath(), "amChipper_zip_entries.txt");
            File.WriteAllLines(dumpPath,
                zip.Entries.Select(e => $"{e.Length,12:N0}  {e.FullName}"));
            AppLogger.Error($"No suitable DLL found. Zip entry list saved to: {dumpPath}");

            throw new FileNotFoundException(
                $"No openmpt DLL found in the zip.\n" +
                $"Entry list saved to: {dumpPath}\n" +
                "Download libopenmpt.dll manually from https://lib.openmpt.org/");
        }

        // Ensure destination directory exists.
        string? destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        AppLogger.Info($"Extracting: {entry.FullName} ({entry.Length:N0} bytes) → {destPath}");

        // Extract and rename to the expected DllName ("libopenmpt.dll") regardless
        // of what the zip entry itself is called (e.g. "libopenmpt-0.dll").
        using (var src = entry.Open())
        using (var dest = File.Open(destPath, FileMode.Create, FileAccess.Write))
            src.CopyTo(dest);

        AppLogger.Info($"libopenmpt.dll extracted. Size: {new FileInfo(destPath).Length:N0} bytes");

        // ── Extract companion DLLs from the same folder ──────────────────────────
        // libopenmpt.dll dynamically links against openmpt-mpg123.dll,
        // openmpt-ogg.dll, openmpt-vorbis.dll, and openmpt-zlib.dll.
        // Without these siblings the DLL fails to load entirely.
        string archFolder = NormPath(Path.GetDirectoryName(entry.FullName) ?? string.Empty)
                                .TrimEnd('/') + "/";

        var companions = zip.Entries
            .Where(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                     && NormPath(e.FullName).StartsWith(archFolder, StringComparison.OrdinalIgnoreCase)
                     && !e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var companion in companions)
        {
            string companionDest = Path.Combine(destDir!, companion.Name);
            AppLogger.Info($"Extracting companion: {companion.FullName} → {companionDest}");
            using var cs = companion.Open();
            using var cf = File.Open(companionDest, FileMode.Create, FileAccess.Write);
            cs.CopyTo(cf);
        }

        AppLogger.Info($"Extraction complete. {1 + companions.Count} DLLs installed.");
    }

    /// <summary>
    /// Executes the RaiseStatus operation.
    /// </summary>
    private void RaiseStatus(string msg)
    {
        AppLogger.Debug(msg);
        StatusChanged?.Invoke(this, msg);
    }
}

/// <summary>
/// Represents the DownloadProgressArgs component.
/// </summary>
public sealed class DownloadProgressArgs(int percent, long bytesReceived, long totalBytes) : EventArgs
{
    /// <summary>
    /// Stores or exposes Percent.
    /// </summary>
    public int Percent { get; } = percent;
    /// <summary>
    /// Stores or exposes BytesReceived.
    /// </summary>
    public long BytesReceived { get; } = bytesReceived;
    /// <summary>
    /// Stores or exposes TotalBytes.
    /// </summary>
    public long TotalBytes { get; } = totalBytes;

    /// <summary>
    /// Stores or exposes Display.
    /// </summary>
    public string Display =>
        $"{BytesReceived / 1024:N0} KB / {TotalBytes / 1024:N0} KB  ({Percent}%)";
}
