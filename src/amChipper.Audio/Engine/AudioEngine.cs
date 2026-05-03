using NAudio.Wave;
using amChipper.Core.Interfaces;
using amChipper.Core.Models;
using amChipper.Core.Persistence;
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
    /// Stores or exposes ChipStreamPlayer.
    /// </summary>
    public ChipStreamPlayer ChipStreamPlayer { get; }

    /// <summary>
    /// Stores or exposes UseModulePlayer.
    /// </summary>
    public bool UseModulePlayer { get; set; } = false;
    /// <summary>
    /// Stores or exposes UseAudioFilePlayer.
    /// </summary>
    public bool UseAudioFilePlayer { get; set; } = false;

    /// <summary>
    /// Stores or exposes UseChipStreamPlayer.
    /// </summary>
    public bool UseChipStreamPlayer { get; set; } = false;

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
        ChipStreamPlayer = new ChipStreamPlayer(_log);
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
        string mode = UseChipStreamPlayer ? "ChipStream" : UseAudioFilePlayer ? "AudioFile" : UseModulePlayer ? "Module" : "Sequencer";
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
        ChipStreamPlayer.Stop();
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
        UseChipStreamPlayer = false;
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

/// <summary>
/// Streams supported chip sources directly into the audio engine without whole-song pre-rendering.
/// </summary>
public sealed class ChipStreamPlayer
{
    private const int StreamChannels = 2;
    private const int ProducerFrames = 512;

    private readonly IAppLogger _log;
    private readonly object _lock = new();
    private amChipper.Core.Persistence.IChipStreamRenderer? _renderer;
    private string? _sourcePath;
    private float[] _ringBuffer = [];
    private int _ringReadIndex;
    private int _ringWriteIndex;
    private int _ringAvailableSamples;
    private int _sampleRate = 44100;
    private double _positionSecs;
    private CancellationTokenSource? _producerCancel;
    private Task? _producerTask;
    private volatile bool _producerFaulted;

    public ChipStreamPlayer(IAppLogger log) => _log = log;

    /// <summary>
    /// Gets whether a live chip stream is ready for playback.
    /// </summary>
    public bool IsLoaded
    {
        get { lock (_lock) return _renderer is not null && !_producerFaulted; }
    }

    /// <summary>
    /// Current stream playback position in seconds.
    /// </summary>
    public double PositionSecs
    {
        get { lock (_lock) return _positionSecs; }
    }

    /// <summary>
    /// Loads a supported chip source for bounded live playback.
    /// </summary>
    public bool Load(byte[] data, string sourcePath, int sampleRate)
    {
        Stop();

        amChipper.Core.Persistence.IChipStreamRenderer renderer;
        try
        {
            renderer = InternalChipRenderer.CreateStreamingRenderer(data, sourcePath, sampleRate);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _renderer = null;
                _sourcePath = null;
                _positionSecs = 0;
                ResetRingLocked();
            }

            _log.Warning($"[ChipStream] Failed to load stream path=\"{sourcePath}\": {ex.Message}");
            return false;
        }

        lock (_lock)
        {
            _renderer = renderer;
            _sourcePath = sourcePath;
            _sampleRate = Math.Max(1, renderer.SampleRate);
            _positionSecs = 0;
            _producerFaulted = false;
            _ringBuffer = new float[Math.Max(_sampleRate * StreamChannels, ProducerFrames * StreamChannels * 4)];
            ResetRingLocked();
            var producerCancel = new CancellationTokenSource();
            _producerCancel = producerCancel;
            _producerTask = Task.Run(() => RunProducer(renderer, sourcePath, producerCancel.Token));
        }

        _log.Info($"[ChipStream] Loaded {renderer.Format} buffered stream path=\"{sourcePath}\" sampleRate={renderer.SampleRate}");
        return true;
    }

    /// <summary>
    /// Renders the next chunk from the live chip source.
    /// </summary>
    public int Render(float[] buffer, int frameCount, int outputChannels)
    {
        int framesRead;
        lock (_lock)
        {
            if (_renderer is null)
                return 0;

            framesRead = ReadRingLocked(buffer, frameCount, outputChannels);
            _positionSecs += frameCount / (double)_sampleRate;
        }

        int samplesRead = framesRead * outputChannels;
        int totalSamples = frameCount * outputChannels;
        if (samplesRead < totalSamples)
            Array.Clear(buffer, samplesRead, totalSamples - samplesRead);

        return frameCount;
    }

    /// <summary>
    /// Clears the active live chip stream.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cancel;
        Task? producer;
        lock (_lock)
        {
            cancel = _producerCancel;
            producer = _producerTask;
            _producerCancel = null;
            _producerTask = null;
            _renderer = null;
            _sourcePath = null;
            _positionSecs = 0;
            _producerFaulted = false;
            ResetRingLocked();
        }

        cancel?.Cancel();
        try
        {
            producer?.Wait(TimeSpan.FromMilliseconds(50));
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException)
        {
            _log.Debug($"[ChipStream] Producer stop ignored: {ex.GetType().Name}");
        }
        finally
        {
            cancel?.Dispose();
        }
    }

    private void RunProducer(amChipper.Core.Persistence.IChipStreamRenderer renderer, string sourcePath, CancellationToken token)
    {
        var chunk = new float[ProducerFrames * StreamChannels];
        try
        {
            while (!token.IsCancellationRequested)
            {
                bool shouldRender;
                lock (_lock)
                    shouldRender = _renderer == renderer && _ringAvailableSamples < _ringBuffer.Length - chunk.Length;

                if (!shouldRender)
                {
                    Thread.Sleep(2);
                    continue;
                }

                Array.Clear(chunk);
                renderer.Render(chunk, ProducerFrames, StreamChannels);

                lock (_lock)
                {
                    if (_renderer != renderer)
                        return;

                    WriteRingLocked(chunk, chunk.Length);
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            lock (_lock)
            {
                if (_renderer == renderer)
                {
                    _producerFaulted = true;
                    _renderer = null;
                    _sourcePath = null;
                    ResetRingLocked();
                }
            }

            _log.Warning($"[ChipStream] Stream producer stopped path=\"{sourcePath}\": {ex.Message}");
        }
    }

    private int ReadRingLocked(float[] destination, int frameCount, int outputChannels)
    {
        if (_ringBuffer.Length == 0 || _ringAvailableSamples <= 0)
            return 0;

        int sourceFrames = Math.Min(frameCount, _ringAvailableSamples / StreamChannels);
        for (int frame = 0; frame < sourceFrames; frame++)
        {
            float left = PopRingSampleLocked();
            float right = PopRingSampleLocked();
            int destinationIndex = frame * outputChannels;

            if (outputChannels == 1)
            {
                destination[destinationIndex] = (left + right) * 0.5f;
            }
            else
            {
                destination[destinationIndex] = left;
                destination[destinationIndex + 1] = right;
                for (int ch = 2; ch < outputChannels; ch++)
                    destination[destinationIndex + ch] = 0;
            }
        }

        return sourceFrames;
    }

    private void WriteRingLocked(float[] source, int sampleCount)
    {
        if (_ringBuffer.Length == 0)
            return;

        int overflow = _ringAvailableSamples + sampleCount - _ringBuffer.Length;
        if (overflow > 0)
        {
            int drop = Math.Min(overflow, _ringAvailableSamples);
            _ringReadIndex = (_ringReadIndex + drop) % _ringBuffer.Length;
            _ringAvailableSamples -= drop;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            _ringBuffer[_ringWriteIndex] = source[i];
            _ringWriteIndex = (_ringWriteIndex + 1) % _ringBuffer.Length;
            if (_ringAvailableSamples < _ringBuffer.Length)
                _ringAvailableSamples++;
        }
    }

    private float PopRingSampleLocked()
    {
        if (_ringAvailableSamples <= 0 || _ringBuffer.Length == 0)
            return 0;

        float sample = _ringBuffer[_ringReadIndex];
        _ringReadIndex = (_ringReadIndex + 1) % _ringBuffer.Length;
        _ringAvailableSamples--;
        return sample;
    }

    private void ResetRingLocked()
    {
        _ringReadIndex = 0;
        _ringWriteIndex = 0;
        _ringAvailableSamples = 0;
        if (_ringBuffer.Length > 0)
            Array.Clear(_ringBuffer);
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

        if (_engine.UseChipStreamPlayer && _engine.ChipStreamPlayer.IsLoaded)
            _engine.ChipStreamPlayer.Render(_floatBuffer, frameCount, channels);
        else if (_engine.UseAudioFilePlayer && _engine.AudioFilePlayer.IsLoaded)
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
