using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.SidPlayer;

/// <summary>
/// Provides the standalone SID playback entry point used by amChipper and release plugins.
/// </summary>
public sealed class SidChipPlayer
{
    public const string PluginVersion = "v0.2.3.0";

    public ChipTuneMetadata ReadMetadata(byte[] sidData, string sourcePath) =>
        ChipTuneFile.ReadMetadata(sidData, sourcePath);

    public Song Import(byte[] sidData, string sourcePath) =>
        ChipTuneFile.ImportAsSong(sidData, sourcePath);

    public IReadOnlyList<SidVoiceRow> InspectVoiceRows(byte[] sidData, int frames = 512) =>
        InternalChipRenderer.InspectSidVoiceRows(sidData, frames);

    public float[] RenderStereoFloat(byte[] sidData, string sourcePath, int seconds, int sampleRate)
    {
        var metadata = ChipTuneFile.ReadMetadata(sidData, sourcePath);
        if (metadata.Format != ModuleFormat.SID)
            throw new InvalidDataException($"Expected SID data, got {metadata.Format}.");

        return InternalChipRenderer.RenderStereoFloat(sidData, sourcePath, seconds, sampleRate);
    }
}
