using System.Diagnostics;
using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the InternalChipRenderer component.
/// </summary>
public static class InternalChipRenderer
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    public const int DefaultRenderSeconds = 180;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>
    /// Executes the CanRender operation.
    /// </summary>
    public static bool CanRender(ModuleFormat format) => format is ModuleFormat.SID or ModuleFormat.NSF;

    /// <summary>
    /// Creates a bounded live chip renderer for source playback without pre-rendering the whole tune.
    /// </summary>
    public static IChipStreamRenderer CreateStreamingRenderer(byte[] data, string sourcePath, int sampleRate = DefaultSampleRate)
    {
        ArgumentNullException.ThrowIfNull(data);

        var metadata = ChipTuneFile.ReadMetadata(data, sourcePath);
        sampleRate = Math.Clamp(sampleRate, 8000, 192000);
        if (metadata.Format == ModuleFormat.NSF)
        {
            var program = NsfProgram.Load(ChipTuneFile.NormalizeNsfData(data)) ?? throw new InvalidDataException("Not an NSF file.");
            return new NsfStreamRenderer(program, metadata, sampleRate);
        }

        throw new InvalidOperationException($"{metadata.Format} does not support live chip streaming yet.");
    }

    /// <summary>
    /// Executes the SidFrequencyRegisterToHzForTests operation.
    /// </summary>
    public static double SidFrequencyRegisterToHzForTests(ushort frequencyRegister, bool pal = true)
    {
        double clock = pal ? 985248.0 : 1022727.0;
        return frequencyRegister * clock / 16777216.0;
    }

    public static (int SidWrites, int VoiceWrites, byte Volume) AnalyzeSidForTests(byte[] data, int frames = 4)
    {
        var sid = SidProgram.Load(data) ?? throw new InvalidDataException("Not a PSID/RSID file.");
        return SidRuntime.Analyze(sid, frames);
    }

    public static (ushort LoadAddress, ushort InitAddress, ushort PlayAddress, ushort IrqVector, int SidWrites) AnalyzeSidExecutionForTests(byte[] data, int frames = 4)
    {
        var sid = SidProgram.Load(data) ?? throw new InvalidDataException("Not a PSID/RSID file.");
        return SidRuntime.AnalyzeExecution(sid, frames);
    }

    /// <summary>
    /// Executes the DumpSidRegistersForTests operation.
    /// </summary>
    public static byte[] DumpSidRegistersForTests(byte[] data, int frames = 4)
    {
        var sid = SidProgram.Load(data) ?? throw new InvalidDataException("Not a PSID/RSID file.");
        return SidRuntime.DumpRegisters(sid, frames);
    }

    /// <summary>
    /// Executes the InspectSidVoiceRows operation.
    /// </summary>
    public static IReadOnlyList<SidVoiceRow> InspectSidVoiceRows(byte[] data, int frames = 512, int? songNumber = null)
    {
        var sid = SidProgram.Load(data, songNumber) ?? throw new InvalidDataException("Not a PSID/RSID file.");
        return SidRuntime.InspectVoiceRows(sid, Math.Clamp(frames, 1, 8192));
    }

    /// <summary>
    /// Executes the InspectNsfVoiceRows operation.
    /// </summary>
    public static IReadOnlyList<NsfVoiceRow> InspectNsfVoiceRows(byte[] data, int rows = 512, int? songNumber = null, int maxMilliseconds = 1200)
    {
        var nsf = NsfProgram.Load(ChipTuneFile.NormalizeNsfData(data)) ?? throw new InvalidDataException("Not an NSF file.");
        int startSong = Math.Clamp(songNumber ?? nsf.StartSong, 1, nsf.SongCount);
        return NsfRuntime.InspectVoiceRows(nsf, startSong, Math.Clamp(rows, 1, 8192), Math.Clamp(maxMilliseconds, 100, 10000));
    }

    /// <summary>
    /// Executes the RenderToWav operation.
    /// </summary>
    public static void RenderToWav(byte[] data, string sourcePath, string outputPath, int seconds = DefaultRenderSeconds, int sampleRate = DefaultSampleRate)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        float[] samples = RenderStereoFloat(data, sourcePath, seconds, sampleRate);
        WriteStereoFloatAsPcm16Wav(outputPath, samples, Math.Clamp(sampleRate, 8000, 192000));
    }

    /// <summary>
    /// Executes the RenderStereoFloat operation.
    /// </summary>
    public static float[] RenderStereoFloat(byte[] data, string sourcePath, int seconds = DefaultRenderSeconds, int sampleRate = DefaultSampleRate)
    {
        ArgumentNullException.ThrowIfNull(data);

        var metadata = ChipTuneFile.ReadMetadata(data, sourcePath);
        if (!CanRender(metadata.Format))
            throw new InvalidOperationException($"{metadata.Format} cannot be rendered by the internal chip renderer.");

        seconds = Math.Clamp(seconds, 1, 3600);
        sampleRate = Math.Clamp(sampleRate, 8000, 192000);
        return metadata.Format == ModuleFormat.SID
            ? RenderSid(data, metadata, seconds, sampleRate)
            : RenderNsf(data, metadata, seconds, sampleRate);
    }

    /// <summary>
    /// Executes the RenderSid operation.
    /// </summary>
    private static float[] RenderSid(byte[] data, ChipTuneMetadata metadata, int seconds, int sampleRate)
    {
        var sid = SidProgram.Load(data);
        if (sid is not null)
            return SidRuntime.Render(sid, metadata, seconds, sampleRate);

        int frames = seconds * sampleRate;
        float[] stereo = new float[frames * 2];
        uint seed = Hash(data) ^ 0xC64C64u;
        double[] phase = [0, 0, 0];
        double[] pulseWidth = [0.5, 0.35, 0.65];
        byte[] digest = BuildDigest(data, 96);

        for (int frame = 0; frame < frames; frame++)
        {
            double t = frame / (double)sampleRate;
            double env = Math.Min(1.0, t * 8.0) * Math.Min(1.0, Math.Max(0.0, seconds - t) * 3.0);
            int step = (int)(t * 50.0) % digest.Length;
            double left = 0;
            double right = 0;

            for (int voice = 0; voice < 3; voice++)
            {
                int a = digest[(step + voice * 7) % digest.Length];
                int b = digest[(step + voice * 13 + 3) % digest.Length];
                double note = 32 + ((a + voice * 7) % 48);
                double freq = 440.0 * Math.Pow(2, (note - 69.0) / 12.0);
                freq *= 0.995 + ((b & 0x0F) - 7) * 0.0015;
                phase[voice] = (phase[voice] + freq / sampleRate) % 1.0;

                double wave = voice switch
                {
                    0 => phase[voice] < pulseWidth[voice] ? 1.0 : -1.0,
                    1 => 1.0 - Math.Abs(phase[voice] * 4.0 - 2.0),
                    _ => NextNoise(ref seed) * 2.0 - 1.0
                };

                double gate = ((a >> voice) & 1) == 0 ? 0.35 : 1.0;
                double amp = env * gate * (0.13 + (b & 0x07) * 0.012);
                double pan = voice == 0 ? -0.45 : voice == 1 ? 0.1 : 0.45;
                left += wave * amp * (1.0 - Math.Max(0, pan));
                right += wave * amp * (1.0 + Math.Min(0, pan));
            }

            stereo[frame * 2] = SoftClip(left);
            stereo[frame * 2 + 1] = SoftClip(right);
        }

        return stereo;
    }

    /// <summary>
    /// Executes the RenderNsf operation.
    /// </summary>
    private static float[] RenderNsf(byte[] data, ChipTuneMetadata metadata, int seconds, int sampleRate)
    {
        byte[] nsfData = ChipTuneFile.NormalizeNsfData(data);
        var program = NsfProgram.Load(nsfData);
        if (program is null)
            return RenderNsfDigestFallback(data, seconds, sampleRate);

        try
        {
            return NsfRuntime.Render(program, metadata, seconds, sampleRate);
        }
        catch
        {
            return RenderNsfDigestFallback(data, seconds, sampleRate);
        }
    }

    /// <summary>
    /// Last-resort NSF fallback used only when the executable NSF driver cannot be started.
    /// </summary>
    private static float[] RenderNsfDigestFallback(byte[] data, int seconds, int sampleRate)
    {
        int frames = seconds * sampleRate;
        float[] stereo = new float[frames * 2];
        uint seed = Hash(data) ^ 0xA9F1u;
        double pulse1 = 0;
        double pulse2 = 0;
        double triangle = 0;
        double noise = 0;
        byte[] digest = BuildDigest(data, 128);

        for (int frame = 0; frame < frames; frame++)
        {
            double t = frame / (double)sampleRate;
            double env = Math.Min(1.0, t * 10.0) * Math.Min(1.0, Math.Max(0.0, seconds - t) * 3.0);
            int step = (int)(t * 60.0) % digest.Length;
            int a = digest[step];
            int b = digest[(step + 17) % digest.Length];
            int c = digest[(step + 43) % digest.Length];

            double baseNote = 36 + (a % 36);
            double f1 = 440.0 * Math.Pow(2, (baseNote - 69.0) / 12.0);
            double f2 = 440.0 * Math.Pow(2, (baseNote + 7 + (b % 5) - 69.0) / 12.0);
            double ft = 440.0 * Math.Pow(2, (baseNote - 12 - 69.0) / 12.0);

            pulse1 = (pulse1 + f1 / sampleRate) % 1.0;
            pulse2 = (pulse2 + f2 / sampleRate) % 1.0;
            triangle = (triangle + ft / sampleRate) % 1.0;
            noise = (noise + (3500 + c * 12) / sampleRate) % 1.0;

            double p1 = pulse1 < 0.25 ? 1.0 : -1.0;
            double p2 = pulse2 < 0.5 ? 1.0 : -1.0;
            double tri = 2.0 * Math.Abs(2.0 * triangle - 1.0) - 1.0;
            double noi = noise < 0.5 ? NextNoise(ref seed) * 2.0 - 1.0 : 0;

            double left = env * (p1 * 0.18 + p2 * 0.10 + tri * 0.16 + noi * 0.05);
            double right = env * (p1 * 0.10 + p2 * 0.18 + tri * 0.16 + noi * 0.05);
            stereo[frame * 2] = SoftClip(left);
            stereo[frame * 2 + 1] = SoftClip(right);
        }

        return stereo;
    }

    /// <summary>
    /// Parsed NSF header and PRG payload.
    /// </summary>
    private sealed class NsfProgram
    {
        public byte[] Data { get; private init; } = [];
        public ushort LoadAddress { get; private init; }
        public ushort InitAddress { get; private init; }
        public ushort PlayAddress { get; private init; }
        public int SongCount { get; private init; }
        public int StartSong { get; private init; }
        public int PlayRateHz { get; private init; }
        public byte[] InitialBanks { get; private init; } = new byte[8];
        public byte ExpansionFlags { get; private init; }
        public bool UsesBanks => InitialBanks.Any(value => value != 0);

        public static NsfProgram? Load(byte[] data)
        {
            if (data.Length < 0x80 || data[0] != (byte)'N' || data[1] != (byte)'E' || data[2] != (byte)'S' || data[3] != (byte)'M')
                return null;

            ushort load = ReadLe(data, 0x08);
            ushort init = ReadLe(data, 0x0A);
            ushort play = ReadLe(data, 0x0C);

            ushort ntscSpeed = ReadLe(data, 0x6E);
            ushort palSpeed = ReadLe(data, 0x78);
            byte region = data.Length > 0x7A ? data[0x7A] : (byte)0;
            bool pal = (region & 0x01) != 0 && (region & 0x02) == 0;
            double microseconds = pal && palSpeed != 0 ? palSpeed : ntscSpeed != 0 ? ntscSpeed : pal ? 20000.0 : 16639.0;
            int hz = Math.Clamp((int)Math.Round(1_000_000.0 / microseconds), 25, 240);

            var banks = new byte[8];
            Array.Copy(data, 0x70, banks, 0, 8);
            bool usesBanks = banks.Any(value => value != 0);
            if (init == 0 || play == 0)
                return null;
            if (load < 0x6000 && !(load == 0 && usesBanks))
                return null;

            return new NsfProgram
            {
                Data = data[0x80..],
                LoadAddress = load,
                InitAddress = init,
                PlayAddress = play,
                SongCount = Math.Max((int)data[0x06], 1),
                StartSong = Math.Max((int)data[0x07], 1),
                PlayRateHz = hz,
                InitialBanks = banks,
                ExpansionFlags = data.Length > 0x7B ? data[0x7B] : (byte)0
            };
        }

        private static ushort ReadLe(byte[] data, int offset) =>
            offset + 1 < data.Length ? (ushort)(data[offset] | (data[offset + 1] << 8)) : (ushort)0;
    }

    /// <summary>
    /// Small NSF runtime that executes the tune driver and renders the NES 2A03 APU.
    /// </summary>
    private sealed class NsfRuntime
    {
        private const int InitInstructionBudget = 80_000;
        private const int PlayInstructionBudget = 18_000;
        private readonly NesApu _apu;
        private readonly NsfCpu _cpu;
        private readonly NsfProgram _program;
        private readonly int _sampleRate;
        private readonly int _playIntervalSamples;
        private long _nextPlaySample;
        private long _renderedSamples;
        private int _playTimeoutStreak;
        private int _deferredPlayCalls;

        public NsfRuntime(NsfProgram program, ChipTuneMetadata metadata, int sampleRate)
            : this(program, Math.Clamp(metadata.StartSong > 0 ? metadata.StartSong : program.StartSong, 1, program.SongCount), sampleRate)
        {
        }

        public NsfRuntime(NsfProgram program, int startSong, int sampleRate)
        {
            _program = program;
            _sampleRate = sampleRate;
            _apu = new NesApu(program.Data);
            _cpu = new NsfCpu(program, _apu);
            _cpu.Call(program.InitAddress, (byte)(startSong - 1), program.PlayRateHz == 50 ? (byte)1 : (byte)0, maxInstructions: InitInstructionBudget, maxMilliseconds: 120);
            _playIntervalSamples = Math.Max(1, sampleRate / Math.Max(program.PlayRateHz, 1));
        }

        public static float[] Render(NsfProgram program, ChipTuneMetadata metadata, int seconds, int sampleRate)
        {
            var runtime = new NsfRuntime(program, metadata, sampleRate);
            int frames = seconds * sampleRate;
            float[] stereo = new float[frames * 2];
            int maxRenderMilliseconds = Math.Clamp(seconds * 250, 1500, 6000);
            runtime.RenderInto(stereo, frames, 2, maxRenderMilliseconds);
            return stereo;
        }

        public void RenderInto(float[] buffer, int frameCount, int channels, int maxMilliseconds = 0)
        {
            channels = Math.Clamp(channels, 1, 2);
            frameCount = Math.Clamp(frameCount, 0, buffer.Length / channels);
            Stopwatch? renderBudget = maxMilliseconds > 0 ? Stopwatch.StartNew() : null;
            for (int frame = 0; frame < frameCount; frame++)
            {
                if (renderBudget is not null &&
                    (frame & 0x1FFF) == 0 &&
                    renderBudget.ElapsedMilliseconds > maxMilliseconds)
                {
                    throw new TimeoutException($"NSF render exceeded {maxMilliseconds}ms safety budget.");
                }

                if (_renderedSamples >= _nextPlaySample)
                {
                    if (_deferredPlayCalls > 0)
                    {
                        _deferredPlayCalls--;
                    }
                    else
                    {
                        _cpu.Call(_program.PlayAddress, _cpu.A, _cpu.X, maxInstructions: PlayInstructionBudget, maxMilliseconds: 2);
                        _playTimeoutStreak = _cpu.LastCallTimedOut
                            ? Math.Min(_playTimeoutStreak + 1, 16)
                            : 0;
                    }

                    _nextPlaySample += _playIntervalSamples;
                    if (_playTimeoutStreak >= 4)
                    {
                        _deferredPlayCalls = Math.Min(_deferredPlayCalls + 1, 8);
                        _playTimeoutStreak = 3;
                    }
                }

                float sample = SoftClip(_apu.RenderSample(_sampleRate));
                int baseIndex = frame * channels;
                buffer[baseIndex] = sample;
                if (channels > 1)
                    buffer[baseIndex + 1] = sample;
                _renderedSamples++;
            }
        }

        public static IReadOnlyList<NsfVoiceRow> InspectVoiceRows(NsfProgram program, int startSong, int rows, int maxMilliseconds)
        {
            var runtime = new NsfRuntime(program, startSong, sampleRate: 44100);
            var result = new List<NsfVoiceRow>(rows * 5);
            var previous = new Dictionary<int, NsfVoiceRow>();
            int samplesPerRow = runtime._playIntervalSamples;
            var budget = Stopwatch.StartNew();

            for (int row = 0; row < rows; row++)
            {
                if (budget.ElapsedMilliseconds > maxMilliseconds)
                    break;

                runtime._cpu.Call(program.PlayAddress, runtime._cpu.A, runtime._cpu.X, maxInstructions: PlayInstructionBudget, maxMilliseconds: 4);
                if (runtime._cpu.LastCallTimedOut)
                {
                    runtime._playTimeoutStreak++;
                    if (runtime._playTimeoutStreak >= 3)
                        break;
                }
                else
                {
                    runtime._playTimeoutStreak = 0;
                }

                for (int i = 0; i < samplesPerRow; i++)
                    runtime._apu.RenderSample(runtime._sampleRate);

                foreach (var current in runtime._apu.Snapshot(row))
                {
                    bool changed = !previous.TryGetValue(current.Voice, out var old) ||
                        old.Pitch != current.Pitch ||
                        old.Volume != current.Volume ||
                        old.EffectColumn != current.EffectColumn ||
                        old.EffectParam != current.EffectParam ||
                        old.VolumeColumn != current.VolumeColumn;

                    if (changed || row % 16 == 0)
                    {
                        result.Add(current);
                        previous[current.Voice] = current;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Live NSF stream renderer used by the audio engine for responsive source playback.
    /// </summary>
    private sealed class NsfStreamRenderer : IChipStreamRenderer
    {
        private readonly NsfRuntime _runtime;

        public NsfStreamRenderer(NsfProgram program, ChipTuneMetadata metadata, int sampleRate)
        {
            _runtime = new NsfRuntime(program, metadata, sampleRate);
            SampleRate = sampleRate;
        }

        public ModuleFormat Format => ModuleFormat.NSF;

        public int SampleRate { get; }

        public void Render(float[] buffer, int frameCount, int channels) =>
            _runtime.RenderInto(buffer, frameCount, channels);
    }

    /// <summary>
    /// Minimal NES APU renderer for NSF 2A03 pulse, triangle, noise, and DPCM register writes.
    /// </summary>
    private sealed class NesApu
    {
        private static readonly int[] LengthTable =
        [
            10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
            12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
        ];

        private static readonly int[] NoisePeriods =
        [
            4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068
        ];

        private static readonly int[] DpcmPeriods =
        [
            428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 85, 72, 54
        ];

        private readonly byte[] _prg;
        private readonly PulseChannel _pulse1 = new();
        private readonly PulseChannel _pulse2 = new();
        private readonly TriangleChannel _triangle = new();
        private readonly NoiseChannel _noise = new();
        private readonly DpcmChannel _dpcm;
        private readonly Vrc6ChannelSet _vrc6 = new();
        private readonly Vrc7ApproxChannelSet _vrc7 = new();
        private readonly Mmc5ChannelSet _mmc5 = new();
        private readonly N163ChannelSet _n163 = new();
        private readonly S5bChannelSet _s5b = new();
        private readonly FdsChannel _fds = new();
        private double _frameSequencer;
        private double _dcInput;
        private double _dcOutput;
        private double _lowPass;
        private int _frameStep;
        private bool _fiveStepFrameSequencer;

        public NesApu(byte[] prg)
        {
            _prg = prg;
            _dpcm = new DpcmChannel(prg);
        }

        public void Write(ushort address, byte value)
        {
            switch (address)
            {
                case <= 0x4003:
                    _pulse1.Write(address, value);
                    break;
                case >= 0x4004 and <= 0x4007:
                    _pulse2.Write((ushort)(address - 4), value);
                    break;
                case >= 0x4008 and <= 0x400B:
                    _triangle.Write(address, value);
                    break;
                case >= 0x400C and <= 0x400F:
                    _noise.Write(address, value);
                    break;
                case >= 0x4010 and <= 0x4013:
                    _dpcm.Write(address, value);
                    break;
                case 0x4015:
                    _pulse1.Enabled = (value & 0x01) != 0;
                    _pulse2.Enabled = (value & 0x02) != 0;
                    _triangle.Enabled = (value & 0x04) != 0;
                    _noise.Enabled = (value & 0x08) != 0;
                    _dpcm.SetEnabled((value & 0x10) != 0);
                    if (!_pulse1.Enabled) _pulse1.ClearLength();
                    if (!_pulse2.Enabled) _pulse2.ClearLength();
                    if (!_triangle.Enabled) _triangle.ClearLength();
                    if (!_noise.Enabled) _noise.ClearLength();
                    break;
                case 0x4017:
                    _fiveStepFrameSequencer = (value & 0x80) != 0;
                    _frameSequencer = 0;
                    _frameStep = 0;
                    if (_fiveStepFrameSequencer)
                    {
                        TickQuarterFrame();
                        TickHalfFrame();
                    }
                    break;
                case 0x4800:
                case 0xF800:
                    _n163.Write(address, value);
                    break;
                case >= 0x5000 and <= 0x5015:
                    _mmc5.Write(address, value);
                    break;
                case 0x9010:
                case 0x9030:
                    _vrc7.Write(address, value);
                    break;
                case >= 0x9000 and <= 0xBFFF:
                    _vrc6.Write(address, value);
                    break;
                case 0xC000:
                case 0xE000:
                    _s5b.Write(address, value);
                    break;
                case >= 0x4040 and <= 0x409F:
                    _fds.Write(address, value);
                    break;
            }
        }

        public byte ReadStatus()
        {
            byte value = 0;
            if (_pulse1.Active) value |= 0x01;
            if (_pulse2.Active) value |= 0x02;
            if (_triangle.Active) value |= 0x04;
            if (_noise.Active) value |= 0x08;
            if (_dpcm.Enabled) value |= 0x10;
            return value;
        }

        public double RenderSample(int sampleRate)
        {
            _frameSequencer += 240.0 / sampleRate;
            while (_frameSequencer >= 1.0)
            {
                _frameSequencer -= 1.0;
                _frameStep = (_frameStep + 1) % (_fiveStepFrameSequencer ? 5 : 4);
                TickQuarterFrame();
                bool halfFrame = _fiveStepFrameSequencer
                    ? _frameStep is 1 or 4
                    : (_frameStep & 1) == 1;
                if (halfFrame)
                    TickHalfFrame();
            }

            double p1 = _pulse1.Render(sampleRate);
            double p2 = _pulse2.Render(sampleRate);
            double tri = _triangle.Render(sampleRate);
            double noi = _noise.Render(sampleRate);
            double dpcm = _dpcm.Render(sampleRate);
            double vrc6 = _vrc6.Render(sampleRate);
            double vrc7 = _vrc7.Render(sampleRate);
            double mmc5 = _mmc5.Render(sampleRate);
            double n163 = _n163.Render(sampleRate);
            double s5b = _s5b.Render(sampleRate);
            double fds = _fds.Render(sampleRate);
            double pulseOut = 0.00752 * (p1 + p2);
            double tndOut = 0.00851 * tri + 0.00494 * noi + 0.00335 * dpcm;
            double expansion = vrc6 * 0.0085 + vrc7 * 0.0068 + mmc5 * 0.0085 + n163 * 0.0052 + s5b * 0.0072 + fds * 0.0092;
            double mixed = DcBlock((pulseOut + tndOut + expansion) * 1.78);
            _lowPass += (mixed - _lowPass) * 0.58;
            return _lowPass;
        }

        private double DcBlock(double input)
        {
            double output = input - _dcInput + 0.996 * _dcOutput;
            _dcInput = input;
            _dcOutput = output;
            return output;
        }

        private void TickQuarterFrame()
        {
            _pulse1.TickQuarterFrame();
            _pulse2.TickQuarterFrame();
            _triangle.TickQuarterFrame();
            _noise.TickQuarterFrame();
        }

        private void TickHalfFrame()
        {
            _pulse1.TickHalfFrame();
            _pulse2.TickHalfFrame();
            _triangle.TickHalfFrame();
            _noise.TickHalfFrame();
        }

        public IEnumerable<NsfVoiceRow> Snapshot(int row)
        {
            if (_pulse1.TrySnapshot(row, 0, "2A03 Pulse 1", out var pulse1))
                yield return pulse1;
            if (_pulse2.TrySnapshot(row, 1, "2A03 Pulse 2", out var pulse2))
                yield return pulse2;
            if (_triangle.TrySnapshot(row, 2, "2A03 Triangle", out var triangle))
                yield return triangle;
            if (_noise.TrySnapshot(row, 3, out var noise))
                yield return noise;
            if (_dpcm.TrySnapshot(row, 4, out var dpcm))
                yield return dpcm;
            foreach (var rowState in _vrc6.Snapshot(row, 5))
                yield return rowState;
            foreach (var rowState in _vrc7.Snapshot(row, 8))
                yield return rowState;
            foreach (var rowState in _mmc5.Snapshot(row, 14))
                yield return rowState;
            foreach (var rowState in _n163.Snapshot(row, 16))
                yield return rowState;
            foreach (var rowState in _s5b.Snapshot(row, 24))
                yield return rowState;
            if (_fds.TrySnapshot(row, 27, out var fds))
                yield return fds;
        }

        private static byte FrequencyToPitch(double frequency)
        {
            if (frequency <= 0)
                return 0;

            int pitch = (int)Math.Round(69.0 + 12.0 * Math.Log2(frequency / 440.0));
            return (byte)Math.Clamp(pitch, 1, 119);
        }

        private sealed class PulseChannel
        {
            private static readonly double[][] Duties =
            [
                [0, 1, 0, 0, 0, 0, 0, 0],
                [0, 1, 1, 0, 0, 0, 0, 0],
                [0, 1, 1, 1, 1, 0, 0, 0],
                [1, 0, 0, 1, 1, 1, 1, 1]
            ];

            private int _duty;
            private int _volume;
            private int _timer;
            private int _lengthCounter;
            private int _envelopePeriod;
            private int _envelopeDivider;
            private int _envelopeDecay = 15;
            private bool _constantVolume;
            private bool _haltLength;
            private bool _envelopeStart;
            private bool _sweepEnabled;
            private bool _sweepNegate;
            private int _sweepPeriod;
            private int _sweepShift;
            private int _sweepDivider;
            private bool _sweepReload;
            private double _phase;
            public bool Enabled { get; set; }
            public bool Active => Enabled && _lengthCounter > 0;

            public void Write(ushort address, byte value)
            {
                switch (address & 0x03)
                {
                    case 0:
                        _duty = (value >> 6) & 0x03;
                        _haltLength = (value & 0x20) != 0;
                        _constantVolume = (value & 0x10) != 0;
                        _volume = value & 0x0F;
                        _envelopePeriod = value & 0x0F;
                        break;
                    case 1:
                        _sweepEnabled = (value & 0x80) != 0;
                        _sweepPeriod = ((value >> 4) & 0x07) + 1;
                        _sweepNegate = (value & 0x08) != 0;
                        _sweepShift = value & 0x07;
                        _sweepReload = true;
                        break;
                    case 2:
                        _timer = (_timer & 0x700) | value;
                        break;
                    case 3:
                        _timer = (_timer & 0x0FF) | ((value & 0x07) << 8);
                        _lengthCounter = LengthTable[(value >> 3) & 0x1F];
                        _envelopeStart = true;
                        _phase = 0;
                        break;
                }
            }

            public void TickQuarterFrame()
            {
                if (_envelopeStart)
                {
                    _envelopeStart = false;
                    _envelopeDecay = 15;
                    _envelopeDivider = _envelopePeriod;
                    return;
                }

                if (_envelopeDivider > 0)
                {
                    _envelopeDivider--;
                    return;
                }

                _envelopeDivider = _envelopePeriod;
                if (_envelopeDecay > 0)
                    _envelopeDecay--;
                else if (_haltLength)
                    _envelopeDecay = 15;
            }

            public void TickHalfFrame()
            {
                if (!_haltLength && _lengthCounter > 0)
                    _lengthCounter--;

                ClockSweep();
            }

            private void ClockSweep()
            {
                if (_sweepDivider > 0)
                    _sweepDivider--;
                else
                {
                    if (_sweepEnabled && _sweepShift > 0 && _timer >= 8)
                    {
                        int delta = _timer >> _sweepShift;
                        int target = _sweepNegate ? _timer - delta : _timer + delta;
                        if ((uint)target <= 0x7FF && target >= 8)
                            _timer = target;
                    }

                    _sweepDivider = _sweepPeriod;
                }

                if (_sweepReload)
                {
                    _sweepReload = false;
                    _sweepDivider = _sweepPeriod;
                }
            }

            public void ClearLength() => _lengthCounter = 0;

            public double Render(int sampleRate)
            {
                int outputVolume = _constantVolume ? _volume : _envelopeDecay;
                if (!Enabled || _lengthCounter <= 0 || _timer < 8 || outputVolume == 0)
                    return 0;
                double frequency = 1_789_773.0 / (16.0 * (_timer + 1));
                _phase = (_phase + frequency / sampleRate) % 1.0;
                int step = (int)(_phase * 8.0) & 7;
                return Duties[_duty][step] * outputVolume;
            }

            public bool TrySnapshot(int row, int voice, string label, out NsfVoiceRow state)
            {
                int outputVolume = _constantVolume ? _volume : _envelopeDecay;
                if (!Enabled || _lengthCounter <= 0 || _timer < 8 || outputVolume == 0)
                {
                    state = default!;
                    return false;
                }

                double frequency = 1_789_773.0 / (16.0 * (_timer + 1));
                state = new NsfVoiceRow(
                    row,
                    voice,
                    label,
                    FrequencyToPitch(frequency),
                    (byte)Math.Clamp(outputVolume * 4, 0, 64),
                    2,
                    (byte)(_duty << 4),
                    (byte)(_timer & 0xFF),
                    (byte)((_timer >> 8) & 0x07),
                    "pulse");
                return true;
            }
        }

        private sealed class TriangleChannel
        {
            private int _linear;
            private int _timer;
            private int _lengthCounter;
            private bool _control;
            private double _phase;
            public bool Enabled { get; set; }
            public bool Active => Enabled && _lengthCounter > 0;

            public void Write(ushort address, byte value)
            {
                switch (address)
                {
                    case 0x4008:
                        _control = (value & 0x80) != 0;
                        _linear = value & 0x7F;
                        break;
                    case 0x400A:
                        _timer = (_timer & 0x700) | value;
                        break;
                    case 0x400B:
                        _timer = (_timer & 0x0FF) | ((value & 0x07) << 8);
                        _lengthCounter = LengthTable[(value >> 3) & 0x1F];
                        _phase = 0;
                        break;
                }
            }

            public void TickQuarterFrame()
            {
            }

            public void TickHalfFrame()
            {
                if (!_control && _lengthCounter > 0)
                    _lengthCounter--;
            }

            public void ClearLength() => _lengthCounter = 0;

            public double Render(int sampleRate)
            {
                if (!Enabled || _lengthCounter <= 0 || _timer < 2 || _linear == 0)
                    return 0;
                double frequency = 1_789_773.0 / (32.0 * (_timer + 1));
                _phase = (_phase + frequency / sampleRate) % 1.0;
                return (1.0 - Math.Abs(_phase * 2.0 - 1.0)) * 15.0;
            }

            public bool TrySnapshot(int row, int voice, string label, out NsfVoiceRow state)
            {
                if (!Enabled || _lengthCounter <= 0 || _timer < 2 || _linear == 0)
                {
                    state = default!;
                    return false;
                }

                double frequency = 1_789_773.0 / (32.0 * (_timer + 1));
                state = new NsfVoiceRow(
                    row,
                    voice,
                    label,
                    FrequencyToPitch(frequency),
                    (byte)Math.Clamp(_linear, 0, 64),
                    2,
                    0x20,
                    (byte)(_timer & 0xFF),
                    (byte)((_timer >> 8) & 0x07),
                    "triangle");
                return true;
            }
        }

        private sealed class NoiseChannel
        {
            private int _volume;
            private int _period = 4;
            private int _lengthCounter;
            private int _envelopePeriod;
            private int _envelopeDivider;
            private int _envelopeDecay = 15;
            private bool _constantVolume;
            private bool _haltLength;
            private bool _envelopeStart;
            private bool _shortMode;
            private double _phase;
            private ushort _lfsr = 1;
            public bool Enabled { get; set; }
            public bool Active => Enabled && _lengthCounter > 0;

            public void Write(ushort address, byte value)
            {
                switch (address)
                {
                    case 0x400C:
                        _haltLength = (value & 0x20) != 0;
                        _constantVolume = (value & 0x10) != 0;
                        _volume = value & 0x0F;
                        _envelopePeriod = value & 0x0F;
                        break;
                    case 0x400E:
                        _shortMode = (value & 0x80) != 0;
                        _period = NoisePeriods[value & 0x0F];
                        break;
                    case 0x400F:
                        _lengthCounter = LengthTable[(value >> 3) & 0x1F];
                        _envelopeStart = true;
                        _lfsr = 1;
                        break;
                }
            }

            public void TickQuarterFrame()
            {
                if (_envelopeStart)
                {
                    _envelopeStart = false;
                    _envelopeDecay = 15;
                    _envelopeDivider = _envelopePeriod;
                    return;
                }

                if (_envelopeDivider > 0)
                {
                    _envelopeDivider--;
                    return;
                }

                _envelopeDivider = _envelopePeriod;
                if (_envelopeDecay > 0)
                    _envelopeDecay--;
                else if (_haltLength)
                    _envelopeDecay = 15;
            }

            public void TickHalfFrame()
            {
                if (!_haltLength && _lengthCounter > 0)
                    _lengthCounter--;
            }

            public void ClearLength() => _lengthCounter = 0;

            public double Render(int sampleRate)
            {
                int outputVolume = _constantVolume ? _volume : _envelopeDecay;
                if (!Enabled || _lengthCounter <= 0 || outputVolume == 0)
                    return 0;

                _phase += 1_789_773.0 / (_period * sampleRate);
                while (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    int tap = _shortMode ? 6 : 1;
                    int feedback = (_lfsr ^ (_lfsr >> tap)) & 1;
                    _lfsr = (ushort)((_lfsr >> 1) | (feedback << 14));
                }

                return ((_lfsr & 1) == 0 ? 1.0 : 0.0) * outputVolume;
            }

            public bool TrySnapshot(int row, int voice, out NsfVoiceRow state)
            {
                int outputVolume = _constantVolume ? _volume : _envelopeDecay;
                if (!Enabled || _lengthCounter <= 0 || outputVolume == 0)
                {
                    state = default!;
                    return false;
                }

                int periodIndex = Array.IndexOf(NoisePeriods, _period);
                byte pitch = (byte)Math.Clamp(36 + (15 - Math.Max(periodIndex, 0)) * 2, 24, 84);
                state = new NsfVoiceRow(
                    row,
                    voice,
                    "2A03 Noise",
                    pitch,
                    (byte)Math.Clamp(outputVolume * 4, 0, 64),
                    1,
                    0x40,
                    (byte)Math.Max(periodIndex, 0),
                    (byte)(_shortMode ? 1 : 0),
                    "noise");
                return true;
            }
        }

        private sealed class DpcmChannel(byte[] prg)
        {
            private int _period = 428;
            private int _output = 32;
            private int _sampleAddress = 0xC000;
            private int _sampleLength = 1;
            private int _currentAddress;
            private int _bytesRemaining;
            private int _bitsRemaining;
            private byte _shiftRegister;
            private double _phase;
            public bool Enabled { get; set; }

            public void Write(ushort address, byte value)
            {
                switch (address)
                {
                    case 0x4010:
                        _period = DpcmPeriods[value & 0x0F];
                        break;
                    case 0x4011:
                        _output = value & 0x7F;
                        break;
                    case 0x4012:
                        _sampleAddress = 0xC000 + value * 64;
                        break;
                    case 0x4013:
                        _sampleLength = value * 16 + 1;
                        break;
                }
            }

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
                if (!enabled)
                {
                    _bytesRemaining = 0;
                    _bitsRemaining = 0;
                    return;
                }

                if (_bytesRemaining <= 0)
                    RestartSample();
            }

            public double Render(int sampleRate)
            {
                if (!Enabled)
                    return 0;
                if (_bytesRemaining <= 0 && _bitsRemaining <= 0)
                    return (_output - 64) / 8.0;

                _phase += 1_789_773.0 / (_period * sampleRate);
                while (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    ClockBit();
                }

                return (_output - 64) / 8.0;
            }

            private void RestartSample()
            {
                _currentAddress = _sampleAddress;
                _bytesRemaining = _sampleLength;
                _bitsRemaining = 0;
            }

            private void ClockBit()
            {
                if (_bitsRemaining <= 0)
                {
                    if (_bytesRemaining <= 0)
                        return;

                    int offset = Math.Clamp(_currentAddress - 0x8000, 0, Math.Max(prg.Length - 1, 0));
                    _shiftRegister = prg.Length == 0 ? (byte)0 : prg[offset % prg.Length];
                    _bitsRemaining = 8;
                    _bytesRemaining--;
                    _currentAddress++;
                    if (_currentAddress > 0xFFFF)
                        _currentAddress = 0x8000;
                }

                if ((_shiftRegister & 0x01) != 0)
                    _output = Math.Min(127, _output + 2);
                else
                    _output = Math.Max(0, _output - 2);

                _shiftRegister >>= 1;
                _bitsRemaining--;
            }

            public bool TrySnapshot(int row, int voice, out NsfVoiceRow state)
            {
                if (!Enabled)
                {
                    state = default!;
                    return false;
                }

                state = new NsfVoiceRow(
                    row,
                    voice,
                    "2A03 DPCM",
                    36,
                    (byte)Math.Clamp(_output / 2, 0, 64),
                    1,
                    0x80,
                    (byte)(_currentAddress >> 8),
                    (byte)Math.Clamp(_bytesRemaining, 0, 255),
                    "dpcm");
                return true;
            }
        }

        private sealed class Vrc6ChannelSet
        {
            private readonly Vrc6Pulse _pulse1 = new();
            private readonly Vrc6Pulse _pulse2 = new();
            private readonly Vrc6Saw _saw = new();

            public void Write(ushort address, byte value)
            {
                switch (address & 0xF003)
                {
                    case 0x9000:
                    case 0x9001:
                    case 0x9002:
                        _pulse1.Write((ushort)(address & 0x03), value);
                        break;
                    case 0xA000:
                    case 0xA001:
                    case 0xA002:
                        _pulse2.Write((ushort)(address & 0x03), value);
                        break;
                    case 0xB000:
                    case 0xB001:
                    case 0xB002:
                        _saw.Write((ushort)(address & 0x03), value);
                        break;
                }
            }

            public double Render(int sampleRate) =>
                _pulse1.Render(sampleRate) + _pulse2.Render(sampleRate) + _saw.Render(sampleRate);

            public IEnumerable<NsfVoiceRow> Snapshot(int row, int baseVoice)
            {
                if (_pulse1.TrySnapshot(row, baseVoice, "VRC6 Pulse 1", out var pulse1))
                    yield return pulse1;
                if (_pulse2.TrySnapshot(row, baseVoice + 1, "VRC6 Pulse 2", out var pulse2))
                    yield return pulse2;
                if (_saw.TrySnapshot(row, baseVoice + 2, out var saw))
                    yield return saw;
            }
        }

        private sealed class Vrc6Pulse
        {
            private int _volume;
            private int _duty;
            private int _timer;
            private bool _enabled;
            private bool _constantHigh;
            private double _phase;

            public void Write(ushort register, byte value)
            {
                switch (register)
                {
                    case 0:
                        _volume = value & 0x0F;
                        _duty = (value >> 4) & 0x07;
                        _constantHigh = (value & 0x80) != 0;
                        break;
                    case 1:
                        _timer = (_timer & 0xF00) | value;
                        break;
                    case 2:
                        _timer = (_timer & 0x0FF) | ((value & 0x0F) << 8);
                        _enabled = (value & 0x80) != 0;
                        break;
                }
            }

            public double Render(int sampleRate)
            {
                if (!_enabled || _volume == 0 || _timer <= 0)
                    return 0;

                double frequency = 1_789_773.0 / (16.0 * (_timer + 1));
                _phase = (_phase + frequency / sampleRate) % 1.0;
                int step = (int)(_phase * 16.0) & 0x0F;
                return (_constantHigh || step <= _duty ? 1.0 : -1.0) * _volume;
            }

            public bool TrySnapshot(int row, int voice, string label, out NsfVoiceRow state)
            {
                if (!_enabled || _volume == 0 || _timer <= 0)
                {
                    state = default!;
                    return false;
                }

                double frequency = 1_789_773.0 / (16.0 * (_timer + 1));
                state = new NsfVoiceRow(
                    row,
                    voice,
                    label,
                    FrequencyToPitch(frequency),
                    (byte)Math.Clamp(_volume * 4, 0, 64),
                    2,
                    (byte)(_duty << 4),
                    (byte)(_timer & 0xFF),
                    (byte)((_timer >> 8) & 0x0F),
                    "vrc6 pulse");
                return true;
            }
        }

        private sealed class Vrc6Saw
        {
            private int _rate;
            private int _timer;
            private bool _enabled;
            private double _phase;

            public void Write(ushort register, byte value)
            {
                switch (register)
                {
                    case 0:
                        _rate = value & 0x3F;
                        break;
                    case 1:
                        _timer = (_timer & 0xF00) | value;
                        break;
                    case 2:
                        _timer = (_timer & 0x0FF) | ((value & 0x0F) << 8);
                        _enabled = (value & 0x80) != 0;
                        break;
                }
            }

            public double Render(int sampleRate)
            {
                if (!_enabled || _rate == 0 || _timer <= 0)
                    return 0;

                double frequency = 1_789_773.0 / (14.0 * (_timer + 1));
                _phase = (_phase + frequency / sampleRate) % 1.0;
                return ((_phase * 2.0) - 1.0) * _rate;
            }

            public bool TrySnapshot(int row, int voice, out NsfVoiceRow state)
            {
                if (!_enabled || _rate == 0 || _timer <= 0)
                {
                    state = default!;
                    return false;
                }

                double frequency = 1_789_773.0 / (14.0 * (_timer + 1));
                state = new NsfVoiceRow(
                    row,
                    voice,
                    "VRC6 Saw",
                    FrequencyToPitch(frequency),
                    (byte)Math.Clamp(_rate, 0, 64),
                    2,
                    0x30,
                    (byte)(_timer & 0xFF),
                    (byte)((_timer >> 8) & 0x0F),
                    "vrc6 saw");
                return true;
            }
        }

        private sealed class Vrc7ApproxChannelSet
        {
            private readonly byte[] _registers = new byte[0x40];
            private readonly double[] _phase = new double[6];
            private byte _selected;

            public void Write(ushort address, byte value)
            {
                if (address == 0x9010)
                    _selected = (byte)(value & 0x3F);
                else if (address == 0x9030)
                    _registers[_selected] = value;
            }

            public double Render(int sampleRate)
            {
                double sum = 0;
                for (int i = 0; i < 6; i++)
                {
                    int fnum = _registers[0x10 + i] | ((_registers[0x20 + i] & 0x01) << 8);
                    int block = (_registers[0x20 + i] >> 1) & 0x07;
                    bool key = (_registers[0x20 + i] & 0x10) != 0;
                    int volume = 15 - (_registers[0x30 + i] & 0x0F);
                    if (!key || fnum == 0 || volume <= 0)
                        continue;

                    double frequency = Math.Max(8, fnum * Math.Pow(2, block) * 49716.0 / 524288.0);
                    _phase[i] = (_phase[i] + frequency / sampleRate) % 1.0;
                    sum += Math.Sin(_phase[i] * Math.PI * 2.0) * volume;
                }

                return sum;
            }

            public IEnumerable<NsfVoiceRow> Snapshot(int row, int baseVoice)
            {
                for (int i = 0; i < 6; i++)
                {
                    int fnum = _registers[0x10 + i] | ((_registers[0x20 + i] & 0x01) << 8);
                    int block = (_registers[0x20 + i] >> 1) & 0x07;
                    bool key = (_registers[0x20 + i] & 0x10) != 0;
                    int volume = 15 - (_registers[0x30 + i] & 0x0F);
                    if (!key || fnum == 0 || volume <= 0)
                        continue;

                    double frequency = Math.Max(8, fnum * Math.Pow(2, block) * 49716.0 / 524288.0);
                    yield return new NsfVoiceRow(row, baseVoice + i, $"VRC7 FM {i + 1}", FrequencyToPitch(frequency), (byte)Math.Clamp(volume * 4, 0, 64), 2, 0x70, (byte)fnum, (byte)((fnum >> 8) | (block << 4)), "vrc7 fm");
                }
            }
        }

        private sealed class Mmc5ChannelSet
        {
            private readonly Vrc6Pulse _pulse1 = new();
            private readonly Vrc6Pulse _pulse2 = new();
            private int _pcm = 32;

            public void Write(ushort address, byte value)
            {
                if (address is >= 0x5000 and <= 0x5003)
                    _pulse1.Write((ushort)Math.Min(address - 0x5000, 2), value);
                else if (address is >= 0x5004 and <= 0x5007)
                    _pulse2.Write((ushort)Math.Min(address - 0x5004, 2), value);
                else if (address == 0x5011)
                    _pcm = value & 0x7F;
            }

            public double Render(int sampleRate) =>
                _pulse1.Render(sampleRate) + _pulse2.Render(sampleRate) + ((_pcm - 64) / 8.0);

            public IEnumerable<NsfVoiceRow> Snapshot(int row, int baseVoice)
            {
                if (_pulse1.TrySnapshot(row, baseVoice, "MMC5 Pulse 1", out var p1))
                    yield return p1;
                if (_pulse2.TrySnapshot(row, baseVoice + 1, "MMC5 Pulse 2", out var p2))
                    yield return p2;
            }
        }

        private sealed class N163ChannelSet
        {
            private readonly byte[] _ram = new byte[128];
            private readonly double[] _phase = new double[8];
            private byte _address;
            private bool _autoIncrement;

            public void Write(ushort address, byte value)
            {
                if (address == 0xF800)
                {
                    _address = (byte)(value & 0x7F);
                    _autoIncrement = (value & 0x80) != 0;
                    return;
                }

                if (address != 0x4800)
                    return;

                _ram[_address & 0x7F] = value;
                if (_autoIncrement)
                    _address = (byte)((_address + 1) & 0x7F);
            }

            public double Render(int sampleRate)
            {
                double sum = 0;
                int channelCount = Math.Clamp(((_ram[0x7F] >> 4) & 0x07) + 1, 1, 8);
                for (int i = 0; i < channelCount; i++)
                {
                    int baseIndex = 0x78 - i * 8;
                    int frequency = _ram[baseIndex] | (_ram[baseIndex + 2] << 8) | ((_ram[baseIndex + 4] & 0x03) << 16);
                    int length = 256 - ((_ram[baseIndex + 4] & 0xFC) >> 2) * 4;
                    int waveAddress = _ram[baseIndex + 6] & 0x7F;
                    int volume = _ram[baseIndex + 7] & 0x0F;
                    if (frequency == 0 || volume == 0 || length <= 0)
                        continue;

                    double hz = frequency * 1_789_773.0 / (15.0 * 65536.0 * Math.Max(channelCount, 1));
                    _phase[i] = (_phase[i] + hz / sampleRate) % 1.0;
                    int step = (int)(_phase[i] * Math.Min(length, 128));
                    byte packed = _ram[(waveAddress + (step >> 1)) & 0x7F];
                    int sample = (step & 1) == 0 ? packed & 0x0F : packed >> 4;
                    sum += (sample - 7.5) / 7.5 * volume;
                }

                return sum;
            }

            public IEnumerable<NsfVoiceRow> Snapshot(int row, int baseVoice)
            {
                int channelCount = Math.Clamp(((_ram[0x7F] >> 4) & 0x07) + 1, 1, 8);
                for (int i = 0; i < channelCount; i++)
                {
                    int baseIndex = 0x78 - i * 8;
                    int frequency = _ram[baseIndex] | (_ram[baseIndex + 2] << 8) | ((_ram[baseIndex + 4] & 0x03) << 16);
                    int volume = _ram[baseIndex + 7] & 0x0F;
                    if (frequency == 0 || volume == 0)
                        continue;

                    double hz = frequency * 1_789_773.0 / (15.0 * 65536.0 * Math.Max(channelCount, 1));
                    yield return new NsfVoiceRow(row, baseVoice + i, $"N163 Wave {i + 1}", FrequencyToPitch(hz), (byte)Math.Clamp(volume * 4, 0, 64), 2, 0x63, (byte)frequency, (byte)(frequency >> 8), "n163");
                }
            }
        }

        private sealed class S5bChannelSet
        {
            private readonly byte[] _registers = new byte[16];
            private readonly double[] _phase = new double[3];
            private byte _selected;

            public void Write(ushort address, byte value)
            {
                if (address == 0xC000)
                    _selected = (byte)(value & 0x0F);
                else if (address == 0xE000)
                    _registers[_selected] = value;
            }

            public double Render(int sampleRate)
            {
                double sum = 0;
                for (int i = 0; i < 3; i++)
                {
                    int period = _registers[i * 2] | ((_registers[i * 2 + 1] & 0x0F) << 8);
                    int volume = _registers[8 + i] & 0x0F;
                    bool disabled = (_registers[7] & (1 << i)) != 0;
                    if (disabled || period == 0 || volume == 0)
                        continue;

                    double frequency = 1_789_773.0 / (16.0 * period);
                    _phase[i] = (_phase[i] + frequency / sampleRate) % 1.0;
                    sum += (_phase[i] < 0.5 ? 1 : -1) * volume;
                }

                return sum;
            }

            public IEnumerable<NsfVoiceRow> Snapshot(int row, int baseVoice)
            {
                for (int i = 0; i < 3; i++)
                {
                    int period = _registers[i * 2] | ((_registers[i * 2 + 1] & 0x0F) << 8);
                    int volume = _registers[8 + i] & 0x0F;
                    bool disabled = (_registers[7] & (1 << i)) != 0;
                    if (disabled || period == 0 || volume == 0)
                        continue;

                    double frequency = 1_789_773.0 / (16.0 * period);
                    yield return new NsfVoiceRow(row, baseVoice + i, $"S5B PSG {i + 1}", FrequencyToPitch(frequency), (byte)Math.Clamp(volume * 4, 0, 64), 2, 0x5B, (byte)period, (byte)(period >> 8), "s5b");
                }
            }
        }

        private sealed class FdsChannel
        {
            private readonly byte[] _wave = new byte[64];
            private int _volume;
            private int _frequency;
            private bool _haltWave;
            private bool _enabled;
            private double _phase;

            public FdsChannel()
            {
                for (int i = 0; i < _wave.Length; i++)
                    _wave[i] = (byte)(32 + Math.Sin(i / 64.0 * Math.PI * 2.0) * 24.0);
            }

            public void Write(ushort address, byte value)
            {
                if (address is >= 0x4040 and <= 0x407F)
                {
                    if (_haltWave)
                        _wave[address - 0x4040] = (byte)(value & 0x3F);
                    return;
                }

                switch (address)
                {
                    case 0x4080:
                        _volume = value & 0x3F;
                        break;
                    case 0x4082:
                        _frequency = (_frequency & 0xF00) | value;
                        break;
                    case 0x4083:
                        _frequency = (_frequency & 0x0FF) | ((value & 0x0F) << 8);
                        _haltWave = (value & 0x80) != 0;
                        _enabled = (value & 0x40) == 0;
                        break;
                }
            }

            public double Render(int sampleRate)
            {
                if (!_enabled || _frequency <= 0 || _volume == 0)
                    return 0;

                double frequency = _frequency * 1_789_773.0 / 65536.0;
                _phase = (_phase + frequency / sampleRate) % 1.0;
                int index = (int)(_phase * 64.0) & 0x3F;
                return ((_wave[index] - 32) / 32.0) * _volume;
            }

            public bool TrySnapshot(int row, int voice, out NsfVoiceRow state)
            {
                if (!_enabled || _frequency <= 0 || _volume == 0)
                {
                    state = default!;
                    return false;
                }

                double frequency = _frequency * 1_789_773.0 / 65536.0;
                state = new NsfVoiceRow(
                    row,
                    voice,
                    "FDS Wavetable",
                    FrequencyToPitch(frequency),
                    (byte)Math.Clamp(_volume, 0, 64),
                    2,
                    0x50,
                    (byte)(_frequency & 0xFF),
                    (byte)((_frequency >> 8) & 0x0F),
                    "fds");
                return true;
            }
        }
    }

    /// <summary>
    /// Practical 6502 interpreter for NSF init/play routines.
    /// </summary>
    private sealed class NsfCpu
    {
        private const byte FlagC = 0x01;
        private const byte FlagZ = 0x02;
        private const byte FlagI = 0x04;
        private const byte FlagD = 0x08;
        private const byte FlagB = 0x10;
        private const byte FlagU = 0x20;
        private const byte FlagV = 0x40;
        private const byte FlagN = 0x80;

        private readonly byte[] _memory = new byte[65536];
        private readonly NsfProgram _program;
        private readonly NesApu _apu;
        private ushort _pc;
        private byte _sp = 0xFD;
        private byte _p = FlagU | FlagI;

        public byte A { get; private set; }
        public byte X { get; private set; }
        public byte Y { get; private set; }
        public bool LastCallTimedOut { get; private set; }

        public NsfCpu(NsfProgram program, NesApu apu)
        {
            _program = program;
            _apu = apu;
            ResetMemory();
        }

        public void Call(ushort address, byte a, byte x, int maxInstructions, int maxMilliseconds = 25)
        {
            LastCallTimedOut = false;
            A = a;
            X = x;
            Y = 0;
            _pc = address;
            _sp = 0xFD;
            _p = FlagU | FlagI;
            PushWord(0xFFFF);
            var budget = Stopwatch.StartNew();
            for (int i = 0; i < maxInstructions && Step(); i++)
            {
                if ((i & 0x3FF) == 0 && budget.ElapsedMilliseconds > maxMilliseconds)
                {
                    LastCallTimedOut = true;
                    break;
                }
            }
        }

        private void ResetMemory()
        {
            Array.Clear(_memory);
            if (_program.UsesBanks)
            {
                for (int slot = 0; slot < 8; slot++)
                    MapBank(slot, _program.InitialBanks[slot]);
            }
            else
            {
                int start = _program.LoadAddress;
                int length = Math.Min(_program.Data.Length, 65536 - start);
                Array.Copy(_program.Data, 0, _memory, start, length);
            }
        }

        private bool Step()
        {
            byte op = Read(_pc++);
            switch (op)
            {
                case 0x00: return false;
                case 0xEA: break;
                case 0xA9: A = Fetch(); SetZn(A); break;
                case 0xA5: A = Read(Zp()); SetZn(A); break;
                case 0xB5: A = Read((byte)(Zp() + X)); SetZn(A); break;
                case 0xAD: A = Read(Abs()); SetZn(A); break;
                case 0xBD: A = Read((ushort)(Abs() + X)); SetZn(A); break;
                case 0xB9: A = Read((ushort)(Abs() + Y)); SetZn(A); break;
                case 0xA1: A = Read(IndX()); SetZn(A); break;
                case 0xB1: A = Read((ushort)(IndYBase() + Y)); SetZn(A); break;
                case 0xA2: X = Fetch(); SetZn(X); break;
                case 0xA6: X = Read(Zp()); SetZn(X); break;
                case 0xB6: X = Read((byte)(Zp() + Y)); SetZn(X); break;
                case 0xAE: X = Read(Abs()); SetZn(X); break;
                case 0xBE: X = Read((ushort)(Abs() + Y)); SetZn(X); break;
                case 0xA0: Y = Fetch(); SetZn(Y); break;
                case 0xA4: Y = Read(Zp()); SetZn(Y); break;
                case 0xB4: Y = Read((byte)(Zp() + X)); SetZn(Y); break;
                case 0xAC: Y = Read(Abs()); SetZn(Y); break;
                case 0xBC: Y = Read((ushort)(Abs() + X)); SetZn(Y); break;
                case 0x85: Write(Zp(), A); break;
                case 0x95: Write((byte)(Zp() + X), A); break;
                case 0x8D: Write(Abs(), A); break;
                case 0x9D: Write((ushort)(Abs() + X), A); break;
                case 0x99: Write((ushort)(Abs() + Y), A); break;
                case 0x81: Write(IndX(), A); break;
                case 0x91: Write((ushort)(IndYBase() + Y), A); break;
                case 0x86: Write(Zp(), X); break;
                case 0x96: Write((byte)(Zp() + Y), X); break;
                case 0x8E: Write(Abs(), X); break;
                case 0x84: Write(Zp(), Y); break;
                case 0x94: Write((byte)(Zp() + X), Y); break;
                case 0x8C: Write(Abs(), Y); break;
                case 0xAA: X = A; SetZn(X); break;
                case 0xA8: Y = A; SetZn(Y); break;
                case 0x8A: A = X; SetZn(A); break;
                case 0x98: A = Y; SetZn(A); break;
                case 0xBA: X = _sp; SetZn(X); break;
                case 0x9A: _sp = X; break;
                case 0x1A:
                case 0x3A:
                case 0x5A:
                case 0x7A:
                case 0xDA:
                case 0xFA:
                    break;
                case 0x48: Push(A); break;
                case 0x68: A = Pop(); SetZn(A); break;
                case 0x08: Push((byte)(_p | FlagB | FlagU)); break;
                case 0x28: _p = (byte)((Pop() | FlagU) & ~FlagB); break;
                case 0xE8: X++; SetZn(X); break;
                case 0xC8: Y++; SetZn(Y); break;
                case 0xCA: X--; SetZn(X); break;
                case 0x88: Y--; SetZn(Y); break;
                case 0xE6: Inc(Zp()); break;
                case 0xF6: Inc((byte)(Zp() + X)); break;
                case 0xEE: Inc(Abs()); break;
                case 0xFE: Inc((ushort)(Abs() + X)); break;
                case 0xC6: Dec(Zp()); break;
                case 0xD6: Dec((byte)(Zp() + X)); break;
                case 0xCE: Dec(Abs()); break;
                case 0xDE: Dec((ushort)(Abs() + X)); break;
                case 0x69: Adc(Fetch()); break;
                case 0x65: Adc(Read(Zp())); break;
                case 0x75: Adc(Read((byte)(Zp() + X))); break;
                case 0x6D: Adc(Read(Abs())); break;
                case 0x7D: Adc(Read((ushort)(Abs() + X))); break;
                case 0x79: Adc(Read((ushort)(Abs() + Y))); break;
                case 0x61: Adc(Read(IndX())); break;
                case 0x71: Adc(Read((ushort)(IndYBase() + Y))); break;
                case 0xE9:
                case 0xEB: Sbc(Fetch()); break;
                case 0xE5: Sbc(Read(Zp())); break;
                case 0xF5: Sbc(Read((byte)(Zp() + X))); break;
                case 0xED: Sbc(Read(Abs())); break;
                case 0xFD: Sbc(Read((ushort)(Abs() + X))); break;
                case 0xF9: Sbc(Read((ushort)(Abs() + Y))); break;
                case 0xE1: Sbc(Read(IndX())); break;
                case 0xF1: Sbc(Read((ushort)(IndYBase() + Y))); break;
                case 0x29: A &= Fetch(); SetZn(A); break;
                case 0x25: A &= Read(Zp()); SetZn(A); break;
                case 0x35: A &= Read((byte)(Zp() + X)); SetZn(A); break;
                case 0x2D: A &= Read(Abs()); SetZn(A); break;
                case 0x3D: A &= Read((ushort)(Abs() + X)); SetZn(A); break;
                case 0x39: A &= Read((ushort)(Abs() + Y)); SetZn(A); break;
                case 0x21: A &= Read(IndX()); SetZn(A); break;
                case 0x31: A &= Read((ushort)(IndYBase() + Y)); SetZn(A); break;
                case 0x09: A |= Fetch(); SetZn(A); break;
                case 0x05: A |= Read(Zp()); SetZn(A); break;
                case 0x15: A |= Read((byte)(Zp() + X)); SetZn(A); break;
                case 0x0D: A |= Read(Abs()); SetZn(A); break;
                case 0x1D: A |= Read((ushort)(Abs() + X)); SetZn(A); break;
                case 0x19: A |= Read((ushort)(Abs() + Y)); SetZn(A); break;
                case 0x01: A |= Read(IndX()); SetZn(A); break;
                case 0x11: A |= Read((ushort)(IndYBase() + Y)); SetZn(A); break;
                case 0x49: A ^= Fetch(); SetZn(A); break;
                case 0x45: A ^= Read(Zp()); SetZn(A); break;
                case 0x55: A ^= Read((byte)(Zp() + X)); SetZn(A); break;
                case 0x4D: A ^= Read(Abs()); SetZn(A); break;
                case 0x5D: A ^= Read((ushort)(Abs() + X)); SetZn(A); break;
                case 0x59: A ^= Read((ushort)(Abs() + Y)); SetZn(A); break;
                case 0x41: A ^= Read(IndX()); SetZn(A); break;
                case 0x51: A ^= Read((ushort)(IndYBase() + Y)); SetZn(A); break;
                case 0xC9: Cmp(A, Fetch()); break;
                case 0xC5: Cmp(A, Read(Zp())); break;
                case 0xD5: Cmp(A, Read((byte)(Zp() + X))); break;
                case 0xCD: Cmp(A, Read(Abs())); break;
                case 0xDD: Cmp(A, Read((ushort)(Abs() + X))); break;
                case 0xD9: Cmp(A, Read((ushort)(Abs() + Y))); break;
                case 0xC1: Cmp(A, Read(IndX())); break;
                case 0xD1: Cmp(A, Read((ushort)(IndYBase() + Y))); break;
                case 0xE0: Cmp(X, Fetch()); break;
                case 0xE4: Cmp(X, Read(Zp())); break;
                case 0xEC: Cmp(X, Read(Abs())); break;
                case 0xC0: Cmp(Y, Fetch()); break;
                case 0xC4: Cmp(Y, Read(Zp())); break;
                case 0xCC: Cmp(Y, Read(Abs())); break;
                case 0x0A: A = Asl(A); break;
                case 0x06: Rmw(Zp(), Asl); break;
                case 0x16: Rmw((byte)(Zp() + X), Asl); break;
                case 0x0E: Rmw(Abs(), Asl); break;
                case 0x1E: Rmw((ushort)(Abs() + X), Asl); break;
                case 0x4A: A = Lsr(A); break;
                case 0x46: Rmw(Zp(), Lsr); break;
                case 0x56: Rmw((byte)(Zp() + X), Lsr); break;
                case 0x4E: Rmw(Abs(), Lsr); break;
                case 0x5E: Rmw((ushort)(Abs() + X), Lsr); break;
                case 0x2A: A = Rol(A); break;
                case 0x26: Rmw(Zp(), Rol); break;
                case 0x36: Rmw((byte)(Zp() + X), Rol); break;
                case 0x2E: Rmw(Abs(), Rol); break;
                case 0x3E: Rmw((ushort)(Abs() + X), Rol); break;
                case 0x6A: A = Ror(A); break;
                case 0x66: Rmw(Zp(), Ror); break;
                case 0x76: Rmw((byte)(Zp() + X), Ror); break;
                case 0x6E: Rmw(Abs(), Ror); break;
                case 0x7E: Rmw((ushort)(Abs() + X), Ror); break;
                case 0x24: Bit(Read(Zp())); break;
                case 0x2C: Bit(Read(Abs())); break;
                case 0x4C: _pc = Abs(); break;
                case 0x6C: _pc = ReadWordBug(Abs()); break;
                case 0x20: { ushort target = Abs(); PushWord((ushort)(_pc - 1)); _pc = target; break; }
                case 0x60: _pc = (ushort)(PopWord() + 1); if (_pc == 0) return false; break;
                case 0x40: _p = (byte)((Pop() | FlagU) & ~FlagB); _pc = PopWord(); break;
                case 0x10: Branch(!Get(FlagN)); break;
                case 0x30: Branch(Get(FlagN)); break;
                case 0x50: Branch(!Get(FlagV)); break;
                case 0x70: Branch(Get(FlagV)); break;
                case 0x90: Branch(!Get(FlagC)); break;
                case 0xB0: Branch(Get(FlagC)); break;
                case 0xD0: Branch(!Get(FlagZ)); break;
                case 0xF0: Branch(Get(FlagZ)); break;
                case 0x18: Set(FlagC, false); break;
                case 0x38: Set(FlagC, true); break;
                case 0x58: Set(FlagI, false); break;
                case 0x78: Set(FlagI, true); break;
                case 0xB8: Set(FlagV, false); break;
                case 0xD8: Set(FlagD, false); break;
                case 0xF8: Set(FlagD, true); break;
                case 0x80:
                case 0x82:
                case 0x89:
                case 0xC2:
                case 0xE2:
                case 0x04:
                case 0x44:
                case 0x64:
                case 0x14:
                case 0x34:
                case 0x54:
                case 0x74:
                case 0xD4:
                case 0xF4:
                    Fetch();
                    break;
                case 0x0C:
                case 0x1C:
                case 0x3C:
                case 0x5C:
                case 0x7C:
                case 0xDC:
                case 0xFC:
                    Abs();
                    break;
                case 0xA7: A = X = Read(Zp()); SetZn(A); break;
                case 0xB7: A = X = Read((byte)(Zp() + Y)); SetZn(A); break;
                case 0xAF: A = X = Read(Abs()); SetZn(A); break;
                case 0xBF: A = X = Read((ushort)(Abs() + Y)); SetZn(A); break;
                case 0xA3: A = X = Read(IndX()); SetZn(A); break;
                case 0xB3: A = X = Read((ushort)(IndYBase() + Y)); SetZn(A); break;
                case 0xAB: A = X = Fetch(); SetZn(A); break;
                case 0x87: Write(Zp(), (byte)(A & X)); break;
                case 0x97: Write((byte)(Zp() + Y), (byte)(A & X)); break;
                case 0x8F: Write(Abs(), (byte)(A & X)); break;
                case 0x83: Write(IndX(), (byte)(A & X)); break;
                case 0x0B:
                case 0x2B: Anc(Fetch()); break;
                case 0x4B: Alr(Fetch()); break;
                case 0x6B: Arr(Fetch()); break;
                case 0x8B: A = (byte)(X & Fetch()); SetZn(A); break;
                case 0xCB: Axs(Fetch()); break;
                case 0x93: Ahx((ushort)(IndYBase() + Y)); break;
                case 0x9B: Tas((ushort)(Abs() + Y)); break;
                case 0x9C: Shy((ushort)(Abs() + X)); break;
                case 0x9E: Shx((ushort)(Abs() + Y)); break;
                case 0x9F: Ahx((ushort)(Abs() + Y)); break;
                case 0x02:
                case 0x12:
                case 0x22:
                case 0x32:
                case 0x42:
                case 0x52:
                case 0x62:
                case 0x72:
                case 0x92:
                case 0xB2:
                case 0xD2:
                case 0xF2:
                    return false;
                case 0xC7: Dcp(Zp()); break;
                case 0xD7: Dcp((byte)(Zp() + X)); break;
                case 0xCF: Dcp(Abs()); break;
                case 0xDF: Dcp((ushort)(Abs() + X)); break;
                case 0xDB: Dcp((ushort)(Abs() + Y)); break;
                case 0xC3: Dcp(IndX()); break;
                case 0xD3: Dcp((ushort)(IndYBase() + Y)); break;
                case 0xE7: Isb(Zp()); break;
                case 0xF7: Isb((byte)(Zp() + X)); break;
                case 0xEF: Isb(Abs()); break;
                case 0xFF: Isb((ushort)(Abs() + X)); break;
                case 0xFB: Isb((ushort)(Abs() + Y)); break;
                case 0xE3: Isb(IndX()); break;
                case 0xF3: Isb((ushort)(IndYBase() + Y)); break;
                case 0x07: Slo(Zp()); break;
                case 0x17: Slo((byte)(Zp() + X)); break;
                case 0x0F: Slo(Abs()); break;
                case 0x1F: Slo((ushort)(Abs() + X)); break;
                case 0x1B: Slo((ushort)(Abs() + Y)); break;
                case 0x03: Slo(IndX()); break;
                case 0x13: Slo((ushort)(IndYBase() + Y)); break;
                case 0x27: Rla(Zp()); break;
                case 0x37: Rla((byte)(Zp() + X)); break;
                case 0x2F: Rla(Abs()); break;
                case 0x3F: Rla((ushort)(Abs() + X)); break;
                case 0x3B: Rla((ushort)(Abs() + Y)); break;
                case 0x23: Rla(IndX()); break;
                case 0x33: Rla((ushort)(IndYBase() + Y)); break;
                case 0x47: Sre(Zp()); break;
                case 0x57: Sre((byte)(Zp() + X)); break;
                case 0x4F: Sre(Abs()); break;
                case 0x5F: Sre((ushort)(Abs() + X)); break;
                case 0x5B: Sre((ushort)(Abs() + Y)); break;
                case 0x43: Sre(IndX()); break;
                case 0x53: Sre((ushort)(IndYBase() + Y)); break;
                case 0x67: Rra(Zp()); break;
                case 0x77: Rra((byte)(Zp() + X)); break;
                case 0x6F: Rra(Abs()); break;
                case 0x7F: Rra((ushort)(Abs() + X)); break;
                case 0x7B: Rra((ushort)(Abs() + Y)); break;
                case 0x63: Rra(IndX()); break;
                case 0x73: Rra((ushort)(IndYBase() + Y)); break;
                default:
                    // Last-resort handling for uncommon unofficial opcodes; common multi-byte NOPs are decoded above.
                    break;
            }

            return true;
        }

        private byte Read(ushort address)
        {
            if (address < 0x2000)
                return _memory[address & 0x07FF];
            if (address == 0x4015)
                return _apu.ReadStatus();
            return _memory[address];
        }

        private void Write(int address, byte value)
        {
            ushort addr = (ushort)address;
            if (addr < 0x2000)
            {
                _memory[addr & 0x07FF] = value;
                return;
            }

            if (addr is >= 0x4000 and <= 0x4017 ||
                addr is >= 0x4040 and <= 0x409F ||
                addr is >= 0x4800 and <= 0x5015 ||
                addr is >= 0x9000 and <= 0xBFFF ||
                addr is 0xC000 or 0xE000 or 0xF800)
            {
                _apu.Write(addr, value);
                _memory[addr] = value;
                return;
            }

            if (addr is >= 0x5FF8 and <= 0x5FFF)
            {
                MapBank(addr - 0x5FF8, value);
                return;
            }
            else
                _memory[addr] = value;
        }

        private void MapBank(int slot, int bank)
        {
            int address = 0x8000 + slot * 0x1000;
            int source = bank * 0x1000 - (_program.LoadAddress & 0x0FFF);
            for (int i = 0; i < 0x1000; i++)
            {
                int index = source + i;
                _memory[address + i] = (uint)index < (uint)_program.Data.Length ? _program.Data[index] : (byte)0;
            }
        }

        private byte Fetch() => Read(_pc++);
        private byte Zp() => Fetch();
        private ushort Abs() { byte lo = Fetch(); byte hi = Fetch(); return (ushort)(lo | (hi << 8)); }
        private ushort IndX() { byte zp = (byte)(Fetch() + X); return (ushort)(Read(zp) | (Read((byte)(zp + 1)) << 8)); }
        private ushort IndYBase() { byte zp = Fetch(); return (ushort)(Read(zp) | (Read((byte)(zp + 1)) << 8)); }
        private ushort ReadWordBug(ushort address) => (ushort)(Read(address) | (Read((ushort)((address & 0xFF00) | ((address + 1) & 0xFF))) << 8));
        private void Push(byte value) => _memory[0x100 + _sp--] = value;
        private byte Pop() => _memory[0x100 + ++_sp];
        private void PushWord(ushort value) { Push((byte)(value >> 8)); Push((byte)value); }
        private ushort PopWord() { byte lo = Pop(); byte hi = Pop(); return (ushort)(lo | (hi << 8)); }
        private bool Get(byte flag) => (_p & flag) != 0;
        private void Set(byte flag, bool value) { if (value) _p |= flag; else _p &= (byte)~flag; }
        private void SetZn(byte value) { Set(FlagZ, value == 0); Set(FlagN, (value & 0x80) != 0); }
        private void Inc(int address) { byte value = (byte)(Read((ushort)address) + 1); Write(address, value); SetZn(value); }
        private void Dec(int address) { byte value = (byte)(Read((ushort)address) - 1); Write(address, value); SetZn(value); }
        private void Rmw(int address, Func<byte, byte> op) => Write(address, op(Read((ushort)address)));
        private void Dcp(int address) { byte value = (byte)(Read((ushort)address) - 1); Write(address, value); Cmp(A, value); }
        private void Isb(int address) { byte value = (byte)(Read((ushort)address) + 1); Write(address, value); Sbc(value); }
        private void Slo(int address) { byte value = Asl(Read((ushort)address)); Write(address, value); A |= value; SetZn(A); }
        private void Rla(int address) { byte value = Rol(Read((ushort)address)); Write(address, value); A &= value; SetZn(A); }
        private void Sre(int address) { byte value = Lsr(Read((ushort)address)); Write(address, value); A ^= value; SetZn(A); }
        private void Rra(int address) { byte value = Ror(Read((ushort)address)); Write(address, value); Adc(value); }
        private void Anc(byte value) { A &= value; SetZn(A); Set(FlagC, (A & 0x80) != 0); }
        private void Alr(byte value) { A &= value; A = Lsr(A); }
        private void Arr(byte value)
        {
            A &= value;
            A = (byte)((A >> 1) | (Get(FlagC) ? 0x80 : 0));
            SetZn(A);
            Set(FlagC, (A & 0x40) != 0);
            Set(FlagV, (((A >> 6) ^ (A >> 5)) & 1) != 0);
        }

        private void Axs(byte value)
        {
            int source = A & X;
            int result = source - value;
            Set(FlagC, source >= value);
            X = (byte)result;
            SetZn(X);
        }

        private void Ahx(ushort address) => Write(address, (byte)(A & X & (((address >> 8) + 1) & 0xFF)));
        private void Tas(ushort address)
        {
            _sp = (byte)(A & X);
            Write(address, (byte)(_sp & (((address >> 8) + 1) & 0xFF)));
        }

        private void Shy(ushort address) => Write(address, (byte)(Y & (((address >> 8) + 1) & 0xFF)));
        private void Shx(ushort address) => Write(address, (byte)(X & (((address >> 8) + 1) & 0xFF)));
        private void Branch(bool condition) { sbyte rel = unchecked((sbyte)Fetch()); if (condition) _pc = (ushort)(_pc + rel); }
        private void Cmp(byte left, byte right) { int value = left - right; Set(FlagC, left >= right); SetZn((byte)value); }
        private void Bit(byte value) { Set(FlagZ, (A & value) == 0); Set(FlagV, (value & 0x40) != 0); Set(FlagN, (value & 0x80) != 0); }
        private void Adc(byte value) { int sum = A + value + (Get(FlagC) ? 1 : 0); Set(FlagC, sum > 0xFF); Set(FlagV, (~(A ^ value) & (A ^ sum) & 0x80) != 0); A = (byte)sum; SetZn(A); }
        private void Sbc(byte value) => Adc((byte)~value);
        private byte Asl(byte value) { Set(FlagC, (value & 0x80) != 0); value <<= 1; SetZn(value); return value; }
        private byte Lsr(byte value) { Set(FlagC, (value & 0x01) != 0); value >>= 1; SetZn(value); return value; }
        private byte Rol(byte value) { bool carry = Get(FlagC); Set(FlagC, (value & 0x80) != 0); value = (byte)((value << 1) | (carry ? 1 : 0)); SetZn(value); return value; }
        private byte Ror(byte value) { bool carry = Get(FlagC); Set(FlagC, (value & 0x01) != 0); value = (byte)((value >> 1) | (carry ? 0x80 : 0)); SetZn(value); return value; }
    }

    /// <summary>
    /// Executes the BuildDigest operation.
    /// </summary>
    private static byte[] BuildDigest(byte[] data, int length)
    {
        byte[] digest = new byte[length];
        if (data.Length == 0)
            return digest;

        for (int i = 0; i < digest.Length; i++)
        {
            int offset = (i * 97 + data[i % data.Length]) % data.Length;
            digest[i] = (byte)(data[offset] ^ data[(offset + i * 31) % data.Length] ^ i);
        }

        return digest;
    }

    /// <summary>
    /// Executes the Hash operation.
    /// </summary>
    private static uint Hash(byte[] data)
    {
        uint hash = 2166136261;
        foreach (byte value in data)
        {
            hash ^= value;
            hash *= 16777619;
        }

        return hash == 0 ? 1u : hash;
    }

    /// <summary>
    /// Executes the NextNoise operation.
    /// </summary>
    private static double NextNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (double)0xFFFFFF;
    }

    /// <summary>
    /// Executes the SoftClip operation.
    /// </summary>
    private static float SoftClip(double value) => (float)Math.Tanh(value * 1.35);

    /// <summary>
    /// Executes the WriteStereoFloatAsPcm16Wav operation.
    /// </summary>
    private static void WriteStereoFloatAsPcm16Wav(string path, float[] stereoSamples, int sampleRate)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        int dataBytes = stereoSamples.Length * sizeof(short);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2 * sizeof(short));
        writer.Write((short)(2 * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        foreach (float sample in stereoSamples)
        {
            short pcm = (short)Math.Clamp(Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
            writer.Write(pcm);
        }
    }

    /// <summary>
    /// Represents the SidProgram component.
    /// </summary>
    private sealed class SidProgram
    {
        /// <summary>
        /// Stores or exposes Memory.
        /// </summary>
        public byte[] Memory { get; } = new byte[65536];
        /// <summary>
        /// Stores or exposes LoadAddress.
        /// </summary>
        public ushort LoadAddress { get; private init; }
        /// <summary>
        /// Stores or exposes InitAddress.
        /// </summary>
        public ushort InitAddress { get; private init; }
        /// <summary>
        /// Stores or exposes PlayAddress.
        /// </summary>
        public ushort PlayAddress { get; private set; }
        /// <summary>
        /// Stores or exposes IsPal.
        /// </summary>
        public bool IsPal { get; private init; }
        /// <summary>
        /// Stores or exposes Is8580.
        /// </summary>
        public bool Is8580 { get; private init; }
        /// <summary>
        /// Stores or exposes StartSong.
        /// </summary>
        public int StartSong { get; private init; }
        /// <summary>
        /// Executes the InitialAccumulatorValue operation.
        /// </summary>
        public byte InitialAccumulatorValue => (byte)Math.Max(0, StartSong - 1);

        /// <summary>
        /// Executes the Load operation.
        /// </summary>
        public static SidProgram? Load(byte[] data, int? songOverride = null)
        {
            if (data.Length < 0x7E)
                return null;

            string magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            if (magic is not ("PSID" or "RSID"))
                return null;

            ushort version = ReadBe(data, 4);
            int dataOffset = ReadBe(data, 6);
            ushort loadAddress = ReadBe(data, 8);
            ushort initAddress = ReadBe(data, 10);
            ushort playAddress = ReadBe(data, 12);
            int songCount = Math.Max((int)ReadBe(data, 14), 1);
            int startSong = Math.Max((int)ReadBe(data, 16), 1);
            if (songOverride is not null)
                startSong = Math.Clamp(songOverride.Value, 1, songCount);
            bool isPal = true;
            bool is8580 = false;
            if (version >= 2 && data.Length >= 0x78)
            {
                ushort flags = ReadBe(data, 0x76);
                int clock = (flags >> 2) & 0x03;
                isPal = clock != 2;
                int sidModel = (flags >> 4) & 0x03;
                is8580 = sidModel == 2;
            }

            if (dataOffset <= 0 || dataOffset >= data.Length)
                return null;

            if (loadAddress == 0)
            {
                if (dataOffset + 1 >= data.Length)
                    return null;
                loadAddress = (ushort)(data[dataOffset] | (data[dataOffset + 1] << 8));
                dataOffset += 2;
            }

            if (initAddress == 0)
                initAddress = loadAddress;
            else
                initAddress = NormalizeEntryAddress(initAddress, loadAddress);
            playAddress = NormalizeEntryAddress(playAddress, loadAddress);

            var program = new SidProgram
            {
                LoadAddress = loadAddress,
                InitAddress = initAddress,
                PlayAddress = playAddress,
                IsPal = isPal,
                Is8580 = is8580,
                StartSong = startSong
            };

            Array.Copy(data, dataOffset, program.Memory, loadAddress, Math.Min(data.Length - dataOffset, 65536 - loadAddress));
            program.Memory[0x0001] = 0x37;
            program.Memory[0xDC05] = isPal ? (byte)0x37 : (byte)0x95;
            return program;
        }

        /// <summary>
        /// Executes the ResolveIrqPlayAddress operation.
        /// </summary>
        public void ResolveIrqPlayAddress()
        {
            if (PlayAddress == 0)
                PlayAddress = (ushort)(Memory[0x0314] | (Memory[0x0315] << 8));
            if (PlayAddress == 0)
                PlayAddress = (ushort)(Memory[0xFFFE] | (Memory[0xFFFF] << 8));
            if (PlayAddress == 0)
                PlayAddress = InitAddress;
        }

        /// <summary>
        /// Executes the ReadBe operation.
        /// </summary>
        private static ushort ReadBe(byte[] data, int offset) =>
            offset + 1 < data.Length ? (ushort)((data[offset] << 8) | data[offset + 1]) : (ushort)0;

        /// <summary>
        /// Executes the NormalizeEntryAddress operation.
        /// </summary>
        private static ushort NormalizeEntryAddress(ushort address, ushort loadAddress)
        {
            if (address == 0)
                return 0;

            // A few real-world PSID files store small play/init offsets even
            // though the spec says these are absolute C64 addresses.
            if (address < 0x0400 && loadAddress >= 0x0400)
                return (ushort)(loadAddress + address);

            return address;
        }
    }

    /// <summary>
    /// Represents the SidRuntime component.
    /// </summary>
    private sealed class SidRuntime
    {
        /// <summary>
        /// Stores or exposes _program.
        /// </summary>
        private readonly SidProgram _program;
        /// <summary>
        /// Stores or exposes _sid.
        /// </summary>
        private readonly SimpleSid _sid;
        /// <summary>
        /// Stores or exposes _io.
        /// </summary>
        private readonly C64Io _io;
        /// <summary>
        /// Stores or exposes _cpu.
        /// </summary>
        private readonly Mos6510 _cpu;
        /// <summary>
        /// Stores or exposes _badFrames.
        /// </summary>
        private int _badFrames;
        private int _deferredPlayFrames;

        private SidRuntime(SidProgram program)
        {
            _program = program;
            _sid = new SimpleSid(program.Is8580);
            _io = new C64Io(program.Memory, _sid, program.IsPal);
            _cpu = new Mos6510(program.Memory, _io.Read, _io.Write);
        }

        /// <summary>
        /// Executes the Render operation.
        /// </summary>
        public static float[] Render(SidProgram program, ChipTuneMetadata metadata, int seconds, int sampleRate)
        {
            var runtime = new SidRuntime(program);
            runtime.Call(program.InitAddress, program.InitialAccumulatorValue, 200000, maxMilliseconds: 120, critical: true);
            program.ResolveIrqPlayAddress();
            if (program.PlayAddress == 0)
                return RenderSidFallbackProgram(program.Memory, seconds, sampleRate);

            int frames = seconds * sampleRate;
            float[] stereo = new float[frames * 2];
            double framesPerSecond = program.IsPal ? 50.0 : 60.0;
            int samplesPerTick = Math.Max(1, (int)Math.Round(sampleRate / framesPerSecond));
            int nextTick = 0;
            int renderRate = Math.Min(sampleRate * 2, 192000);
            int oversample = renderRate > sampleRate ? 2 : 1;

            for (int frame = 0; frame < frames; frame++)
            {
                if (frame >= nextTick)
                {
                    runtime._io.BeginFrame();
                    runtime.PlayFrame(program.PlayAddress);
                    nextTick += samplesPerTick;
                }

                double left = 0;
                double right = 0;
                for (int sub = 0; sub < oversample; sub++)
                {
                    var sample = runtime._sid.RenderSample(renderRate, program.IsPal);
                    left += sample.Left;
                    right += sample.Right;
                }

                stereo[frame * 2] = (float)(left / oversample);
                stereo[frame * 2 + 1] = (float)(right / oversample);
            }

            return stereo;
        }

        public static (int SidWrites, int VoiceWrites, byte Volume) Analyze(SidProgram program, int frames)
        {
            var runtime = new SidRuntime(program);
            runtime.Call(program.InitAddress, program.InitialAccumulatorValue, 200000, maxMilliseconds: 120, critical: true);
            program.ResolveIrqPlayAddress();
            for (int i = 0; i < frames; i++)
            {
                runtime._io.BeginFrame();
                runtime.Call(program.PlayAddress, 0, 240000, maxMilliseconds: 6);
            }

            return (runtime._sid.WriteCount, runtime._sid.VoiceWriteCount, runtime._sid.Volume);
        }

        public static (ushort LoadAddress, ushort InitAddress, ushort PlayAddress, ushort IrqVector, int SidWrites) AnalyzeExecution(SidProgram program, int frames)
        {
            var runtime = new SidRuntime(program);
            runtime.Call(program.InitAddress, program.InitialAccumulatorValue, 200000, maxMilliseconds: 120, critical: true);
            ushort irq = (ushort)(program.Memory[0x0314] | (program.Memory[0x0315] << 8));
            program.ResolveIrqPlayAddress();
            for (int i = 0; i < frames; i++)
            {
                runtime._io.BeginFrame();
                runtime.Call(program.PlayAddress, 0, 240000, maxMilliseconds: 6);
            }

            return (program.LoadAddress, program.InitAddress, program.PlayAddress, irq, runtime._sid.WriteCount);
        }

        /// <summary>
        /// Executes the DumpRegisters operation.
        /// </summary>
        public static byte[] DumpRegisters(SidProgram program, int frames)
        {
            var runtime = new SidRuntime(program);
            runtime.Call(program.InitAddress, program.InitialAccumulatorValue, 200000, maxMilliseconds: 120, critical: true);
            program.ResolveIrqPlayAddress();
            for (int i = 0; i < frames; i++)
            {
                runtime._io.BeginFrame();
                runtime.Call(program.PlayAddress, 0, 240000, maxMilliseconds: 6);
            }
            return runtime._sid.RegisterSnapshot();
        }

        /// <summary>
        /// Executes the InspectVoiceRows operation.
        /// </summary>
        public static IReadOnlyList<SidVoiceRow> InspectVoiceRows(SidProgram program, int frames)
        {
            var runtime = new SidRuntime(program);
            runtime.Call(program.InitAddress, program.InitialAccumulatorValue, 200000, maxMilliseconds: 120, critical: true);
            program.ResolveIrqPlayAddress();
            if (program.PlayAddress == 0)
                return [];

            var events = new List<SidVoiceRow>();
            var previous = new SidVoiceState[3];
            for (int row = 0; row < frames; row++)
            {
                runtime._io.BeginFrame();
                runtime.Call(program.PlayAddress, 0, 240000, maxMilliseconds: 6);
                byte[] regs = runtime._sid.RegisterSnapshot();
                byte masterVolume = (byte)Math.Clamp((regs[0x18] & 0x0F) * 4, 0, 64);
                ushort filterCutoff = (ushort)(((regs[0x16] << 3) | (regs[0x15] & 0x07)) & 0x07FF);
                for (int voice = 0; voice < 3; voice++)
                {
                    int offset = voice * 7;
                    ushort freq = (ushort)(regs[offset] | (regs[offset + 1] << 8));
                    ushort pulseWidth = (ushort)(regs[offset + 2] | ((regs[offset + 3] & 0x0F) << 8));
                    byte control = regs[offset + 4];
                    byte attackDecay = regs[offset + 5];
                    byte sustainRelease = regs[offset + 6];
                    bool gate = (control & 0x01) != 0;
                    byte pitch = freq == 0 || !gate
                        ? (byte)SpecialNote.NoteOff
                        : HzToMidi(SidFrequencyRegisterToHzForTests(freq, program.IsPal));
                    var current = new SidVoiceState(
                        pitch,
                        gate,
                        control,
                        (byte)(control & 0xF0),
                        freq,
                        pulseWidth,
                        attackDecay,
                        sustainRelease,
                        regs[0x17],
                        regs[0x18],
                        filterCutoff);

                    if (!current.Equals(previous[voice]))
                    {
                        byte volume = gate ? masterVolume == 0 ? (byte)48 : masterVolume : (byte)0;
                        events.Add(new SidVoiceRow(
                            row,
                            voice,
                            pitch,
                            volume,
                            control,
                            (byte)(control & 0xF0),
                            1,
                            freq,
                            pulseWidth,
                            attackDecay,
                            sustainRelease,
                            regs[0x17],
                            regs[0x18],
                            filterCutoff));
                        previous[voice] = current;
                    }
                }
            }

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                int nextRow = frames;
                for (int j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Voice == ev.Voice)
                    {
                        nextRow = events[j].Row;
                        break;
                    }
                }

                events[i] = ev with { DurationRows = Math.Max(1, nextRow - ev.Row) };
            }

            return events;
        }

        /// <summary>
        /// Executes the HzToMidi operation.
        /// </summary>
        private static byte HzToMidi(double hz)
        {
            if (hz <= 0)
                return 0;

            int pitch = (int)Math.Round(69 + 12 * Math.Log2(hz / 440.0));
            return (byte)Math.Clamp(pitch, 1, 127);
        }

        /// <summary>
        /// Executes the Call operation.
        /// </summary>
        private void Call(ushort address, byte a, int maxInstructions, int maxMilliseconds = 25, bool critical = false)
        {
            if (address == 0)
                return;

            try
            {
                _cpu.A = a;
                _cpu.Call(address, maxInstructions, maxMilliseconds);
                _badFrames = _cpu.LastCallTimedOut ? Math.Min(_badFrames + 1, 24) : 0;
            }
            catch (InvalidOperationException)
            {
                if (critical)
                    throw;

                _badFrames++;
                if (_badFrames > 3)
                    throw;
            }
        }

        private void PlayFrame(ushort address)
        {
            if (_deferredPlayFrames > 0)
            {
                _deferredPlayFrames--;
                return;
            }

            Call(address, 0, 240000, maxMilliseconds: 4);
            if (_badFrames >= 8)
                _deferredPlayFrames = Math.Min(_deferredPlayFrames + 1, 3);
        }

        /// <summary>
        /// Executes the RenderSidFallbackProgram operation.
        /// </summary>
        private static float[] RenderSidFallbackProgram(byte[] memory, int seconds, int sampleRate)
        {
            int frames = seconds * sampleRate;
            float[] stereo = new float[frames * 2];
            var sid = new SimpleSid(is8580: false);
            for (int i = 0; i < 25; i++)
                sid.WriteRegister(0xD400 + i, memory[0xD400 + i]);
            for (int frame = 0; frame < frames; frame++)
            {
                var (left, right) = sid.RenderSample(sampleRate, true);
                stereo[frame * 2] = left;
                stereo[frame * 2 + 1] = right;
            }

            return stereo;
        }
    }

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct SidVoiceState(
        byte Pitch,
        bool Gate,
        byte Control,
        byte Waveform,
        ushort FrequencyRegister,
        ushort PulseWidth,
        byte AttackDecay,
        byte SustainRelease,
        byte FilterRouting,
        byte FilterModeVolume,
        ushort FilterCutoff);

    /// <summary>
    /// Represents the C64Io component.
    /// </summary>
    private sealed class C64Io(byte[] memory, SimpleSid sid, bool pal)
    {
        /// <summary>
        /// Stores or exposes _frame.
        /// </summary>
        private int _frame;
        /// <summary>
        /// Stores or exposes _rasterLine.
        /// </summary>
        private int _rasterLine;
        /// <summary>
        /// Executes the _ciaTimerA operation.
        /// </summary>
        private ushort _ciaTimerA = pal ? (ushort)0x4CC7 : (ushort)0x4295;
        /// <summary>
        /// Stores or exposes _ciaInterrupt.
        /// </summary>
        private byte _ciaInterrupt = 0x81;

        /// <summary>
        /// Executes the BeginFrame operation.
        /// </summary>
        public void BeginFrame()
        {
            _frame++;
            _rasterLine = (_frame * 17) % (pal ? 312 : 263);
            _ciaInterrupt = 0x81;
            memory[0xD012] = (byte)(_rasterLine & 0xFF);
            memory[0xD011] = 0x1B;
            memory[0xD019] = 0x01;
            memory[0xDC0D] = _ciaInterrupt;
        }

        /// <summary>
        /// Executes the Read operation.
        /// </summary>
        public byte Read(int address)
        {
            address &= 0xFFFF;
            if (address is >= 0xD400 and <= 0xD41F)
                return sid.ReadRegister(address);

            return address switch
            {
                0xD011 => 0x1B,
                0xD012 => ReadRaster(),
                0xD019 => 0x01,
                0xDC04 => (byte)_ciaTimerA,
                0xDC05 => (byte)(_ciaTimerA >> 8),
                0xDC0D => _ciaInterrupt,
                0xDD0D => _ciaInterrupt,
                _ => memory[address]
            };
        }

        /// <summary>
        /// Executes the ReadRaster operation.
        /// </summary>
        private byte ReadRaster()
        {
            int max = pal ? 312 : 263;
            byte value = (byte)(_rasterLine & 0xFF);
            _rasterLine = (_rasterLine + 1) % max;
            memory[0xD012] = value;
            memory[0xD011] = (byte)((memory[0xD011] & 0x7F) | (_rasterLine >= 256 ? 0x80 : 0));
            return value;
        }

        /// <summary>
        /// Executes the Write operation.
        /// </summary>
        public void Write(int address, byte value)
        {
            address &= 0xFFFF;
            memory[address] = value;
            if (address is >= 0xD400 and <= 0xD41F)
            {
                sid.WriteRegister(address, value);
                return;
            }

            switch (address)
            {
                case 0xDC04:
                    _ciaTimerA = (ushort)((_ciaTimerA & 0xFF00) | value);
                    break;
                case 0xDC05:
                    _ciaTimerA = (ushort)((_ciaTimerA & 0x00FF) | (value << 8));
                    break;
                case 0xDC0D:
                case 0xDD0D:
                case 0xD019:
                    memory[address] = 0;
                    break;
            }
        }
    }

    /// <summary>
    /// Represents the SimpleSid component.
    /// </summary>
    private sealed class SimpleSid(bool is8580)
    {
        /// <summary>
        /// Stores or exposes _registers.
        /// </summary>
        private readonly byte[] _registers = new byte[0x20];
        /// <summary>
        /// Executes the _voices operation.
        /// </summary>
        private readonly Voice[] _voices = [new(-0.35), new(0.0), new(0.35)];
        /// <summary>
        /// Stores or exposes _filterCutoff.
        /// </summary>
        private ushort _filterCutoff;
        /// <summary>
        /// Stores or exposes _filterResRouting.
        /// </summary>
        private byte _filterResRouting;
        /// <summary>
        /// Stores or exposes _filterModeVolume.
        /// </summary>
        private byte _filterModeVolume = 0x0F;
        /// <summary>
        /// Stores or exposes _filterLow.
        /// </summary>
        private double _filterLow;
        /// <summary>
        /// Stores or exposes _filterBand.
        /// </summary>
        private double _filterBand;
        /// <summary>
        /// Stores or exposes _volumeDacInput.
        /// </summary>
        private double _volumeDacInput;
        /// <summary>
        /// Stores or exposes _volumeDacOutput.
        /// </summary>
        private double _volumeDacOutput;
        private double _masterDcInputLeft;
        private double _masterDcOutputLeft;
        private double _masterDcInputRight;
        private double _masterDcOutputRight;
        private double _masterLowLeft;
        private double _masterLowRight;
        /// <summary>
        /// Stores or exposes double.
        /// </summary>
        private const double PalClock = 985248.0;
        /// <summary>
        /// Stores or exposes WriteCount.
        /// </summary>
        public int WriteCount { get; private set; }
        /// <summary>
        /// Stores or exposes VoiceWriteCount.
        /// </summary>
        public int VoiceWriteCount { get; private set; }
        /// <summary>
        /// Executes the Volume operation.
        /// </summary>
        public byte Volume => (byte)(_filterModeVolume & 0x0F);
        /// <summary>
        /// Executes the RegisterSnapshot operation.
        /// </summary>
        public byte[] RegisterSnapshot() => (byte[])_registers.Clone();
        /// <summary>
        /// Executes the ReadRegister operation.
        /// </summary>
        public byte ReadRegister(int address)
        {
            int index = address - 0xD400;
            if ((uint)index >= _registers.Length)
                return 0;

            return _registers[index];
        }

        /// <summary>
        /// Executes the WriteRegister operation.
        /// </summary>
        public void WriteRegister(int address, byte value)
        {
            int index = address - 0xD400;
            if ((uint)index >= _registers.Length)
                return;

            WriteCount++;
            if (index < 21)
                VoiceWriteCount++;
            _registers[index] = value;
            if (index < 21)
            {
                int voiceIndex = index / 7;
                int voiceOffset = index % 7;
                if (voiceIndex < _voices.Length)
                    _voices[voiceIndex].Write(voiceOffset, value);
            }
            else if (index == 0x18)
            {
                _filterModeVolume = value;
            }
            else if (index == 0x15)
            {
                _filterCutoff = (ushort)((_filterCutoff & 0x7F8) | (value & 0x07));
            }
            else if (index == 0x16)
            {
                _filterCutoff = (ushort)((_filterCutoff & 0x007) | (value << 3));
            }
            else if (index == 0x17)
            {
                _filterResRouting = value;
            }
        }

        public (float Left, float Right) RenderSample(int sampleRate, bool pal)
        {
            double clock = pal ? PalClock : 1022727.0;
            double volume = (_filterModeVolume & 0x0F) / 15.0;

            double left = 0;
            double right = 0;
            double routedToFilter = 0;
            for (int i = 0; i < _voices.Length; i++)
            {
                bool voice3Muted = i == 2 && (_filterModeVolume & 0x80) != 0;
                if (voice3Muted)
                    continue;

                var syncSource = _voices[(i + 2) % _voices.Length];
                double sample = _voices[i].Render(sampleRate, clock, syncSource) * 0.28;
                if ((_filterResRouting & (1 << i)) != 0)
                {
                    routedToFilter += sample;
                    continue;
                }

                double pan = _voices[i].Pan;
                left += sample * (pan <= 0 ? 1.0 : 1.0 - pan);
                right += sample * (pan >= 0 ? 1.0 : 1.0 + pan);
            }

            if (Math.Abs(routedToFilter) > 0.000001)
            {
                double filtered = ApplyFilter(routedToFilter, sampleRate);
                left += filtered * 0.92;
                right += filtered * 0.92;
            }

            double volumeDac = RenderVolumeDac();

            left *= volume;
            right *= volume;
            left += volumeDac;
            right += volumeDac;
            double masterGain = is8580 ? 0.76 : 0.68;
            left = MasterDcBlock(left * masterGain, ref _masterDcInputLeft, ref _masterDcOutputLeft);
            right = MasterDcBlock(right * masterGain, ref _masterDcInputRight, ref _masterDcOutputRight);
            left = MasterSmooth(left, ref _masterLowLeft);
            right = MasterSmooth(right, ref _masterLowRight);
            return (SoftClip(left), SoftClip(right));
        }

        /// <summary>
        /// Executes the RenderVolumeDac operation.
        /// </summary>
        private double RenderVolumeDac()
        {
            double digiLevel = is8580 ? 0.040 : 0.16;
            double input = (((_filterModeVolume & 0x0F) / 15.0) - 0.5) * digiLevel;
            double output = input - _volumeDacInput + 0.992 * _volumeDacOutput;
            _volumeDacInput = input;
            _volumeDacOutput = output;
            return output;
        }

        private static double MasterDcBlock(double input, ref double previousInput, ref double previousOutput)
        {
            double output = input - previousInput + 0.997 * previousOutput;
            previousInput = input;
            previousOutput = output;
            return output;
        }

        private static double MasterSmooth(double input, ref double previous)
        {
            previous += (input - previous) * 0.74;
            return previous;
        }

        /// <summary>
        /// Executes the ApplyFilter operation.
        /// </summary>
        private double ApplyFilter(double input, int sampleRate)
        {
            double normalizedCutoff = Math.Clamp(_filterCutoff / 2047.0, 0.001, 1.0);
            double cutoff = is8580
                ? 45.0 + normalizedCutoff * normalizedCutoff * 12500.0
                : 25.0 + Math.Pow(normalizedCutoff, 1.55) * 8200.0;
            double f = 2.0 * Math.Sin(Math.PI * Math.Min(cutoff, sampleRate * 0.45) / sampleRate);
            double resonance = (_filterResRouting >> 4) / 15.0;
            double q = (is8580 ? 1.12 : 1.34) - resonance * (is8580 ? 0.78 : 1.02);

            _filterLow += f * _filterBand;
            double high = input - _filterLow - q * _filterBand;
            _filterBand += f * high;
            double notch = high + _filterLow;

            double output = 0;
            byte mode = (byte)(_filterModeVolume & 0x70);
            if ((mode & 0x10) != 0)
                output += _filterLow;
            if ((mode & 0x20) != 0)
                output += _filterBand;
            if ((mode & 0x40) != 0)
                output += high;
            if (mode == 0)
                output = 0;

            return Math.Clamp(output, -1.4, 1.4);
        }

        /// <summary>
        /// Represents the Voice component.
        /// </summary>
        private sealed class Voice(double pan)
        {
            private static readonly double[] AttackTimes = [0.002, 0.008, 0.016, 0.024, 0.038, 0.056, 0.068, 0.080, 0.100, 0.250, 0.500, 0.800, 1.000, 3.000, 5.000, 8.000];
            private static readonly double[] DecayReleaseTimes = [0.006, 0.024, 0.048, 0.072, 0.114, 0.168, 0.204, 0.240, 0.300, 0.750, 1.500, 2.400, 3.000, 9.000, 15.000, 24.000];
            private ushort _freq;
            private ushort _pulseWidth = 0x800;
            private byte _control;
            private byte _attackDecay;
            private byte _sustainRelease;
            private uint _accumulator;
            private uint _previousAccumulator;
            private uint _noiseShift = 0x7FFFF8;
            private double _envelope;
            private bool _lastGate;
            private bool _released = true;
            private double _dcInput;
            private double _dcOutput;

            public double Pan { get; } = pan;

            public void Write(int offset, byte value)
            {
                switch (offset)
                {
                    case 0: _freq = (ushort)((_freq & 0xFF00) | value); break;
                    case 1: _freq = (ushort)((_freq & 0x00FF) | (value << 8)); break;
                    case 2: _pulseWidth = (ushort)((_pulseWidth & 0xF00) | value); break;
                    case 3: _pulseWidth = (ushort)((_pulseWidth & 0x0FF) | ((value & 0x0F) << 8)); break;
                    case 4: _control = value; break;
                    case 5: _attackDecay = value; break;
                    case 6: _sustainRelease = value; break;
                }
            }

            public bool MsbSet => (_accumulator & 0x800000) != 0;
            public bool MsbCrossed { get; private set; }

            public double Render(int sampleRate, double clockRate, Voice syncSource)
            {
                bool gate = (_control & 0x01) != 0;
                if (gate && !_lastGate)
                {
                    _envelope = Math.Max(_envelope, 0.02);
                    _released = false;
                }
                else if (!gate && _lastGate)
                {
                    _released = true;
                }
                _lastGate = gate;

                StepEnvelope(sampleRate);

                if ((_control & 0x08) != 0)
                {
                    _accumulator = 0;
                    MsbCrossed = false;
                    return 0;
                }

                if ((_control & 0x02) != 0 && syncSource.MsbCrossed)
                    _accumulator = 0;

                _previousAccumulator = _accumulator;
                // SID oscillator frequency is freqReg * clock / 2^24 Hz.
                // Therefore the 24-bit phase accumulator advances by
                // freqReg * clock / sampleRate each output sample.
                uint step = (uint)Math.Clamp(Math.Round(_freq * clockRate / sampleRate), 0, 0xFFFFFF);
                _accumulator = (_accumulator + step) & 0xFFFFFF;
                MsbCrossed = ((_previousAccumulator ^ _accumulator) & 0x800000) != 0;
                if (MsbCrossed)
                    AdvanceNoise();

                double output = RenderWaveform(step / 16777216.0, syncSource.MsbSet);
                return DcBlock(output * _envelope);
            }

            private void StepEnvelope(int sampleRate)
            {
                double sustain = ((_sustainRelease >> 4) & 0x0F) / 15.0;
                if (_lastGate && !_released)
                {
                    if (_envelope < 0.995)
                    {
                        double attack = AttackTimes[(_attackDecay >> 4) & 0x0F];
                        _envelope += (1.0 - _envelope) / Math.Max(attack * sampleRate, 1.0);
                    }
                    else if (_envelope > sustain)
                    {
                        double decay = DecayReleaseTimes[_attackDecay & 0x0F];
                        _envelope -= (_envelope - sustain) / Math.Max(decay * sampleRate, 1.0);
                    }
                }
                else
                {
                    double release = DecayReleaseTimes[_sustainRelease & 0x0F];
                    _envelope -= _envelope / Math.Max(release * sampleRate, 1.0);
                }

                _envelope = Math.Clamp(_envelope, 0.0, 1.0);
            }

            private double RenderWaveform(double phaseStep, bool ringSourceMsb)
            {
                double phase = (_accumulator & 0xFFFFFF) / 16777216.0;
                double pw = Math.Clamp(_pulseWidth / 4095.0, 0.03, 0.97);
                byte wave = (byte)(_control & 0xF0);
                double saw = BandLimitedSaw(phase, phaseStep);
                double pulse = BandLimitedPulse(phase, phaseStep, pw);
                double trianglePhase = ((_control & 0x04) != 0 && ringSourceMsb) ? (phase + 0.5) % 1.0 : phase;
                double triangle = 1.0 - Math.Abs(trianglePhase * 4.0 - 2.0);
                double noise = (((_noiseShift >> 4) & 0xFFFFF) / (double)0x7FFFF) * 2.0 - 1.0;
                return wave switch
                {
                    0x10 => saw,
                    0x20 => triangle,
                    0x40 => pulse,
                    0x80 => noise,
                    0x30 => saw * triangle,
                    0x50 => saw * pulse,
                    0x60 => triangle * pulse,
                    0x70 => saw * triangle * pulse,
                    _ => RenderCombinedWaveform(wave, saw, triangle, pulse, noise)
                };
            }

            private static double RenderCombinedWaveform(byte wave, double saw, double triangle, double pulse, double noise)
            {
                double combined = 1.0;
                int active = 0;

                if ((wave & 0x10) != 0)
                {
                    combined *= saw;
                    active++;
                }

                if ((wave & 0x20) != 0)
                {
                    combined *= triangle;
                    active++;
                }

                if ((wave & 0x40) != 0)
                {
                    combined *= pulse;
                    active++;
                }

                if ((wave & 0x80) != 0)
                {
                    combined *= noise;
                    active++;
                }

                return active == 0 ? 0 : Math.Clamp(combined * 0.66, -1.0, 1.0);
            }

            private double DcBlock(double input)
            {
                double output = input - _dcInput + 0.995 * _dcOutput;
                _dcInput = input;
                _dcOutput = output;
                return output;
            }

            private static double BandLimitedSaw(double phase, double phaseStep) =>
                phase * 2.0 - 1.0 - PolyBlep(phase, phaseStep);

            private static double BandLimitedPulse(double phase, double phaseStep, double width)
            {
                double shifted = phase - width;
                if (shifted < 0)
                    shifted += 1.0;

                return (phase < width ? 1.0 : -1.0)
                    + PolyBlep(phase, phaseStep)
                    - PolyBlep(shifted, phaseStep);
            }

            private static double PolyBlep(double t, double dt)
            {
                dt = Math.Clamp(dt, 0.000001, 0.5);
                if (t < dt)
                {
                    t /= dt;
                    return t + t - t * t - 1.0;
                }

                if (t > 1.0 - dt)
                {
                    t = (t - 1.0) / dt;
                    return t * t + t + t + 1.0;
                }

                return 0.0;
            }

            private void AdvanceNoise()
            {
                uint bit = ((_noiseShift >> 22) ^ (_noiseShift >> 17)) & 1;
                _noiseShift = ((_noiseShift << 1) | bit) & 0x7FFFFF;
                if (_noiseShift == 0)
                    _noiseShift = 0x7FFFF8;
            }
        }
    }

    /// <summary>
    /// Represents the Mos6510 component.
    /// </summary>
    private sealed class Mos6510
    {
        /// <summary>
        /// Stores or exposes _memory.
        /// </summary>
        private readonly byte[] _memory;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private readonly Func<int, byte> _ioRead;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private readonly Action<int, byte> _ioWrite;
        /// <summary>
        /// Stores or exposes A.
        /// </summary>
        public byte A;
        /// <summary>
        /// Stores or exposes _x.
        /// </summary>
        private byte _x;
        /// <summary>
        /// Stores or exposes _y.
        /// </summary>
        private byte _y;
        /// <summary>
        /// Stores or exposes _sp.
        /// </summary>
        private byte _sp = 0xFF;
        /// <summary>
        /// Stores or exposes _p.
        /// </summary>
        private byte _p = 0x24;
        /// <summary>
        /// Stores or exposes _pc.
        /// </summary>
        private ushort _pc;
        public bool LastCallTimedOut { get; private set; }

        public Mos6510(byte[] memory, Func<int, byte> ioRead, Action<int, byte> ioWrite)
        {
            _memory = memory;
            _ioRead = ioRead;
            _ioWrite = ioWrite;
        }

        /// <summary>
        /// Executes the Call operation.
        /// </summary>
        public void Call(ushort address, int maxInstructions, int maxMilliseconds = 25)
        {
            LastCallTimedOut = false;
            PushWord(0xFFFF);
            _pc = address;
            var budget = Stopwatch.StartNew();
            for (int i = 0; i < maxInstructions; i++)
            {
                if (Step())
                    return;

                if ((i & 0x7FF) == 0 && budget.ElapsedMilliseconds > maxMilliseconds)
                {
                    LastCallTimedOut = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Executes the Step operation.
        /// </summary>
        private bool Step()
        {
            if (IsKernalRoutine(_pc))
            {
                _pc = (ushort)(PopWord() + 1);
                return _pc == 0;
            }

            byte op = Read(_pc++);
            switch (op)
            {
                case 0x00: return true;
                case 0xEA: break;
                case 0x78: SetFlag(Interrupt, true); break;
                case 0x58: SetFlag(Interrupt, false); break;
                case 0x18: SetFlag(Carry, false); break;
                case 0x38: SetFlag(Carry, true); break;
                case 0xD8: SetFlag(Decimal, false); break;
                case 0xF8: SetFlag(Decimal, true); break;
                case 0xB8: SetFlag(Overflow, false); break;
                case 0xA9: A = Load(Immediate()); break;
                case 0xA5: A = Load(Read(Zp())); break;
                case 0xB5: A = Load(Read((byte)(Zp() + _x))); break;
                case 0xAD: A = Load(Read(Abs())); break;
                case 0xBD: A = Load(Read((ushort)(Abs() + _x))); break;
                case 0xB9: A = Load(Read((ushort)(Abs() + _y))); break;
                case 0xA1: A = Load(Read(IndX())); break;
                case 0xB1: A = Load(Read(IndY())); break;
                case 0xA2: _x = Load(Immediate()); break;
                case 0xA6: _x = Load(Read(Zp())); break;
                case 0xB6: _x = Load(Read((byte)(Zp() + _y))); break;
                case 0xAE: _x = Load(Read(Abs())); break;
                case 0xBE: _x = Load(Read((ushort)(Abs() + _y))); break;
                case 0xA0: _y = Load(Immediate()); break;
                case 0xA4: _y = Load(Read(Zp())); break;
                case 0xB4: _y = Load(Read((byte)(Zp() + _x))); break;
                case 0xAC: _y = Load(Read(Abs())); break;
                case 0xBC: _y = Load(Read((ushort)(Abs() + _x))); break;
                case 0x85: Write(Zp(), A); break;
                case 0x95: Write((byte)(Zp() + _x), A); break;
                case 0x8D: Write(Abs(), A); break;
                case 0x9D: Write((ushort)(Abs() + _x), A); break;
                case 0x99: Write((ushort)(Abs() + _y), A); break;
                case 0x81: Write(IndX(), A); break;
                case 0x91: Write(IndY(), A); break;
                case 0x86: Write(Zp(), _x); break;
                case 0x96: Write((byte)(Zp() + _y), _x); break;
                case 0x8E: Write(Abs(), _x); break;
                case 0x84: Write(Zp(), _y); break;
                case 0x94: Write((byte)(Zp() + _x), _y); break;
                case 0x8C: Write(Abs(), _y); break;
                case 0xAA: _x = Load(A); break;
                case 0xA8: _y = Load(A); break;
                case 0x8A: A = Load(_x); break;
                case 0x98: A = Load(_y); break;
                case 0xBA: _x = Load(_sp); break;
                case 0x9A: _sp = _x; break;
                case 0xE8: _x = Load((byte)(_x + 1)); break;
                case 0xCA: _x = Load((byte)(_x - 1)); break;
                case 0xC8: _y = Load((byte)(_y + 1)); break;
                case 0x88: _y = Load((byte)(_y - 1)); break;
                case 0x69: Adc(Immediate()); break;
                case 0x65: Adc(Read(Zp())); break;
                case 0x75: Adc(Read((byte)(Zp() + _x))); break;
                case 0x6D: Adc(Read(Abs())); break;
                case 0x7D: Adc(Read((ushort)(Abs() + _x))); break;
                case 0x79: Adc(Read((ushort)(Abs() + _y))); break;
                case 0x61: Adc(Read(IndX())); break;
                case 0x71: Adc(Read(IndY())); break;
                case 0xE9:
                case 0xEB: Sbc(Immediate()); break;
                case 0xE5: Sbc(Read(Zp())); break;
                case 0xF5: Sbc(Read((byte)(Zp() + _x))); break;
                case 0xED: Sbc(Read(Abs())); break;
                case 0xFD: Sbc(Read((ushort)(Abs() + _x))); break;
                case 0xF9: Sbc(Read((ushort)(Abs() + _y))); break;
                case 0xE1: Sbc(Read(IndX())); break;
                case 0xF1: Sbc(Read(IndY())); break;
                case 0x29: A = Load((byte)(A & Immediate())); break;
                case 0x25: A = Load((byte)(A & Read(Zp()))); break;
                case 0x35: A = Load((byte)(A & Read((byte)(Zp() + _x)))); break;
                case 0x2D: A = Load((byte)(A & Read(Abs()))); break;
                case 0x3D: A = Load((byte)(A & Read((ushort)(Abs() + _x)))); break;
                case 0x39: A = Load((byte)(A & Read((ushort)(Abs() + _y)))); break;
                case 0x21: A = Load((byte)(A & Read(IndX()))); break;
                case 0x31: A = Load((byte)(A & Read(IndY()))); break;
                case 0x09: A = Load((byte)(A | Immediate())); break;
                case 0x05: A = Load((byte)(A | Read(Zp()))); break;
                case 0x15: A = Load((byte)(A | Read((byte)(Zp() + _x)))); break;
                case 0x0D: A = Load((byte)(A | Read(Abs()))); break;
                case 0x1D: A = Load((byte)(A | Read((ushort)(Abs() + _x)))); break;
                case 0x19: A = Load((byte)(A | Read((ushort)(Abs() + _y)))); break;
                case 0x01: A = Load((byte)(A | Read(IndX()))); break;
                case 0x11: A = Load((byte)(A | Read(IndY()))); break;
                case 0x49: A = Load((byte)(A ^ Immediate())); break;
                case 0x45: A = Load((byte)(A ^ Read(Zp()))); break;
                case 0x55: A = Load((byte)(A ^ Read((byte)(Zp() + _x)))); break;
                case 0x4D: A = Load((byte)(A ^ Read(Abs()))); break;
                case 0x5D: A = Load((byte)(A ^ Read((ushort)(Abs() + _x)))); break;
                case 0x59: A = Load((byte)(A ^ Read((ushort)(Abs() + _y)))); break;
                case 0x41: A = Load((byte)(A ^ Read(IndX()))); break;
                case 0x51: A = Load((byte)(A ^ Read(IndY()))); break;
                case 0xC9: Compare(A, Immediate()); break;
                case 0xC5: Compare(A, Read(Zp())); break;
                case 0xD5: Compare(A, Read((byte)(Zp() + _x))); break;
                case 0xCD: Compare(A, Read(Abs())); break;
                case 0xDD: Compare(A, Read((ushort)(Abs() + _x))); break;
                case 0xD9: Compare(A, Read((ushort)(Abs() + _y))); break;
                case 0xC1: Compare(A, Read(IndX())); break;
                case 0xD1: Compare(A, Read(IndY())); break;
                case 0xE0: Compare(_x, Immediate()); break;
                case 0xE4: Compare(_x, Read(Zp())); break;
                case 0xEC: Compare(_x, Read(Abs())); break;
                case 0xC0: Compare(_y, Immediate()); break;
                case 0xC4: Compare(_y, Read(Zp())); break;
                case 0xCC: Compare(_y, Read(Abs())); break;
                case 0xE6: Inc(Zp()); break;
                case 0xF6: Inc((byte)(Zp() + _x)); break;
                case 0xEE: Inc(Abs()); break;
                case 0xFE: Inc((ushort)(Abs() + _x)); break;
                case 0xC6: Dec(Zp()); break;
                case 0xD6: Dec((byte)(Zp() + _x)); break;
                case 0xCE: Dec(Abs()); break;
                case 0xDE: Dec((ushort)(Abs() + _x)); break;
                case 0x0A: A = Asl(A); break;
                case 0x06: Shift(Zp(), Asl); break;
                case 0x16: Shift((byte)(Zp() + _x), Asl); break;
                case 0x0E: Shift(Abs(), Asl); break;
                case 0x1E: Shift((ushort)(Abs() + _x), Asl); break;
                case 0x4A: A = Lsr(A); break;
                case 0x46: Shift(Zp(), Lsr); break;
                case 0x56: Shift((byte)(Zp() + _x), Lsr); break;
                case 0x4E: Shift(Abs(), Lsr); break;
                case 0x5E: Shift((ushort)(Abs() + _x), Lsr); break;
                case 0x2A: A = Rol(A); break;
                case 0x26: Shift(Zp(), Rol); break;
                case 0x36: Shift((byte)(Zp() + _x), Rol); break;
                case 0x2E: Shift(Abs(), Rol); break;
                case 0x3E: Shift((ushort)(Abs() + _x), Rol); break;
                case 0x6A: A = Ror(A); break;
                case 0x66: Shift(Zp(), Ror); break;
                case 0x76: Shift((byte)(Zp() + _x), Ror); break;
                case 0x6E: Shift(Abs(), Ror); break;
                case 0x7E: Shift((ushort)(Abs() + _x), Ror); break;
                case 0x24: Bit(Read(Zp())); break;
                case 0x2C: Bit(Read(Abs())); break;
                case 0x20: Jsr(Abs()); break;
                case 0x60: _pc = (ushort)(PopWord() + 1); return _pc == 0;
                case 0x40: _p = Pop(); _pc = PopWord(); break;
                case 0x4C: _pc = Abs(); break;
                case 0x6C: _pc = ReadWordBug(Abs()); break;
                case 0x48: Push(A); break;
                case 0x68: A = Load(Pop()); break;
                case 0x08: Push((byte)(_p | Break | Unused)); break;
                case 0x28: _p = (byte)(Pop() | Unused); break;
                case 0x10: Branch(!GetFlag(Negative)); break;
                case 0x30: Branch(GetFlag(Negative)); break;
                case 0x50: Branch(!GetFlag(Overflow)); break;
                case 0x70: Branch(GetFlag(Overflow)); break;
                case 0x90: Branch(!GetFlag(Carry)); break;
                case 0xB0: Branch(GetFlag(Carry)); break;
                case 0xD0: Branch(!GetFlag(Zero)); break;
                case 0xF0: Branch(GetFlag(Zero)); break;
                case 0xC7: Dcp(Zp()); break;
                case 0xD7: Dcp((byte)(Zp() + _x)); break;
                case 0xCF: Dcp(Abs()); break;
                case 0xDF: Dcp((ushort)(Abs() + _x)); break;
                case 0xDB: Dcp((ushort)(Abs() + _y)); break;
                case 0xC3: Dcp(IndX()); break;
                case 0xD3: Dcp(IndY()); break;
                case 0xA7: A = _x = Load(Read(Zp())); break;
                case 0xB7: A = _x = Load(Read((byte)(Zp() + _y))); break;
                case 0xAF: A = _x = Load(Read(Abs())); break;
                case 0xBF: A = _x = Load(Read((ushort)(Abs() + _y))); break;
                case 0xA3: A = _x = Load(Read(IndX())); break;
                case 0xB3: A = _x = Load(Read(IndY())); break;
                case 0x87: Write(Zp(), (byte)(A & _x)); break;
                case 0x97: Write((byte)(Zp() + _y), (byte)(A & _x)); break;
                case 0x8F: Write(Abs(), (byte)(A & _x)); break;
                case 0x83: Write(IndX(), (byte)(A & _x)); break;
                case 0xE7: Isc(Zp()); break;
                case 0xF7: Isc((byte)(Zp() + _x)); break;
                case 0xEF: Isc(Abs()); break;
                case 0xFF: Isc((ushort)(Abs() + _x)); break;
                case 0xFB: Isc((ushort)(Abs() + _y)); break;
                case 0xE3: Isc(IndX()); break;
                case 0xF3: Isc(IndY()); break;
                case 0x07: Slo(Zp()); break;
                case 0x17: Slo((byte)(Zp() + _x)); break;
                case 0x0F: Slo(Abs()); break;
                case 0x1F: Slo((ushort)(Abs() + _x)); break;
                case 0x1B: Slo((ushort)(Abs() + _y)); break;
                case 0x03: Slo(IndX()); break;
                case 0x13: Slo(IndY()); break;
                case 0x27: Rla(Zp()); break;
                case 0x37: Rla((byte)(Zp() + _x)); break;
                case 0x2F: Rla(Abs()); break;
                case 0x3F: Rla((ushort)(Abs() + _x)); break;
                case 0x3B: Rla((ushort)(Abs() + _y)); break;
                case 0x23: Rla(IndX()); break;
                case 0x33: Rla(IndY()); break;
                case 0x47: Sre(Zp()); break;
                case 0x57: Sre((byte)(Zp() + _x)); break;
                case 0x4F: Sre(Abs()); break;
                case 0x5F: Sre((ushort)(Abs() + _x)); break;
                case 0x5B: Sre((ushort)(Abs() + _y)); break;
                case 0x43: Sre(IndX()); break;
                case 0x53: Sre(IndY()); break;
                case 0x67: Rra(Zp()); break;
                case 0x77: Rra((byte)(Zp() + _x)); break;
                case 0x6F: Rra(Abs()); break;
                case 0x7F: Rra((ushort)(Abs() + _x)); break;
                case 0x7B: Rra((ushort)(Abs() + _y)); break;
                case 0x63: Rra(IndX()); break;
                case 0x73: Rra(IndY()); break;
                case 0x0B:
                case 0x2B: Anc(Immediate()); break;
                case 0x8B: A = Load((byte)(_x & Immediate())); break;
                case 0xBB: A = _x = _sp = Load((byte)(Read((ushort)(Abs() + _y)) & _sp)); break;
                case 0xCB: Axs(Immediate()); break;
                case 0x6B: Arr(Immediate()); break;
                case 0x93: Ahx(IndY()); break;
                case 0x9B: Tas((ushort)(Abs() + _y)); break;
                case 0x9C: Shy((ushort)(Abs() + _x)); break;
                case 0x9E: Shx((ushort)(Abs() + _y)); break;
                case 0x9F: Ahx((ushort)(Abs() + _y)); break;
                case 0x02:
                case 0x12:
                case 0x22:
                case 0x32:
                case 0x42:
                case 0x52:
                case 0x62:
                case 0x72:
                case 0x92:
                case 0xB2:
                case 0xD2:
                case 0xF2:
                    return true;
                default:
                    if (IsCommonNop(op))
                        SkipIllegalNop(op);
                    else
                        throw new InvalidOperationException($"Unsupported 6510 opcode ${op:X2} at ${(ushort)(_pc - 1):X4}.");
                    break;
            }

            return false;
        }

        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Carry = 0x01;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Zero = 0x02;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Interrupt = 0x04;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Decimal = 0x08;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Break = 0x10;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Unused = 0x20;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Overflow = 0x40;
        /// <summary>
        /// Stores or exposes byte.
        /// </summary>
        private const byte Negative = 0x80;

        /// <summary>
        /// Executes the Immediate operation.
        /// </summary>
        private byte Immediate() => Read(_pc++);
        /// <summary>
        /// Executes the Zp operation.
        /// </summary>
        private byte Zp() => Immediate();
        /// <summary>
        /// Executes the Abs operation.
        /// </summary>
        private ushort Abs() { byte lo = Immediate(); return (ushort)(lo | (Immediate() << 8)); }
        /// <summary>
        /// Executes the IndX operation.
        /// </summary>
        private ushort IndX() { byte zp = (byte)(Immediate() + _x); return ReadWordZp(zp); }
        /// <summary>
        /// Executes the IndY operation.
        /// </summary>
        private ushort IndY() => (ushort)(ReadWordZp(Immediate()) + _y);
        /// <summary>
        /// Executes the Read operation.
        /// </summary>
        private byte Read(ushort address) =>
            address is >= 0xD000 and <= 0xDFFF ? _ioRead(address) : _memory[address];

        /// <summary>
        /// Executes the Read operation.
        /// </summary>
        private byte Read(byte address) => _memory[address];
        /// <summary>
        /// Executes the Write operation.
        /// </summary>
        private void Write(int address, byte value)
        {
            address &= 0xFFFF;
            if (address is >= 0xD000 and <= 0xDFFF)
                _ioWrite(address, value);
            else
                _memory[address] = value;
        }

        /// <summary>
        /// Executes the ReadWordZp operation.
        /// </summary>
        private ushort ReadWordZp(byte address) => (ushort)(Read(address) | (Read((byte)(address + 1)) << 8));
        /// <summary>
        /// Executes the ReadWordBug operation.
        /// </summary>
        private ushort ReadWordBug(ushort address)
        {
            byte lo = Read(address);
            ushort hiAddress = (ushort)((address & 0xFF00) | ((address + 1) & 0x00FF));
            return (ushort)(lo | (Read(hiAddress) << 8));
        }

        /// <summary>
        /// Executes the Load operation.
        /// </summary>
        private byte Load(byte value)
        {
            SetZn(value);
            return value;
        }

        /// <summary>
        /// Executes the SetZn operation.
        /// </summary>
        private void SetZn(byte value)
        {
            SetFlag(Zero, value == 0);
            SetFlag(Negative, (value & 0x80) != 0);
        }

        /// <summary>
        /// Executes the GetFlag operation.
        /// </summary>
        private bool GetFlag(byte flag) => (_p & flag) != 0;
        /// <summary>
        /// Executes the SetFlag operation.
        /// </summary>
        private void SetFlag(byte flag, bool value) => _p = value ? (byte)(_p | flag) : (byte)(_p & ~flag);
        /// <summary>
        /// Executes the Push operation.
        /// </summary>
        private void Push(byte value) => Write((ushort)(0x0100 | _sp--), value);
        /// <summary>
        /// Executes the Pop operation.
        /// </summary>
        private byte Pop() => Read((ushort)(0x0100 | ++_sp));
        /// <summary>
        /// Executes the PushWord operation.
        /// </summary>
        private void PushWord(ushort value) { Push((byte)(value >> 8)); Push((byte)value); }
        /// <summary>
        /// Executes the PopWord operation.
        /// </summary>
        private ushort PopWord() { byte lo = Pop(); return (ushort)(lo | (Pop() << 8)); }
        /// <summary>
        /// Executes the Jsr operation.
        /// </summary>
        private void Jsr(ushort address) { PushWord((ushort)(_pc - 1)); _pc = address; }
        /// <summary>
        /// Executes the Branch operation.
        /// </summary>
        private void Branch(bool condition)
        {
            sbyte offset = unchecked((sbyte)Immediate());
            if (condition)
                _pc = (ushort)(_pc + offset);
        }

        /// <summary>
        /// Executes the Compare operation.
        /// </summary>
        private void Compare(byte left, byte right)
        {
            int result = left - right;
            SetFlag(Carry, left >= right);
            SetZn((byte)result);
        }

        /// <summary>
        /// Executes the Adc operation.
        /// </summary>
        private void Adc(byte value)
        {
            int carry = GetFlag(Carry) ? 1 : 0;
            int result = A + value + carry;
            SetFlag(Carry, result > 0xFF);
            SetFlag(Overflow, (~(A ^ value) & (A ^ result) & 0x80) != 0);
            A = Load((byte)result);
        }

        /// <summary>
        /// Executes the Sbc operation.
        /// </summary>
        private void Sbc(byte value) => Adc((byte)~value);
        /// <summary>
        /// Executes the Inc operation.
        /// </summary>
        private void Inc(int address) => Write(address, Load((byte)(Read((ushort)address) + 1)));
        /// <summary>
        /// Executes the Dec operation.
        /// </summary>
        private void Dec(int address) => Write(address, Load((byte)(Read((ushort)address) - 1)));
        /// <summary>
        /// Executes the Dcp operation.
        /// </summary>
        private void Dcp(int address)
        {
            byte value = (byte)(Read((ushort)address) - 1);
            Write(address, value);
            Compare(A, value);
        }
        /// <summary>
        /// Executes the Isc operation.
        /// </summary>
        private void Isc(int address)
        {
            byte value = (byte)(Read((ushort)address) + 1);
            Write(address, value);
            Sbc(value);
        }
        /// <summary>
        /// Executes the Slo operation.
        /// </summary>
        private void Slo(int address)
        {
            byte value = Asl(Read((ushort)address));
            Write(address, value);
            A = Load((byte)(A | value));
        }
        /// <summary>
        /// Executes the Rla operation.
        /// </summary>
        private void Rla(int address)
        {
            byte value = Rol(Read((ushort)address));
            Write(address, value);
            A = Load((byte)(A & value));
        }
        /// <summary>
        /// Executes the Sre operation.
        /// </summary>
        private void Sre(int address)
        {
            byte value = Lsr(Read((ushort)address));
            Write(address, value);
            A = Load((byte)(A ^ value));
        }
        /// <summary>
        /// Executes the Rra operation.
        /// </summary>
        private void Rra(int address)
        {
            byte value = Ror(Read((ushort)address));
            Write(address, value);
            Adc(value);
        }
        /// <summary>
        /// Executes the Anc operation.
        /// </summary>
        private void Anc(byte value)
        {
            A = Load((byte)(A & value));
            SetFlag(Carry, (A & 0x80) != 0);
        }
        /// <summary>
        /// Executes the Axs operation.
        /// </summary>
        private void Axs(byte value)
        {
            int source = A & _x;
            int result = source - value;
            SetFlag(Carry, source >= value);
            _x = Load((byte)result);
        }
        /// <summary>
        /// Executes the Arr operation.
        /// </summary>
        private void Arr(byte value)
        {
            A = (byte)(A & value);
            A = Load((byte)((A >> 1) | (GetFlag(Carry) ? 0x80 : 0)));
            SetFlag(Carry, (A & 0x40) != 0);
            SetFlag(Overflow, ((A >> 6) ^ (A >> 5) & 1) != 0);
        }
        /// <summary>
        /// Executes the Ahx operation.
        /// </summary>
        private void Ahx(ushort address) => Write(address, (byte)(A & _x & (((address >> 8) + 1) & 0xFF)));
        /// <summary>
        /// Executes the Tas operation.
        /// </summary>
        private void Tas(ushort address)
        {
            _sp = (byte)(A & _x);
            Write(address, (byte)(_sp & (((address >> 8) + 1) & 0xFF)));
        }
        /// <summary>
        /// Executes the Shy operation.
        /// </summary>
        private void Shy(ushort address) => Write(address, (byte)(_y & (((address >> 8) + 1) & 0xFF)));
        /// <summary>
        /// Executes the Shx operation.
        /// </summary>
        private void Shx(ushort address) => Write(address, (byte)(_x & (((address >> 8) + 1) & 0xFF)));
        /// <summary>
        /// Executes the Shift operation.
        /// </summary>
        private void Shift(int address, Func<byte, byte> op) => Write(address, op(Read((ushort)address)));
        /// <summary>
        /// Executes the Asl operation.
        /// </summary>
        private byte Asl(byte value) { SetFlag(Carry, (value & 0x80) != 0); return Load((byte)(value << 1)); }
        /// <summary>
        /// Executes the Lsr operation.
        /// </summary>
        private byte Lsr(byte value) { SetFlag(Carry, (value & 1) != 0); return Load((byte)(value >> 1)); }
        /// <summary>
        /// Executes the Rol operation.
        /// </summary>
        private byte Rol(byte value) { bool carry = GetFlag(Carry); SetFlag(Carry, (value & 0x80) != 0); return Load((byte)((value << 1) | (carry ? 1 : 0))); }
        /// <summary>
        /// Executes the Ror operation.
        /// </summary>
        private byte Ror(byte value) { bool carry = GetFlag(Carry); SetFlag(Carry, (value & 1) != 0); return Load((byte)((value >> 1) | (carry ? 0x80 : 0))); }
        /// <summary>
        /// Executes the Bit operation.
        /// </summary>
        private void Bit(byte value)
        {
            SetFlag(Zero, (A & value) == 0);
            SetFlag(Overflow, (value & 0x40) != 0);
            SetFlag(Negative, (value & 0x80) != 0);
        }

        /// <summary>
        /// Executes the IsCommonNop operation.
        /// </summary>
        private static bool IsCommonNop(byte op) => op is 0x1A or 0x3A or 0x5A or 0x7A or 0xDA or 0xFA or 0x80 or 0x82 or 0x89 or 0xC2 or 0xE2 or 0x04 or 0x44 or 0x64 or 0x14 or 0x34 or 0x54 or 0x74 or 0xD4 or 0xF4 or 0x0C or 0x1C or 0x3C or 0x5C or 0x7C or 0xDC or 0xFC;
        /// <summary>
        /// Executes the IsKernalRoutine operation.
        /// </summary>
        private static bool IsKernalRoutine(ushort address) =>
            address is 0xFF81 or 0xFF84 or 0xFF87 or 0xFF8A or 0xFF8D or 0xFF90 or 0xFF93 or 0xFF96 or 0xFF99 or 0xFF9C or 0xFF9F or 0xFFA2 or 0xFFA5 or 0xFFA8 or 0xFFAB or 0xFFAE or 0xFFB1 or 0xFFB4 or 0xFFB7 or 0xFFBA or 0xFFBD or 0xFFC0 or 0xFFC3 or 0xFFC6 or 0xFFC9 or 0xFFCC or 0xFFCF or 0xFFD2 or 0xFFD5 or 0xFFD8 or 0xFFDB or 0xFFDE or 0xFFE1 or 0xFFE4 or 0xFFE7 or 0xFFEA or 0xFFED or 0xFFF0 or 0xFFF3 or 0xFFF6 or 0xFFF9;

        /// <summary>
        /// Executes the SkipIllegalNop operation.
        /// </summary>
        private void SkipIllegalNop(byte op)
        {
            if (op is 0x80 or 0x82 or 0x89 or 0xC2 or 0xE2)
                _pc++;
            else if (op is 0x04 or 0x44 or 0x64 or 0x14 or 0x34 or 0x54 or 0x74 or 0xD4 or 0xF4)
                _pc++;
            else if (op is 0x0C or 0x1C or 0x3C or 0x5C or 0x7C or 0xDC or 0xFC)
                _pc += 2;
        }
    }
}

/// <summary>
/// Carries SidVoiceRow data.
/// </summary>
public sealed record SidVoiceRow(
    int Row,
    int Voice,
    byte Pitch,
    byte Volume,
    byte Control,
    byte Waveform,
    int DurationRows,
    ushort FrequencyRegister,
    ushort PulseWidth,
    byte AttackDecay,
    byte SustainRelease,
    byte FilterRouting,
    byte FilterModeVolume,
    ushort FilterCutoff);

/// <summary>
/// Carries one tracker-visible NSF voice state sampled from the running NSF driver.
/// </summary>
public sealed record NsfVoiceRow(
    int Row,
    int Voice,
    string ChannelName,
    byte Pitch,
    byte Volume,
    int DurationRows,
    byte VolumeColumn,
    byte EffectColumn,
    byte EffectParam,
    string Source);

/// <summary>
/// Streams chip source audio into an interleaved floating-point output buffer.
/// </summary>
public interface IChipStreamRenderer
{
    /// <summary>
    /// Gets the source chip format handled by this stream renderer.
    /// </summary>
    ModuleFormat Format { get; }

    /// <summary>
    /// Gets the configured output sample rate.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Renders the next chunk of source audio into an interleaved float buffer.
    /// </summary>
    void Render(float[] buffer, int frameCount, int channels);
}
