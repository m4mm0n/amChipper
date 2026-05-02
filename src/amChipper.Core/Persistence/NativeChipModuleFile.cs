using System.IO.Compression;
using System.Text.Json;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the NativeChipModuleFile component.
/// </summary>
public static class NativeChipModuleFile
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    public const int CurrentVersion = 1;
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    public const string Extension = ".amc";
    /// <summary>
    /// Executes the Magic operation.
    /// </summary>
    private static readonly byte[] Magic = "AMCHIPMOD1"u8.ToArray();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, string path)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[]? sourceModuleData = song.OriginalModuleData is { Length: > 0 }
            ? (byte[])song.OriginalModuleData.Clone()
            : null;
        var moduleSong = song.Clone();
        moduleSong.OriginalModuleData = null;
        if (sourceModuleData is null)
        {
            moduleSong.Format = ModuleFormat.AmChip;
            moduleSong.SourceModuleType = "AMC";
            moduleSong.SourceModuleExtension = Extension;
        }

        SongProjectSerializer.Normalize(moduleSong);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var file = new NativeChipModulePackage
        {
            Version = CurrentVersion,
            SavedUtc = DateTimeOffset.UtcNow,
            Song = moduleSong,
            SourceModuleType = sourceModuleData is null ? string.Empty : song.SourceModuleType,
            SourceModuleExtension = sourceModuleData is null ? string.Empty : song.SourceModuleExtension,
            SourceModuleFormat = sourceModuleData is null ? ModuleFormat.Unknown : song.Format,
            SourceModuleBytes = sourceModuleData?.Length ?? 0
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(file, Options);
        byte[] compressedJson = Compress(json);
        byte[] compressedSource = sourceModuleData is null ? [] : Compress(sourceModuleData);
        using var output = File.Create(path);
        output.Write(Magic);
        output.WriteByte(0);
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: false);
        writer.Write(compressedJson.Length);
        writer.Write(compressedSource.Length);
        writer.Write(compressedJson);
        writer.Write(compressedSource);
    }

    /// <summary>
    /// Executes the Load operation.
    /// </summary>
    public static Song Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Loads a native chip module from an in-memory AMC byte payload.
    /// </summary>
    public static Song Load(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!IsNativeChipModule(bytes))
            throw new InvalidDataException("Not an amChipper native chip module.");

        NativeChipModulePackage package;
        byte[]? sourceModuleData;
        try
        {
            (package, sourceModuleData) = ReadSourcePreservingPackage(bytes);
        }
        catch
        {
            package = ReadLegacyPackage(bytes);
            sourceModuleData = null;
        }

        if (package.Version < 1 || package.Version > CurrentVersion)
            throw new InvalidDataException($"Unsupported native chip module version: {package.Version}.");

        var song = package.Song ?? throw new InvalidDataException("The native chip module does not contain a song.");
        if (sourceModuleData is { Length: > 0 })
        {
            song.Format = package.SourceModuleFormat == ModuleFormat.Unknown ? song.Format : package.SourceModuleFormat;
            song.SourceModuleType = string.IsNullOrWhiteSpace(package.SourceModuleType) ? song.SourceModuleType : package.SourceModuleType;
            song.SourceModuleExtension = string.IsNullOrWhiteSpace(package.SourceModuleExtension)
                ? song.SourceModuleExtension
                : package.SourceModuleExtension;
            song.OriginalModuleData = sourceModuleData;
        }
        else
        {
            song.Format = ModuleFormat.AmChip;
            song.SourceModuleType = "AMC";
            song.SourceModuleExtension = Extension;
            song.OriginalModuleData = null;
        }

        SongProjectSerializer.Normalize(song);
        return song;
    }

    /// <summary>
    /// Executes the IsNativeChipModule operation.
    /// </summary>
    public static bool IsNativeChipModule(byte[] bytes) =>
        bytes.Length > Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>
    /// Executes the SaveToBytes operation.
    /// </summary>
    public static byte[] SaveToBytes(Song song)
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}{Extension}");
        try
        {
            Save(song, path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Compresses raw section data for the native module container.
    /// </summary>
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(data);
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses raw section data from the native module container.
    /// </summary>
    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Reads the legacy single-stream JSON package.
    /// </summary>
    private static NativeChipModulePackage ReadLegacyPackage(byte[] bytes)
    {
        int offset = Magic.Length;
        if (offset < bytes.Length && bytes[offset] == 0)
            offset++;

        using var input = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<NativeChipModulePackage>(brotli, Options)
            ?? throw new InvalidDataException("The native chip module is empty or invalid.");
    }

    /// <summary>
    /// Reads the source-preserving package.
    /// </summary>
    private static (NativeChipModulePackage Package, byte[]? SourceModuleData) ReadSourcePreservingPackage(byte[] bytes)
    {
        int offset = Magic.Length;
        if (offset < bytes.Length && bytes[offset] == 0)
            offset++;

        using var input = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: false);
        int jsonLength = reader.ReadInt32();
        int sourceLength = reader.ReadInt32();
        if (jsonLength <= 0 || jsonLength > input.Length - input.Position)
            throw new InvalidDataException("The native chip module metadata section is invalid.");

        byte[] jsonCompressed = reader.ReadBytes(jsonLength);
        byte[] json = Decompress(jsonCompressed);
        var package = JsonSerializer.Deserialize<NativeChipModulePackage>(json, Options)
            ?? throw new InvalidDataException("The native chip module is empty or invalid.");

        byte[]? sourceModuleData = null;
        if (sourceLength > 0)
        {
            if (sourceLength > input.Length - input.Position)
                throw new InvalidDataException("The native chip module source section is invalid.");
            sourceModuleData = Decompress(reader.ReadBytes(sourceLength));
        }

        return (package, sourceModuleData);
    }
}

/// <summary>
/// Represents the NativeChipModulePackage component.
/// </summary>
public sealed class NativeChipModulePackage
{
    /// <summary>
    /// Stores or exposes Version.
    /// </summary>
    public int Version { get; set; }
    /// <summary>
    /// Stores or exposes SavedUtc.
    /// </summary>
    public DateTimeOffset SavedUtc { get; set; }
    /// <summary>
    /// Stores or exposes SourceModuleFormat.
    /// </summary>
    public ModuleFormat SourceModuleFormat { get; set; } = ModuleFormat.Unknown;
    /// <summary>
    /// Stores or exposes SourceModuleType.
    /// </summary>
    public string SourceModuleType { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes SourceModuleExtension.
    /// </summary>
    public string SourceModuleExtension { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes SourceModuleBytes.
    /// </summary>
    public int SourceModuleBytes { get; set; }
    /// <summary>
    /// Stores or exposes Song.
    /// </summary>
    public Song? Song { get; set; }
}
