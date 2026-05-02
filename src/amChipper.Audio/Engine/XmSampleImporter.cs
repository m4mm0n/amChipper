using System.Buffers.Binary;
using System.Text;
using amChipper.Core.Models;

namespace amChipper.Audio.Engine;

/// <summary>
/// Represents the XmSampleImporter component.
/// </summary>
internal static class XmSampleImporter
{
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    private const string Signature = "Extended Module: ";
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int C4SampleRate = 8363;

    /// <summary>
    /// Executes the Parse operation.
    /// </summary>
    public static XmSampleImport Parse(byte[] data, string? sourceName)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        if (stream.Length < 80)
            throw new FormatException($"{sourceName ?? "xm"}: File is too small to be a FastTracker XM module.");

        string signature = Encoding.ASCII.GetString(reader.ReadBytes(17));
        if (!signature.Equals(Signature, StringComparison.Ordinal))
            throw new FormatException($"{sourceName ?? "xm"}: Invalid FastTracker XM signature.");

        _ = ReadFixedString(reader, 20);
        byte marker = reader.ReadByte();
        if (marker != 0x1A)
            throw new FormatException($"{sourceName ?? "xm"}: Invalid XM header marker.");

        _ = ReadFixedString(reader, 20);
        _ = reader.ReadUInt16();
        uint headerSize = reader.ReadUInt32();
        long headerStart = stream.Position - sizeof(uint);
        ushort songLength = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        ushort channels = reader.ReadUInt16();
        ushort patternCount = reader.ReadUInt16();
        ushort instrumentCount = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadBytes(256);

        Require(songLength is > 0 and <= 256, sourceName, "XM song length must be between 1 and 256.");
        Require(channels is > 0 and <= 64, sourceName, "XM channel count must be between 1 and 64.");
        Require(patternCount > 0, sourceName, "XM must contain at least one pattern.");

        stream.Position = headerStart + headerSize;

        for (int patternIndex = 0; patternIndex < patternCount; patternIndex++)
            SkipPattern(reader, sourceName);

        var instruments = new List<XmImportedInstrument>(instrumentCount);
        for (int instrumentIndex = 0; instrumentIndex < instrumentCount; instrumentIndex++)
            instruments.Add(ReadInstrument(reader, sourceName));

        return new XmSampleImport(instruments);
    }

    /// <summary>
    /// Executes the SkipPattern operation.
    /// </summary>
    private static void SkipPattern(BinaryReader reader, string? sourceName)
    {
        long headerStart = reader.BaseStream.Position;
        uint headerLength = reader.ReadUInt32();
        _ = reader.ReadByte();
        _ = reader.ReadUInt16();
        ushort packedSize = reader.ReadUInt16();

        reader.BaseStream.Position = headerStart + headerLength;
        reader.BaseStream.Position += packedSize;
        if (reader.BaseStream.Position > reader.BaseStream.Length)
            throw new FormatException($"{sourceName ?? "xm"}: XM pattern data is truncated.");
    }

    /// <summary>
    /// Executes the ReadInstrument operation.
    /// </summary>
    private static XmImportedInstrument ReadInstrument(BinaryReader reader, string? sourceName)
    {
        long headerStart = reader.BaseStream.Position;
        uint instrumentHeaderSize = reader.ReadUInt32();
        string name = ReadFixedString(reader, 22);
        _ = reader.ReadByte();
        ushort sampleCount = reader.ReadUInt16();
        if (sampleCount == 0)
        {
            reader.BaseStream.Position = headerStart + instrumentHeaderSize;
            return new XmImportedInstrument(name, new byte[96], []);
        }

        uint sampleHeaderSize = reader.ReadUInt32();
        byte[] sampleMap = reader.ReadBytes(96);
        reader.BaseStream.Position = headerStart + instrumentHeaderSize;

        var headers = new List<XmSampleHeader>(sampleCount);
        for (int index = 0; index < sampleCount; index++)
            headers.Add(ReadSampleHeader(reader, sampleHeaderSize));

        var samples = new List<Sample>(sampleCount);
        for (int index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            if (reader.BaseStream.Position + header.Length > reader.BaseStream.Length)
                throw new FormatException($"{sourceName ?? "xm"}: XM sample data is truncated.");

            byte[] deltaData = reader.ReadBytes(header.Length);
            float[] decoded = DecodeSampleData(deltaData, header.Type);
            bool is16Bit = (header.Type & 0x10) != 0;
            int loopStart = is16Bit ? header.LoopStart / 2 : header.LoopStart;
            int loopLength = is16Bit ? header.LoopLength / 2 : header.LoopLength;

            samples.Add(new Sample
            {
                Name = string.IsNullOrWhiteSpace(header.Name) ? $"{name} sample {index + 1}" : header.Name,
                Data = ToPcm16(decoded),
                SampleRate = C4SampleRate,
                Channels = 1,
                BitsPerSample = 16,
                Looped = (header.Type & 0x03) != 0 && loopLength > 2 && loopStart >= 0 && loopStart + loopLength <= decoded.Length,
                PingPongLoop = (header.Type & 0x02) != 0,
                LoopStart = Math.Clamp(loopStart, 0, Math.Max(decoded.Length - 1, 0)),
                LoopEnd = Math.Clamp(loopStart + loopLength, 0, decoded.Length),
                BaseNote = (byte)Math.Clamp(60 - header.RelativeNote, 0, 127),
                FineTune = (int)Math.Round(header.FineTune * 100.0 / 128.0),
                RelativeVolume = (byte)Math.Clamp((int)Math.Round(header.Volume / 64.0 * 255.0), 0, 255),
                RelativePanning = (byte)Math.Clamp(header.Panning, 0, 255)
            });
        }

        return new XmImportedInstrument(name, sampleMap, samples);
    }

    /// <summary>
    /// Executes the ReadSampleHeader operation.
    /// </summary>
    private static XmSampleHeader ReadSampleHeader(BinaryReader reader, uint sampleHeaderSize)
    {
        long start = reader.BaseStream.Position;
        int length = checked((int)reader.ReadUInt32());
        int loopStart = checked((int)reader.ReadUInt32());
        int loopLength = checked((int)reader.ReadUInt32());
        int volume = reader.ReadByte();
        sbyte fineTune = reader.ReadSByte();
        byte type = reader.ReadByte();
        int panning = reader.ReadByte();
        sbyte relativeNote = reader.ReadSByte();
        _ = reader.ReadByte();
        string name = ReadFixedString(reader, 22);
        reader.BaseStream.Position = start + sampleHeaderSize;
        return new XmSampleHeader(length, loopStart, loopLength, volume, fineTune, type, panning, relativeNote, name);
    }

    /// <summary>
    /// Executes the DecodeSampleData operation.
    /// </summary>
    private static float[] DecodeSampleData(byte[] data, byte type)
    {
        if (data.Length == 0)
            return [];

        if ((type & 0x10) != 0)
        {
            var samples = new float[data.Length / 2];
            int previous = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                short delta = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(index * 2, 2));
                previous = unchecked((short)(previous + delta));
                samples[index] = previous / 32768.0f;
            }

            return samples;
        }

        var result = new float[data.Length];
        int accumulator = 0;
        for (int index = 0; index < data.Length; index++)
        {
            accumulator = unchecked((sbyte)(accumulator + (sbyte)data[index]));
            result[index] = accumulator / 128.0f;
        }

        return result;
    }

    /// <summary>
    /// Executes the ToPcm16 operation.
    /// </summary>
    private static byte[] ToPcm16(float[] decoded)
    {
        var data = new byte[decoded.Length * 2];
        for (int i = 0; i < decoded.Length; i++)
        {
            short value = (short)Math.Clamp(
                (int)Math.Round(decoded[i] * short.MaxValue),
                short.MinValue,
                short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), value);
        }

        return data;
    }

    /// <summary>
    /// Executes the ReadFixedString operation.
    /// </summary>
    private static string ReadFixedString(BinaryReader reader, int length)
        => Encoding.ASCII.GetString(reader.ReadBytes(length)).TrimEnd('\0', ' ');

    /// <summary>
    /// Executes the Require operation.
    /// </summary>
    private static void Require(bool condition, string? sourceName, string message)
    {
        if (!condition)
            throw new FormatException($"{sourceName ?? "xm"}: {message}");
    }

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct XmSampleHeader(
        int Length,
        int LoopStart,
        int LoopLength,
        int Volume,
        sbyte FineTune,
        byte Type,
        int Panning,
        sbyte RelativeNote,
        string Name);
}

/// <summary>
/// Carries XmSampleImport data.
/// </summary>
internal sealed record XmSampleImport(IReadOnlyList<XmImportedInstrument> Instruments);

/// <summary>
/// Carries XmImportedInstrument data.
/// </summary>
internal sealed record XmImportedInstrument(
    string Name,
    byte[] SampleMap,
    IReadOnlyList<Sample> Samples);
