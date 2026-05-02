using amChipper.Core.Editing;
using amChipper.Core.Models;

namespace amChipper.Core.Tests;

public sealed class PianoRollLaneCommitterTests
{
    [Fact]
    public void Commit_ResizePreservesStartMidAndEndTrackerEffects()
    {
        var pattern = new Pattern(32, 1);
        pattern.SetNote(4, 0, new Note
        {
            Pitch = 60,
            InstrumentIndex = 2,
            Volume = 48,
            Effect = EffectCommand.Vibrato,
            EffectColumn = 0x04,
            EffectParam = 0x37,
            VolumeColumn = 0x4C
        });
        pattern.SetNote(6, 0, new Note
        {
            Effect = EffectCommand.VolumeSlide,
            EffectColumn = 0x0A,
            EffectParam = 0x0F
        });
        pattern.SetNote(8, 0, new Note
        {
            Pitch = (byte)SpecialNote.NoteOff,
            Effect = EffectCommand.NoteCut,
            EffectColumn = 0xEC,
            EffectParam = 0x03
        });

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, 0, 4, sources).ToList();
        notes[0].DurationTicks = 8;

        var result = PianoRollLaneCommitter.Commit(pattern, 0, notes, sources, () => 2);

        Assert.Equal(1, result.CommittedNotes);
        Assert.Equal(3, result.SourceEffectRows);
        Assert.Equal(3, result.EffectsAfter);

        AssertEffect(pattern.GetNote(4, 0), EffectCommand.Vibrato, 0x04, 0x37, 0x4C);
        AssertEffect(pattern.GetNote(6, 0), EffectCommand.VolumeSlide, 0x0A, 0x0F, 0x00);
        Assert.Equal((byte)SpecialNote.NoteOff, pattern.GetNote(12, 0).Pitch);
        AssertEffect(pattern.GetNote(12, 0), EffectCommand.NoteCut, 0xEC, 0x03, 0x00);
        AssertEffect(pattern.GetNote(8, 0), EffectCommand.None, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void Commit_MoveKeepsEffectOnlyRowsOutsideEditedSpan()
    {
        var pattern = new Pattern(32, 1);
        pattern.SetNote(2, 0, new Note
        {
            Pitch = 64,
            InstrumentIndex = 1,
            Effect = EffectCommand.TonePorta,
            EffectColumn = 0x03,
            EffectParam = 0x22
        });
        pattern.SetNote(5, 0, new Note { Pitch = (byte)SpecialNote.NoteOff });
        pattern.SetNote(20, 0, new Note
        {
            Effect = EffectCommand.SetSpeed,
            EffectColumn = 0x0F,
            EffectParam = 0x06
        });

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, 0, 4, sources).ToList();
        notes[0].StartTick = 10;

        PianoRollLaneCommitter.Commit(pattern, 0, notes, sources, () => 1);

        AssertEffect(pattern.GetNote(2, 0), EffectCommand.None, 0x00, 0x00, 0x00);
        AssertEffect(pattern.GetNote(10, 0), EffectCommand.TonePorta, 0x03, 0x22, 0x00);
        AssertEffect(pattern.GetNote(20, 0), EffectCommand.SetSpeed, 0x0F, 0x06, 0x00);
    }

    [Fact]
    public void Commit_DoesNotStealEffectsFromNextPitchedNote()
    {
        var pattern = new Pattern(32, 1);
        pattern.SetNote(4, 0, new Note { Pitch = 60, InstrumentIndex = 1 });
        pattern.SetNote(8, 0, new Note
        {
            Pitch = 64,
            InstrumentIndex = 1,
            Effect = EffectCommand.Vibrato,
            EffectColumn = 0x04,
            EffectParam = 0xF1
        });

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, 0, 4, sources).ToList();
        notes[0].DurationTicks = 2;

        PianoRollLaneCommitter.Commit(pattern, 0, notes, sources, () => 1);

        AssertEffect(pattern.GetNote(8, 0), EffectCommand.Vibrato, 0x04, 0xF1, 0x00);
    }

    [Fact]
    public void Commit_PreservesStartEffectWhenNextNoteImmediatelyEndsSpan()
    {
        var pattern = new Pattern(32, 1);
        pattern.SetNote(4, 0, new Note
        {
            Pitch = 60,
            InstrumentIndex = 1,
            Effect = EffectCommand.Vibrato,
            EffectColumn = 0x04,
            EffectParam = 0xF1
        });
        pattern.SetNote(5, 0, new Note { Pitch = 62, InstrumentIndex = 1 });

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, 0, 4, sources).ToList();

        PianoRollLaneCommitter.Commit(pattern, 0, notes, sources, () => 1);

        AssertEffect(pattern.GetNote(4, 0), EffectCommand.Vibrato, 0x04, 0xF1, 0x00);
    }

    [Fact]
    public void Commit_ShorteningNoteKeepsLaterTrackerEffectsOnOriginalRows()
    {
        var pattern = new Pattern(32, 1);
        pattern.SetNote(4, 0, new Note { Pitch = 60, InstrumentIndex = 1 });
        pattern.SetNote(10, 0, new Note
        {
            Effect = EffectCommand.Vibrato,
            EffectColumn = 0x04,
            EffectParam = 0xF1
        });
        pattern.SetNote(12, 0, new Note { Pitch = 64, InstrumentIndex = 1 });

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, 0, 4, sources).ToList();
        notes[0].DurationTicks = 3;

        PianoRollLaneCommitter.Commit(pattern, 0, notes, sources, () => 1);

        AssertEffect(pattern.GetNote(7, 0), EffectCommand.None, 0x00, 0x00, 0x00);
        AssertEffect(pattern.GetNote(10, 0), EffectCommand.Vibrato, 0x04, 0xF1, 0x00);
    }

    private static void AssertEffect(Note note, EffectCommand effect, byte effectColumn, byte effectParam, byte volumeColumn)
    {
        Assert.Equal(effect, note.Effect);
        Assert.Equal(effectColumn, note.EffectColumn);
        Assert.Equal(effectParam, note.EffectParam);
        Assert.Equal(volumeColumn, note.VolumeColumn);
    }
}
