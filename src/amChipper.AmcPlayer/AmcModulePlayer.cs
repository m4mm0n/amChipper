using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.AmcPlayer;

/// <summary>
/// Small standalone player for amChipper AMC files.
/// It has no NAudio or libopenmpt dependency and renders the normalized AMC song model to stereo float PCM.
/// </summary>
public sealed class AmcModulePlayer : IDisposable
{
    private readonly AmcPlaybackOptions _options;
    private readonly List<Voice> _voices = [];
    private Song? _song;
    private int _orderIndex;
    private int _row;
    private int _frameInRow;
    private int _framesPerRow;
    private double _positionSeconds;
    private bool _isPlaying;

    /// <summary>Creates a new standalone AMC player.</summary>
    public AmcModulePlayer(AmcPlaybackOptions? options = null)
    {
        _options = (options ?? new AmcPlaybackOptions()).Normalize();
    }

    /// <summary>Current output sample rate.</summary>
    public int SampleRate => _options.SampleRate;

    /// <summary>Loaded module metadata, or null before a module is loaded.</summary>
    public AmcPlaybackInfo? Info { get; private set; }

    /// <summary>Current playback time in seconds.</summary>
    public double PositionSeconds => _positionSeconds;

    /// <summary>True while the renderer advances through the song.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Loads an AMC file from disk.</summary>
    public AmcPlaybackInfo Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path));
    }

    /// <summary>Loads an AMC file from bytes.</summary>
    public AmcPlaybackInfo Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _song = NativeChipModuleFile.Load(data);
        _song.Patterns.ForEach(pattern => pattern.EnsureStorage());
        _framesPerRow = ComputeFramesPerRow(_song);
        Stop();
        Info = BuildInfo(_song);
        return Info;
    }

    /// <summary>Starts playback from the current position.</summary>
    public void Play()
    {
        EnsureLoaded();
        _isPlaying = true;
    }

    /// <summary>Stops playback and rewinds to the beginning.</summary>
    public void Stop()
    {
        _isPlaying = false;
        _orderIndex = 0;
        _row = 0;
        _frameInRow = 0;
        _positionSeconds = 0;
        _voices.Clear();
    }

    /// <summary>Seeks to an approximate position in seconds.</summary>
    public void Seek(double seconds)
    {
        var song = EnsureLoaded();
        int rowCount = Math.Max(1, (int)Math.Round(Math.Max(0, seconds) * _options.SampleRate / Math.Max(1, _framesPerRow)));
        _orderIndex = 0;
        _row = 0;
        while (_orderIndex < song.OrderList.Count)
        {
            int patternIndex = song.OrderList[_orderIndex];
            int rows = IsValidPattern(song, patternIndex) ? song.Patterns[patternIndex].RowCount : song.DefaultRowsPerPattern;
            if (rowCount < rows)
                break;
            rowCount -= rows;
            _orderIndex++;
        }

        _orderIndex = Math.Clamp(_orderIndex, 0, Math.Max(song.OrderList.Count - 1, 0));
        _row = Math.Max(0, rowCount);
        _frameInRow = 0;
        _positionSeconds = Math.Max(0, seconds);
        _voices.Clear();
    }

    /// <summary>Renders stereo float PCM into <paramref name="buffer"/> and returns frames written.</summary>
    public int Render(float[] buffer, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length < frameCount * 2)
            throw new ArgumentException("Stereo buffer is too small for the requested frame count.", nameof(buffer));

        Array.Clear(buffer, 0, frameCount * 2);
        var song = EnsureLoaded();
        if (!_isPlaying || frameCount <= 0 || song.OrderList.Count == 0)
            return 0;

        int offset = 0;
        while (offset < frameCount && _isPlaying)
        {
            if (_frameInRow == 0)
                TriggerCurrentRow(song);

            int frames = Math.Min(frameCount - offset, Math.Max(1, _framesPerRow - _frameInRow));
            RenderVoices(buffer, offset, frames);
            offset += frames;
            _frameInRow += frames;
            _positionSeconds += frames / (double)_options.SampleRate;

            if (_frameInRow >= _framesPerRow)
                AdvanceRow(song);
        }

        return offset;
    }

    /// <summary>Releases playback resources.</summary>
    public void Dispose() => Stop();

    private Song EnsureLoaded() => _song ?? throw new InvalidOperationException("No AMC module has been loaded.");

    private static bool IsValidPattern(Song song, int patternIndex) => patternIndex >= 0 && patternIndex < song.Patterns.Count;

    private int ComputeFramesPerRow(Song song)
    {
        double rowsPerSecond = Math.Max(1, song.Bpm) * Math.Max(1, song.RowsPerBeat) / 60.0;
        return Math.Max(1, (int)Math.Round(_options.SampleRate / rowsPerSecond));
    }

    private AmcPlaybackInfo BuildInfo(Song song)
    {
        int totalRows = song.OrderList
            .Select(index => IsValidPattern(song, index) ? song.Patterns[index].RowCount : song.DefaultRowsPerPattern)
            .Sum();
        double duration = totalRows * _framesPerRow / (double)_options.SampleRate;
        string embeddedFormat = song.OriginalModuleData is null ? "internal" : song.Format.ToString();
        string embeddedExtension = string.IsNullOrWhiteSpace(song.SourceModuleExtension) ? ".amc" : song.SourceModuleExtension;
        return new AmcPlaybackInfo(
            song.Title,
            song.Artist,
            "amChipper AMC (.amc)",
            embeddedFormat,
            embeddedExtension,
            song.Tracks.Count,
            song.Patterns.Count,
            song.OrderList.Count,
            song.Instruments.Count,
            song.Bpm,
            song.RowsPerBeat,
            duration);
    }

    private void TriggerCurrentRow(Song song)
    {
        if (_orderIndex >= song.OrderList.Count)
        {
            _isPlaying = false;
            return;
        }

        int patternIndex = song.OrderList[_orderIndex];
        if (!IsValidPattern(song, patternIndex))
            return;

        var pattern = song.Patterns[patternIndex];
        if (_row < 0 || _row >= pattern.RowCount)
            return;

        int channels = Math.Min(pattern.ChannelCount, Math.Max(pattern.ChannelCount, song.Tracks.Count));
        for (int channel = 0; channel < channels; channel++)
        {
            var note = pattern.GetNote(_row, channel);
            if (note.Pitch is 0 or >= (byte)SpecialNote.NoteOff)
                continue;

            int instrumentIndex = ResolveInstrumentIndex(song, note, channel);
            if (instrumentIndex < 0)
                continue;

            var instrument = song.Instruments[instrumentIndex];
            double pan = ResolvePan(song, note, channel);
            double volume = ResolveVolume(song, note, channel, instrument);
            int durationRows = note.DurationTicks > 0
                ? Math.Max(1, (int)Math.Ceiling(note.DurationTicks / (double)Math.Max(1, song.RowsPerBeat)))
                : _options.DefaultNoteRows;
            _voices.Add(new Voice(instrument, note.Pitch, volume, pan, durationRows * _framesPerRow, _options.SampleRate));
        }

        if (_voices.Count > _options.MaxVoices)
            _voices.RemoveRange(0, _voices.Count - _options.MaxVoices);
    }

    private static int ResolveInstrumentIndex(Song song, Note note, int channel)
    {
        if (note.InstrumentIndex > 0)
            return Math.Clamp(note.InstrumentIndex - 1, 0, song.Instruments.Count - 1);

        if (channel >= 0 && channel < song.Tracks.Count)
            return Math.Clamp(song.Tracks[channel].InstrumentIndex, 0, song.Instruments.Count - 1);

        return song.Instruments.Count > 0 ? 0 : -1;
    }

    private static double ResolvePan(Song song, Note note, int channel)
    {
        if (note.Panning != 255)
            return note.Panning / 255.0;
        if (channel >= 0 && channel < song.Tracks.Count)
            return song.Tracks[channel].Panning / 255.0;
        return 0.5;
    }

    private static double ResolveVolume(Song song, Note note, int channel, Instrument instrument)
    {
        double noteVolume = note.Volume == 255 ? 1.0 : note.Volume / 64.0;
        double velocity = Math.Clamp(note.Velocity / 127.0, 0.0, 1.0);
        double trackVolume = channel >= 0 && channel < song.Tracks.Count ? song.Tracks[channel].Volume / 128.0 : 1.0;
        double instrumentVolume = instrument.GlobalVolume / 128.0;
        double globalVolume = song.GlobalVolume / 128.0;
        return Math.Clamp(noteVolume * velocity * trackVolume * instrumentVolume * globalVolume, 0.0, 2.0);
    }

    private void RenderVoices(float[] buffer, int offset, int frames)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (!_voices[i].Render(buffer, offset, frames, _options.MasterGain))
                _voices.RemoveAt(i);
        }
    }

    private void AdvanceRow(Song song)
    {
        _frameInRow = 0;
        _row++;

        if (_orderIndex >= song.OrderList.Count)
        {
            _isPlaying = false;
            return;
        }

        int patternIndex = song.OrderList[_orderIndex];
        int rows = IsValidPattern(song, patternIndex) ? song.Patterns[patternIndex].RowCount : song.DefaultRowsPerPattern;
        if (_row < rows)
            return;

        _row = 0;
        _orderIndex++;
        if (_orderIndex >= song.OrderList.Count)
            _isPlaying = false;
    }

    private sealed class Voice
    {
        private readonly Instrument _instrument;
        private readonly double _frequency;
        private readonly double _volume;
        private readonly double _leftGain;
        private readonly double _rightGain;
        private readonly int _sampleRate;
        private readonly int _totalFrames;
        private int _age;
        private uint _noise = 0x12345678;
        private double _phase;

        public Voice(Instrument instrument, byte pitch, double volume, double pan, int totalFrames, int sampleRate)
        {
            _instrument = instrument;
            _frequency = 440.0 * Math.Pow(2.0, (pitch - 69) / 12.0);
            _volume = volume;
            _leftGain = Math.Cos(Math.Clamp(pan, 0.0, 1.0) * Math.PI * 0.5);
            _rightGain = Math.Sin(Math.Clamp(pan, 0.0, 1.0) * Math.PI * 0.5);
            _totalFrames = Math.Max(1, totalFrames);
            _sampleRate = sampleRate;
        }

        public bool Render(float[] buffer, int offset, int frames, float masterGain)
        {
            for (int i = 0; i < frames; i++)
            {
                if (_age >= _totalFrames)
                    return false;

                double env = Envelope();
                double sample = Oscillator() * _volume * env * masterGain;
                int dst = (offset + i) * 2;
                buffer[dst] = Clamp(buffer[dst] + sample * _leftGain);
                buffer[dst + 1] = Clamp(buffer[dst + 1] + sample * _rightGain);
                _phase += _frequency / _sampleRate;
                _phase -= Math.Floor(_phase);
                _age++;
            }

            return _age < _totalFrames;
        }

        private double Envelope()
        {
            int attackFrames = Math.Max(1, _instrument.AttackMs * _sampleRate / 1000);
            int releaseFrames = Math.Max(1, _instrument.ReleaseMs * _sampleRate / 1000);
            double attack = _age < attackFrames ? _age / (double)attackFrames : 1.0;
            int remaining = _totalFrames - _age;
            double release = remaining < releaseFrames ? Math.Max(0, remaining / (double)releaseFrames) : 1.0;
            return Math.Min(attack, release);
        }

        private double Oscillator()
        {
            return _instrument.Waveform switch
            {
                SynthWaveform.Triangle => 4.0 * Math.Abs(_phase - 0.5) - 1.0,
                SynthWaveform.Saw => 2.0 * _phase - 1.0,
                SynthWaveform.Noise => NextNoise(),
                _ => _phase < Math.Clamp(_instrument.PulseWidth, 0.05, 0.95) ? 1.0 : -1.0
            };
        }

        private double NextNoise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return ((_noise & 0xFFFF) / 32768.0) - 1.0;
        }

        private static float Clamp(double value) => (float)Math.Clamp(value, -1.0, 1.0);
    }
}
