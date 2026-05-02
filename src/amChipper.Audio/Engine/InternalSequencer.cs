using amChipper.Core.Interfaces;
using amChipper.Core.Models;

namespace amChipper.Audio.Engine;

/// <summary>
/// Lightweight internal sequencer for playing back an in-memory Song model.
/// Drives SampleVoice instances for each active note.
/// Used when working on a song natively in amChipper (not loading a module file).
/// </summary>
public sealed class InternalSequencer
{
    /// <summary>
    /// Stores or exposes _sampleRate.
    /// </summary>
    private readonly int _sampleRate;
    /// <summary>
    /// Stores or exposes _log.
    /// </summary>
    private readonly IAppLogger _log;
    /// <summary>
    /// Stores or exposes _song.
    /// </summary>
    private Song? _song;
    /// <summary>
    /// Stores or exposes _patternIndex.
    /// </summary>
    private int _patternIndex;
    /// <summary>
    /// Stores or exposes _orderIndex.
    /// </summary>
    private int _orderIndex;
    /// <summary>
    /// Stores or exposes _globalRow.
    /// </summary>
    private int _globalRow;
    /// <summary>
    /// Stores or exposes _useArrangement.
    /// </summary>
    private bool _useArrangement;
    /// <summary>
    /// Stores or exposes _scope.
    /// </summary>
    private PlaybackScope _scope = PlaybackScope.Song;
    /// <summary>
    /// Stores or exposes _channelFilter.
    /// </summary>
    private int? _channelFilter;
    /// <summary>
    /// Stores or exposes _previewChannel.
    /// </summary>
    private int _previewChannel = 10_000;
    /// <summary>
    /// Stores or exposes _lastLoggedArrangementRow.
    /// </summary>
    private int _lastLoggedArrangementRow = -1;
    /// <summary>
    /// Stores or exposes _lastLoggedPatternRow.
    /// </summary>
    private int _lastLoggedPatternRow = -1;
    /// <summary>
    /// Stores or exposes _pendingOrderJump.
    /// </summary>
    private int _pendingOrderJump = -1;
    /// <summary>
    /// Stores or exposes _pendingRowJump.
    /// </summary>
    private int _pendingRowJump = -1;
    /// <summary>
    /// Stores or exposes _pendingNotes.
    /// </summary>
    private readonly List<PendingNote> _pendingNotes = [];
    /// <summary>
    /// Stores or exposes byte.
    /// </summary>
    private Dictionary<EffectCommand, byte>[] _effectMemoryByTrack = [];

    // Playback state
    /// <summary>
    /// Stores or exposes _row.
    /// </summary>
    private int _row;
    /// <summary>
    /// Stores or exposes _tick.
    /// </summary>
    private int _tick;          // sub-row tick counter
    /// <summary>
    /// Stores or exposes _speed.
    /// </summary>
    private int _speed;         // ticks per row
    /// <summary>
    /// Stores or exposes _bpm.
    /// </summary>
    private int _bpm;
    /// <summary>
    /// Stores or exposes _runtimeGlobalVolume.
    /// </summary>
    private byte _runtimeGlobalVolume = 128;
    /// <summary>
    /// Stores or exposes _samplesPerTick.
    /// </summary>
    private double _samplesPerTick;
    /// <summary>
    /// Stores or exposes _sampleAccum.
    /// </summary>
    private double _sampleAccum;

    /// <summary>
    /// Stores or exposes _voices.
    /// </summary>
    private readonly List<ISequencerVoice> _voices = [];
    private readonly object _lock = new();
    /// <summary>
    /// Stores or exposes _trackMeters.
    /// </summary>
    private float[] _trackMeters = [];

    /// <summary>
    /// Stores or exposes IsPlaying.
    /// </summary>
    public bool IsPlaying { get; private set; }
    /// <summary>
    /// Executes the CurrentRow operation.
    /// </summary>
    public int CurrentRow { get { lock (_lock) return _row; } }
    /// <summary>
    /// Executes the CurrentPatternIndex operation.
    /// </summary>
    public int CurrentPatternIndex { get { lock (_lock) return _patternIndex; } }
    /// <summary>
    /// Executes the CurrentGlobalRow operation.
    /// </summary>
    public int CurrentGlobalRow { get { lock (_lock) return _globalRow; } }
    /// <summary>
    /// Stores or exposes CurrentBeat.
    /// </summary>
    public double CurrentBeat
    {
        get
        {
            lock (_lock)
                return _song is null ? 0 : _globalRow / (double)Math.Max(_song.RowsPerBeat, 1);
        }
    }

    /// <summary>
    /// Executes the EventHandler operation.
    /// </summary>
    public event EventHandler<(int row, int pattern, double beat)>? RowAdvanced;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<MeterLevelsEventArgs>? MeterLevelsChanged;

    public InternalSequencer(int sampleRate = 44100, IAppLogger? logger = null)
    {
        _sampleRate = sampleRate;
        _log = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// Executes the SetSong operation.
    /// </summary>
    public void SetSong(Song song)
    {
        lock (_lock)
        {
            _song = song;
            _bpm = Math.Clamp(song.Bpm, 32, 255);
            _speed = Math.Clamp(song.InitialSpeed <= 0 ? 6 : song.InitialSpeed, 1, 31);
            _runtimeGlobalVolume = song.GlobalVolume;
            _trackMeters = new float[Math.Max(song.Tracks.Count, 0)];
            _effectMemoryByTrack = Enumerable.Range(0, Math.Max(song.Tracks.Count, 0))
                .Select(_ => new Dictionary<EffectCommand, byte>())
                .ToArray();
            RecalcTiming();
            _log.Info(
                $"[Sequencer] SetSong title=\"{song.Title}\" bpm={song.Bpm} rpb={song.RowsPerBeat} speed={song.InitialSpeed} " +
                $"patterns={song.Patterns.Count} tracks={song.Tracks.Count} instruments={song.Instruments.Count} blocks={song.Tracks.Sum(t => t.Blocks.Count)}");
        }
    }

    /// <summary>
    /// Executes the Play operation.
    /// </summary>
    public void Play(int patternIndex = 0, int startRow = 0) =>
        Play(PlaybackScope.Song, patternIndex, startRow);

    /// <summary>
    /// Executes the Play operation.
    /// </summary>
    public void Play(PlaybackScope scope, int patternIndex = 0, int startRow = 0, int? channelFilter = null, double startBeat = 0)
    {
        lock (_lock)
        {
            if (_song is null)
                return;

            _scope = scope;
            _channelFilter = scope == PlaybackScope.PianoRoll ? channelFilter : null;
            _patternIndex = Math.Clamp(patternIndex, 0, Math.Max(_song.Patterns.Count - 1, 0));
            _orderIndex = ResolveOrderIndex(_patternIndex);
            _useArrangement = scope == PlaybackScope.Song && HasArrangement();
            _globalRow = scope == PlaybackScope.Song
                ? Math.Max(0, (int)Math.Round(startBeat * Math.Max(_song.RowsPerBeat, 1)))
                : Math.Max(0, startRow);
            UpdatePatternPositionFromGlobalRow();
            if (scope != PlaybackScope.Song)
                _row = Math.Clamp(startRow, 0, Math.Max(CurrentPattern()?.RowCount - 1 ?? 0, 0));
            _tick = 0;
            _sampleAccum = 0;
            _voices.Clear();
            Array.Clear(_trackMeters, 0, _trackMeters.Length);
            _runtimeGlobalVolume = _song.GlobalVolume;
            _pendingOrderJump = -1;
            _pendingRowJump = -1;
            IsPlaying = true;
            _lastLoggedArrangementRow = -1;
            _lastLoggedPatternRow = -1;
            _log.Info(
                $"[Sequencer] Play scope={scope} pattern={_patternIndex} row={_row} globalRow={_globalRow} " +
                $"startBeat={startBeat:0.###} channelFilter={_channelFilter?.ToString() ?? "all"} " +
                $"useArrangement={_useArrangement} arrangementEndRow={ArrangementEndRow()} bpm={_bpm} speed={_speed}");
            ProcessCurrentRow();
        }
    }

    /// <summary>
    /// Executes the SeekToBeat operation.
    /// </summary>
    public void SeekToBeat(double beat)
    {
        lock (_lock)
        {
            if (_song is null)
                return;

            int row = Math.Max(0, (int)Math.Round(beat * Math.Max(_song.RowsPerBeat, 1)));
            _globalRow = row;
            _row = row;
            UpdatePatternPositionFromGlobalRow();
            _tick = 0;
            _sampleAccum = 0;
            _voices.Clear();

            if (IsPlaying)
                ProcessCurrentRow();

            _log.Info($"[Sequencer] SeekToBeat beat={beat:0.###} globalRow={_globalRow} pattern={_patternIndex} row={_row} scope={_scope} useArrangement={_useArrangement}");
        }
    }

    /// <summary>
    /// Executes the Stop operation.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            IsPlaying = false;
            _voices.Clear();
            _log.Debug("Sequencer stopped.");
        }
    }

    /// <summary>
    /// Render stereo float audio. Called from the audio thread.
    /// Buffer is interleaved L/R float samples.
    /// </summary>
    public void Render(float[] buffer, int frameCount)
    {
        if (_song is null)
        {
            Array.Clear(buffer, 0, frameCount * 2);
            return;
        }

        lock (_lock)
        {
            Array.Clear(buffer, 0, frameCount * 2);

            int framesLeft = frameCount;
            int offset = 0;

            float masterPeak = 0f;
            while (framesLeft > 0)
            {
                if (!IsPlaying)
                {
                    masterPeak = Math.Max(masterPeak, MixVoices(buffer, offset, framesLeft));
                    break;
                }

                double framesUntilNextTick = _samplesPerTick - _sampleAccum;
                int chunk = Math.Min(framesLeft, (int)Math.Ceiling(framesUntilNextTick));

                // Mix active voices
                masterPeak = Math.Max(masterPeak, MixVoices(buffer, offset, chunk));
                DecayMeters(chunk);

                _sampleAccum += chunk;
                offset += chunk;
                framesLeft -= chunk;

                if (_sampleAccum >= _samplesPerTick)
                {
                    _sampleAccum -= _samplesPerTick;
                    AdvanceTick();
                }
            }

            var meterSnapshot = _trackMeters.Length > 0 ? (float[])_trackMeters.Clone() : [];
            MeterLevelsChanged?.Invoke(this, new MeterLevelsEventArgs(meterSnapshot, masterPeak));
        }
    }

    /// <summary>
    /// Executes the PreviewNote operation.
    /// </summary>
    public void PreviewNote(int pitch, int instrumentIndex, int channel, byte velocity = 110, int milliseconds = 450)
    {
        lock (_lock)
        {
            if (_song is null || _song.Instruments.Count == 0)
                return;

            int instIdx = Math.Clamp(instrumentIndex, 0, _song.Instruments.Count - 1);
            int previewChannel = _previewChannel + Math.Clamp(channel, 0, 256);
            _voices.RemoveAll(v => v.Channel == previewChannel);

            var note = new Note
            {
                Pitch = (byte)Math.Clamp(pitch, 0, 127),
                InstrumentIndex = (byte)(instIdx + 1),
                Velocity = velocity,
                Volume = (byte)Math.Clamp(velocity / 2, 0, 64)
            };

            AddVoice(note, previewChannel, instIdx, instIdx, milliseconds, 0);
            _log.Info($"[Sequencer] PreviewNote pitch={note.Pitch} instrument={instIdx}:{_song.Instruments[instIdx].Name} channel={channel} previewChannel={previewChannel} velocity={velocity} ms={milliseconds}");
        }
    }

    /// <summary>
    /// Executes the StopPreviewNote operation.
    /// </summary>
    public void StopPreviewNote(int channel)
    {
        lock (_lock)
        {
            int previewChannel = _previewChannel + Math.Clamp(channel, 0, 256);
            int removed = _voices.RemoveAll(v => v.Channel == previewChannel);
            _log.Debug($"[Sequencer] StopPreviewNote channel={channel} previewChannel={previewChannel} removed={removed}");
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the RecalcTiming operation.
    /// </summary>
    private void RecalcTiming()
    {
        // samples per tick = sampleRate * 60 / (bpm * 24)  ... standard tracker formula
        _samplesPerTick = _sampleRate * 2.5 / _bpm;
    }

    /// <summary>
    /// Executes the AdvanceTick operation.
    /// </summary>
    private void AdvanceTick()
    {
        int speed = Math.Clamp(_speed <= 0 ? 6 : _speed, 1, 31);
        for (int i = 0; i < _voices.Count; i++)
            _voices[i].TickAdvance(_tick + 1, speed);

        _tick++;
        ProcessPendingNotesForTick(_tick);
        if (_tick < speed) return;

        _tick = 0;
        AdvanceRow();
    }

    /// <summary>
    /// Executes the AdvanceRow operation.
    /// </summary>
    private void AdvanceRow()
    {
        if (_song is null) return;

        if (_pendingOrderJump >= 0)
        {
            ApplyPendingJump();
            ProcessCurrentRow();
            return;
        }

        _globalRow++;

        if (_useArrangement)
        {
            int endRow = ArrangementEndRow();
            if (endRow > 0 && _globalRow >= endRow)
                _globalRow = 0;
            ProcessArrangementRow();
            return;
        }

        _row++;
        var pat = CurrentPattern();
        if (pat is null)
            return;

        if (_row >= pat.RowCount)
        {
            if (_scope is PlaybackScope.Pattern or PlaybackScope.PianoRoll)
            {
                _row = 0;
                _globalRow = 0;
            }
            else
            {
                AdvanceOrder();
            }

            pat = CurrentPattern();
            if (pat is null) return;
        }

        ProcessCurrentRow();
    }

    /// <summary>
    /// Executes the ProcessCurrentRow operation.
    /// </summary>
    private void ProcessCurrentRow()
    {
        _pendingNotes.Clear();
        if (_useArrangement)
            ProcessArrangementRow();
        else
            ProcessPatternRow(_patternIndex, _row);
    }

    /// <summary>
    /// Executes the ProcessArrangementRow operation.
    /// </summary>
    private void ProcessArrangementRow()
    {
        if (_song is null) return;

        double beat = _globalRow / (double)Math.Max(_song.RowsPerBeat, 1);
        bool hasSolo = _song.Tracks.Any(t => t.Solo);
        int displayPattern = _patternIndex;
        int displayRow = 0;
        int activeBlocks = 0;

        for (int trackIndex = 0; trackIndex < _song.Tracks.Count; trackIndex++)
        {
            var track = _song.Tracks[trackIndex];
            if (track.Muted || (hasSolo && !track.Solo))
            {
                if (_globalRow != _lastLoggedArrangementRow)
                    _log.Debug($"[Sequencer] Track skipped track={trackIndex} muted={track.Muted} solo={track.Solo} hasSolo={hasSolo}");
                continue;
            }

            foreach (var block in track.Blocks)
            {
                if (block.Muted || block.PatternIndex < 0 || block.PatternIndex >= _song.Patterns.Count)
                {
                    if (_globalRow != _lastLoggedArrangementRow)
                        _log.Debug($"[Sequencer] Block skipped track={trackIndex} pattern={block.PatternIndex} muted={block.Muted}");
                    continue;
                }

                int blockStartRow = (int)Math.Round(block.StartBeat * _song.RowsPerBeat);
                int blockRows = Math.Max(1, (int)Math.Round(block.DurationBeats * _song.RowsPerBeat));
                int localRowRaw = _globalRow - blockStartRow;
                var pattern = _song.Patterns[block.PatternIndex];
                if (localRowRaw < 0 || localRowRaw >= blockRows || pattern.RowCount <= 0)
                    continue;

                activeBlocks++;
                int localRow = localRowRaw % pattern.RowCount;
                int channel = Math.Min(trackIndex, pattern.ChannelCount - 1);
                ProcessNote(pattern.GetNote(localRow, channel), channel: trackIndex, trackIndex: trackIndex, beat: beat,
                    block: block, blockVolume: block.Volume, blockPan: block.Panning);

                displayPattern = block.PatternIndex;
                displayRow = localRow;
            }
        }

        if (_globalRow != _lastLoggedArrangementRow)
        {
            _lastLoggedArrangementRow = _globalRow;
            _log.Debug($"[Sequencer] ArrangementRow globalRow={_globalRow} beat={beat:0.###} activeBlocks={activeBlocks} displayPattern={displayPattern} displayRow={displayRow}");
        }

        RowAdvanced?.Invoke(this, (displayRow, displayPattern, beat));
    }

    /// <summary>
    /// Executes the ProcessPatternRow operation.
    /// </summary>
    private void ProcessPatternRow(int patternIndex, int row)
    {
        var pat = PatternAt(patternIndex);
        if (pat is null || _song is null) return;

        double beat = _globalRow / (double)Math.Max(_song.RowsPerBeat, 1);

        int firstChannel = Math.Clamp(_channelFilter ?? 0, 0, Math.Max(pat.ChannelCount - 1, 0));
        int lastChannel = _channelFilter.HasValue ? firstChannel : pat.ChannelCount - 1;

        for (int ch = firstChannel; ch <= lastChannel; ch++)
        {
            ProcessNote(pat.GetNote(row, ch), ch, ch, beat);
        }
        if (row != _lastLoggedPatternRow)
        {
            _lastLoggedPatternRow = row;
            _log.Debug($"[Sequencer] PatternRow scope={_scope} pattern={patternIndex} row={row} beat={beat:0.###} channels={firstChannel}-{lastChannel}");
        }
        RowAdvanced?.Invoke(this, (row, patternIndex, beat));
    }

    /// <summary>
    /// Executes the ProcessNote operation.
    /// </summary>
    private void ProcessNote(Note note, int channel, int trackIndex, double beat, PatternBlock? block = null, byte? blockVolume = null, byte? blockPan = null)
    {
        if (_song is null) return;

        var playNote = note.Clone();
        playNote.EffectParam = ResolveEffectParam(trackIndex, playNote.Effect, playNote.EffectParam);

        if (_globalRow != _lastLoggedArrangementRow && _scope == PlaybackScope.Song)
        {
            _log.Debug(
                $"[Sequencer] Note event beat={beat:0.###} track={trackIndex} channel={channel} " +
                $"pitch={playNote.Pitch} inst={playNote.InstrumentIndex} vol={playNote.Volume} pan={playNote.Panning} " +
                $"eff={playNote.Effect} param={playNote.EffectParam:X2} block={(block is null ? "none" : $"{block.PatternIndex}@{block.StartBeat:0.###}")}");
        }

        ApplyRowEffect(playNote, trackIndex);

        if (playNote.Effect == EffectCommand.NoteDelay)
        {
            int delayTick = Math.Clamp(playNote.EffectParam & 0x0F, 0, Math.Max(_speed - 1, 0));
            if (delayTick > 0 && playNote.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
            {
                _pendingNotes.Add(new PendingNote(
                    delayTick,
                    playNote.Clone(),
                    channel,
                    trackIndex,
                    beat,
                    block,
                    blockVolume,
                    blockPan));
                _log.Debug(
                    $"[Sequencer] NoteDelay scheduled tick={delayTick} channel={channel} track={trackIndex} " +
                    $"pitch={playNote.Pitch} inst={playNote.InstrumentIndex} beat={beat:0.###}");
                return;
            }
        }

        if (playNote.Pitch == 0 && playNote.InstrumentIndex == 0)
            return;

        _voices.RemoveAll(v => v.Channel == channel);

        if (playNote.Pitch == (byte)SpecialNote.NoteOff ||
            playNote.Pitch == (byte)SpecialNote.NoteFade)
        {
            _log.Debug($"[Sequencer] NoteOff channel={channel} track={trackIndex} pitch={playNote.Pitch}");
            return;
        }

        int instIdx = ResolveInstrumentIndex(playNote, trackIndex);
        if (instIdx < 0 || instIdx >= _song.Instruments.Count)
        {
            _log.Warning($"[Sequencer] Note skipped: bad instrument pitch={playNote.Pitch} instrument={playNote.InstrumentIndex} resolved={instIdx} track={trackIndex} channel={channel}");
            return;
        }

        AddVoice(playNote, channel, instIdx, trackIndex, null, beat, block, blockVolume, blockPan);
    }

    /// <summary>
    /// Executes the AddVoice operation.
    /// </summary>
    private void AddVoice(Note note, int channel, int instIdx, int trackIndex, int? maxMilliseconds, double beat, PatternBlock? block = null, byte? blockVolume = null, byte? blockPan = null)
    {
        if (_song is null || instIdx < 0 || instIdx >= _song.Instruments.Count)
            return;

        var inst = _song.Instruments[instIdx];
        var track = trackIndex >= 0 && trackIndex < _song.Tracks.Count
            ? _song.Tracks[trackIndex]
            : null;

        float automationVol = ResolveAutomation(track, beat, AutomationTarget.Volume) / 128f;
        float automationPan = ResolveAutomation(track, beat, AutomationTarget.Pan) / 255f;
        double localBeat = block is null ? beat : Math.Max(0, beat - block.StartBeat);
        float blockAutomationVol = ResolveAutomation(block, localBeat, AutomationTarget.Volume) / 128f;
        float blockAutomationPan = ResolveAutomation(block, localBeat, AutomationTarget.Pan) / 255f;
        float clipVol = (blockVolume ?? 128) / 128f;
        float clipPan = (blockPan ?? 128) / 255f;

        float vol = (note.Volume < 255 ? note.Volume / 64f : note.Velocity / 127f)
                    * (inst.GlobalVolume / 128f)
                    * (_runtimeGlobalVolume / 128f)
                    * ((track?.Volume ?? 128) / 128f)
                    * automationVol
                    * clipVol
                    * blockAutomationVol;

        float pan = (note.Panning < 255 ? note.Panning : (track?.Panning ?? 128)) / 255f;
        pan = Math.Clamp((pan + clipPan) * 0.5f, 0f, 1f);
        pan = Math.Clamp((pan + blockAutomationPan) * 0.5f, 0f, 1f);
        if (track is not null)
            pan = Math.Clamp((pan + automationPan) * 0.5f, 0f, 1f);

        int? maxSamples = maxMilliseconds.HasValue
            ? Math.Max(1, maxMilliseconds.Value * _sampleRate / 1000)
            : null;

        Instrument playbackInstrument = inst;
        EffectCommand playbackEffect = note.Effect;
        byte playbackEffectParam = note.EffectParam;
        if (TryBuildSidTraceInstrument(inst, note, out var sidInstrument))
        {
            playbackInstrument = sidInstrument;
            playbackEffect = DecodeSidTracePlaybackEffect(note);
            playbackEffectParam = DecodeSidTracePlaybackParam(note);
        }

        if (playbackInstrument.SourceType == InstrumentSourceType.Sample)
        {
            int sampIdx = note.Pitch < 128 ? playbackInstrument.NoteMap[note.Pitch] : (byte)255;
            if (sampIdx < playbackInstrument.Samples.Count)
            {
                var sample = playbackInstrument.Samples[sampIdx];
                float sampleVolume = sample.RelativeVolume / 255f;
                float samplePan = note.Panning < 255
                    ? note.Panning / 255f
                    : sample.RelativePanning < 255
                        ? sample.RelativePanning / 255f
                        : pan;
                int sampleOffsetBytes = playbackEffect == EffectCommand.SampleOffset ? playbackEffectParam * 256 : 0;

                float finalVol = vol * sampleVolume;
                _voices.Add(new SampleVoice(channel, sample, note.Pitch, finalVol, samplePan, _sampleRate, playbackEffect, playbackEffectParam, sampleOffsetBytes, maxSamples));
                PulseTrackMeter(trackIndex, finalVol);
                _log.Debug(
                    $"[Sequencer] Voice sample channel={channel} track={trackIndex} pitch={note.Pitch} inst={instIdx}:{playbackInstrument.Name} " +
                    $"sample={sampIdx}:{sample.Name} vol={finalVol:0.###} pan={samplePan:0.###} clipVol={clipVol:0.###} clipPan={clipPan:0.###} maxMs={maxMilliseconds?.ToString() ?? "none"}");
                return;
            }

            _log.Warning($"[Sequencer] Sample voice fallback to synth: pitch={note.Pitch} inst={instIdx}:{playbackInstrument.Name} mappedSample={sampIdx} sampleCount={playbackInstrument.Samples.Count}");
        }

        _voices.Add(new SynthVoice(channel, playbackInstrument, note.Pitch, vol, pan, _sampleRate, playbackEffect, playbackEffectParam, maxSamples));
        PulseTrackMeter(trackIndex, vol);
        _log.Debug(
            $"[Sequencer] Voice synth channel={channel} track={trackIndex} pitch={note.Pitch} inst={instIdx}:{playbackInstrument.Name} " +
            $"wave={playbackInstrument.Waveform} vol={vol:0.###} pan={pan:0.###} clipVol={clipVol:0.###} clipPan={clipPan:0.###} " +
            $"effect={playbackEffect} param={playbackEffectParam:X2} maxMs={maxMilliseconds?.ToString() ?? "none"}");
    }

    /// <summary>
    /// Executes the TryBuildSidTraceInstrument operation.
    /// </summary>
    private static bool TryBuildSidTraceInstrument(Instrument source, Note note, out Instrument instrument)
    {
        instrument = source;
        if (!IsSidTraceCell(note))
            return false;

        instrument = source.Clone();
        instrument.SourceType = InstrumentSourceType.Synth;
        instrument.Waveform = DecodeSidTraceWaveform(note.VolumeColumn);
        instrument.PulseWidth = DecodeSidTracePulseWidth(note.EffectColumn);
        instrument.AttackMs = 0;
        instrument.ReleaseMs = 8;
        return true;
    }

    /// <summary>
    /// Executes the IsSidTraceCell operation.
    /// </summary>
    private static bool IsSidTraceCell(Note note) =>
        note.InstrumentIndex is >= 1 and <= 3 &&
        note.VolumeColumn is 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80;

    /// <summary>
    /// Executes the DecodeSidTraceWaveform operation.
    /// </summary>
    private static SynthWaveform DecodeSidTraceWaveform(byte waveform)
    {
        byte wave = (byte)(waveform & 0xF0);
        if ((wave & 0x80) != 0)
            return SynthWaveform.Noise;
        if ((wave & 0x40) != 0)
            return SynthWaveform.Square;
        if ((wave & 0x10) != 0)
            return SynthWaveform.Saw;
        if ((wave & 0x20) != 0)
            return SynthWaveform.Triangle;

        return SynthWaveform.Square;
    }

    /// <summary>
    /// Executes the DecodeSidTracePulseWidth operation.
    /// </summary>
    private static double DecodeSidTracePulseWidth(byte pulseWidthHigh) =>
        Math.Clamp(pulseWidthHigh / 255.0, 0.05, 0.95);

    /// <summary>
    /// Executes the DecodeSidTracePlaybackEffect operation.
    /// </summary>
    private static EffectCommand DecodeSidTracePlaybackEffect(Note note)
    {
        byte control = note.EffectParam;
        if ((control & 0x04) != 0)
            return EffectCommand.Vibrato;

        return EffectCommand.None;
    }

    /// <summary>
    /// Executes the DecodeSidTracePlaybackParam operation.
    /// </summary>
    private static byte DecodeSidTracePlaybackParam(Note note)
    {
        byte control = note.EffectParam;
        if ((control & 0x04) == 0)
            return 0;

        int speed = ((control >> 1) & 0x07) + 1;
        int depth = ((note.EffectColumn ^ control) & 0x03) + 1;
        return (byte)((speed << 4) | depth);
    }

    /// <summary>
    /// Executes the MixVoices operation.
    /// </summary>
    private float MixVoices(float[] buffer, int offset, int frames)
    {
        foreach (var voice in _voices)
            voice.Render(buffer, offset, frames);

        // Remove finished voices
        _voices.RemoveAll(v => v.IsFinished);

        int end = Math.Min(buffer.Length, (offset + frames) * 2);
        for (int i = offset * 2; i < end; i++)
            buffer[i] = Math.Clamp(buffer[i], -1f, 1f);

        float peak = 0f;
        for (int i = offset * 2; i < end; i++)
        {
            float abs = Math.Abs(buffer[i]);
            if (abs > peak)
                peak = abs;
        }

        return peak;
    }

    /// <summary>
    /// Executes the PulseTrackMeter operation.
    /// </summary>
    private void PulseTrackMeter(int trackIndex, float amount)
    {
        if (trackIndex < 0 || trackIndex >= _trackMeters.Length)
            return;

        _trackMeters[trackIndex] = Math.Max(_trackMeters[trackIndex], Math.Clamp(amount, 0f, 1f));
    }

    /// <summary>
    /// Executes the DecayMeters operation.
    /// </summary>
    private void DecayMeters(int frames)
    {
        if (_trackMeters.Length == 0)
            return;

        float decay = MathF.Exp(-frames / (_sampleRate * 0.08f));
        for (int i = 0; i < _trackMeters.Length; i++)
            _trackMeters[i] = Math.Clamp(_trackMeters[i] * decay, 0f, 1f);
    }

    /// <summary>
    /// Executes the ResolveAutomation operation.
    /// </summary>
    private byte ResolveAutomation(Track? track, double beat, AutomationTarget target)
    {
        if (_song is null || track is null)
            return target == AutomationTarget.Pan ? (byte)128 : (byte)128;

        var points = target == AutomationTarget.Volume ? track.VolumeAutomation : track.PanAutomation;
        if (points is null || points.Count == 0)
            return target == AutomationTarget.Pan ? (byte)128 : (byte)128;

        var ordered = points.OrderBy(p => p.Beat).ToList();
        if (beat <= ordered[0].Beat)
            return ordered[0].Value;

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            if (beat < a.Beat || beat > b.Beat)
                continue;

            double t = (beat - a.Beat) / Math.Max(b.Beat - a.Beat, 0.0001);
            return (byte)Math.Clamp(a.Value + (b.Value - a.Value) * t, 0, 255);
        }

        return ordered[^1].Value;
    }

    /// <summary>
    /// Executes the ResolveAutomation operation.
    /// </summary>
    private byte ResolveAutomation(PatternBlock? block, double beat, AutomationTarget target)
    {
        if (block is null)
            return target == AutomationTarget.Pan ? (byte)128 : (byte)128;

        var points = target == AutomationTarget.Volume ? block.VolumeAutomation : block.PanAutomation;
        if (points is null || points.Count == 0)
            return target == AutomationTarget.Pan ? (byte)128 : (byte)128;

        var ordered = points.OrderBy(p => p.Beat).ToList();
        if (beat <= ordered[0].Beat)
            return ordered[0].Value;

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            if (beat < a.Beat || beat > b.Beat)
                continue;

            double t = (beat - a.Beat) / Math.Max(b.Beat - a.Beat, 0.0001);
            return (byte)Math.Clamp(a.Value + (b.Value - a.Value) * t, 0, target == AutomationTarget.Volume ? 128 : 255);
        }

        return ordered[^1].Value;
    }

    /// <summary>
    /// Executes the CurrentPattern operation.
    /// </summary>
    private Pattern? CurrentPattern() =>
        _song is not null && _patternIndex >= 0 && _patternIndex < _song.Patterns.Count
            ? _song.Patterns[_patternIndex]
            : null;

    /// <summary>
    /// Executes the PatternAt operation.
    /// </summary>
    private Pattern? PatternAt(int index) =>
        _song is not null && index >= 0 && index < _song.Patterns.Count
            ? _song.Patterns[index]
            : null;

    /// <summary>
    /// Executes the ResolveOrderIndex operation.
    /// </summary>
    private int ResolveOrderIndex(int patternIndex)
    {
        if (_song is null || _song.OrderList.Count == 0)
            return 0;

        int order = _song.OrderList.IndexOf(patternIndex);
        return order >= 0 ? order : 0;
    }

    /// <summary>
    /// Executes the ResolveStartGlobalRow operation.
    /// </summary>
    private int ResolveStartGlobalRow(int patternIndex, int startRow)
    {
        if (_song is null) return startRow;

        foreach (var block in _song.Tracks.SelectMany(t => t.Blocks))
        {
            if (block.PatternIndex == patternIndex)
                return (int)Math.Round(block.StartBeat * _song.RowsPerBeat) + startRow;
        }

        return startRow;
    }

    /// <summary>
    /// Executes the HasArrangement operation.
    /// </summary>
    private bool HasArrangement() =>
        _song is not null && _song.Tracks.Any(t => t.Blocks.Count > 0);

    /// <summary>
    /// Executes the ArrangementEndRow operation.
    /// </summary>
    private int ArrangementEndRow()
    {
        if (_song is null) return 0;

        return _song.Tracks
            .SelectMany(t => t.Blocks)
            .Where(b => b.PatternIndex >= 0 && b.PatternIndex < _song.Patterns.Count)
            .Select(b => (int)Math.Ceiling((b.StartBeat + b.DurationBeats) * _song.RowsPerBeat))
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    /// Executes the UpdatePatternPositionFromGlobalRow operation.
    /// </summary>
    private void UpdatePatternPositionFromGlobalRow()
    {
        if (_song is null)
            return;

        if (_useArrangement)
        {
            foreach (var block in _song.Tracks
                         .SelectMany(t => t.Blocks)
                         .OrderBy(b => b.StartBeat))
            {
                if (block.PatternIndex < 0 || block.PatternIndex >= _song.Patterns.Count)
                    continue;

                int startRow = (int)Math.Round(block.StartBeat * _song.RowsPerBeat);
                int blockRows = Math.Max(1, (int)Math.Round(block.DurationBeats * _song.RowsPerBeat));
                int localRaw = _globalRow - startRow;
                if (localRaw < 0 || localRaw >= blockRows)
                    continue;

                _patternIndex = block.PatternIndex;
                var pattern = _song.Patterns[_patternIndex];
                _row = pattern.RowCount > 0 ? localRaw % pattern.RowCount : 0;
                _orderIndex = ResolveOrderIndex(_patternIndex);
                return;
            }

            _row = 0;
            return;
        }

        if (_scope == PlaybackScope.Song && _song.OrderList.Count > 0)
        {
            int remaining = _globalRow;
            for (int order = 0; order < _song.OrderList.Count; order++)
            {
                int patIdx = _song.OrderList[order];
                var pattern = PatternAt(patIdx);
                if (pattern is null) continue;

                if (remaining < pattern.RowCount)
                {
                    _orderIndex = order;
                    _patternIndex = patIdx;
                    _row = remaining;
                    return;
                }

                remaining -= pattern.RowCount;
            }
        }

        var current = CurrentPattern();
        if (current is not null && current.RowCount > 0)
            _row = Math.Clamp(_globalRow, 0, current.RowCount - 1);
    }

    /// <summary>
    /// Executes the AdvanceOrder operation.
    /// </summary>
    private void AdvanceOrder()
    {
        if (_song is null || _song.OrderList.Count == 0)
        {
            _row = 0;
            return;
        }

        _orderIndex++;
        if (_orderIndex >= _song.OrderList.Count)
            _orderIndex = 0;

        _patternIndex = _song.OrderList[_orderIndex];
        _row = 0;
    }

    /// <summary>
    /// Executes the ResolveInstrumentIndex operation.
    /// </summary>
    private int ResolveInstrumentIndex(Note note, int trackIndex)
    {
        if (_song is null) return -1;

        int instIdx = note.InstrumentIndex > 0 ? note.InstrumentIndex - 1 : -1;
        if (instIdx >= 0 && instIdx < _song.Instruments.Count)
            return instIdx;

        if (trackIndex >= 0 && trackIndex < _song.Tracks.Count)
            return Math.Clamp(_song.Tracks[trackIndex].InstrumentIndex, 0, Math.Max(_song.Instruments.Count - 1, 0));

        return _song.Instruments.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Executes the ResolveEffectParam operation.
    /// </summary>
    private byte ResolveEffectParam(int trackIndex, EffectCommand effect, byte param)
    {
        if (trackIndex < 0 || trackIndex >= _effectMemoryByTrack.Length)
            return param;

        var memory = _effectMemoryByTrack[trackIndex];
        bool usesMemory = effect is EffectCommand.PortaUp or EffectCommand.PortaDown or EffectCommand.TonePorta
            or EffectCommand.Vibrato or EffectCommand.VolSlide or EffectCommand.PortaVolSlide
            or EffectCommand.Tremolo or EffectCommand.VolumeSlide or EffectCommand.SampleOffset;

        if (!usesMemory)
            return param;

        if (param != 0)
        {
            memory[effect] = param;
            return param;
        }

        return memory.TryGetValue(effect, out byte remembered) ? remembered : param;
    }

    /// <summary>
    /// Executes the ApplyRowEffect operation.
    /// </summary>
    private void ApplyRowEffect(Note note, int trackIndex)
    {
        switch (note.Effect)
        {
            case EffectCommand.None when note.EffectParam != 0:
                return;
            case EffectCommand.SetSpeed:
                if (note.EffectParam == 0)
                    return;

                if (note.EffectParam <= 0x20)
                    _speed = Math.Clamp((int)note.EffectParam, 1, 31);
                else
                    _bpm = Math.Clamp((int)note.EffectParam, 32, 255);

                RecalcTiming();
                _log.Info($"[Sequencer] Speed/BPM effect param={note.EffectParam:X2} bpm={_bpm} speed={_speed}");
                return;
            case EffectCommand.SetBpm:
                _bpm = Math.Clamp((int)note.EffectParam, 32, 255);
                RecalcTiming();
                _log.Info($"[Sequencer] SetBpm param={note.EffectParam:X2} bpm={_bpm}");
                return;
            case EffectCommand.SetGlobalVol:
                _runtimeGlobalVolume = (byte)Math.Clamp((int)note.EffectParam, 0, 128);
                _log.Info($"[Sequencer] SetGlobalVol param={note.EffectParam:X2} globalVol={_runtimeGlobalVolume}");
                return;
            case EffectCommand.SetVolume:
                note.Volume = (byte)Math.Clamp((int)note.EffectParam, 0, 64);
                return;
            case EffectCommand.SetPan:
                note.Panning = (byte)Math.Clamp((int)note.EffectParam, 0, 255);
                return;
            case EffectCommand.VolumeSlide:
            case EffectCommand.VolSlide:
            case EffectCommand.PortaVolSlide:
                return;
            case EffectCommand.PortaUp:
            case EffectCommand.PortaDown:
            case EffectCommand.TonePorta:
            case EffectCommand.Vibrato:
            case EffectCommand.Tremolo:
            case EffectCommand.FinePortaUp:
            case EffectCommand.FinePortaDown:
            case EffectCommand.SampleOffset:
            case EffectCommand.NoteCut:
            case EffectCommand.NoteDelay:
            case EffectCommand.RetrigNote:
                _log.Debug($"[Sequencer] Effect preserved but not fully emulated effect={note.Effect} param={note.EffectParam:X2} track={trackIndex}");
                return;
            case EffectCommand.PosJump:
                if (_scope == PlaybackScope.Song && !_useArrangement)
                {
                    _pendingOrderJump = note.EffectParam;
                    _pendingRowJump = 0;
                    _log.Info($"[Sequencer] PosJump queued order={_pendingOrderJump}");
                }
                else
                {
                    _log.Debug($"[Sequencer] PosJump ignored scope={_scope} arrangement={_useArrangement} param={note.EffectParam:X2}");
                }
                return;
            case EffectCommand.PatternBreak:
                if (_scope == PlaybackScope.Song && !_useArrangement)
                {
                    var song = _song;
                    _pendingOrderJump = song is null
                        ? 0
                        : Math.Min(_orderIndex + 1, Math.Max(song.OrderList.Count - 1, 0));
                    _pendingRowJump = BcdToRow(note.EffectParam);
                    _log.Info($"[Sequencer] PatternBreak queued order={_pendingOrderJump} row={_pendingRowJump}");
                }
                else
                {
                    _log.Debug($"[Sequencer] PatternBreak ignored scope={_scope} arrangement={_useArrangement} param={note.EffectParam:X2}");
                }
                return;
        }
    }

    /// <summary>
    /// Executes the ApplyPendingJump operation.
    /// </summary>
    private void ApplyPendingJump()
    {
        if (_song is null)
            return;

        if (_pendingOrderJump < 0)
            return;

        if (_song.OrderList.Count == 0)
        {
            _pendingOrderJump = -1;
            _pendingRowJump = -1;
            return;
        }

        int order = Math.Clamp(_pendingOrderJump, 0, _song.OrderList.Count - 1);
        _orderIndex = order;
        _patternIndex = _song.OrderList[order];
        _row = Math.Clamp(_pendingRowJump, 0, Math.Max(CurrentPattern()?.RowCount - 1 ?? 0, 0));
        _globalRow = ResolveGlobalRowForOrderRow(order, _row);
        _pendingOrderJump = -1;
        _pendingRowJump = -1;
        _log.Info($"[Sequencer] Applied jump order={_orderIndex} pattern={_patternIndex} row={_row} globalRow={_globalRow}");
    }

    /// <summary>
    /// Executes the ResolveGlobalRowForOrderRow operation.
    /// </summary>
    private int ResolveGlobalRowForOrderRow(int orderIndex, int row)
    {
        if (_song is null || _song.OrderList.Count == 0)
            return row;

        int globalRow = 0;
        for (int i = 0; i < orderIndex && i < _song.OrderList.Count; i++)
        {
            var pat = PatternAt(_song.OrderList[i]);
            if (pat is not null)
                globalRow += pat.RowCount;
        }

        return globalRow + Math.Max(0, row);
    }

    /// <summary>
    /// Executes the BcdToRow operation.
    /// </summary>
    private static int BcdToRow(byte param) => ((param >> 4) & 0x0F) * 10 + (param & 0x0F);

    /// <summary>
    /// Executes the ProcessPendingNotesForTick operation.
    /// </summary>
    private void ProcessPendingNotesForTick(int tickInRow)
    {
        if (_pendingNotes.Count == 0)
            return;

        for (int i = _pendingNotes.Count - 1; i >= 0; i--)
        {
            var pending = _pendingNotes[i];
            if (pending.TriggerTick != tickInRow)
                continue;

            _pendingNotes.RemoveAt(i);
            var note = pending.Note.Clone();
            note.Effect = EffectCommand.None;
            note.EffectParam = 0;
            _log.Debug(
                $"[Sequencer] NoteDelay fire tick={tickInRow} channel={pending.Channel} track={pending.TrackIndex} " +
                $"pitch={note.Pitch} inst={note.InstrumentIndex} beat={pending.Beat:0.###}");
            ProcessNote(note, pending.Channel, pending.TrackIndex, pending.Beat, pending.Block, pending.BlockVolume, pending.BlockPan);
        }
    }
}

/// <summary>
/// Represents the MeterLevelsEventArgs component.
/// </summary>
public sealed class MeterLevelsEventArgs(float[] trackLevels, float masterLevel) : EventArgs
{
    /// <summary>
    /// Stores or exposes TrackLevels.
    /// </summary>
    public float[] TrackLevels { get; } = trackLevels;
    /// <summary>
    /// Executes the MasterLevel operation.
    /// </summary>
    public float MasterLevel { get; } = Math.Clamp(masterLevel, 0f, 1f);
}

/// <summary>
/// Carries PendingNote data.
/// </summary>
internal sealed record PendingNote(
    int TriggerTick,
    Note Note,
    int Channel,
    int TrackIndex,
    double Beat,
    PatternBlock? Block,
    byte? BlockVolume,
    byte? BlockPan);

/// <summary>
/// Defines the ISequencerVoice contract.
/// </summary>
internal interface ISequencerVoice
{
    int Channel { get; }
    bool IsFinished { get; }
    void TickAdvance(int tickInRow, int speed);
    void Render(float[] buffer, int offset, int frames);
}

/// <summary>A single playing sample voice on one sequencer channel.</summary>
internal sealed class SampleVoice : ISequencerVoice
{
    /// <summary>
    /// Stores or exposes Channel.
    /// </summary>
    public int Channel { get; }
    /// <summary>
    /// Stores or exposes IsFinished.
    /// </summary>
    public bool IsFinished => _maxSamples.HasValue && _ageSamples >= _maxSamples.Value ||
        _pos >= _sample.FrameCount && (!_sample.Looped || _sample.LoopEnd <= _sample.LoopStart);

    /// <summary>
    /// Stores or exposes _sample.
    /// </summary>
    private readonly Sample _sample;
    /// <summary>
    /// Stores or exposes _basePitch.
    /// </summary>
    private readonly int _basePitch;
    /// <summary>
    /// Stores or exposes _outputRate.
    /// </summary>
    private readonly int _outputRate;
    /// <summary>
    /// Stores or exposes _pitchRatio.
    /// </summary>
    private double _pitchRatio;
    /// <summary>
    /// Stores or exposes _pos.
    /// </summary>
    private double _pos;
    /// <summary>
    /// Stores or exposes _baseVolume.
    /// </summary>
    private readonly float _baseVolume;
    /// <summary>
    /// Stores or exposes _volume.
    /// </summary>
    private float _volume;
    /// <summary>
    /// Stores or exposes _leftGain.
    /// </summary>
    private readonly float _leftGain;
    /// <summary>
    /// Stores or exposes _rightGain.
    /// </summary>
    private readonly float _rightGain;
    /// <summary>
    /// Stores or exposes _effect.
    /// </summary>
    private readonly EffectCommand _effect;
    /// <summary>
    /// Stores or exposes _effectParam.
    /// </summary>
    private readonly byte _effectParam;
    /// <summary>
    /// Stores or exposes _maxSamples.
    /// </summary>
    private readonly int? _maxSamples;
    /// <summary>
    /// Stores or exposes _ageSamples.
    /// </summary>
    private int _ageSamples;
    /// <summary>
    /// Stores or exposes _tickInRow.
    /// </summary>
    private int _tickInRow;
    /// <summary>
    /// Stores or exposes _pitchOffsetSemis.
    /// </summary>
    private double _pitchOffsetSemis;
    /// <summary>
    /// Stores or exposes _vibratoPhase.
    /// </summary>
    private double _vibratoPhase;
    public SampleVoice(int channel, Sample sample, int midiPitch, float volume, float pan, int outputRate, EffectCommand effect, byte effectParam, int startOffsetBytes = 0, int? maxSamples = null)
    {
        Channel = channel;
        _sample = sample;
        _basePitch = midiPitch;
        _outputRate = outputRate;
        _baseVolume = volume;
        _volume = volume;
        _pos = 0;
        _maxSamples = maxSamples;
        _leftGain = MathF.Sqrt(1f - Math.Clamp(pan, 0f, 1f));
        _rightGain = MathF.Sqrt(Math.Clamp(pan, 0f, 1f));
        _effect = effect;
        _effectParam = effectParam;

        // Pitch ratio: transpose from base note to target note
        SetPitch(midiPitch, outputRate);

        int bytesPerSample = sample.BitsPerSample == 16 ? 2 : 1;
        int sampleChannels = Math.Max(sample.Channels, 1);
        int bytesPerFrame = Math.Max(1, bytesPerSample * sampleChannels);
        if (startOffsetBytes > 0)
            _pos = Math.Clamp(startOffsetBytes / (double)bytesPerFrame, 0, Math.Max(sample.FrameCount - 1, 0));
    }

    /// <summary>
    /// Executes the TickAdvance operation.
    /// </summary>
    public void TickAdvance(int tickInRow, int speed)
    {
        _tickInRow = tickInRow;

        if (_effect == EffectCommand.None && _effectParam != 0)
        {
            int high = (_effectParam >> 4) & 0x0F;
            int low = _effectParam & 0x0F;
            int phase = Math.Max(0, tickInRow);
            while (phase >= 3)
                phase -= 3;
            int offset = phase switch
            {
                1 => high,
                2 => low,
                _ => 0
            };
            SetPitch(_basePitch + offset + _pitchOffsetSemis, _outputRate);
            return;
        }

        if (_effect is EffectCommand.PortaUp or EffectCommand.FinePortaUp)
        {
            double step = (_effect == EffectCommand.FinePortaUp ? 0.25 : 1.0) * Math.Max(1, (int)_effectParam) / 16.0;
            _pitchOffsetSemis += step;
            SetPitch(_basePitch + _pitchOffsetSemis, _outputRate);
        }
        else if (_effect is EffectCommand.PortaDown or EffectCommand.FinePortaDown)
        {
            double step = (_effect == EffectCommand.FinePortaDown ? 0.25 : 1.0) * Math.Max(1, (int)_effectParam) / 16.0;
            _pitchOffsetSemis -= step;
            SetPitch(_basePitch + _pitchOffsetSemis, _outputRate);
        }

        if (_effect is EffectCommand.Vibrato or EffectCommand.PortaVolSlide)
        {
            int speedStep = Math.Max(1, (_effectParam >> 4) & 0x0F);
            int depth = Math.Max(1, _effectParam & 0x0F);
            _vibratoPhase += speedStep * 0.35;
            double vib = Math.Sin(_vibratoPhase) * depth / 32.0;
            SetPitch(_basePitch + _pitchOffsetSemis + vib, _outputRate);
        }

        if (_effect is EffectCommand.VolumeSlide or EffectCommand.VolSlide or EffectCommand.PortaVolSlide)
        {
            int up = (_effectParam >> 4) & 0x0F;
            int down = _effectParam & 0x0F;
            float next = _volume;
            if (up > 0 && down == 0)
                next += up / 64f;
            else if (down > 0 && up == 0)
                next -= down / 64f;

            _volume = Math.Clamp(next, 0f, 1f);
        }

        if (_effect == EffectCommand.NoteCut && _tickInRow >= _effectParam)
            _volume = 0f;

        if (_effect == EffectCommand.RetrigNote)
        {
            int retrig = Math.Max(1, _effectParam & 0x0F);
            if (_tickInRow > 0 && _tickInRow % retrig == 0)
            {
                _pos = 0;
                _ageSamples = 0;
            }
        }
    }

    /// <summary>
    /// Executes the SetPitch operation.
    /// </summary>
    private void SetPitch(double midiPitch, int outputRate)
    {
        double semitones = midiPitch - _sample.BaseNote + _sample.FineTune / 100.0;
        double freq = Math.Max(1, _sample.SampleRate) * Math.Pow(2.0, semitones / 12.0);
        _pitchRatio = freq / Math.Max(1, outputRate);
    }

    /// <summary>
    /// Executes the Render operation.
    /// </summary>
    public void Render(float[] buffer, int offset, int frames)
    {
        if (IsFinished) return;

        int totalFrames = _sample.FrameCount;
        bool is16Bit = _sample.BitsPerSample == 16;
        int sampleChannels = Math.Max(_sample.Channels, 1);
        int bytesPerSample = is16Bit ? 2 : 1;

        for (int f = 0; f < frames; f++)
        {
            if (_pos >= totalFrames)
            {
                if (_sample.Looped && _sample.LoopEnd > _sample.LoopStart)
                    _pos = _sample.LoopStart + (_pos - _sample.LoopEnd) % (_sample.LoopEnd - _sample.LoopStart);
                else
                    break;
            }

            int ipos = (int)_pos;
            int baseOffset = ipos * sampleChannels * bytesPerSample;
            if (baseOffset < 0 || baseOffset + bytesPerSample > _sample.Data.Length)
                break;

            float left = ReadSample(baseOffset, is16Bit);
            float right = sampleChannels > 1 && baseOffset + bytesPerSample * 2 <= _sample.Data.Length
                ? ReadSample(baseOffset + bytesPerSample, is16Bit)
                : left;

            left *= _volume * _leftGain;
            right *= _volume * _rightGain;

            int bufIdx = (offset + f) * 2;
            if (bufIdx + 1 < buffer.Length)
            {
                buffer[bufIdx] += left;
                buffer[bufIdx + 1] += right;
            }

            _pos += _pitchRatio;
            _ageSamples++;
        }
    }

    /// <summary>
    /// Executes the ReadSample operation.
    /// </summary>
    private float ReadSample(int offset, bool is16Bit) =>
        is16Bit
            ? BitConverter.ToInt16(_sample.Data, offset) / 32768f
            : (_sample.Data[offset] - 128) / 128f;
}

/// <summary>
/// Represents the SynthVoice component.
/// </summary>
internal sealed class SynthVoice : ISequencerVoice
{
    /// <summary>
    /// Stores or exposes Channel.
    /// </summary>
    public int Channel { get; }
    /// <summary>
    /// Stores or exposes IsFinished.
    /// </summary>
    public bool IsFinished => _maxSamples.HasValue && _ageSamples >= _maxSamples.Value;

    /// <summary>
    /// Stores or exposes _waveform.
    /// </summary>
    private readonly SynthWaveform _waveform;
    /// <summary>
    /// Stores or exposes _sampleRate.
    /// </summary>
    private readonly double _sampleRate;
    /// <summary>
    /// Stores or exposes _frequency.
    /// </summary>
    private double _frequency;
    /// <summary>
    /// Stores or exposes _phaseStep.
    /// </summary>
    private double _phaseStep;
    /// <summary>
    /// Stores or exposes _pulseWidth.
    /// </summary>
    private readonly double _pulseWidth;
    /// <summary>
    /// Stores or exposes _baseVolume.
    /// </summary>
    private readonly float _baseVolume;
    /// <summary>
    /// Stores or exposes _volume.
    /// </summary>
    private float _volume;
    /// <summary>
    /// Stores or exposes _leftGain.
    /// </summary>
    private readonly float _leftGain;
    /// <summary>
    /// Stores or exposes _rightGain.
    /// </summary>
    private readonly float _rightGain;
    /// <summary>
    /// Stores or exposes _attackSamples.
    /// </summary>
    private readonly int _attackSamples;
    /// <summary>
    /// Stores or exposes _effect.
    /// </summary>
    private readonly EffectCommand _effect;
    /// <summary>
    /// Stores or exposes _effectParam.
    /// </summary>
    private readonly byte _effectParam;
    /// <summary>
    /// Stores or exposes _maxSamples.
    /// </summary>
    private readonly int? _maxSamples;
    /// <summary>
    /// Stores or exposes _phase.
    /// </summary>
    private double _phase;
    /// <summary>
    /// Stores or exposes _ageSamples.
    /// </summary>
    private long _ageSamples;
    /// <summary>
    /// Stores or exposes _noise.
    /// </summary>
    private uint _noise = 0x12345678;
    /// <summary>
    /// Stores or exposes _basePitch.
    /// </summary>
    private int _basePitch;
    /// <summary>
    /// Stores or exposes _pitchOffsetSemis.
    /// </summary>
    private double _pitchOffsetSemis;
    /// <summary>
    /// Stores or exposes _vibratoPhase.
    /// </summary>
    private double _vibratoPhase;
    /// <summary>
    /// Stores native instrument fine tune in semitones.
    /// </summary>
    private readonly double _fineTuneSemis;
    /// <summary>
    /// Stores native instrument LFO amount in semitones.
    /// </summary>
    private readonly double _instrumentLfoAmount;
    /// <summary>
    /// Stores native instrument LFO speed in Hz.
    /// </summary>
    private readonly double _instrumentLfoSpeed;

    public SynthVoice(int channel, Instrument instrument, int midiPitch, float volume, float pan, int sampleRate, EffectCommand effect, byte effectParam, int? maxSamples = null)
    {
        Channel = channel;
        _waveform = instrument.Waveform;
        _sampleRate = sampleRate;
        _fineTuneSemis = instrument.FineTuneCents / 100.0;
        _instrumentLfoAmount = Math.Clamp(instrument.LfoAmount, 0, 24);
        _instrumentLfoSpeed = Math.Clamp(instrument.LfoSpeedHz, 0, 64);
        _basePitch = midiPitch;
        SetPitch(midiPitch + _fineTuneSemis);
        _pulseWidth = Math.Clamp(instrument.PulseWidth, 0.05, 0.95);
        _baseVolume = volume * 0.35f;
        _volume = _baseVolume;
        _leftGain = MathF.Sqrt(1f - Math.Clamp(pan, 0f, 1f));
        _rightGain = MathF.Sqrt(Math.Clamp(pan, 0f, 1f));
        _attackSamples = Math.Max(0, instrument.AttackMs * sampleRate / 1000);
        _effect = effect;
        _effectParam = effectParam;
        _maxSamples = maxSamples;
    }

    /// <summary>
    /// Executes the TickAdvance operation.
    /// </summary>
    public void TickAdvance(int tickInRow, int speed)
    {
        if (_effect == EffectCommand.None && _effectParam != 0)
        {
            int high = (_effectParam >> 4) & 0x0F;
            int low = _effectParam & 0x0F;
            int offset = tickInRow % 3 switch
            {
                1 => high,
                2 => low,
                _ => 0
            };
            SetPitch(_basePitch + _fineTuneSemis + _pitchOffsetSemis + offset);
            return;
        }

        if (_effect is EffectCommand.PortaUp or EffectCommand.FinePortaUp)
        {
            double step = (_effect == EffectCommand.FinePortaUp ? 0.25 : 1.0) * Math.Max(1, (int)_effectParam) / 16.0;
            _pitchOffsetSemis += step;
            SetPitch(_basePitch + _fineTuneSemis + _pitchOffsetSemis);
        }
        else if (_effect is EffectCommand.PortaDown or EffectCommand.FinePortaDown)
        {
            double step = (_effect == EffectCommand.FinePortaDown ? 0.25 : 1.0) * Math.Max(1, (int)_effectParam) / 16.0;
            _pitchOffsetSemis -= step;
            SetPitch(_basePitch + _fineTuneSemis + _pitchOffsetSemis);
        }

        if (_effect is EffectCommand.Vibrato or EffectCommand.PortaVolSlide)
        {
            int speedStep = Math.Max(1, (_effectParam >> 4) & 0x0F);
            int depth = Math.Max(1, _effectParam & 0x0F);
            _vibratoPhase += speedStep * 0.35;
            double vib = Math.Sin(_vibratoPhase) * depth / 32.0;
            SetPitch(_basePitch + _fineTuneSemis + _pitchOffsetSemis + vib);
        }

        if (_effect is EffectCommand.VolumeSlide or EffectCommand.VolSlide or EffectCommand.PortaVolSlide)
        {
            int up = (_effectParam >> 4) & 0x0F;
            int down = _effectParam & 0x0F;
            float next = _volume;
            if (up > 0 && down == 0)
                next += up / 64f;
            else if (down > 0 && up == 0)
                next -= down / 64f;

            _volume = Math.Clamp(next, 0f, 1f);
        }

        if (_effect == EffectCommand.Tremolo)
        {
            int speedStep = Math.Max(1, (_effectParam >> 4) & 0x0F);
            int depth = Math.Max(1, _effectParam & 0x0F);
            _vibratoPhase += speedStep * 0.35;
            double trem = Math.Sin(_vibratoPhase) * depth / 24.0;
            _volume = Math.Clamp(_baseVolume * (1.0f + (float)trem), 0f, 1f);
        }

        if (_effect == EffectCommand.NoteCut && tickInRow >= _effectParam)
            _volume = 0f;

        if (_effect == EffectCommand.RetrigNote)
        {
            int retrig = Math.Max(1, _effectParam & 0x0F);
            if (tickInRow > 0 && tickInRow % retrig == 0)
            {
                _phase = 0;
                _ageSamples = 0;
                _volume = _baseVolume;
            }
        }
    }

    /// <summary>
    /// Executes the SetPitch operation.
    /// </summary>
    private void SetPitch(double midiPitch)
    {
        _frequency = 440.0 * Math.Pow(2.0, (midiPitch - 69) / 12.0);
        _phaseStep = _frequency / _sampleRate;
    }

    /// <summary>
    /// Executes the Render operation.
    /// </summary>
    public void Render(float[] buffer, int offset, int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            if (_instrumentLfoAmount > 0 && _instrumentLfoSpeed > 0)
            {
                double lfo = Math.Sin((_ageSamples / _sampleRate) * Math.PI * 2.0 * _instrumentLfoSpeed) * _instrumentLfoAmount;
                SetPitch(_basePitch + _fineTuneSemis + _pitchOffsetSemis + lfo);
            }

            float sample = NextSample();
            float envelope = _attackSamples <= 0
                ? 1f
                : (float)Math.Min(1.0, _ageSamples / (double)_attackSamples);

            sample *= _volume * envelope;

            int bufIdx = (offset + f) * 2;
            if (bufIdx + 1 < buffer.Length)
            {
                buffer[bufIdx] += sample * _leftGain;
                buffer[bufIdx + 1] += sample * _rightGain;
            }

            _phase += _phaseStep;
            _phase -= Math.Floor(_phase);
            _ageSamples++;
        }
    }

    /// <summary>
    /// Executes the NextSample operation.
    /// </summary>
    private float NextSample()
    {
        return _waveform switch
        {
            SynthWaveform.Triangle => (float)(4.0 * Math.Abs(_phase - 0.5) - 1.0),
            SynthWaveform.Saw => (float)(2.0 * _phase - 1.0),
            SynthWaveform.Noise => NextNoise(),
            _ => _phase < _pulseWidth ? 1f : -1f
        };
    }

    /// <summary>
    /// Executes the NextNoise operation.
    /// </summary>
    private float NextNoise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return ((_noise & 0xFFFF) / 32768f) - 1f;
    }
}
