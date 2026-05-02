namespace amChipper.Core.Models;

/// <summary>
/// Carries ModuleFormatInfo data.
/// </summary>
public sealed record ModuleFormatInfo(
    string Extension,
    string Type,
    string DisplayName,
    ModuleFormat Format,
    bool DirtyNativePatchSupported);

/// <summary>
/// Represents the ModuleFormatCatalog component.
/// </summary>
public static class ModuleFormatCatalog
{
    /// <summary>
    /// Stores or exposes SupportedFormats.
    /// </summary>
    private static readonly ModuleFormatInfo[] SupportedFormats =
    [
        new(".mod", "MOD", "ProTracker MOD", ModuleFormat.MOD, true),
        new(".xm", "XM", "FastTracker XM", ModuleFormat.XM, true),
        new(".it", "IT", "Impulse Tracker IT", ModuleFormat.IT, false),
        new(".s3m", "S3M", "Scream Tracker S3M", ModuleFormat.S3M, false),
        new(".amc", "AMC", "amChipper Native Chip Module", ModuleFormat.AmChip, true),
        new(".mptm", "MPTM", "OpenMPT MPTM", ModuleFormat.OpenMpt, false),
        new(".669", "669", "Composer 669", ModuleFormat.OpenMpt, false),
        new(".abc", "ABC", "ABC Music Notation", ModuleFormat.OpenMpt, false),
        new(".ahx", "AHX", "Abyss Highest Experience", ModuleFormat.OpenMpt, false),
        new(".amf", "AMF", "DSMI / Asylum AMF", ModuleFormat.OpenMpt, false),
        new(".ams", "AMS", "Velvet Studio AMS", ModuleFormat.OpenMpt, false),
        new(".c67", "C67", "CDFM / Composer 670", ModuleFormat.OpenMpt, false),
        new(".dbm", "DBM", "DigiBooster Pro", ModuleFormat.OpenMpt, false),
        new(".digi", "DIGI", "DIGI Booster", ModuleFormat.OpenMpt, false),
        new(".dmf", "DMF", "Delusion/X-Tracker DMF", ModuleFormat.OpenMpt, false),
        new(".dsm", "DSM", "Digital Sound Module", ModuleFormat.OpenMpt, false),
        new(".dtm", "DTM", "Digital Tracker", ModuleFormat.OpenMpt, false),
        new(".far", "FAR", "Farandole Composer", ModuleFormat.OpenMpt, false),
        new(".gdm", "GDM", "General DigiMusic", ModuleFormat.OpenMpt, false),
        new(".gtk", "GTK", "Graoumf Tracker", ModuleFormat.OpenMpt, false),
        new(".hvl", "HVL", "HivelyTracker", ModuleFormat.OpenMpt, false),
        new(".imf", "IMF", "Imago Orpheus", ModuleFormat.OpenMpt, false),
        new(".ice", "ICE", "Ice Tracker", ModuleFormat.MOD, false),
        new(".itp", "ITP", "Impulse Tracker Project", ModuleFormat.OpenMpt, false),
        new(".j2b", "J2B", "Jazz Jackrabbit 2", ModuleFormat.OpenMpt, false),
        new(".m15", "M15", "SoundTracker 15-Instrument MOD", ModuleFormat.MOD, false),
        new(".mdl", "MDL", "DigiTrakker MDL", ModuleFormat.OpenMpt, false),
        new(".med", "MED", "OctaMED / MED", ModuleFormat.OpenMpt, false),
        new(".mgt", "MGT", "Megatracker", ModuleFormat.OpenMpt, false),
        new(".mms", "MMS", "MMS Module", ModuleFormat.OpenMpt, false),
        new(".mo3", "MO3", "MO3 Compressed Module", ModuleFormat.OpenMpt, false),
        new(".mt2", "MT2", "MadTracker 2", ModuleFormat.OpenMpt, false),
        new(".mtm", "MTM", "MultiTracker MTM", ModuleFormat.OpenMpt, false),
        new(".nst", "NST", "NoiseTracker", ModuleFormat.MOD, false),
        new(".okt", "OKT", "Oktalyzer", ModuleFormat.OpenMpt, false),
        new(".plm", "PLM", "DisorderTracker 2", ModuleFormat.OpenMpt, false),
        new(".psm", "PSM", "ProTracker Studio", ModuleFormat.OpenMpt, false),
        new(".ptm", "PTM", "PolyTracker", ModuleFormat.OpenMpt, false),
        new(".pt36", "PT36", "ProTracker 3.6", ModuleFormat.MOD, false),
        new(".sfx", "SFX", "SoundFX", ModuleFormat.OpenMpt, false),
        new(".sfx2", "SFX2", "SoundFX 2", ModuleFormat.OpenMpt, false),
        new(".st26", "ST26", "SoundTracker 2.6", ModuleFormat.MOD, false),
        new(".stk", "STK", "The Ultimate Soundtracker", ModuleFormat.MOD, false),
        new(".stm", "STM", "Scream Tracker STM", ModuleFormat.OpenMpt, false),
        new(".stp", "STP", "SoundTracker Pro II", ModuleFormat.MOD, false),
        new(".ult", "ULT", "UltraTracker", ModuleFormat.OpenMpt, false),
        new(".umx", "UMX", "Unreal Music Package", ModuleFormat.OpenMpt, false),
        new(".wow", "WOW", "Grave Composer WOW", ModuleFormat.OpenMpt, false),
        new(".xpk", "XPK", "XPK Packed Module", ModuleFormat.OpenMpt, false),
        new(".sid", "SID", "Commodore 64 SID / PSID / RSID", ModuleFormat.SID, false),
        new(".psid", "PSID", "Commodore 64 PSID", ModuleFormat.SID, false),
        new(".rsid", "RSID", "Commodore 64 RSID", ModuleFormat.SID, false),
        new(".nsf", "NSF", "Nintendo Sound Format", ModuleFormat.NSF, false),
        new(".nsfe", "NSFE", "Nintendo Sound Format Extended", ModuleFormat.NSF, false)
    ];

    /// <summary>
    /// Stores or exposes ModuleFormatInfo.
    /// </summary>
    private static readonly Dictionary<string, ModuleFormatInfo> ByExtension =
        SupportedFormats.ToDictionary(f => f.Extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores or exposes ModuleFormatInfo.
    /// </summary>
    private static readonly Dictionary<string, ModuleFormatInfo> ByType =
        SupportedFormats.GroupBy(f => f.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores or exposes Formats.
    /// </summary>
    public static IReadOnlyList<ModuleFormatInfo> Formats => SupportedFormats;

    /// <summary>
    /// Stores or exposes SupportedExtensionsPattern.
    /// </summary>
    public static string SupportedExtensionsPattern =>
        string.Join(";", SupportedFormats.Select(f => $"*{f.Extension}"));

    /// <summary>
    /// Stores or exposes OpenDialogFilter.
    /// </summary>
    public static string OpenDialogFilter =>
        $"amChipper Project (*.amchip)|*.amchip|Tracker / Chiptune / Console Music ({SupportedExtensionsPattern})|{SupportedExtensionsPattern}|All Files (*.*)|*.*";

    /// <summary>
    /// Executes the IsEmulatedChipFormat operation.
    /// </summary>
    public static bool IsEmulatedChipFormat(ModuleFormat format) =>
        format is ModuleFormat.SID or ModuleFormat.NSF;

    /// <summary>
    /// Executes the IsSupportedExtension operation.
    /// </summary>
    public static bool IsSupportedExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && ByExtension.ContainsKey(NormalizeExtension(extension));

    /// <summary>
    /// Executes the ResolveModuleFormat operation.
    /// </summary>
    public static ModuleFormat ResolveModuleFormat(string? type, string? extension)
    {
        if (TryResolve(type, extension, out var info))
            return info.Format;

        return ModuleFormat.OpenMpt;
    }

    /// <summary>
    /// Executes the TryResolve operation.
    /// </summary>
    public static bool TryResolve(string? type, string? extension, out ModuleFormatInfo info)
    {
        string cleanType = NormalizeType(type);
        if (cleanType.Length > 0 && ByType.TryGetValue(cleanType, out info!))
            return true;

        string cleanExtension = NormalizeExtension(extension);
        if (cleanExtension.Length > 0 && ByExtension.TryGetValue(cleanExtension, out info!))
            return true;

        info = null!;
        return false;
    }

    /// <summary>
    /// Executes the GetPreferredExtension operation.
    /// </summary>
    public static string GetPreferredExtension(ModuleFormat format, string? sourceExtension = null)
    {
        string cleanExtension = NormalizeExtension(sourceExtension);
        if (cleanExtension.Length > 0 && IsSupportedExtension(cleanExtension))
            return cleanExtension;

        return format switch
        {
            ModuleFormat.MOD => ".mod",
            ModuleFormat.XM => ".xm",
            ModuleFormat.IT => ".it",
            ModuleFormat.S3M => ".s3m",
            ModuleFormat.AmChip => ".amc",
            ModuleFormat.SID => ".sid",
            ModuleFormat.NSF => ".nsf",
            _ => ".mod"
        };
    }

    /// <summary>
    /// Executes the GetDisplayLabel operation.
    /// </summary>
    public static string GetDisplayLabel(Song song)
    {
        if (!string.IsNullOrWhiteSpace(song.SourceModuleType))
            return song.SourceModuleType.Trim().ToUpperInvariant();

        if (TryResolve(null, song.SourceModuleExtension, out var info))
            return info.Type;

        return song.Format == ModuleFormat.OpenMpt ? "OPENMPT" : song.Format.ToString();
    }

    /// <summary>
    /// Executes the NormalizeExtension operation.
    /// </summary>
    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        string clean = extension.Trim().ToLowerInvariant();
        return clean.StartsWith('.') ? clean : $".{clean}";
    }

    /// <summary>
    /// Executes the NormalizeType operation.
    /// </summary>
    private static string NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        string clean = type.Trim().ToUpperInvariant();
        int separator = clean.IndexOfAny([' ', '/', '\\', ';', ',']);
        return separator > 0 ? clean[..separator] : clean;
    }
}
