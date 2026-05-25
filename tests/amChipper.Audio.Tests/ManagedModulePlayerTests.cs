using System.Text;
using amChipper.Audio.Engine;
using amChipper.Core.Interfaces;
using NAudio.Wave;

namespace amChipper.Audio.Tests;

public sealed class ManagedModulePlayerTests
{
    [Fact]
    public void ManagedBackendRendersLocalTrackerModulesWithoutGaps()
    {
        string root = FindRepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "Outlive no2.xm"),
            Path.Combine(root, "her10.mod")
        ];

        var renderedModules = 0;
        foreach (string file in files.Where(File.Exists))
        {
            byte[] data = File.ReadAllBytes(file);
            using var managed = new ManagedModulePlayer(44_100);

            RenderStats stats = RenderStatsFor(managed, data, file);

            Assert.True(stats.Loaded, $"{Path.GetFileName(file)} did not load through the managed backend.");
            Assert.Equal(44_100 * 8, stats.FramesRendered);
            Assert.InRange(stats.Peak, 0.01f, 2.0f);
            Assert.InRange(stats.Rms, 0.01, 1.5);
            Assert.InRange(stats.ZeroCrossings, 100, 20_000);
            Assert.InRange(stats.RealtimeRatio, 0.0, 0.25);
            renderedModules++;
        }

        if (renderedModules == 0)
            return;
    }

    /// <summary>
    /// Ensures managed XM imports keep real sample data for editable preview playback.
    /// </summary>
    [Fact]
    public void ManagedBackendImportsXmSampleDataForEditablePreview()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OneDrive",
            "Dokumenter",
            "My FTPRush Downloads",
            "Svenzzon",
            "blizzard.xm");

        if (!File.Exists(path))
            return;

        using var managed = new ManagedModulePlayer(44_100);
        Assert.True(managed.Load(File.ReadAllBytes(path), path));

        var song = managed.ImportAsSong();

        Assert.NotNull(song);
        Assert.True(song.Instruments.Sum(static instrument => instrument.Samples.Count) > 0);
        Assert.Contains(song.Instruments, static instrument => instrument.SourceType == amChipper.Core.Models.InstrumentSourceType.Sample);
    }

    [Fact]
    public void FactoryCreatesManagedBackendAndRendersDemoModuleWhenRequested()
    {
        string? previous = Environment.GetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable, ModulePlayerFactory.ManagedBackendName);

            using IModulePlayer player = ModulePlayerFactory.Create(44_100);
            Assert.Equal(ModulePlayerFactory.ManagedBackendName, player.BackendName);
            Assert.True(player.Load(DemoModuleFactory.CreateAudibleMod(), "managed-demo.mod"));

            var buffer = new float[2048 * 2];
            int frames = player.Render(buffer, 2048);

            Assert.True(frames > 0);
            Assert.Contains(buffer.Take(frames * 2), sample => Math.Abs(sample) > 0.0001f);

            var imported = player.ImportAsSong();
            Assert.NotNull(imported);
            Assert.NotEmpty(imported.Patterns);
            Assert.NotEmpty(imported.Tracks);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void AudioEngineWaveProviderFillsLargeModuleBufferRequests()
    {
        string? previous = Environment.GetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable, ModulePlayerFactory.ManagedBackendName);

            using var engine = new AudioEngine();
            Assert.Equal(ModulePlayerFactory.ManagedBackendName, engine.ModulePlayer.BackendName);
            Assert.True(engine.ModulePlayer.Load(DemoModuleFactory.CreateAudibleMod(), "managed-large-buffer.mod"));
            engine.UseModulePlayer = true;

            var provider = CreateWaveProvider(engine, 44_100, 2);
            const int requestedFrames = 8_820;
            var buffer = new byte[requestedFrames * 2 * sizeof(float)];

            int bytesRead = provider.Read(buffer, 0, buffer.Length);

            Assert.Equal(buffer.Length, bytesRead);
            Assert.Contains(ReadFloatSamples(buffer, bytesRead), sample => Math.Abs(sample) > 0.0001f);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ModulePlayerFactory.BackendEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ManagedBackendSeekToOrderUsesExactOrderRow()
    {
        using var player = new ManagedModulePlayer(44_100);
        Assert.True(player.Load(DemoModuleFactory.CreateTwoOrderAudibleMod(), "managed-two-order.mod"));

        player.SeekToOrder(1, 3);

        Assert.Equal(1, player.CurrentOrder);
        Assert.Equal(3, player.CurrentRow);

        var buffer = new float[512];
        int frames = player.Render(buffer, 256);

        Assert.Equal(256, frames);
        Assert.Equal(1, player.CurrentOrder);
        Assert.Equal(3, player.CurrentRow);
        Assert.Contains(buffer.Take(frames * 2), sample => Math.Abs(sample) > 0.0001f);
    }

    private static IWaveProvider CreateWaveProvider(AudioEngine engine, int sampleRate, int channels)
    {
        var providerType = typeof(AudioEngine).Assembly.GetType("amChipper.Audio.Engine.ChipWaveProvider", throwOnError: true)!;
        return (IWaveProvider)Activator.CreateInstance(
            providerType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [sampleRate, channels, engine],
            culture: null)!;
    }

    private static IEnumerable<float> ReadFloatSamples(byte[] buffer, int bytesRead)
    {
        for (var offset = 0; offset < bytesRead; offset += sizeof(float))
        {
            yield return BitConverter.ToSingle(buffer, offset);
        }
    }

    private static RenderStats RenderStatsFor(IModulePlayer player, byte[] data, string file)
    {
        bool loaded = player.Load(data, Path.GetFileName(file));
        if (!loaded)
            return new RenderStats(false, 0, 0, 0, -1, 0, 0, 0);

        const int chunkFrames = 4096;
        const int totalFrames = 44_100 * 8;
        var buffer = new float[chunkFrames * 2];
        double sumSquares = 0;
        double sumAbsDelta = 0;
        float peak = 0;
        float previous = 0;
        int zeroCrossings = 0;
        int firstAudible = -1;
        int framesRendered = 0;
        long renderTicks = 0;
        long maxChunkTicks = 0;
        int chunks = 0;

        while (framesRendered < totalFrames)
        {
            int frames = Math.Min(chunkFrames, totalFrames - framesRendered);
            long before = System.Diagnostics.Stopwatch.GetTimestamp();
            int rendered = player.Render(buffer, frames);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - before;
            renderTicks += elapsed;
            maxChunkTicks = Math.Max(maxChunkTicks, elapsed);
            chunks++;

            if (rendered <= 0)
                break;

            for (int i = 0; i < rendered * 2; i += 2)
            {
                float sample = buffer[i];
                float abs = Math.Abs(sample);
                peak = Math.Max(peak, abs);
                sumSquares += sample * sample;
                sumAbsDelta += Math.Abs(sample - previous);
                if (firstAudible < 0 && abs > 0.0001f)
                    firstAudible = framesRendered + (i / 2);

                int sign = Math.Sign(sample);
                int previousSign = Math.Sign(previous);
                if (sign != 0 && previousSign != 0 && sign != previousSign)
                    zeroCrossings++;

                previous = sample;
            }

            framesRendered += rendered;
        }

        double seconds = framesRendered / 44100.0;
        double rms = framesRendered == 0 ? 0 : Math.Sqrt(sumSquares / framesRendered);
        double renderMs = renderTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double audioMs = seconds * 1000.0;

        return new RenderStats(
            true,
            framesRendered,
            peak,
            rms,
            firstAudible,
            zeroCrossings,
            sumAbsDelta / Math.Max(framesRendered, 1),
            renderMs / Math.Max(audioMs, 1));
    }

    private readonly record struct RenderStats(
        bool Loaded,
        int FramesRendered,
        float Peak,
        double Rms,
        int FirstAudibleFrame,
        int ZeroCrossings,
        double AverageDelta,
        double RealtimeRatio);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "amChipper.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate amChipper repository root.");
    }

    private static class DemoModuleFactory
    {
        public static byte[] CreateAudibleMod()
        {
            using var memory = new MemoryStream();
            WriteAscii(memory, "amChipper managed", 20);

            for (int i = 0; i < 31; i++)
            {
                WriteAscii(memory, i == 0 ? "square wave" : string.Empty, 22);
                if (i == 0)
                {
                    WriteBigEndianWord(memory, 64);
                    memory.WriteByte(0);
                    memory.WriteByte(64);
                    WriteBigEndianWord(memory, 0);
                    WriteBigEndianWord(memory, 64);
                }
                else
                {
                    memory.Write(new byte[8]);
                }
            }

            memory.WriteByte(1);
            memory.WriteByte(0);
            memory.WriteByte(0);
            memory.Write(new byte[127]);
            memory.Write("M.K."u8);

            var pattern = new byte[64 * 4 * 4];
            pattern[0] = 0x01;
            pattern[1] = 0xAC;
            pattern[2] = 0x1C;
            pattern[3] = 0x40;
            memory.Write(pattern);

            for (int i = 0; i < 128; i++)
                memory.WriteByte((byte)(i % 32 < 16 ? 96 : unchecked((byte)-96)));

            return memory.ToArray();
        }

        public static byte[] CreateTwoOrderAudibleMod()
        {
            using var memory = new MemoryStream();
            WriteAscii(memory, "managed exact seek", 20);

            for (int i = 0; i < 31; i++)
            {
                WriteAscii(memory, i == 0 ? "square wave" : string.Empty, 22);
                if (i == 0)
                {
                    WriteBigEndianWord(memory, 64);
                    memory.WriteByte(0);
                    memory.WriteByte(64);
                    WriteBigEndianWord(memory, 0);
                    WriteBigEndianWord(memory, 64);
                }
                else
                {
                    memory.Write(new byte[8]);
                }
            }

            memory.WriteByte(2);
            memory.WriteByte(0);
            memory.WriteByte(0);
            memory.WriteByte(1);
            memory.Write(new byte[126]);
            memory.Write("M.K."u8);

            var firstPattern = new byte[64 * 4 * 4];
            WriteModCell(firstPattern, row: 0, channel: 0, period: 0x1AC, instrument: 1, effect: 0, parameter: 0);
            memory.Write(firstPattern);

            var secondPattern = new byte[64 * 4 * 4];
            WriteModCell(secondPattern, row: 3, channel: 0, period: 0x1AC, instrument: 1, effect: 0, parameter: 0);
            memory.Write(secondPattern);

            for (int i = 0; i < 128; i++)
                memory.WriteByte((byte)(i % 32 < 16 ? 96 : unchecked((byte)-96)));

            return memory.ToArray();
        }

        private static void WriteAscii(Stream stream, string value, int length)
        {
            var buffer = new byte[length];
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, buffer, Math.Min(bytes.Length, length));
            stream.Write(buffer);
        }

        private static void WriteBigEndianWord(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteModCell(byte[] pattern, int row, int channel, int period, int instrument, int effect, int parameter)
        {
            var offset = ((row * 4) + channel) * 4;
            pattern[offset] = (byte)(((instrument & 0xF0) | ((period >> 8) & 0x0F)));
            pattern[offset + 1] = (byte)(period & 0xFF);
            pattern[offset + 2] = (byte)(((instrument & 0x0F) << 4) | (effect & 0x0F));
            pattern[offset + 3] = (byte)(parameter & 0xFF);
        }
    }
}
