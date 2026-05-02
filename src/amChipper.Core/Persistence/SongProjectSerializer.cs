using System.IO.Compression;
using System.Text.Json;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the SongProjectSerializer component.
/// </summary>
public static class SongProjectSerializer
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    public const int CurrentVersion = 2;
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    public const string Extension = ".amchip";
    /// <summary>
    /// Executes the Magic operation.
    /// </summary>
    private static readonly byte[] Magic = "AMCHIP2"u8.ToArray();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Executes the Save operation.
    /// </summary>
    public static void Save(Song song, string path)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Normalize(song);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var project = new SongProjectFile
        {
            Version = CurrentVersion,
            SavedUtc = DateTimeOffset.UtcNow,
            Song = song
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        using var file = File.Create(path);
        file.Write(Magic);
        file.WriteByte(0);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize, leaveOpen: false);
        brotli.Write(json);
    }

    /// <summary>
    /// Executes the Load operation.
    /// </summary>
    public static Song Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes = File.ReadAllBytes(path);
        string json = IsCompressedProject(bytes)
            ? DecompressProjectJson(bytes)
            : System.Text.Encoding.UTF8.GetString(bytes);

        var project = JsonSerializer.Deserialize<SongProjectFile>(json, Options)
            ?? throw new InvalidDataException("The project file is empty or invalid.");

        if (project.Version < 1 || project.Version > CurrentVersion)
            throw new InvalidDataException($"Unsupported amChipper project version: {project.Version}.");

        var song = project.Song ?? throw new InvalidDataException("The project file does not contain a song.");
        Normalize(song);
        return song;
    }

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
    /// Executes the IsCompressedProject operation.
    /// </summary>
    private static bool IsCompressedProject(byte[] bytes) =>
        bytes.Length > Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>
    /// Executes the DecompressProjectJson operation.
    /// </summary>
    private static string DecompressProjectJson(byte[] bytes)
    {
        int offset = Magic.Length;
        if (offset < bytes.Length && bytes[offset] == 0)
            offset++;

        using var input = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// Executes the Normalize operation.
    /// </summary>
    public static void Normalize(Song song)
    {
        song.Title = string.IsNullOrWhiteSpace(song.Title) ? "Untitled" : song.Title;
        song.SourceModuleType = song.SourceModuleType?.Trim() ?? string.Empty;
        song.SourceModuleExtension = ModuleFormatCatalog.NormalizeExtension(song.SourceModuleExtension);
        song.Bpm = Math.Clamp(song.Bpm, 6, 999);
        song.RowsPerBeat = Math.Clamp(song.RowsPerBeat, 1, 32);
        song.DefaultRowsPerPattern = Math.Clamp(song.DefaultRowsPerPattern, 1, 512);
        song.InitialSpeed = Math.Clamp(song.InitialSpeed, 1, 31);
        song.RestartOrder = song.RestartOrder < 0 ? -1 : song.RestartOrder;

        song.Instruments ??= [];
        song.Patterns ??= [];
        song.Tracks ??= [];
        song.OrderList ??= [];

        foreach (var instrument in song.Instruments)
        {
            instrument.Samples ??= [];
            instrument.VolumeEnvelope ??= [];
            instrument.PanEnvelope ??= [];
            instrument.NoteMap = NormalizeNoteMap(instrument.NoteMap);
            instrument.PulseWidth = Math.Clamp(instrument.PulseWidth, 0.05, 0.95);
            instrument.RootNote = (byte)Math.Clamp((int)instrument.RootNote, 0, 127);
            instrument.FineTuneCents = Math.Clamp(instrument.FineTuneCents, -1200, 1200);
            instrument.MaxPolyphony = Math.Clamp(instrument.MaxPolyphony, 0, 256);
            instrument.PortaTimeMs = Math.Clamp(instrument.PortaTimeMs, 0, 5000);
            instrument.DelayMs = Math.Clamp(instrument.DelayMs, 0, 10000);
            instrument.AttackMs = Math.Clamp(instrument.AttackMs, 0, 10000);
            instrument.HoldMs = Math.Clamp(instrument.HoldMs, 0, 10000);
            instrument.DecayMs = Math.Clamp(instrument.DecayMs, 0, 10000);
            instrument.ReleaseMs = Math.Clamp(instrument.ReleaseMs, 0, 10000);
            instrument.SustainLevel = (byte)Math.Clamp((int)instrument.SustainLevel, 0, 128);
            instrument.LfoAmount = Math.Clamp(instrument.LfoAmount, 0, 24);
            instrument.LfoSpeedHz = Math.Clamp(instrument.LfoSpeedHz, 0, 64);
            instrument.ArpRange = Math.Clamp(instrument.ArpRange, 0, 48);
            instrument.ArpRepeat = Math.Clamp(instrument.ArpRepeat, 1, 32);
            instrument.EchoFeedback = Math.Clamp(instrument.EchoFeedback, 0, 0.98);
            instrument.EchoTimeMs = Math.Clamp(instrument.EchoTimeMs, 0, 4000);
            instrument.FilterCutoff = Math.Clamp(instrument.FilterCutoff, 0, 1);
            instrument.FilterResonance = Math.Clamp(instrument.FilterResonance, 0, 1);
        }

        int channelCount = Math.Max(1, song.Tracks.Count);
        foreach (var pattern in song.Patterns)
        {
            if (pattern.ChannelCount < channelCount)
                pattern.Resize(pattern.RowCount, channelCount);
            pattern.EnsureStorage();
        }

        foreach (var track in song.Tracks)
        {
            track.Blocks ??= [];
            track.VolumeAutomation ??= [];
            track.PanAutomation ??= [];
            foreach (var block in track.Blocks)
            {
                block.VolumeAutomation ??= [];
                block.PanAutomation ??= [];
            }
        }

        song.OrderList.RemoveAll(i => i < 0 || i >= song.Patterns.Count);
        if (song.Patterns.Count > 0 && song.OrderList.Count == 0)
            song.OrderList.Add(0);
    }

    /// <summary>
    /// Executes the NormalizeNoteMap operation.
    /// </summary>
    private static byte[] NormalizeNoteMap(byte[]? noteMap)
    {
        var normalized = Enumerable.Repeat((byte)255, 128).ToArray();
        if (noteMap is null) return normalized;

        int count = Math.Min(normalized.Length, noteMap.Length);
        Array.Copy(noteMap, normalized, count);
        return normalized;
    }
}

/// <summary>
/// Represents the SongProjectFile component.
/// </summary>
public sealed class SongProjectFile
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
    /// Stores or exposes Song.
    /// </summary>
    public Song? Song { get; set; }
}
