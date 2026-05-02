using System.Runtime.InteropServices;
using System.Globalization;
using amChipper.Audio.Interop;
using amChipper.Core.Interfaces;
using amChipper.Core.Models;

namespace amChipper.Audio.Engine;

/// <summary>
/// libopenmpt-backed module player.  Handles MOD / XM / IT / S3M and the
/// additional tracker/chiptune module formats supported by libopenmpt.
/// Falls back to a stub renderer when libopenmpt.dll is not present.
/// </summary>
public sealed class ModulePlayer : IModulePlayer
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Executes the int operation.
    /// </summary>
    private delegate int SetChannelMuteStatusDelegate(nint moduleExt, int channel, int mute);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Executes the double operation.
    /// </summary>
    private delegate double GetChannelVolumeDelegate(nint moduleExt, int channel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Executes the int operation.
    /// </summary>
    private delegate int SetChannelVolumeDelegate(nint moduleExt, int channel, double volume);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Executes the double operation.
    /// </summary>
    private delegate double GetChannelPanningDelegate(nint moduleExt, int channel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Executes the int operation.
    /// </summary>
    private delegate int SetChannelPanningDelegate(nint moduleExt, int channel, double panning);

    /// <summary>
    /// Stores or exposes _module.
    /// </summary>
    private nint _module;
    /// <summary>
    /// Stores or exposes _moduleExt.
    /// </summary>
    private nint _moduleExt;
    /// <summary>
    /// Stores or exposes _disposed.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores or exposes _sampleRate.
    /// </summary>
    private readonly int _sampleRate;
    private readonly object _lock = new();
    /// <summary>
    /// Stores or exposes _log.
    /// </summary>
    private readonly IAppLogger _log;
    /// <summary>
    /// Stores or exposes _moduleData.
    /// </summary>
    private byte[]? _moduleData;
    /// <summary>
    /// Stores or exposes _sourceFileName.
    /// </summary>
    private string? _sourceFileName;
    /// <summary>
    /// Stores or exposes _setChannelMuteStatus.
    /// </summary>
    private SetChannelMuteStatusDelegate? _setChannelMuteStatus;
    /// <summary>
    /// Stores or exposes _getChannelVolume.
    /// </summary>
    private GetChannelVolumeDelegate? _getChannelVolume;
    /// <summary>
    /// Stores or exposes _setChannelVolume.
    /// </summary>
    private SetChannelVolumeDelegate? _setChannelVolume;
    /// <summary>
    /// Stores or exposes _getChannelPanning.
    /// </summary>
    private GetChannelPanningDelegate? _getChannelPanning;
    /// <summary>
    /// Stores or exposes _setChannelPanning.
    /// </summary>
    private SetChannelPanningDelegate? _setChannelPanning;

    // Pre-allocated native render buffer — avoids AllocHGlobal on every audio callback.
    // Sized for the largest single Read() call the wave provider will make (4096 frames
    // stereo = 32 KB).  Allocated once at construction, freed in Dispose.
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int RenderBufFrames = 4096;
    /// <summary>
    /// Stores or exposes _renderBuf.
    /// </summary>
    private readonly nint _renderBuf;

    /// <summary>
    /// Stores or exposes _dllAvailable.
    /// </summary>
    private static bool? _dllAvailable;

    public ModulePlayer(int sampleRate = 44100, IAppLogger? logger = null)
    {
        _sampleRate = sampleRate;
        _log = logger ?? NullAppLogger.Instance;
        _renderBuf = Marshal.AllocHGlobal(RenderBufFrames * 2 * sizeof(float));
        CheckDll();
    }

    /// <summary>
    /// Executes the CheckDll operation.
    /// </summary>
    private bool CheckDll()
    {
        if (_dllAvailable.HasValue) return _dllAvailable.Value;
        try
        {
            uint ver = LibOpenMpt.GetLibraryVersion();
            _dllAvailable = true;
            _log.Info($"libopenmpt loaded — version 0x{ver:X8}");
        }
        catch (Exception ex)
        {
            _dllAvailable = false;
            _log.Warning($"libopenmpt.dll not found or failed to load: {ex.Message}. Module file playback unavailable.");
        }
        return _dllAvailable.Value;
    }

    // ── IModulePlayer ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes IsLoaded.
    /// </summary>
    public bool IsLoaded => _module != nint.Zero;
    /// <summary>
    /// Executes the OrderCount operation.
    /// </summary>
    public int OrderCount => IsLoaded ? LibOpenMpt.GetNumOrders(_module) : 0;
    /// <summary>
    /// Executes the PatternCount operation.
    /// </summary>
    public int PatternCount => IsLoaded ? LibOpenMpt.GetNumPatterns(_module) : 0;
    /// <summary>
    /// Executes the ChannelCount operation.
    /// </summary>
    public int ChannelCount => IsLoaded ? LibOpenMpt.GetNumChannels(_module) : 0;

    /// <summary>
    /// Executes the CurrentOrder operation.
    /// </summary>
    public int CurrentOrder => IsLoaded ? LibOpenMpt.GetCurrentOrder(_module) : 0;
    /// <summary>
    /// Executes the CurrentRow operation.
    /// </summary>
    public int CurrentRow => IsLoaded ? LibOpenMpt.GetCurrentRow(_module) : 0;
    /// <summary>
    /// Executes the DurationSecs operation.
    /// </summary>
    public double DurationSecs => IsLoaded ? LibOpenMpt.GetDuration(_module) : 0;
    /// <summary>
    /// Stores or exposes RestartOrder.
    /// </summary>
    public int RestartOrder { get; private set; } = -1;
    /// <summary>
    /// Stores or exposes LoopEnabled.
    /// </summary>
    public bool LoopEnabled { get; set; }
    /// <summary>
    /// Stores or exposes LoopFromRestartOrder.
    /// </summary>
    public bool LoopFromRestartOrder { get; set; }
    /// <summary>
    /// Stores or exposes CurrentTempo.
    /// </summary>
    public int CurrentTempo
    {
        get
        {
            if (!IsLoaded)
                return 0;

            try { return LibOpenMpt.GetCurrentTempo(_module); }
            catch (EntryPointNotFoundException) { return 0; }
        }
    }

    /// <summary>
    /// Stores or exposes CurrentSpeed.
    /// </summary>
    public int CurrentSpeed
    {
        get
        {
            if (!IsLoaded)
                return 0;

            try { return LibOpenMpt.GetCurrentSpeed(_module); }
            catch (EntryPointNotFoundException) { return 0; }
        }
    }

    /// <summary>
    /// Stores or exposes PositionSecs.
    /// </summary>
    public double PositionSecs
    {
        get => IsLoaded ? LibOpenMpt.GetPosition(_module) : 0;
        set { if (IsLoaded) LibOpenMpt.SetPosition(_module, value); }
    }

    /// <summary>
    /// Stores or exposes Format.
    /// </summary>
    public ModuleFormat Format { get; private set; }
    /// <summary>
    /// Stores or exposes SourceModuleType.
    /// </summary>
    public string SourceModuleType { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes SourceModuleExtension.
    /// </summary>
    public string SourceModuleExtension { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes Title.
    /// </summary>
    public string Title { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes Artist.
    /// </summary>
    public string Artist { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes Comment.
    /// </summary>
    public string Comment { get; private set; } = string.Empty;

    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<RowChangedEventArgs>? RowChanged;
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler<OrderChangedEventArgs>? OrderChanged;

    /// <summary>
    /// Stores or exposes _lastOrder.
    /// </summary>
    private int _lastOrder = -1;
    /// <summary>
    /// Stores or exposes _lastRow.
    /// </summary>
    private int _lastRow = -1;

    /// <summary>
    /// Executes the Load operation.
    /// </summary>
    public bool Load(byte[] data, string? fileName = null)
    {
        if (!CheckDll())
        {
            _log.Error("Cannot load module: libopenmpt.dll unavailable.");
            return false;
        }

        lock (_lock)
        {
            FreeModule();
            _log.Debug($"Loading module: {fileName ?? "(from memory)"} ({data.Length:N0} bytes)");

            nint buf = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buf, data.Length);
                _moduleExt = LibOpenMpt.CreateExtFromMemory(buf, (nuint)data.Length,
                    nint.Zero, nint.Zero, nint.Zero, nint.Zero,
                    nint.Zero, nint.Zero, nint.Zero);
                if (_moduleExt != nint.Zero)
                {
                    _module = LibOpenMpt.GetExtModule(_moduleExt);
                    BindInteractiveInterface();
                }
                else
                {
                    _module = LibOpenMpt.CreateFromMemory(buf, (nuint)data.Length,
                        nint.Zero, nint.Zero, nint.Zero, nint.Zero,
                        nint.Zero, nint.Zero, nint.Zero);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }

            if (_module == nint.Zero)
            {
                _log.Error($"libopenmpt rejected the file: {fileName}");
                return false;
            }

            _moduleData = (byte[])data.Clone();
            _sourceFileName = fileName;

            Title = LibOpenMpt.GetMetadata(_module, "title") ?? string.Empty;
            Artist = LibOpenMpt.GetMetadata(_module, "artist") ?? string.Empty;
            Comment = LibOpenMpt.GetMetadata(_module, "message") ?? string.Empty;
            SourceModuleType = LibOpenMpt.GetMetadata(_module, "type") ?? string.Empty;
            SourceModuleExtension = Path.GetExtension(fileName ?? string.Empty);
            Format = DetectFormat(SourceModuleType, SourceModuleExtension);
            RestartOrder = TryReadRestartOrder(data, Format);

            TrySetCtl("render.resampler.emulate_amiga", "1");
            TrySetCtl("render.resampler.emulate_amiga_type", "auto");

            _lastOrder = -1;
            _lastRow = -1;

            _log.Info($"Module loaded: \"{Title}\" by \"{Artist}\" | Format={Format} | Type={SourceModuleType} | Ext={SourceModuleExtension} | Orders={OrderCount} | Patterns={PatternCount} | RestartOrder={RestartOrder} | Duration={DurationSecs:F1}s");
        }
        return true;
    }

    /// <summary>
    /// Executes the Render operation.
    /// </summary>
    public int Render(float[] buffer, int frameCount)
    {
        if (!IsLoaded || _disposed) return 0;

        // Clamp to pre-allocated buffer capacity.
        int frames = Math.Min(frameCount, RenderBufFrames);

        int rendered;
        lock (_lock)
        {
            // Render directly into the pre-allocated native buffer — no per-call alloc.
            rendered = (int)LibOpenMpt.ReadInterleavedFloatStereo(
                _module, _sampleRate, (nuint)frames, _renderBuf);

            if (rendered <= 0 && LoopEnabled)
            {
                int loopOrder = LoopFromRestartOrder && RestartOrder >= 0 ? RestartOrder : 0;
                _log.Debug($"Module reached end; looping to order {loopOrder}.");
                LibOpenMpt.SetPositionOrderRow(_module, loopOrder, 0);
                rendered = (int)LibOpenMpt.ReadInterleavedFloatStereo(
                    _module, _sampleRate, (nuint)frames, _renderBuf);
            }

            if (rendered > 0)
                Marshal.Copy(_renderBuf, buffer, 0, rendered * 2);

            int ord = LibOpenMpt.GetCurrentOrder(_module);
            int row = LibOpenMpt.GetCurrentRow(_module);
            if (row != _lastRow || ord != _lastOrder)
            {
                int pat = LibOpenMpt.GetCurrentPattern(_module);
                RowChanged?.Invoke(this, new RowChangedEventArgs(ord, pat, row));
                if (ord != _lastOrder)
                    OrderChanged?.Invoke(this, new OrderChangedEventArgs(ord));
                _lastOrder = ord;
                _lastRow = row;
            }
        }
        return rendered;
    }

    /// <summary>
    /// Executes the SeekToOrder operation.
    /// </summary>
    public void SeekToOrder(int order, int row = 0)
    {
        if (!IsLoaded) return;
        lock (_lock)
        {
            _log.Debug($"Seek → Order {order}, Row {row}");
            LibOpenMpt.SetPositionOrderRow(_module, order, row);
        }
    }

    /// <summary>
    /// Executes the SetChannelMuteStatus operation.
    /// </summary>
    public void SetChannelMuteStatus(int channel, bool mute)
    {
        if (_moduleExt == nint.Zero || _setChannelMuteStatus is null)
            return;

        lock (_lock)
        {
            _setChannelMuteStatus(_moduleExt, channel, mute ? 1 : 0);
        }
    }

    /// <summary>
    /// Executes the GetChannelVolume operation.
    /// </summary>
    public double GetChannelVolume(int channel)
    {
        if (_moduleExt == nint.Zero || _getChannelVolume is null)
            return 1.0;

        lock (_lock)
        {
            return Math.Clamp(_getChannelVolume(_moduleExt, channel), 0.0, 1.0);
        }
    }

    /// <summary>
    /// Executes the SetChannelVolume operation.
    /// </summary>
    public void SetChannelVolume(int channel, double volume)
    {
        if (_moduleExt == nint.Zero || _setChannelVolume is null)
            return;

        lock (_lock)
        {
            _setChannelVolume(_moduleExt, channel, Math.Clamp(volume, 0.0, 1.0));
        }
    }

    /// <summary>
    /// Executes the GetChannelPanning operation.
    /// </summary>
    public double GetChannelPanning(int channel)
    {
        if (_moduleExt == nint.Zero || _getChannelPanning is null)
            return 0.0;

        lock (_lock)
        {
            return Math.Clamp(_getChannelPanning(_moduleExt, channel), -1.0, 1.0);
        }
    }

    /// <summary>
    /// Executes the GetCurrentChannelVuMono operation.
    /// </summary>
    public double GetCurrentChannelVuMono(int channel)
    {
        if (!IsLoaded)
            return -1.0;

        lock (_lock)
        {
            try { return Math.Clamp(LibOpenMpt.GetCurrentChannelVuMono(_module, channel), 0.0, 1.0); }
            catch (EntryPointNotFoundException) { return -1.0; }
        }
    }

    /// <summary>
    /// Executes the SetChannelPanning operation.
    /// </summary>
    public void SetChannelPanning(int channel, double panning)
    {
        if (_moduleExt == nint.Zero || _setChannelPanning is null)
            return;

        lock (_lock)
        {
            _setChannelPanning(_moduleExt, channel, Math.Clamp(panning, -1.0, 1.0));
        }
    }

    /// <summary>
    /// Executes the ApplyChannelMutes operation.
    /// </summary>
    public void ApplyChannelMutes(IReadOnlyList<bool> mutedChannels)
    {
        if (_moduleExt == nint.Zero || _setChannelMuteStatus is null || mutedChannels.Count == 0)
            return;

        lock (_lock)
        {
            int count = Math.Min(mutedChannels.Count, ChannelCount);
            for (int i = 0; i < count; i++)
                _setChannelMuteStatus(_moduleExt, i, mutedChannels[i] ? 1 : 0);
        }
    }

    /// <summary>
    /// Executes the UnmuteAllChannels operation.
    /// </summary>
    public void UnmuteAllChannels()
    {
        if (ChannelCount <= 0)
            return;

        ApplyChannelMutes(new bool[ChannelCount]);
    }

    /// <summary>
    /// Executes the ImportAsSong operation.
    /// </summary>
    public Song? ImportAsSong()
    {
        if (!IsLoaded) return null;
        _log.Info("Importing module into Song model…");

        var song = new Song
        {
            Title = Title,
            Artist = Artist,
            Comment = Comment,
            Format = Format,
            SourceModuleType = SourceModuleType,
            SourceModuleExtension = ModuleFormatCatalog.NormalizeExtension(SourceModuleExtension)
        };
        song.RestartOrder = RestartOrder;

        // Ask libopenmpt for the module's own rows-per-beat value so block widths
        // in the Song Editor reflect the actual musical grid.
        // Some DLL builds (notably older VS2022 dev packages) do not export
        // openmpt_module_get_current_rows_per_beat, so we catch the missing-entry
        // exception and fall back to the standard default of 4.
        int rpb = 0;
        try { rpb = LibOpenMpt.GetCurrentRowsPerBeat(_module); }
        catch (EntryPointNotFoundException) { _log.Warning("openmpt_module_get_current_rows_per_beat not exported by this libopenmpt build; defaulting RowsPerBeat=4."); }
        song.RowsPerBeat = rpb > 0 ? rpb : 4;
        try
        {
            int tempo = LibOpenMpt.GetCurrentTempo(_module);
            if (tempo > 0)
                song.Bpm = Math.Clamp(tempo, 6, 999);
        }
        catch (EntryPointNotFoundException)
        {
            _log.Debug("openmpt_module_get_current_tempo not exported by this libopenmpt build; keeping default BPM.");
        }
        try
        {
            int speed = LibOpenMpt.GetCurrentSpeed(_module);
            song.InitialSpeed = speed > 0 ? Math.Clamp(speed, 1, 31) : 6;
        }
        catch (EntryPointNotFoundException)
        {
            song.InitialSpeed = 6;
            _log.Debug("openmpt_module_get_current_speed not exported by this libopenmpt build; defaulting InitialSpeed=6.");
        }

        // ── Instruments / samples ─────────────────────────────────────────────
        int numInstr = LibOpenMpt.GetNumInstruments(_module);
        int numSamp = LibOpenMpt.GetNumSamples(_module);
        int srcCount = numInstr > 0 ? numInstr : numSamp;

        uint[] palette = [
            0xFF2979FF, 0xFFFF6D00, 0xFF00C853, 0xFFD500F9,
            0xFFE74C3C, 0xFF1ABC9C, 0xFFF39C12, 0xFF9B59B6
        ];

        for (int i = 0; i < srcCount; i++)
        {
            string? name = numInstr > 0
                ? LibOpenMpt.GetInstrumentName(_module, i)
                : LibOpenMpt.GetSampleName(_module, i);
            song.Instruments.Add(new Instrument
            {
                Name = name ?? $"Instrument {i + 1}",
                NoteColor = palette[i % palette.Length]
            });
        }

        ApplyNativeSampleData(song);

        // ── Channels → tracks ─────────────────────────────────────────────────
        int numCh = LibOpenMpt.GetNumChannels(_module);
        var channelNames = new string[numCh];
        var channelInstrumentCounts = new Dictionary<int, int>[numCh];
        for (int t = 0; t < numCh; t++)
        {
            channelNames[t] = LibOpenMpt.GetChannelName(_module, t) ?? $"Ch {t + 1}";
            channelInstrumentCounts[t] = new Dictionary<int, int>();
        }

        for (int t = 0; t < numCh; t++)
            song.Tracks.Add(new Track
            {
                Name = string.IsNullOrWhiteSpace(channelNames[t]) ? $"Ch {t + 1}" : channelNames[t].Trim(),
                Color = palette[t % palette.Length]
            });

        // ── Patterns + note data ──────────────────────────────────────────────
        int numPat = PatternCount;
        for (int p = 0; p < numPat; p++)
        {
            int rows = LibOpenMpt.GetPatternNumRows(_module, p);
            var pat = new Pattern(rows, numCh) { Name = $"Pattern {p:D2}" };

            for (int row = 0; row < rows; row++)
            {
                for (int ch = 0; ch < numCh; ch++)
                {
                    int rawNote = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdNote);
                    int instrIdx = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdInstrument);
                    int volEffect = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdVolumeEffect);
                    int volCmd = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdVolume);
                    int effect = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdEffect);
                    int param = LibOpenMpt.GetPatternRowChannelCommand(_module, p, row, ch, LibOpenMpt.PatCmdParam);
                    string volumeEffectText = FormatPatternCommand(p, row, ch, LibOpenMpt.PatCmdVolumeEffect);
                    string volumeParamText = FormatPatternCommand(p, row, ch, LibOpenMpt.PatCmdVolume);
                    string effectText = FormatPatternCommand(p, row, ch, LibOpenMpt.PatCmdEffect);
                    string paramText = FormatPatternCommand(p, row, ch, LibOpenMpt.PatCmdParam);

                    // Completely empty cell — leave the default blank Note in place.
                    if (rawNote == 0 && instrIdx == 0 && volEffect == 0 && volCmd == 0 && effect == 0 && param == 0)
                        continue;

                    var note = new Note();

                    // OpenMPT note encoding: 1=C-0, 2=C#0, …, 13=C-1, …, 120=B-9
                    // Our pitch: PitchToName does octave = pitch/12 - 1
                    //   → rawNote 1  (C-0) → pitch 12  → "C-0"
                    //   → rawNote 13 (C-1) → pitch 24  → "C-1"
                    //   → rawNote 49 (C-4) → pitch 60  → "C-4" (middle C)
                    // Conversion: pitch = rawNote + 11, clamped 0-127.
                    if (rawNote > 0)
                    {
                        note.Pitch = rawNote >= 253
                            ? (byte)SpecialNote.NoteOff
                            : (byte)Math.Min(rawNote + 11, 127);
                    }

                    note.InstrumentIndex = (byte)Math.Min(instrIdx, 255);

                    note.VolumeColumn = MapXmVolumeColumn(volumeEffectText, volumeParamText, volEffect, volCmd);
                    if (note.VolumeColumn is >= 0x10 and <= 0x50)
                        note.Volume = (byte)(note.VolumeColumn - 0x10);

                    MapPatternEffect(effectText, paramText, effect, param, out var mappedEffect, out byte mappedParam);
                    note.Effect = mappedEffect;
                    note.EffectColumn = MapXmEffectColumn(effectText, effect);
                    note.EffectParam = mappedParam;

                    pat.SetNote(row, ch, note);

                    if (note.InstrumentIndex > 0 && ch < channelInstrumentCounts.Length)
                    {
                        var counts = channelInstrumentCounts[ch];
                        counts.TryGetValue(note.InstrumentIndex, out int count);
                        counts[note.InstrumentIndex] = count + 1;
                    }
                }
            }

            song.Patterns.Add(pat);
        }

        // ── Order list + PatternBlocks on all tracks ──────────────────────────
        // In tracker mode every channel plays the same pattern simultaneously,
        // so we place a matching PatternBlock on every track lane for each order
        // position.  GetOrderPattern returns the real pattern index for each
        // order slot, or -1 for separator markers (+++ / ---) which we skip.
        int orders = OrderCount;
        double beatPos = 0.0;

        for (int o = 0; o < orders; o++)
        {
            int patIdx = LibOpenMpt.GetOrderPattern(_module, o);
            if (patIdx < 0 || patIdx >= song.Patterns.Count)
                continue;

            song.OrderList.Add(patIdx);

            double dur = Math.Max((double)song.Patterns[patIdx].RowCount / song.RowsPerBeat, 1.0);
            foreach (var track in song.Tracks)
            {
                track.Blocks.Add(new PatternBlock
                {
                    PatternIndex = patIdx,
                    StartBeat = beatPos,
                    DurationBeats = dur
                });
            }
            beatPos += dur;
        }

        for (int t = 0; t < song.Tracks.Count; t++)
        {
            int preferredInstrument = GetDominantInstrumentIndex(channelInstrumentCounts[t]);
            if (preferredInstrument > 0)
                song.Tracks[t].InstrumentIndex = preferredInstrument - 1;

            song.Tracks[t].Volume = (byte)Math.Clamp(Math.Round(GetChannelVolume(t) * 128.0), 0, 128);
            double panning = GetChannelPanning(t);
            song.Tracks[t].Panning = (byte)Math.Clamp(Math.Round(((panning + 1.0) * 0.5) * 255.0), 0, 255);
        }

        _log.Info($"Import complete: {srcCount} instruments, {numPat} patterns, " +
                  $"{numCh} channels, {orders} order slots, {beatPos:F1} beats total.");
        return song;
    }

    /// <summary>
    /// Executes the ApplyNativeSampleData operation.
    /// </summary>
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

    /// <summary>
    /// Executes the FormatPatternCommand operation.
    /// </summary>
    private string FormatPatternCommand(int pattern, int row, int channel, int command)
    {
        nint ptr = LibOpenMpt.FormatPatternRowChannelCommand(_module, pattern, row, channel, command);
        return ptr == nint.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    /// <summary>
    /// Executes the MapPatternEffect operation.
    /// </summary>
    private static void MapPatternEffect(string effectText, string paramText, int rawEffect, int rawParam, out EffectCommand effect, out byte param)
    {
        param = ParseEffectParam(paramText, rawParam);
        char command = string.IsNullOrWhiteSpace(effectText) ? '\0' : char.ToUpperInvariant(effectText.Trim()[0]);
        effect = command switch
        {
            '\0' or '.' => rawEffect == 0 && param != 0 ? EffectCommand.None : EffectCommand.None,
            '0' => EffectCommand.None,
            '1' => EffectCommand.PortaUp,
            '2' => EffectCommand.PortaDown,
            '3' => EffectCommand.TonePorta,
            '4' => EffectCommand.Vibrato,
            '5' => EffectCommand.VolSlide,
            '6' => EffectCommand.PortaVolSlide,
            '7' => EffectCommand.Tremolo,
            '8' => EffectCommand.SetPan,
            '9' => EffectCommand.SampleOffset,
            'A' => EffectCommand.VolumeSlide,
            'B' => EffectCommand.PosJump,
            'C' => EffectCommand.SetVolume,
            'D' => EffectCommand.PatternBreak,
            'E' => MapExtendedEffect(param, out param),
            'F' => EffectCommand.SetSpeed,
            'G' => EffectCommand.SetGlobalVol,
            'R' => EffectCommand.RetrigNote,
            'T' => EffectCommand.SetBpm,
            _ => EffectCommand.None
        };
    }

    /// <summary>
    /// Executes the MapXmEffectColumn operation.
    /// </summary>
    private static byte MapXmEffectColumn(string effectText, int rawEffect)
    {
        char command = string.IsNullOrWhiteSpace(effectText) ? '\0' : char.ToUpperInvariant(effectText.Trim()[0]);
        if (command is '\0' or '.')
            return (byte)Math.Clamp(rawEffect, 0, 255);

        return command switch
        {
            >= '0' and <= '9' => (byte)(command - '0'),
            >= 'A' and <= 'Z' => (byte)(0x0A + command - 'A'),
            _ => (byte)Math.Clamp(rawEffect, 0, 255)
        };
    }

    /// <summary>
    /// Executes the ParseEffectParam operation.
    /// </summary>
    private static byte ParseEffectParam(string paramText, int rawParam)
    {
        string clean = new(paramText.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length > 2)
            clean = clean[^2..];
        if (clean.Length > 0 && byte.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte parsed))
            return parsed;

        return (byte)Math.Clamp(rawParam, 0, 255);
    }

    /// <summary>
    /// Executes the MapXmVolumeColumn operation.
    /// </summary>
    private static byte MapXmVolumeColumn(string effectText, string paramText, int rawEffect, int rawParam)
    {
        int value = Math.Clamp(rawParam, 0, 64);
        char command = string.IsNullOrWhiteSpace(effectText) ? '\0' : char.ToUpperInvariant(effectText.Trim()[0]);
        byte nibble = (byte)(Math.Clamp(rawParam, 0, 15) & 0x0F);

        return command switch
        {
            '\0' or '.' => MapXmVolumeColumnRaw(rawEffect, rawParam),
            'V' => (byte)(0x10 + value),
            'C' or '+' or 'U' => (byte)(0x70 | nibble),
            'D' or '-' => (byte)(0x60 | nibble),
            'A' => (byte)(0x90 | nibble),
            'B' => (byte)(0x80 | nibble),
            'H' => (byte)(0xA0 | nibble),
            'P' => (byte)(0xC0 | nibble),
            'L' => (byte)(0xD0 | nibble),
            'R' => (byte)(0xE0 | nibble),
            'G' => (byte)(0xF0 | nibble),
            _ => MapXmVolumeColumnRaw(rawEffect, rawParam)
        };
    }

    /// <summary>
    /// Executes the MapXmVolumeColumnRaw operation.
    /// </summary>
    private static byte MapXmVolumeColumnRaw(int rawEffect, int rawParam)
    {
        int value = Math.Clamp(rawParam, 0, 64);
        byte nibble = (byte)(Math.Clamp(rawParam, 0, 15) & 0x0F);

        return rawEffect switch
        {
            1 => (byte)(0x10 + value), // set volume
            2 => (byte)(0xC0 | nibble), // set panning
            3 => (byte)(0x70 | nibble), // volume slide up
            4 => (byte)(0x60 | nibble), // volume slide down
            5 => (byte)(0x90 | nibble), // fine volume slide up
            6 => (byte)(0x80 | nibble), // fine volume slide down
            7 => (byte)(0xA0 | nibble), // vibrato speed
            8 => (byte)(0xB0 | nibble), // vibrato depth
            9 => (byte)(0xD0 | nibble), // pan slide left
            10 => (byte)(0xE0 | nibble), // pan slide right
            11 => (byte)(0xF0 | nibble), // tone portamento
            _ => 0
        };
    }

    /// <summary>
    /// Executes the MapExtendedEffect operation.
    /// </summary>
    private static EffectCommand MapExtendedEffect(byte rawParam, out byte param)
    {
        int x = rawParam & 0xF0;
        param = (byte)(rawParam & 0x0F);
        return x switch
        {
            0x10 => EffectCommand.FinePortaUp,
            0x20 => EffectCommand.FinePortaDown,
            0xC0 => EffectCommand.NoteCut,
            0xD0 => EffectCommand.NoteDelay,
            _ => EffectCommand.None
        };
    }

    /// <summary>
    /// Executes the DetectFormat operation.
    /// </summary>
    private static ModuleFormat DetectFormat(string type, string extension) =>
        ModuleFormatCatalog.ResolveModuleFormat(type, extension);

    /// <summary>
    /// Executes the TrySetCtl operation.
    /// </summary>
    private void TrySetCtl(string ctl, string value)
    {
        int result = LibOpenMpt.CtlSet(_module, ctl, value);
        if (result == 0)
            _log.Debug($"libopenmpt ctl not applied: {ctl}={value}");
    }

    /// <summary>
    /// Executes the TryReadRestartOrder operation.
    /// </summary>
    private static int TryReadRestartOrder(byte[] data, ModuleFormat format)
    {
        if (format == ModuleFormat.XM &&
            data.Length >= 68 &&
            System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(17, data.Length)) == "Extended Module: ")
        {
            int songLength = data[64] | (data[65] << 8);
            int restart = data[66] | (data[67] << 8);
            return restart >= 0 && restart < songLength ? restart : -1;
        }

        if (format == ModuleFormat.MOD && data.Length > 951)
        {
            int songLength = data[950];
            int restart = data[951];
            return songLength > 0 && restart >= 0 && restart < songLength ? restart : -1;
        }

        return -1;
    }

    /// <summary>
    /// Executes the FreeModule operation.
    /// </summary>
    private void FreeModule()
    {
        _setChannelMuteStatus = null;
        _setChannelVolume = null;
        _getChannelVolume = null;
        _setChannelPanning = null;
        _getChannelPanning = null;
        if (_module != nint.Zero)
        {
            _log.Debug("Freeing loaded module.");
            if (_moduleExt != nint.Zero)
                LibOpenMpt.DestroyExt(_moduleExt);
            else
                LibOpenMpt.Destroy(_module);
        }

        _module = nint.Zero;
        _moduleExt = nint.Zero;
        _moduleData = null;
        _sourceFileName = null;
        SourceModuleType = string.Empty;
        SourceModuleExtension = string.Empty;
    }

    /// <summary>
    /// Executes the BindInteractiveInterface operation.
    /// </summary>
    private void BindInteractiveInterface()
    {
        if (_moduleExt == nint.Zero)
            return;

        var iface = new LibOpenMpt.OpenMptInteractiveInterface();
        int ok = LibOpenMpt.GetExtInterface(
            _moduleExt,
            LibOpenMpt.ExtInterfaceInteractive,
            ref iface,
            (nuint)Marshal.SizeOf<LibOpenMpt.OpenMptInteractiveInterface>());

        if (ok == 0 || iface.SetChannelMuteStatus == nint.Zero)
        {
            _log.Warning("libopenmpt interactive interface unavailable; channel muting will be limited.");
            return;
        }

        _setChannelMuteStatus = Marshal.GetDelegateForFunctionPointer<SetChannelMuteStatusDelegate>(iface.SetChannelMuteStatus);
        if (iface.SetChannelVolume != nint.Zero)
            _setChannelVolume = Marshal.GetDelegateForFunctionPointer<SetChannelVolumeDelegate>(iface.SetChannelVolume);
        if (iface.GetChannelVolume != nint.Zero)
            _getChannelVolume = Marshal.GetDelegateForFunctionPointer<GetChannelVolumeDelegate>(iface.GetChannelVolume);

        var iface2 = new LibOpenMpt.OpenMptInteractiveInterface2();
        ok = LibOpenMpt.GetExtInterface2(
            _moduleExt,
            LibOpenMpt.ExtInterfaceInteractive2,
            ref iface2,
            (nuint)Marshal.SizeOf<LibOpenMpt.OpenMptInteractiveInterface2>());
        if (ok != 0)
        {
            if (iface2.SetChannelPanning != nint.Zero)
                _setChannelPanning = Marshal.GetDelegateForFunctionPointer<SetChannelPanningDelegate>(iface2.SetChannelPanning);
            if (iface2.GetChannelPanning != nint.Zero)
                _getChannelPanning = Marshal.GetDelegateForFunctionPointer<GetChannelPanningDelegate>(iface2.GetChannelPanning);
        }
        else
        {
            _log.Debug("libopenmpt interactive2 interface unavailable; channel panning will use song defaults.");
        }

        _log.Debug("libopenmpt interactive interface bound.");
    }

    /// <summary>
    /// Executes the GetDominantInstrumentIndex operation.
    /// </summary>
    private static int GetDominantInstrumentIndex(Dictionary<int, int> counts)
    {
        if (counts.Count == 0)
            return 0;

        return counts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .First().Key;
    }

    /// <summary>
    /// Executes the Dispose operation.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock) FreeModule();
        Marshal.FreeHGlobal(_renderBuf);
        _log.Debug("ModulePlayer disposed.");
    }
}
