using amChipper.Core.Models;
using amChipper.Core.Persistence;
using AmcModulePlayer = amChipper.AmcPlayer.AmcModulePlayer;

namespace amChipper.Core.Tests;

public sealed class SongProjectSerializerTests
{
    [Fact]
    public void CreateDefaultSong_IsAudibleWithoutImportedSamples()
    {
        Song song = Song.CreateDefault();

        Assert.NotEmpty(song.Instruments);
        Assert.Contains(song.Instruments, instrument => instrument.SourceType == InstrumentSourceType.Synth);
        Assert.Contains(song.Instruments, instrument => instrument.SourceType == InstrumentSourceType.Sample);
        Assert.Contains(song.Instruments, instrument => instrument.Waveform == SynthWaveform.Square);
        Assert.Contains(song.Instruments, instrument => instrument.Waveform == SynthWaveform.Noise);
        Assert.Contains(song.Instruments, instrument => instrument.Samples.Count > 0);
        Assert.Equal(8, song.Tracks.Count);
    }

    [Fact]
    public void CreateDefaultSong_AppliesScratchProjectOptions()
    {
        Song song = Song.CreateDefault(new NewSongOptions
        {
            Title = "Scratch MOD",
            Format = ModuleFormat.MOD,
            Channels = 12,
            Patterns = 3,
            RowsPerPattern = 32,
            RowsPerBeat = 8,
            IncludeDefaultSamples = true
        });

        Assert.Equal("Scratch MOD", song.Title);
        Assert.Equal(ModuleFormat.MOD, song.Format);
        Assert.Equal(".mod", song.SourceModuleExtension);
        Assert.Equal(12, song.Tracks.Count);
        Assert.Equal(3, song.Patterns.Count);
        Assert.All(song.Patterns, pattern =>
        {
            Assert.Equal(32, pattern.RowCount);
            Assert.Equal(12, pattern.ChannelCount);
        });
        Assert.All(song.Tracks, track => Assert.NotEmpty(track.Blocks));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSongPatternAndArrangementData()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.amchip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Title = "Roundtrip";
            song.Tracks[0].Blocks.Clear();
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                Volume = 48,
                DurationTicks = 4
            });
            song.Tracks[0].Blocks.Add(new PatternBlock
            {
                PatternIndex = 0,
                StartBeat = 0,
                DurationBeats = 16
            });

            SongProjectSerializer.Save(song, path);
            Song loaded = SongProjectSerializer.Load(path);
            byte[] saved = File.ReadAllBytes(path);

            Assert.Equal("Roundtrip", loaded.Title);
            Assert.Equal(8, loaded.Tracks.Count);
            Assert.Single(loaded.Tracks[0].Blocks);
            Assert.Equal(60, loaded.Patterns[0].GetNote(0, 0).Pitch);
            Assert.Equal(InstrumentSourceType.Synth, loaded.Instruments[0].SourceType);
            Assert.True(saved.AsSpan(0, "AMCHIP2"u8.Length).SequenceEqual("AMCHIP2"u8));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Save_CompressesSampleHeavyNativeProject()
    {
        Song song = Song.CreateDefault(new NewSongOptions
        {
            Channels = 16,
            Patterns = 8,
            IncludeDefaultSamples = true
        });

        byte[] compressed = SongProjectSerializer.SaveToBytes(song);
        string legacyJson = System.Text.Json.JsonSerializer.Serialize(new SongProjectFile
        {
            Version = SongProjectSerializer.CurrentVersion,
            SavedUtc = DateTimeOffset.UtcNow,
            Song = song
        });

        Assert.True(compressed.AsSpan(0, "AMCHIP2"u8.Length).SequenceEqual("AMCHIP2"u8));
        Assert.True(compressed.Length < System.Text.Encoding.UTF8.GetByteCount(legacyJson));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOriginalModuleData()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.amchip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.OriginalModuleData = [0x46, 0x4C, 0x68, 0x64, 0x01, 0x02, 0x03];

            SongProjectSerializer.Save(song, path);
            Song loaded = SongProjectSerializer.Load(path);

            Assert.Equal(song.OriginalModuleData, loaded.OriginalModuleData);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void PatternResize_PreservesExistingCells()
    {
        var pattern = new Pattern(4, 1);
        pattern.SetNote(2, 0, new Note { Pitch = 64, InstrumentIndex = 1 });

        pattern.Resize(8, 2);

        Assert.Equal(8, pattern.RowCount);
        Assert.Equal(2, pattern.ChannelCount);
        Assert.Equal(64, pattern.GetNote(2, 0).Pitch);
        Assert.Equal(0, pattern.GetNote(2, 1).Pitch);
    }

    [Fact]
    public void NativeChipModule_RoundTripsAsInternalPlayableFormat()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.amc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions
            {
                Format = ModuleFormat.AmChip,
                Channels = 6,
                Patterns = 2,
                IncludeDefaultSamples = true
            });
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 48, DurationTicks = 4 });

            NativeChipModuleFile.Save(song, path);
            byte[] bytes = File.ReadAllBytes(path);
            Song loaded = NativeChipModuleFile.Load(path);

            Assert.True(NativeChipModuleFile.IsNativeChipModule(bytes));
            Assert.Equal(ModuleFormat.AmChip, loaded.Format);
            Assert.Equal("AMC", loaded.SourceModuleType);
            Assert.Equal(".amc", loaded.SourceModuleExtension);
            Assert.Null(loaded.OriginalModuleData);
            Assert.Equal(6, loaded.Tracks.Count);
            Assert.Equal(2, loaded.Patterns.Count);
            Assert.Equal(60, loaded.Patterns[0].GetNote(0, 0).Pitch);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void NativeChipModule_PreservesEmbeddedSourceModuleForExactPlayback()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.amc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions
            {
                Format = ModuleFormat.XM,
                Channels = 2,
                Patterns = 1
            });
            byte[] originalModule = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
            song.SourceModuleType = "XM";
            song.SourceModuleExtension = ".xm";
            song.OriginalModuleData = originalModule;
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                Effect = EffectCommand.Vibrato,
                EffectColumn = 0x04,
                EffectParam = 0x37,
                VolumeColumn = 0x40
            });

            NativeChipModuleFile.Save(song, path);
            Song loaded = NativeChipModuleFile.Load(path);

            Assert.Equal(ModuleFormat.XM, loaded.Format);
            Assert.Equal("XM", loaded.SourceModuleType);
            Assert.Equal(".xm", loaded.SourceModuleExtension);
            Assert.Equal(originalModule, loaded.OriginalModuleData);
            Assert.Equal(EffectCommand.Vibrato, loaded.Patterns[0].GetNote(0, 0).Effect);
            Assert.Equal(0x04, loaded.Patterns[0].GetNote(0, 0).EffectColumn);
            Assert.Equal(0x37, loaded.Patterns[0].GetNote(0, 0).EffectParam);
            Assert.Equal(0x40, loaded.Patterns[0].GetNote(0, 0).VolumeColumn);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void AmcModulePlayer_LoadsNativeModuleAndRendersAudiblePcm()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.amc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions
            {
                Format = ModuleFormat.AmChip,
                Channels = 4,
                Patterns = 1,
                IncludeDefaultSamples = false
            });
            song.Title = "AMC Player Test";
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                Volume = 64,
                Velocity = 127,
                DurationTicks = 4
            });

            NativeChipModuleFile.Save(song, path);

            using var player = new AmcModulePlayer();
            var info = player.Load(path);
            var buffer = new float[44100 * 2];
            player.Play();
            int frames = player.Render(buffer, 44100);
            float peak = buffer.Max(Math.Abs);

            Assert.Equal("amChipper AMC (.amc)", info.ContainerFormat);
            Assert.Equal("internal", info.EmbeddedSourceFormat);
            Assert.Equal(4, info.Channels);
            Assert.True(frames > 0);
            Assert.True(peak > 0.001f);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
