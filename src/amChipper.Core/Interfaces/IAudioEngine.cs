using amChipper.Core.Models;

namespace amChipper.Core.Interfaces;

/// <summary>
/// Contract for the low-level audio output engine.
/// The implementation drives NAudio WasapiOut (or WaveOutEvent as fallback).
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>Current audio output state.</summary>
    PlaybackState State { get; }

    /// <summary>Master volume 0.0-1.0.</summary>
    float MasterVolume { get; set; }

    /// <summary>Sample rate of the audio output stream.</summary>
    int SampleRate { get; }

    /// <summary>Initialise the audio device. Must be called before Play.</summary>
    void Initialise(int sampleRate = 44100, int channels = 2);

    void Play();
    void Pause();
    void Stop();

    /// <summary>Raised on the audio thread each time a new buffer is rendered.</summary>
    event EventHandler<AudioBufferEventArgs>? BufferRendered;
}

/// <summary>
/// Represents the AudioBufferEventArgs component.
/// </summary>
public sealed class AudioBufferEventArgs(float[] buffer, int frameCount) : EventArgs
{
    /// <summary>
    /// Stores or exposes Buffer.
    /// </summary>
    public float[] Buffer { get; } = buffer;
    /// <summary>
    /// Stores or exposes FrameCount.
    /// </summary>
    public int FrameCount { get; } = frameCount;
}
