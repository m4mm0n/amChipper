using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.NsfPlayer;

/// <summary>
/// Provides the standalone NSF playback and trace entry point used by amChipper and release plugins.
/// </summary>
public sealed class NsfChipPlayer
{
    public const string PluginVersion = "v0.2.1.0";

    public ChipTuneMetadata ReadMetadata(byte[] nsfData, string sourcePath) =>
        ChipTuneFile.ReadMetadata(nsfData, sourcePath);

    public Song Import(byte[] nsfData, string sourcePath) =>
        ChipTuneFile.ImportAsSong(nsfData, sourcePath);

    public IReadOnlyList<NsfVoiceRow> InspectVoiceRows(byte[] nsfData, int rows = 512, int? songNumber = null, int maxMilliseconds = 1200) =>
        InternalChipRenderer.InspectNsfVoiceRows(nsfData, rows, songNumber, maxMilliseconds);

    public float[] RenderStereoFloat(byte[] nsfData, string sourcePath, int seconds, int sampleRate)
    {
        var metadata = ChipTuneFile.ReadMetadata(nsfData, sourcePath);
        if (metadata.Format != ModuleFormat.NSF)
            throw new InvalidDataException($"Expected NSF data, got {metadata.Format}.");

        return InternalChipRenderer.RenderStereoFloat(nsfData, sourcePath, seconds, sampleRate);
    }
}
