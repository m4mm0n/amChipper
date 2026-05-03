using System.Text;
using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.Core.Tests;

public sealed class ChipTuneFileTests
{
    [Fact]
    public void ReadsSidMetadataAndCreatesPreservedSong()
    {
        byte[] sid = new byte[0x80];
        Encoding.ASCII.GetBytes("PSID").CopyTo(sid, 0);
        sid[0x0E] = 0;
        sid[0x0F] = 3;
        sid[0x10] = 0;
        sid[0x11] = 2;
        WriteFixed(sid, 0x16, "Cybernoid");
        WriteFixed(sid, 0x36, "Jeroen Tel");
        WriteFixed(sid, 0x56, "1988");

        var metadata = ChipTuneFile.ReadMetadata(sid, "Cybernoid.sid");
        var song = ChipTuneFile.ImportAsSong(sid, "Cybernoid.sid");

        Assert.Equal(ModuleFormat.SID, metadata.Format);
        Assert.Equal("PSID", metadata.Type);
        Assert.Equal("Cybernoid", metadata.Title);
        Assert.Equal(3, metadata.SongCount);
        Assert.Equal(2, metadata.StartSong);
        Assert.Equal(0x0076, metadata.DataOffset);
        Assert.Equal("Unknown", metadata.Clock);
        Assert.Equal(ModuleFormat.SID, song.Format);
        Assert.Equal(".sid", song.SourceModuleExtension);
        Assert.Equal(sid, song.OriginalModuleData);
        Assert.Equal(4, song.Tracks.Count);
        Assert.Contains("Load=$0000", song.Comment);
    }

    [Fact]
    public void ReadsNsfMetadataAndCreatesPreservedSong()
    {
        byte[] nsf = new byte[0x90];
        Encoding.ASCII.GetBytes("NESM\x1A").CopyTo(nsf, 0);
        nsf[0x06] = 5;
        nsf[0x07] = 1;
        WriteFixed(nsf, 0x0E, "Duck Tales");
        WriteFixed(nsf, 0x2E, "Capcom");
        WriteFixed(nsf, 0x4E, "1989");

        var metadata = ChipTuneFile.ReadMetadata(nsf, "ducktales.nsf");
        var song = ChipTuneFile.ImportAsSong(nsf, "ducktales.nsf");

        Assert.Equal(ModuleFormat.NSF, metadata.Format);
        Assert.Equal("NSF", metadata.Type);
        Assert.Equal("Duck Tales", metadata.Title);
        Assert.Equal(5, metadata.SongCount);
        Assert.Equal(ModuleFormat.NSF, song.Format);
        Assert.Equal(".nsf", song.SourceModuleExtension);
        Assert.Equal(nsf, song.OriginalModuleData);
    }

    [Fact]
    public void NsfeImportConvertsChunksForRenderer()
    {
        byte[] nsfe = CreateNsfeFromNsf(CreateNsf());

        var metadata = ChipTuneFile.ReadMetadata(nsfe, "chunked.nsfe");
        var song = ChipTuneFile.ImportAsSong(nsfe, "chunked.nsfe");
        float[] samples = InternalChipRenderer.RenderStereoFloat(nsfe, "chunked.nsfe", seconds: 1, sampleRate: 8000);

        Assert.Equal(ModuleFormat.NSF, metadata.Format);
        Assert.Equal("NSFE", metadata.Type);
        Assert.Equal(ModuleFormat.NSF, song.Format);
        Assert.Equal(".nsfe", song.SourceModuleExtension);
        Assert.Contains("converted from NSFE", song.Comment);
        Assert.True(samples.Max(sample => Math.Abs(sample)) > 0.0001f);
    }

    [Theory]
    [InlineData("sid")]
    [InlineData("nsf")]
    public void InternalRendererWritesPlayableWav(string kind)
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.wav");
        byte[] data = kind == "sid" ? CreateSid() : CreateNsf();

        try
        {
            InternalChipRenderer.RenderToWav(data, $"test.{kind}", path, seconds: 1, sampleRate: 8000);

            byte[] header = File.ReadAllBytes(path);
            Assert.True(header.Length > 44);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
            Assert.Contains(header.Skip(44), b => b != 0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void SidFrequencyRegisterUsesSidClockRatio()
    {
        double hz = InternalChipRenderer.SidFrequencyRegisterToHzForTests(7492, pal: true);

        Assert.InRange(hz, 439.0, 441.0);
    }

    [Fact]
    public void SidRendererSupportsRelativePlayAddressAndImmediateAlu()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.wav");
        byte[] sid = CreateRelativePlaySid();

        try
        {
            InternalChipRenderer.RenderToWav(sid, "relative.sid", wavPath, seconds: 1, sampleRate: 8000);
            byte[] wav = File.ReadAllBytes(wavPath);

            Assert.True(MeasurePcm16Peak(wav) > 800);
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    [Fact]
    public void SidImportBuildsVisibleVoiceRowsFromRegisterWrites()
    {
        byte[] sid = CreateRelativePlaySid();

        var song = ChipTuneFile.ImportAsSong(sid, "relative.sid");

        Assert.Equal(4, song.Tracks.Count);
        Assert.InRange(song.OrderList.Count, 1, 32);
        Assert.InRange(song.Patterns.Count, 1, song.OrderList.Count);
        Assert.Equal("SID Voice 1", song.Tracks[0].Name);
        Assert.Equal("SID Filter / D418", song.Tracks[3].Name);
        Assert.Contains(song.Tracks[0].Blocks, block => block.StartBeat > 0);
        Assert.Contains(
            song.Patterns.SelectMany(pattern => Enumerable.Range(0, pattern.RowCount).Select(row => pattern.GetNote(row, 0))),
            note => note.Pitch is > 0 and < (byte)SpecialNote.NoteOff);
        Assert.Contains(
            song.Patterns.SelectMany(pattern => pattern.Notes),
            note => note.Pitch == (byte)SpecialNote.NoteOff);
        Assert.Contains(
            song.Patterns.SelectMany(pattern => Enumerable.Range(0, pattern.RowCount).Select(row => pattern.GetNote(row, 3))),
            note => note.Effect == EffectCommand.SetGlobalVol || note.VolumeColumn != 0 || note.EffectParam != 0);
        Assert.Contains("Init=$1000", song.Comment);
        Assert.Contains("Play=$1003", song.Comment);
        Assert.Contains("traced rows", song.Comment);
    }

    [Fact]
    public void SidImportCreatesTracePatternGroupsForSubtunes()
    {
        byte[] sid = CreateRelativePlaySid();
        sid[0x0E] = 0;
        sid[0x0F] = 3;

        var song = ChipTuneFile.ImportAsSong(sid, "multi.sid");

        Assert.InRange(song.OrderList.Count, 3, 96);
        Assert.InRange(song.Patterns.Count, 1, song.OrderList.Count);
        Assert.Contains(song.Patterns, pattern => pattern.Name.StartsWith("SID S01 Phrase ", StringComparison.Ordinal));
        Assert.Contains(song.Patterns, pattern => pattern.Name.StartsWith("SID S02 Phrase ", StringComparison.Ordinal));
        Assert.Contains(song.Patterns, pattern => pattern.Name.StartsWith("SID S03 Phrase ", StringComparison.Ordinal));
        Assert.Contains(song.Tracks[0].Blocks, block => block.StartBeat > 8);
    }

    [Fact]
    public void NsfImportBuildsVisibleApuTraceRows()
    {
        byte[] nsf = CreateNsf();

        var song = ChipTuneFile.ImportAsSong(nsf, "trace.nsf");

        Assert.Equal(ModuleFormat.NSF, song.Format);
        Assert.Equal(28, song.Tracks.Count);
        Assert.Equal("2A03 Pulse 1", song.Tracks[0].Name);
        Assert.Equal("VRC7 FM 1", song.Tracks[8].Name);
        Assert.Equal("FDS Wavetable", song.Tracks[27].Name);
        Assert.NotEmpty(song.OrderList);
        Assert.NotEmpty(song.Patterns);
        Assert.Contains(song.Tracks[0].Blocks, block => block.PatternIndex >= 0);
        Assert.Contains(
            song.Patterns.SelectMany(pattern => pattern.Notes),
            note => note.Pitch is > 0 and < (byte)SpecialNote.NoteOff && note.InstrumentIndex > 0);
        Assert.Contains("NSF trace import", song.Comment);
    }

    [Fact]
    public void NsfStreamingRendererProducesBoundedAudibleChunks()
    {
        byte[] nsf = CreateNsf();
        var renderer = InternalChipRenderer.CreateStreamingRenderer(nsf, "stream.nsf", 44100);
        float[] buffer = new float[4096 * 2];

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 12; i++)
            renderer.Render(buffer, 4096, 2);
        watch.Stop();

        float peak = buffer.Max(sample => Math.Abs(sample));
        Assert.Equal(ModuleFormat.NSF, renderer.Format);
        Assert.Equal(44100, renderer.SampleRate);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"NSF stream chunk render took {watch.ElapsedMilliseconds}ms.");
        Assert.True(peak > 0.0001f, $"Expected audible NSF stream peak, got {peak}.");
    }

    [Fact]
    public void InternalRendererRunsIncludedPsidProgram()
    {
        string sidPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "64s_2nd_Choice.sid"));
        if (!File.Exists(sidPath))
            return;

        string wavPath = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.wav");
        byte[] data = File.ReadAllBytes(sidPath);

        try
        {
            var metadata = ChipTuneFile.ReadMetadata(data, sidPath);
            InternalChipRenderer.RenderToWav(data, sidPath, wavPath, seconds: 2, sampleRate: 8000);

            byte[] wav = File.ReadAllBytes(wavPath);
            Assert.Equal(ModuleFormat.SID, metadata.Format);
            Assert.Equal("64's 2nd Choice", metadata.Title);
            Assert.True(wav.Length > 32000);
            Assert.Contains(wav.Skip(44), b => b != 0);
            Assert.True(MeasurePcm16Peak(wav) > 1200);
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    [Fact]
    public void InternalRendererRunsAllIncludedPsidPrograms()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string[] paths = Directory.GetFiles(root, "*.sid")
            .Where(path => File.ReadAllBytes(path).Take(4).SequenceEqual(Encoding.ASCII.GetBytes("PSID")) ||
                           File.ReadAllBytes(path).Take(4).SequenceEqual(Encoding.ASCII.GetBytes("RSID")))
            .ToArray();

        foreach (string sidPath in paths)
        {
            string wavPath = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.wav");
            try
            {
                byte[] data = File.ReadAllBytes(sidPath);
                InternalChipRenderer.RenderToWav(data, sidPath, wavPath, seconds: 1, sampleRate: 8000);
                byte[] wav = File.ReadAllBytes(wavPath);
                Assert.True(MeasurePcm16Peak(wav) > 800, Path.GetFileName(sidPath));
            }
            finally
            {
                if (File.Exists(wavPath))
                    File.Delete(wavPath);
            }
        }
    }

    [Fact]
    public void InternalRendererRunsRepresentativeIncludedSidCorpus()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "c64"));
        if (!Directory.Exists(root))
            return;

        string[] paths = Directory.GetFiles(root, "*.sid", SearchOption.AllDirectories)
            .Where(IsPsidOrRsid)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToArray();

        Assert.NotEmpty(paths);
        foreach (string sidPath in paths)
        {
            string wavPath = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.wav");
            try
            {
                byte[] data = File.ReadAllBytes(sidPath);
                InternalChipRenderer.RenderToWav(data, sidPath, wavPath, seconds: 1, sampleRate: 8000);
                byte[] wav = File.ReadAllBytes(wavPath);
                Assert.True(MeasurePcm16Peak(wav) > 500, Path.GetRelativePath(root, sidPath));
            }
            finally
            {
                if (File.Exists(wavPath))
                    File.Delete(wavPath);
            }
        }
    }

    private static bool IsPsidOrRsid(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(4).ToArray();
        return header.SequenceEqual(Encoding.ASCII.GetBytes("PSID")) ||
               header.SequenceEqual(Encoding.ASCII.GetBytes("RSID"));
    }

    [Fact]
    public void PowerPackerSidFailsClearly()
    {
        byte[] data = [0x50, 0x50, 0x32, 0x30, 0, 0, 0, 0];

        var ex = Assert.Throws<InvalidDataException>(() => ChipTuneFile.ReadMetadata(data, "packed.sid"));

        Assert.Contains("PowerPacker", ex.Message);
    }

    private static void WriteFixed(byte[] data, int offset, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, data, offset, Math.Min(bytes.Length, 32));
    }

    private static int MeasurePcm16Peak(byte[] wav)
    {
        int peak = 0;
        for (int i = 44; i + 1 < wav.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(wav, i);
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static byte[] CreateSid()
    {
        byte[] sid = new byte[0x100];
        Encoding.ASCII.GetBytes("PSID").CopyTo(sid, 0);
        sid[0x0E] = 0;
        sid[0x0F] = 1;
        sid[0x10] = 0;
        sid[0x11] = 1;
        WriteFixed(sid, 0x16, "Render SID");
        for (int i = 0x80; i < sid.Length; i++)
            sid[i] = (byte)(i * 17);
        return sid;
    }

    private static byte[] CreateRelativePlaySid()
    {
        byte[] sid = new byte[0x120];
        Encoding.ASCII.GetBytes("PSID").CopyTo(sid, 0);
        sid[0x04] = 0;
        sid[0x05] = 2;
        sid[0x06] = 0;
        sid[0x07] = 0x7C;
        sid[0x0C] = 0;
        sid[0x0D] = 3;
        sid[0x0E] = 0;
        sid[0x0F] = 1;
        sid[0x10] = 0;
        sid[0x11] = 1;
        WriteFixed(sid, 0x16, "Relative Play");
        sid[0x7C] = 0;
        sid[0x7D] = 0x10;

        byte[] program =
        [
            0x60, 0xEA, 0xEA,
            0xA9, 0x34, 0x8D, 0x00, 0xD4,
            0xA9, 0x12, 0x8D, 0x01, 0xD4,
            0xA9, 0xF0, 0x8D, 0x05, 0xD4,
            0xA9, 0xF0, 0x8D, 0x06, 0xD4,
            0xA9, 0x0F, 0x29, 0xFF, 0x8D, 0x18, 0xD4,
            0xA9, 0x41, 0x8D, 0x04, 0xD4,
            0x60
        ];
        program.CopyTo(sid, 0x7E);
        return sid;
    }

    private static byte[] CreateNsf()
    {
        byte[] nsf = new byte[0x100];
        Encoding.ASCII.GetBytes("NESM\x1A").CopyTo(nsf, 0);
        nsf[0x06] = 1;
        nsf[0x07] = 1;
        nsf[0x08] = 0x00;
        nsf[0x09] = 0x80;
        nsf[0x0A] = 0x00;
        nsf[0x0B] = 0x80;
        nsf[0x0C] = 0x20;
        nsf[0x0D] = 0x80;
        WriteFixed(nsf, 0x0E, "Render NSF");
        byte[] init =
        [
            0xA9, 0x01, 0x8D, 0x15, 0x40,
            0xA9, 0xBF, 0x8D, 0x00, 0x40,
            0xA9, 0xFD, 0x8D, 0x02, 0x40,
            0xA9, 0x07, 0x8D, 0x03, 0x40,
            0x60
        ];
        byte[] play =
        [
            0xAD, 0x02, 0x40,
            0x18,
            0x69, 0x01,
            0x8D, 0x02, 0x40,
            0x60
        ];

        init.CopyTo(nsf, 0x80);
        play.CopyTo(nsf, 0xA0);
        return nsf;
    }

    private static byte[] CreateNsfeFromNsf(byte[] nsf)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("NSFE"));

        byte[] info = new byte[10];
        info[0] = nsf[0x08];
        info[1] = nsf[0x09];
        info[2] = nsf[0x0A];
        info[3] = nsf[0x0B];
        info[4] = nsf[0x0C];
        info[5] = nsf[0x0D];
        info[6] = 0;
        info[7] = nsf[0x7B];
        info[8] = nsf[0x06];
        info[9] = 0;
        AddNsfeChunk(bytes, "INFO", info);
        AddNsfeChunk(bytes, "DATA", nsf[0x80..]);
        AddNsfeChunk(bytes, "NEND", []);
        return bytes.ToArray();
    }

    private static void AddNsfeChunk(List<byte> bytes, string id, byte[] payload)
    {
        bytes.Add((byte)(payload.Length & 0xFF));
        bytes.Add((byte)((payload.Length >> 8) & 0xFF));
        bytes.Add((byte)((payload.Length >> 16) & 0xFF));
        bytes.Add((byte)((payload.Length >> 24) & 0xFF));
        bytes.AddRange(Encoding.ASCII.GetBytes(id));
        bytes.AddRange(payload);
    }
}
