namespace amChipper.Core.Models;

/// <summary>
/// Top-level document model.  Holds all patterns, instruments, tracks, and
/// song-level metadata for one amChipper project.
/// </summary>
public sealed class Song
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Title.
    /// </summary>
    public string Title { get; set; } = "Untitled";
    /// <summary>
    /// Stores or exposes Artist.
    /// </summary>
    public string Artist { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes Comment.
    /// </summary>
    public string Comment { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes Format.
    /// </summary>
    public ModuleFormat Format { get; set; } = ModuleFormat.IT;
    /// <summary>
    /// Stores or exposes SourceModuleType.
    /// </summary>
    public string SourceModuleType { get; set; } = string.Empty;
    /// <summary>
    /// Stores or exposes SourceModuleExtension.
    /// </summary>
    public string SourceModuleExtension { get; set; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>Beats per minute (6-999).</summary>
    public int Bpm { get; set; } = 125;

    /// <summary>Ticks per beat / rows per beat (1-32, default 4).</summary>
    public int RowsPerBeat { get; set; } = 4;

    /// <summary>Rows per pattern default (used when creating new patterns).</summary>
    public int DefaultRowsPerPattern { get; set; } = 64;

    /// <summary>Initial speed in ticks per row (1-31).</summary>
    public int InitialSpeed { get; set; } = 6;

    /// <summary>Tracker restart order, when the source format exposes one. -1 means not available.</summary>
    public int RestartOrder { get; set; } = -1;

    // ── Global volume / mix ───────────────────────────────────────────────────

    /// <summary>Global volume 0-128.</summary>
    public byte GlobalVolume { get; set; } = 128;

    /// <summary>Mix volume 0-128 (IT) / global volume amplification.</summary>
    public byte MixVolume { get; set; } = 128;

    /// <summary>Stereo separation 0-128 (128 = full stereo).</summary>
    public byte Separation { get; set; } = 128;

    /// <summary>
    /// Stores or exposes Interpolation.
    /// </summary>
    public InterpolationMode Interpolation { get; set; } = InterpolationMode.Sinc;

    /// <summary>Original module bytes used for exact native playback/export until the song is edited.</summary>
    public byte[]? OriginalModuleData { get; set; }

    // ── Data ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores or exposes Instruments.
    /// </summary>
    public List<Instrument> Instruments { get; set; } = [];
    /// <summary>
    /// Stores or exposes Patterns.
    /// </summary>
    public List<Pattern> Patterns { get; set; } = [];
    /// <summary>
    /// Stores or exposes Tracks.
    /// </summary>
    public List<Track> Tracks { get; set; } = [];

    /// <summary>Song order list — indices into Patterns (classic tracker order table).</summary>
    public List<int> OrderList { get; set; } = [];

    // ── Factories ─────────────────────────────────────────────────────────────

    /// <summary>Create a blank song with sensible defaults.</summary>
    public static Song CreateDefault() => CreateDefault(NewSongOptions.Default);

    /// <summary>
    /// Executes the CreateDefault operation.
    /// </summary>
    public static Song CreateDefault(NewSongOptions options)
    {
        options = (options ?? NewSongOptions.Default).Normalize();
        var song = new Song
        {
            Title = options.Title,
            Format = options.Format,
            SourceModuleType = options.Format switch
            {
                ModuleFormat.OpenMpt => "MPTM",
                ModuleFormat.AmChip => "AMC",
                _ => options.Format.ToString().ToUpperInvariant()
            },
            SourceModuleExtension = ModuleFormatCatalog.GetPreferredExtension(options.Format),
            Bpm = options.Bpm,
            RowsPerBeat = options.RowsPerBeat,
            DefaultRowsPerPattern = options.RowsPerPattern,
            InitialSpeed = options.InitialSpeed
        };

        SynthWaveform[] waveforms =
        [
            SynthWaveform.Square,
            SynthWaveform.Triangle,
            SynthWaveform.Saw,
            SynthWaveform.Noise
        ];

        string[] instrumentNames =
        [
            "Pulse Lead",
            "Triangle Bass",
            "Saw Chord",
            "Noise Kit"
        ];

        // Add audible default chiptune instruments.
        for (int i = 0; i < 4; i++)
        {
            song.Instruments.Add(new Instrument
            {
                Name = instrumentNames[i],
                SourceType = InstrumentSourceType.Synth,
                Waveform = waveforms[i],
                PulseWidth = i == 0 ? 0.25 : 0.5,
                NoteColor = DefaultNoteColors[i % DefaultNoteColors.Length]
            });
        }

        if (options.IncludeDefaultSamples)
            song.Instruments.AddRange(DefaultSampleLibrary.CreateStarterInstruments().Select(i => i.Clone()));

        int channels = Math.Clamp(options.Channels, 1, 64);
        var trackColors = new uint[] { 0xFF2979FF, 0xFFFF6D00, 0xFF00C853, 0xFFD500F9, 0xFF40C4FF, 0xFFFFD54F, 0xFFFF4081, 0xFF69F0AE };
        for (int i = 0; i < channels; i++)
        {
            int instrumentIndex = i < song.Instruments.Count ? i : i % Math.Max(song.Instruments.Count, 1);
            string trackName = i < song.Instruments.Count ? song.Instruments[instrumentIndex].Name : $"Channel {i + 1}";
            song.Tracks.Add(new Track
            {
                Name = trackName,
                InstrumentIndex = instrumentIndex,
                StepPitch = DefaultStepPitch(i, song.Instruments[instrumentIndex]),
                Volume = 118,
                Panning = (byte)(channels == 1 ? 128 : Math.Clamp(64 + i * 128 / Math.Max(channels - 1, 1), 0, 255)),
                VolumeAutomation = [new AutomationPoint { Beat = 0, Value = 128 }],
                PanAutomation = [new AutomationPoint { Beat = 0, Value = 128 }],
                Color = trackColors[i % trackColors.Length]
            });
        }

        int patternCount = Math.Clamp(options.Patterns, 1, 256);
        for (int i = 0; i < patternCount; i++)
        {
            var pat = new Pattern(options.RowsPerPattern, channels) { Name = $"Pattern {i:00}" };
            song.Patterns.Add(pat);
            song.OrderList.Add(i);
        }

        if (options.CreatePlaylistBlocks)
        {
            double durationBeats = options.RowsPerPattern / (double)Math.Max(options.RowsPerBeat, 1);
            for (int channel = 0; channel < song.Tracks.Count; channel++)
            {
                song.Tracks[channel].Blocks.Add(new PatternBlock
                {
                    PatternIndex = 0,
                    StartBeat = 0,
                    DurationBeats = durationBeats
                });
            }
        }

        return song;
    }

    /// <summary>
    /// Executes the DefaultStepPitch operation.
    /// </summary>
    private static byte DefaultStepPitch(int channel, Instrument instrument)
    {
        if (instrument.SourceType == InstrumentSourceType.Sample && instrument.Samples.Count > 0)
            return instrument.Samples[0].BaseNote;

        return (byte)(channel % 4 switch
        {
            0 => 72,
            1 => 48,
            2 => 60,
            _ => 36
        });
    }

    /// <summary>
    /// Stores or exposes DefaultNoteColors.
    /// </summary>
    private static readonly uint[] DefaultNoteColors =
    [
        0xFF3A7BD5, 0xFFE74C3C, 0xFF2ECC71, 0xFFF39C12,
        0xFF9B59B6, 0xFF1ABC9C, 0xFFE67E22, 0xFF34495E
    ];

    /// <summary>
    /// Executes the Clone operation.
    /// </summary>
    public Song Clone()
    {
        return new Song
        {
            Title = Title,
            Artist = Artist,
            Comment = Comment,
            Format = Format,
            SourceModuleType = SourceModuleType,
            SourceModuleExtension = SourceModuleExtension,
            Bpm = Bpm,
            RowsPerBeat = RowsPerBeat,
            DefaultRowsPerPattern = DefaultRowsPerPattern,
            InitialSpeed = InitialSpeed,
            RestartOrder = RestartOrder,
            GlobalVolume = GlobalVolume,
            MixVolume = MixVolume,
            Separation = Separation,
            Interpolation = Interpolation,
            OriginalModuleData = OriginalModuleData is null ? null : (byte[])OriginalModuleData.Clone(),
            Instruments = Instruments.Select(i => i.Clone()).ToList(),
            Patterns = Patterns.Select(p => p.Clone()).ToList(),
            Tracks = Tracks.Select(t => t.Clone()).ToList(),
            OrderList = [.. OrderList]
        };
    }
}
