using NAudio.Wave;
using amChipper.Core.Interfaces;
using amChipper.Core.Models;
using CorePlaybackState = amChipper.Core.Models.PlaybackState;

namespace amChipper.Audio.Engine;

/// <summary>
/// NAudio-backed audio engine.  Supports two rendering modes:
///   • ModuleMode  — streams audio from a libopenmpt ModulePlayer
///   • AudioFileMode — streams rendered chip audio from WAV/AIFF/etc.
///   • SequencerMode — streams audio from the InternalSequencer
/// </summary>
public sealed class AudioEngine : IAudioEngine
{
    /// <summary>
    /// Stores or exposes _waveOut.
    /// </summary>
    private WaveOutEvent? _waveOut;
    /// <summary>
    /// Stores or exposes _provider.
    /// </summary>
    private ChipWaveProvider? _provider;
    /// <summary>
    /// Stores or exposes _disposed.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores or exposes _channels.
    /// </summary>
    private int _channels = 2;

    /// <summary>
    /// Stores or exposes _log.
    /// </summary>
    private readonly IAppLogger _log;

    /// <summary>
    /// Stores or exposes ModulePlayer.
    /// </summary>
    public ModulePlayer ModulePlayer { get; }
    /// <summary>
    /// Stores or exposes Sequencer.
    /// </summary>
    public InternalSequencer Sequencer { get; }
    /// <summary>
    /// Stores or exposes AudioFilePlayer.
    /// </summary>
    public RenderedAudioFilePlayer AudioFilePlayer { get; }

    /// <summary>
    /// Stores or exposes UseModulePlayer.
    /// </summary>
    public bool UseModulePlayer { get; set; } = false;
    /// <summary>
    /// Stores or exposes UseAudioFilePlayer.
    /// </summary>
    public bool UseAudioFilePlayer { get; set; } = false;

    /// <summary>
    /// Stores or exposes State.
    /// </summary>
    public CorePlaybackState State { get; private set; } = CorePlaybackState.Stopped;

    /// <summary>
    /// Stores or exposes MasterVolume.
    /// </summary>
    public float MasterVolume
    {
        get => _waveOut?.Volume ?? 1f;
        set
        {
            if (_waveOut is not null)
            {
                _waveOut.Volume = Math.Clamp(value, 0f, 1f);
                _log.Debug($"Master volume → {_waveOut.Volume:P0}");
            }
        }
    }

    /// <summary>
    /// Stores or exposes SampleRate.
    /// </summary>
    public int SampleRate { get; private set; } = 44100;
    /// <summary>
    /// Stores or exposes OutputDeviceNumber.
    /// </summary>
    public int OutputDeviceNumber { get; private set; } = -1;
    /// <summary>
    /// Stores or exposes DesiredLatencyMs.
    /// </summary>
    public int DesiredLatencyMs { get; private set; } = 200;
    /// <summary>
    /// Stores or exposes BufferCount.
    /// </summary>
    public int BufferCount { get; private set; } = 4;

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<AudioBufferEventArgs>? BufferRendered;

    public AudioEngine(IAppLogger? logger = null)
    {
        _log = logger ?? NullAppLogger.Instance;
        ModulePlayer = new ModulePlayer(44100, _log);
        Sequencer = new InternalSequencer(44100, _log);
        AudioFilePlayer = new RenderedAudioFilePlayer(_log);
    }

    /// <summary>
    /// Executes the Initialise operation.
    /// </summary>
    public void Initialise(int sampleRate = 44100, int channels = 2)
    {
        Initialise(sampleRate, channels, OutputDeviceNumber, DesiredLatencyMs, BufferCount);
    }

    /// <summary>
    /// Executes the Initialise operation.
    /// </summary>
    public void Initialise(int sampleRate, int channels, int outputDeviceNumber, int desiredLatencyMs, int bufferCount)
    {
        SampleRate = sampleRate;
        _channels = Math.Clamp(channels, 1, 2);
        OutputDeviceNumber = outputDeviceNumber;
        DesiredLatencyMs = Math.Clamp(desiredLatencyMs, 40, 1000);
        BufferCount = Math.Clamp(bufferCount, 2, 8);

        _waveOut?.Dispose();
        _provider = new ChipWaveProvider(sampleRate, channels, this);
        _waveOut = new WaveOutEvent
        {
            DeviceNumber = outputDeviceNumber,
            DesiredLatency = DesiredLatencyMs,
            NumberOfBuffers = BufferCount
        };
        _waveOut.Init(_provider);
        _log.Info($"Audio engine initialised: {sampleRate} Hz, {channels} ch, device={outputDeviceNumber}, latency={DesiredLatencyMs}ms, buffers={BufferCount}.");
    }

    /// <summary>
    /// Executes the Reconfigure operation.
    /// </summary>
    public void Reconfigure(int sampleRate, int outputDeviceNumber, int desiredLatencyMs, int bufferCount)
    {
        bool wasPlaying = State == CorePlaybackState.Playing;
        _waveOut?.Stop();
        Initialise(sampleRate, _channels, outputDeviceNumber, desiredLatencyMs, bufferCount);
        if (wasPlaying)
            _waveOut?.Play();
    }

    /// <summary>
    /// Executes the Play operation.
    /// </summary>
    public void Play()
    {
        if (_waveOut is null) Initialise();
        _waveOut!.Play();
        State = CorePlaybackState.Playing;
        string mode = UseAudioFilePlayer ? "AudioFile" : UseModulePlayer ? "Module" : "Sequencer";
        _log.Info($"Playback started (mode: {mode}).");
    }

    /// <summary>
    /// Executes the Pause operation.
    /// </summary>
    public void Pause()
    {
        _waveOut?.Pause();
        State = CorePlaybackState.Paused;
        _log.Info("Playback paused.");
    }

    /// <summary>
    /// Executes the Stop operation.
    /// </summary>
    public void Stop()
    {
        _waveOut?.Stop();
        Sequencer.Stop();
        AudioFilePlayer.Stop();
        State = CorePlaybackState.Stopped;
        _log.Info("Playback stopped.");
    }

    /// <summary>
    /// Executes the PreviewNote operation.
    /// </summary>
    public void PreviewNote(Song song, int pitch, int instrumentIndex, int channel = 0, byte velocity = 110, int milliseconds = 60000)
    {
        if (_waveOut is null) Initialise();

        _log.Info($"[AudioEngine] PreviewNote pitch={pitch} instrumentIndex={instrumentIndex} channel={channel} velocity={velocity} ms={milliseconds}");
        UseModulePlayer = false;
        UseAudioFilePlayer = false;
        Sequencer.SetSong(song);
        Sequencer.PreviewNote(pitch, instrumentIndex, channel, velocity, milliseconds);

        if (State != CorePlaybackState.Playing)
            _waveOut!.Play();
    }

    /// <summary>
    /// Executes the StopPreviewNote operation.
    /// </summary>
    public void StopPreviewNote(int channel = 0)
    {
        Sequencer.StopPreviewNote(channel);
    }

    /// <summary>
    /// Executes the OnBufferFilled operation.
    /// </summary>
    internal void OnBufferFilled(float[] buffer, int frames) =>
        BufferRendered?.Invoke(this, new AudioBufferEventArgs(buffer, frames));

    /// <summary>
    /// Executes the Dispose operation.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _waveOut?.Dispose();
        ModulePlayer.Dispose();
        AudioFilePlayer.Dispose();
        _log.Info("AudioEngine disposed.");
    }
}

/// <summary>
/// Represents the RenderedAudioFilePlayer component.
/// </summary>
public sealed class RenderedAudioFilePlayer : IDisposable
{
    /// <summary>
    /// Stores or exposes _log.
    /// </summary>
    private readonly IAppLogger _log;
    /// <summary>
    /// Stores or exposes _reader.
    /// </summary>
    private AudioFileReader? _reader;
    private readonly object _lock = new();

    public RenderedAudioFilePlayer(IAppLogger log) => _log = log;

    /// <summary>
    /// Stores or exposes IsLoaded.
    /// </summary>
    public bool IsLoaded => _reader is not null;
    /// <summary>
    /// Stores or exposes FilePath.
    /// </summary>
    public string? FilePath { get; private set; }
    /// <summary>
    /// Stores or exposes DurationSecs.
    /// </summary>
    public double DurationSecs => _reader?.TotalTime.TotalSeconds ?? 0;
    /// <summary>
    /// Stores or exposes PositionSecs.
    /// </summary>
    public double PositionSecs
    {
        get => _reader?.CurrentTime.TotalSeconds ?? 0;
        set
        {
            lock (_lock)
            {
                if (_reader is null)
                    return;
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value, 0, DurationSecs));
            }
        }
    }

    /// <summary>
    /// Executes the Load operation.
    /// </summary>
    public bool Load(string path)
    {
        lock (_lock)
        {
            DisposeReader();
            try
            {
                _reader = new AudioFileReader(path);
                FilePath = path;
                _log.Info($"Rendered audio loaded path=\"{path}\" duration={DurationSecs:0.###}s wave={_reader.WaveFormat}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Could not load rendered audio file \"{path}\": {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Executes the Render operation.
    /// </summary>
    public int Render(float[] buffer, int frameCount, int outputChannels)
    {
        lock (_lock)
        {
            if (_reader is null)
                return 0;

            int samplesNeeded = frameCount * outputChannels;
            float[] temp = outputChannels == _reader.WaveFormat.Channels
                ? buffer
                : new float[samplesNeeded];
            int samplesRead = _reader.Read(temp, 0, samplesNeeded);

            if (outputChannels == _reader.WaveFormat.Channels)
                return samplesRead / Math.Max(1, outputChannels);

            int inputChannels = Math.Max(1, _reader.WaveFormat.Channels);
            int framesRead = samplesRead / inputChannels;
            for (int frame = 0; frame < framesRead; frame++)
            {
                float mono = 0;
                for (int ch = 0; ch < inputChannels; ch++)
                    mono += temp[frame * inputChannels + ch];
                mono /= inputChannels;

                for (int ch = 0; ch < outputChannels; ch++)
                    buffer[frame * outputChannels + ch] = mono;
            }

            return framesRead;
        }
    }

    /// <summary>
    /// Executes the Stop operation.
    /// </summary>
    public void Stop() => PositionSecs = 0;

    /// <summary>
    /// Executes the Dispose operation.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
            DisposeReader();
    }

    /// <summary>
    /// Executes the DisposeReader operation.
    /// </summary>
    private void DisposeReader()
    {
        _reader?.Dispose();
        _reader = null;
        FilePath = null;
    }
}

/// <summary>IWaveProvider that pulls from ModulePlayer or InternalSequencer.</summary>
internal sealed class ChipWaveProvider : IWaveProvider
{
    /// <summary>
    /// Stores or exposes _engine.
    /// </summary>
    private readonly AudioEngine _engine;
    /// <summary>
    /// Stores or exposes _floatBuffer.
    /// </summary>
    private readonly float[] _floatBuffer;
    // Match ModulePlayer's pre-allocated native render buffer capacity (4096 frames).
    // NAudio may request up to ~(sampleRate * desiredLatency / 1000) frames per call;
    // at 44100 Hz / 200 ms that is ~8820 — we cap at 4096 to stay within the native
    // buffer, but crucially we no longer under-fill with the old 512-frame limit which
    // caused silence gaps and audible stuttering.
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int FramesPerBuffer = 4096;

    /// <summary>
    /// Stores or exposes WaveFormat.
    /// </summary>
    public WaveFormat WaveFormat { get; }

    public ChipWaveProvider(int sampleRate, int channels, AudioEngine engine)
    {
        _engine = engine;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _floatBuffer = new float[FramesPerBuffer * channels];
    }

    /// <summary>
    /// Executes the Read operation.
    /// </summary>
    public int Read(byte[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels;
        int frameCount = Math.Min(count / (sizeof(float) * channels), FramesPerBuffer);

        Array.Clear(_floatBuffer, 0, frameCount * channels);

        if (_engine.UseAudioFilePlayer && _engine.AudioFilePlayer.IsLoaded)
            _engine.AudioFilePlayer.Render(_floatBuffer, frameCount, channels);
        else if (_engine.UseModulePlayer && _engine.ModulePlayer.IsLoaded)
            _engine.ModulePlayer.Render(_floatBuffer, frameCount);
        else
            _engine.Sequencer.Render(_floatBuffer, frameCount);

        _engine.OnBufferFilled(_floatBuffer, frameCount);

        int byteCount = frameCount * channels * sizeof(float);
        System.Buffer.BlockCopy(_floatBuffer, 0, buffer, offset, byteCount);
        return byteCount;
    }
}
