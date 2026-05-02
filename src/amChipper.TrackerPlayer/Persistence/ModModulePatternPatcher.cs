using amChipper.Core.Models;

namespace amChipper.Core.Persistence;

/// <summary>
/// Represents the ModModulePatternPatcher component.
/// </summary>
public static class ModModulePatternPatcher
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int HeaderSize = 1084;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int RowsPerPattern = 64;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int CellSize = 4;

    /// <summary>
    /// Stores or exposes Periods.
    /// </summary>
    private static readonly int[] Periods =
    [
        1712, 1616, 1524, 1440, 1356, 1280, 1208, 1140, 1076, 1016, 960, 906,
        856, 808, 762, 720, 678, 640, 604, 570, 538, 508, 480, 453,
        428, 404, 381, 360, 339, 320, 302, 285, 269, 254, 240, 226,
        214, 202, 190, 180, 170, 160, 151, 143, 135, 127, 120, 113
    ];

    /// <summary>
    /// Executes the TrySavePatchedModule operation.
    /// </summary>
    public static bool TrySavePatchedModule(Song song, byte[] originalModule, string path)
    {
        if (!TryCreatePatchedModule(song, originalModule, out byte[] patched))
            return false;

        File.WriteAllBytes(path, patched);
        return true;
    }

    /// <summary>
    /// Executes the TryCreatePatchedModule operation.
    /// </summary>
    public static bool TryCreatePatchedModule(Song song, byte[] originalModule, out byte[] patched)
    {
        patched = [];
        if (song.Format != ModuleFormat.MOD || originalModule.Length < HeaderSize || song.Patterns.Count == 0)
            return false;

        int channels = DetectChannelCount(originalModule);
        if (channels <= 0)
            return false;

        int songLength = originalModule[950];
        if (songLength <= 0)
            return false;

        int maxPattern = 0;
        for (int i = 0; i < Math.Min(songLength, 128); i++)
            maxPattern = Math.Max(maxPattern, originalModule[952 + i]);

        int patternCount = maxPattern + 1;
        int patternBytes = patternCount * RowsPerPattern * channels * CellSize;
        if (originalModule.Length < HeaderSize + patternBytes)
            return false;

        patched = (byte[])originalModule.Clone();
        int copyPatterns = Math.Min(patternCount, song.Patterns.Count);
        for (int patternIndex = 0; patternIndex < copyPatterns; patternIndex++)
        {
            var pattern = song.Patterns[patternIndex];
            int rows = Math.Min(RowsPerPattern, pattern.RowCount);
            int cols = Math.Min(channels, pattern.ChannelCount);

            for (int row = 0; row < rows; row++)
            {
                for (int channel = 0; channel < cols; channel++)
                {
                    int offset = HeaderSize + ((patternIndex * RowsPerPattern + row) * channels + channel) * CellSize;
                    WriteCell(patched, offset, pattern.GetNote(row, channel));
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Executes the DetectChannelCount operation.
    /// </summary>
    private static int DetectChannelCount(byte[] data)
    {
        string tag = System.Text.Encoding.ASCII.GetString(data, 1080, 4);
        return tag switch
        {
            "M.K." or "M!K!" or "4CHN" or "FLT4" => 4,
            "6CHN" => 6,
            "8CHN" or "FLT8" => 8,
            _ when char.IsDigit(tag[0]) && tag.EndsWith("CHN", StringComparison.Ordinal) => tag[0] - '0',
            _ => 4
        };
    }

    /// <summary>
    /// Executes the WriteCell operation.
    /// </summary>
    private static void WriteCell(byte[] data, int offset, Note note)
    {
        int sample = Math.Clamp((int)note.InstrumentIndex, 0, 31);
        int period = PitchToPeriod(note.Pitch);
        byte effect = MapEffect(note, out byte param);

        data[offset + 0] = (byte)((sample & 0xF0) | ((period >> 8) & 0x0F));
        data[offset + 1] = (byte)(period & 0xFF);
        data[offset + 2] = (byte)(((sample & 0x0F) << 4) | (effect & 0x0F));
        data[offset + 3] = param;
    }

    /// <summary>
    /// Executes the PitchToPeriod operation.
    /// </summary>
    private static int PitchToPeriod(byte pitch)
    {
        if (pitch is 0 or >= (byte)SpecialNote.NoteOff)
            return 0;

        int index = pitch - 24;
        if (index < 0)
            index = 0;
        if (index >= Periods.Length)
            index = Periods.Length - 1;
        return Periods[index];
    }

    /// <summary>
    /// Executes the MapEffect operation.
    /// </summary>
    private static byte MapEffect(Note note, out byte param)
    {
        param = note.EffectParam;
        if (note.EffectColumn <= 0x0F)
            return note.EffectColumn;

        return note.Effect switch
        {
            EffectCommand.None => 0x0,
            EffectCommand.PortaUp => 0x1,
            EffectCommand.PortaDown => 0x2,
            EffectCommand.TonePorta => 0x3,
            EffectCommand.Vibrato => 0x4,
            EffectCommand.VolSlide => 0x5,
            EffectCommand.PortaVolSlide => 0x6,
            EffectCommand.Tremolo => 0x7,
            EffectCommand.SetPan => 0x8,
            EffectCommand.SampleOffset => 0x9,
            EffectCommand.VolumeSlide => 0xA,
            EffectCommand.PosJump => 0xB,
            EffectCommand.SetVolume => 0xC,
            EffectCommand.PatternBreak => 0xD,
            EffectCommand.SetSpeed or EffectCommand.SetBpm => 0xF,
            _ => 0
        };
    }
}
