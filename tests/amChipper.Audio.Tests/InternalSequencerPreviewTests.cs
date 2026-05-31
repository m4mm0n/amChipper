/*
 * ====================================================================================================
 *  Project        : amChipper
 *  File           : InternalSequencerPreviewTests.cs
 *  Author         : ZeroLinez Softworx
 *  Created By     : Codex
 *  Created        : 2026-05-31 18:05
 *  Modified By    : Codex
 *  Last Modified  : 2026-05-31 18:05
 *  CRC32          : 0924C316
 *
 *  Description    :
 *                   Regression tests for InternalSequencer note-preview voice lifecycle behavior.
 *
 *  License        :
 *                   GPL-3.0-only
 *                   https://www.gnu.org/licenses/gpl-3.0.html
 *
 *  Notes          :
 *                   Verifies that piano-roll audition voices can be observed and stopped without
 *                   requiring the managed libopenmpt.net backend.
 * ====================================================================================================
 */

// CRC32-BODY: 0924C316

using amChipper.Audio.Engine;
using amChipper.Core.Models;

namespace amChipper.Audio.Tests;

/// <summary>
/// Regression coverage for sequencer-backed note preview voices.
/// </summary>
public sealed class InternalSequencerPreviewTests
{
    /// <summary>
    /// Verifies that preview voices are created and removed by the public preview API.
    /// </summary>
    [Fact]
    public void PreviewNoteCreatesAndStopsPreviewVoice()
    {
        var sequencer = new InternalSequencer();
        var song = Song.CreateDefault(new NewSongOptions
        {
            Format = ModuleFormat.AmChip,
            Channels = 1,
            Patterns = 1,
            IncludeDefaultSamples = true
        });

        sequencer.SetSong(song);

        Assert.False(sequencer.HasPreviewVoices);

        sequencer.PreviewNote(60, 0, 0, velocity: 100, milliseconds: 250);

        Assert.True(sequencer.HasPreviewVoices);

        sequencer.StopPreviewNote(0);

        Assert.False(sequencer.HasPreviewVoices);
    }
}
