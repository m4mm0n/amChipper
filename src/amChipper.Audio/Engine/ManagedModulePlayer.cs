#if AMCHIPPER_LIBOPENMPT_NET
using amChipper.Core.Interfaces;
using amChipper.Core.Models;
using libopenmpt.net.Core;
using libopenmpt.net.Core.Api;
using ManagedCommand = libopenmpt.net.Core.Command;
using ManagedNote = libopenmpt.net.Core.Note;

namespace amChipper.Audio.Engine;

public sealed class ManagedModulePlayer : IModulePlayer
{
    private readonly int _sampleRate;
    private readonly IAppLogger _log;
    private readonly object _lock = new();
    private OpenMptModule? _module;
    private byte[]? _moduleData;
    private string? _sourceFileName;
    private double _positionSecs;
    private int _lastOrder = -1;
    private int _lastRow = -1;
    private readonly HashSet<int> _mutedChannels = [];
    private double[] _channelVolumes = [];
    private double[] _channelPans = [];

    public ManagedModulePlayer(int sampleRate = 44100, IAppLogger? logger = null)
    {
        _sampleRate = sampleRate;
        _log = logger ?? NullAppLogger.Instance;
    }

    public string BackendName => ModulePlayerFactory.ManagedBackendName;
    public bool IsLoaded => _module is not null;
    public int OrderCount => _module?.OrderCount ?? 0;
    public int PatternCount => _module?.PatternCount ?? 0;
    public int ChannelCount => _module?.ChannelCount ?? 0;
    public int CurrentOrder => _module?.CurrentOrder ?? 0;
    public int CurrentRow => _module?.CurrentRow ?? 0;
    public double DurationSecs => _module?.DurationSeconds ?? 0;
    public int CurrentTempo => _module?.CurrentTempo ?? 0;
    public int CurrentSpeed => _module?.CurrentSpeed ?? 0;
    public int RestartOrder { get; private set; } = -1;
    public bool LoopEnabled { get; set; }
    public bool LoopFromRestartOrder { get; set; }
    public ModuleFormat Format { get; private set; }
    public string SourceModuleType { get; private set; } = string.Empty;
    public string SourceModuleExtension { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Artist { get; private set; } = string.Empty;
    public string Comment { get; private set; } = string.Empty;

    public double PositionSecs
    {
        get => _positionSecs;
        set
        {
            if (_module is null)
                return;

            lock (_lock)
            {
                double target = Math.Max(0, value);
                if (_module.SeekSeconds(target))
                {
                    ReapplyChannelMutes();
                    _positionSecs = target;
                    RaisePositionEvents();
                }
            }
        }
    }

    public event EventHandler<RowChangedEventArgs>? RowChanged;
    public event EventHandler<OrderChangedEventArgs>? OrderChanged;

    public bool Load(byte[] data, string? fileName = null)
    {
        if (data is null || data.Length == 0)
            return false;

        lock (_lock)
        {
            FreeModule();
            try
            {
                var settings = new OpenMptSettings
                {
                    SampleRate = _sampleRate,
                    Channels = 2,
                    RepeatCount = LoopEnabled ? -1 : 0
                };
                _module = OpenMptModuleFactory.FromBytes(data, settings, fileName ?? string.Empty);
                _moduleData = (byte[])data.Clone();
                _sourceFileName = fileName;
                _positionSecs = 0;

                Title = _module.GetMetadata(OpenMptMetadataKeys.Title);
                Artist = _module.GetMetadata(OpenMptMetadataKeys.Artist);
                Comment = _module.GetMetadata(OpenMptMetadataKeys.Message);
                SourceModuleType = _module.GetMetadata(OpenMptMetadataKeys.Type);
                SourceModuleExtension = Path.GetExtension(fileName ?? string.Empty);
                Format = DetectFormat(SourceModuleType, SourceModuleExtension);
                RestartOrder = -1;
                ResetChannelControls();
                _lastOrder = -1;
                _lastRow = -1;

                _log.Info($"libopenmpt.net managed module loaded: \"{Title}\" | Format={Format} | Type={SourceModuleType} | Orders={OrderCount} | Patterns={PatternCount} | Channels={ChannelCount} | Duration={DurationSecs:F1}s");
                return true;
            }
            catch (Exception ex)
            {
                FreeModule();
                _log.Warning($"libopenmpt.net managed backend rejected {fileName ?? "(memory)"}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }

    public int Render(float[] buffer, int frameCount)
    {
        if (_module is null || buffer is null || frameCount <= 0)
            return 0;

        lock (_lock)
        {
            int frames = _module.ReadInterleavedFloatStereo(buffer, frameCount);
            if (frames <= 0 && LoopEnabled)
            {
                SeekToOrder(LoopFromRestartOrder && RestartOrder >= 0 ? RestartOrder : 0, 0);
                frames = _module.ReadInterleavedFloatStereo(buffer, frameCount);
            }

            if (frames > 0)
            {
                ApplyChannelControls(buffer, frames);
                _positionSecs += frames / (double)_sampleRate;
                RaisePositionEvents();
            }

            return frames;
        }
    }

    public void SeekToOrder(int order, int row = 0)
    {
        if (_module is null)
            return;

        lock (_lock)
        {
            double seconds = EstimateSecondsForOrderRow(order, row);
            if (_module.SeekOrderRow(order, row))
            {
                ReapplyChannelMutes();
                _positionSecs = seconds;
                RaisePositionEvents();
            }
        }
    }

    public double GetCurrentChannelVuMono(int channel) => -1.0;
    public double GetChannelVolume(int channel) => (uint)channel < (uint)_channelVolumes.Length ? _channelVolumes[channel] : 1.0;
    public double GetChannelPanning(int channel) => (uint)channel < (uint)_channelPans.Length ? _channelPans[channel] : 0.0;

    public void SetChannelMuteStatus(int channel, bool mute)
    {
        if ((uint)channel >= (uint)ChannelCount)
            return;

        lock (_lock)
        {
            if (mute)
                _mutedChannels.Add(channel);
            else
                _mutedChannels.Remove(channel);

            _module?.SetChannelMuteStatus(channel, mute);
        }
    }

    public void SetChannelVolume(int channel, double volume)
    {
        if ((uint)channel < (uint)_channelVolumes.Length)
            _channelVolumes[channel] = Math.Clamp(volume, 0.0, 1.0);
    }

    public void SetChannelPanning(int channel, double panning)
    {
        if ((uint)channel < (uint)_channelPans.Length)
            _channelPans[channel] = Math.Clamp(panning, -1.0, 1.0);
    }

    public void UnmuteAllChannels()
    {
        lock (_lock)
        {
            foreach (int channel in _mutedChannels.ToArray())
                _module?.SetChannelMuteStatus(channel, false);

            _mutedChannels.Clear();
        }
    }

    public Song? ImportAsSong()
    {
        if (_module is null)
            return null;

        var song = new Song
        {
            Title = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title,
            Artist = Artist,
            Comment = Comment,
            Format = Format,
            SourceModuleType = SourceModuleType,
            SourceModuleExtension = ModuleFormatCatalog.NormalizeExtension(SourceModuleExtension),
            RowsPerBeat = 4,
            Bpm = Math.Clamp(CurrentTempo > 0 ? CurrentTempo : 125, 6, 999),
            InitialSpeed = CurrentSpeed > 0 ? Math.Clamp(CurrentSpeed, 1, 31) : 6,
            RestartOrder = RestartOrder,
            OriginalModuleData = _moduleData is null ? null : (byte[])_moduleData.Clone()
        };

        AddInstruments(song);
        ApplyNativeSampleData(song);
        AddTracks(song);
        AddPatterns(song);
        AddOrderBlocks(song);

        _log.Info($"libopenmpt.net managed import complete: instruments={song.Instruments.Count} patterns={song.Patterns.Count} channels={song.Tracks.Count} orders={song.OrderList.Count}.");
        return song;
    }

    public void Dispose() => FreeModule();

    private void FreeModule()
    {
        _module?.Dispose();
        _module = null;
        _moduleData = null;
        _sourceFileName = null;
        _positionSecs = 0;
        _mutedChannels.Clear();
        _channelVolumes = [];
        _channelPans = [];
    }

    private void ResetChannelControls()
    {
        _channelVolumes = Enumerable.Repeat(1.0, Math.Max(ChannelCount, 0)).ToArray();
        _channelPans = new double[Math.Max(ChannelCount, 0)];
        _mutedChannels.Clear();
    }

    private void ReapplyChannelMutes()
    {
        if (_module is null)
            return;

        foreach (int channel in _mutedChannels)
            _module.SetChannelMuteStatus(channel, true);
    }

    private void RaisePositionEvents()
    {
        if (_module is null)
            return;

        int order = _module.CurrentOrder;
        int row = _module.CurrentRow;
        if (row == _lastRow && order == _lastOrder)
            return;

        RowChanged?.Invoke(this, new RowChangedEventArgs(order, _module.CurrentPattern, row));
        if (order != _lastOrder)
            OrderChanged?.Invoke(this, new OrderChangedEventArgs(order));
        _lastOrder = order;
        _lastRow = row;
    }

    private void ApplyChannelControls(float[] buffer, int frames)
    {
        if (_mutedChannels.Count == 0 && _channelVolumes.All(v => Math.Abs(v - 1.0) < 0.0001))
            return;

        double totalScale = 1.0;
        if (_channelVolumes.Length > 0)
            totalScale = _channelVolumes.Where((_, i) => !_mutedChannels.Contains(i)).DefaultIfEmpty(0.0).Average();

        float scale = (float)Math.Clamp(totalScale, 0.0, 1.0);
        for (int i = 0; i < frames * 2; i++)
            buffer[i] *= scale;
    }

    private void AddInstruments(Song song)
    {
        if (_module is null)
            return;

        int instrumentCount = _module.SoundFile.NumInstruments > 0
            ? _module.SoundFile.NumInstruments
            : _module.SoundFile.NumSamples;
        instrumentCount = Math.Max(instrumentCount, 1);

        for (int i = 1; i <= instrumentCount; i++)
        {
            var source = i < _module.SoundFile.Instruments.Length ? _module.SoundFile.Instruments[i] : null;
            string? name = source?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = source?.Samples.FirstOrDefault()?.Name;

            song.Instruments.Add(new Instrument
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"Instrument {i}" : name.Trim(),
                SourceType = InstrumentSourceType.Sample
            });
        }
    }

    /// <summary>
    /// Imports native XM sample payloads into the editable song model for preview and export paths.
    /// </summary>
    /// <param name="song">The song model receiving decoded sample data.</param>
    private void ApplyNativeSampleData(Song song)
    {
        if (Format != ModuleFormat.XM || _moduleData is null)
            return;

        try
        {
            var imported = XmSampleImporter.Parse(_moduleData, _sourceFileName);
            int instrumentCount = Math.Min(song.Instruments.Count, imported.Instruments.Count);
            int loadedSamples = 0;

            for (int i = 0; i < instrumentCount; i++)
            {
                var source = imported.Instruments[i];
                if (source.Samples.Count == 0)
                    continue;

                var target = song.Instruments[i];
                target.SourceType = InstrumentSourceType.Sample;
                target.Samples.Clear();
                target.NoteMap = Enumerable.Repeat((byte)255, 128).ToArray();

                foreach (var sourceSample in source.Samples)
                    target.Samples.Add(sourceSample);

                for (int note = 0; note < source.SampleMap.Length && note < 96; note++)
                {
                    int mapped = source.SampleMap[note];
                    if (mapped >= 0 && mapped < target.Samples.Count)
                        target.NoteMap[note + 12] = (byte)mapped;
                }

                byte low = target.NoteMap[12] == 255 ? (byte)0 : target.NoteMap[12];
                byte high = target.NoteMap[107] == 255 ? low : target.NoteMap[107];
                for (int note = 0; note < 12; note++)
                    target.NoteMap[note] = low;
                for (int note = 108; note < target.NoteMap.Length; note++)
                    target.NoteMap[note] = high;

                loadedSamples += target.Samples.Count;
            }

            _log.Info($"XM sample import complete: instrumentsWithSamples={song.Instruments.Count(i => i.SourceType == InstrumentSourceType.Sample)} samples={loadedSamples}");
        }
        catch (Exception ex)
        {
            _log.Warning($"XM sample import failed; native playback will use synth fallback. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void AddTracks(Song song)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            song.Tracks.Add(new Track
            {
                Name = _module?.GetChannelName(channel) ?? $"Ch {channel + 1}",
                Volume = (byte)Math.Clamp(Math.Round(GetChannelVolume(channel) * 128.0), 0, 128),
                Panning = (byte)Math.Clamp(Math.Round(((GetChannelPanning(channel) + 1.0) * 0.5) * 255.0), 0, 255)
            });
        }
    }

    private void AddPatterns(Song song)
    {
        if (_module is null)
            return;

        var currentInstrumentByChannel = new int[Math.Max(ChannelCount, 0)];
        var channelInstrumentCounts = Enumerable
            .Range(0, Math.Max(ChannelCount, 0))
            .Select(_ => new Dictionary<int, int>())
            .ToArray();

        for (int patternIndex = 0; patternIndex < PatternCount; patternIndex++)
        {
            int rows = Math.Max(_module.GetPatternRowCount(patternIndex), 1);
            var pattern = new Pattern(rows, ChannelCount) { Name = _module.GetPatternName(patternIndex) };

            for (int row = 0; row < rows; row++)
            {
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    ModCommand command = _module.GetPatternCommand(patternIndex, row, channel);
                    if (IsEmpty(command))
                        continue;

                    if (command.Instrument > 0)
                        currentInstrumentByChannel[channel] = command.Instrument;

                    var note = ConvertNote(command, Format);
                    if (note.InstrumentIndex == 0
                        && IsPlayableImportedNote(note)
                        && currentInstrumentByChannel[channel] > 0)
                    {
                        note.InstrumentIndex = (byte)Math.Min(currentInstrumentByChannel[channel], 255);
                    }

                    if (IsPlayableImportedNote(note) && note.InstrumentIndex > 0)
                    {
                        var counts = channelInstrumentCounts[channel];
                        counts.TryGetValue(note.InstrumentIndex, out int count);
                        counts[note.InstrumentIndex] = count + 1;
                    }

                    pattern.SetNote(row, channel, note);
                }
            }

            song.Patterns.Add(pattern);
        }

        for (int channel = 0; channel < song.Tracks.Count && channel < channelInstrumentCounts.Length; channel++)
        {
            int preferredInstrument = GetDominantInstrumentIndex(channelInstrumentCounts[channel]);
            if (preferredInstrument > 0)
                song.Tracks[channel].InstrumentIndex = preferredInstrument - 1;
        }
    }

    private void AddOrderBlocks(Song song)
    {
        if (_module is null)
            return;

        double beatPos = 0;
        for (int order = 0; order < OrderCount; order++)
        {
            int patternIndex = _module.GetOrderPattern(order);
            if (patternIndex < 0 || patternIndex >= song.Patterns.Count)
                continue;

            song.OrderList.Add(patternIndex);
            double duration = Math.Max((double)song.Patterns[patternIndex].RowCount / song.RowsPerBeat, 1.0);
            foreach (var track in song.Tracks)
            {
                track.Blocks.Add(new PatternBlock
                {
                    PatternIndex = patternIndex,
                    StartBeat = beatPos,
                    DurationBeats = duration
                });
            }
            beatPos += duration;
        }
    }

    private double EstimateSecondsForOrderRow(int order, int row)
    {
        if (_module is null)
            return 0;

        int clampedOrder = Math.Clamp(order, 0, Math.Max(OrderCount - 1, 0));
        int rows = 0;
        for (int i = 0; i < clampedOrder; i++)
        {
            int pattern = _module.GetOrderPattern(i);
            rows += pattern >= 0 ? _module.GetPatternRowCount(pattern) : 0;
        }
        rows += Math.Max(row, 0);

        double tempo = CurrentTempo > 0 ? CurrentTempo : 125;
        double speed = CurrentSpeed > 0 ? CurrentSpeed : 6;
        return rows * speed * (2.5 / tempo);
    }

    private static bool IsEmpty(ModCommand command) =>
        command.Note == ManagedNote.None
        && command.Instrument == 0
        && command.VolCmd == VolCmd.None
        && command.Vol == 0
        && command.Command == ManagedCommand.None
        && command.Param == 0;

    private static amChipper.Core.Models.Note ConvertNote(ModCommand command, ModuleFormat format)
    {
        var note = new amChipper.Core.Models.Note
        {
            InstrumentIndex = command.Instrument,
            EffectParam = command.Param,
            Effect = MapEffect(command.Command),
            EffectColumn = (byte)Math.Clamp((int)command.Command, 0, 255)
        };

        if (ModCommand.IsNote(command.Note))
        {
            int pitch = format == ModuleFormat.XM
                ? (int)command.Note - 1
                : (int)command.Note + 11;
            note.Pitch = (byte)Math.Clamp(pitch, 0, 127);
        }
        else if (command.Note is ManagedNote.Off or ManagedNote.Stop)
            note.Pitch = (byte)SpecialNote.NoteOff;

        if (command.VolCmd == VolCmd.Volume)
        {
            note.Volume = (byte)Math.Clamp((int)command.Vol, 0, 64);
            note.VolumeColumn = (byte)(0x10 + note.Volume);
        }

        return note;
    }

    private static bool IsPlayableImportedNote(amChipper.Core.Models.Note note)
    {
        return note.Pitch > 0
            && note.Pitch != (byte)SpecialNote.NoteOff
            && note.Pitch != (byte)SpecialNote.NoteFade;
    }

    private static int GetDominantInstrumentIndex(Dictionary<int, int> counts)
    {
        if (counts.Count == 0)
            return 0;

        return counts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .First().Key;
    }

    private static EffectCommand MapEffect(ManagedCommand command) => command switch
    {
        ManagedCommand.PortamentoUp => EffectCommand.PortaUp,
        ManagedCommand.PortamentoDown => EffectCommand.PortaDown,
        ManagedCommand.TonePortamento => EffectCommand.TonePorta,
        ManagedCommand.Vibrato => EffectCommand.Vibrato,
        ManagedCommand.TonePortamentoVolSlide => EffectCommand.PortaVolSlide,
        ManagedCommand.VibratoVolSlide => EffectCommand.VolSlide,
        ManagedCommand.Tremolo => EffectCommand.Tremolo,
        ManagedCommand.Panning8Bit => EffectCommand.SetPan,
        ManagedCommand.Offset => EffectCommand.SampleOffset,
        ManagedCommand.VolumeSlide => EffectCommand.VolumeSlide,
        ManagedCommand.PositionJump => EffectCommand.PosJump,
        ManagedCommand.Volume => EffectCommand.SetVolume,
        ManagedCommand.PatternBreak => EffectCommand.PatternBreak,
        ManagedCommand.Speed => EffectCommand.SetSpeed,
        ManagedCommand.Tempo => EffectCommand.SetBpm,
        ManagedCommand.GlobalVolume => EffectCommand.SetGlobalVol,
        ManagedCommand.Retrig => EffectCommand.RetrigNote,
        _ => EffectCommand.None
    };

    private static ModuleFormat DetectFormat(string type, string extension)
    {
        string normalized = ModuleFormatCatalog.NormalizeExtension(extension);
        return normalized switch
        {
            ".mod" => ModuleFormat.MOD,
            ".xm" => ModuleFormat.XM,
            ".it" => ModuleFormat.IT,
            ".s3m" => ModuleFormat.S3M,
            _ => type.Trim().ToLowerInvariant() switch
            {
                "mod" => ModuleFormat.MOD,
                "xm" => ModuleFormat.XM,
                "it" => ModuleFormat.IT,
                "s3m" => ModuleFormat.S3M,
                _ => ModuleFormat.OpenMpt
            }
        };
    }
}
#endif
