using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.Core.Tests;

public sealed class ModuleExportTests
{
    [Fact]
    public void MidiExportImport_RoundTripsCurrentLaneNotes()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.mid");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Patterns = 1 });
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 48, DurationTicks = 4 });
            song.Patterns[0].SetNote(8, 0, new Note { Pitch = 64, InstrumentIndex = 1, Volume = 40, DurationTicks = 2 });

            MidiFile.ExportPatternChannel(song, 0, 0, path);
            var imported = MidiFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Equal(2, imported.Count);
            Assert.Equal(60, imported[0].Pitch);
            Assert.Equal(0, imported[0].StartTick);
            Assert.Equal(4, imported[0].DurationTicks);
            Assert.Equal(64, imported[1].Pitch);
            Assert.Equal(8, imported[1].StartTick);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void MidiExport_CanWriteMultiplePatternChannels()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.mid");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Patterns = 1 });
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 48, DurationTicks = 4 });
            song.Patterns[0].SetNote(4, 1, new Note { Pitch = 67, InstrumentIndex = 1, Volume = 48, DurationTicks = 4 });

            MidiFile.ExportPatternChannels(song, 0, [0, 1], path);
            byte[] bytes = File.ReadAllBytes(path);

            Assert.Equal("MThd", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(1, (bytes[8] << 8) | bytes[9]);
            Assert.Equal(3, (bytes[10] << 8) | bytes[11]);

            var imported = MidiFile.ImportPatternChannel(path, song.RowsPerBeat);
            Assert.Contains(imported, note => note.Pitch == 60 && note.StartTick == 0);
            Assert.Contains(imported, note => note.Pitch == 67 && note.StartTick == 4);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void MidiExport_CanWriteMultiplePatternsInSequence()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.mid");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Patterns = 1 });
            song.Patterns[0].RowCount = 8;
            song.Patterns[0].Resize(8, song.Patterns[0].ChannelCount);
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 48, DurationTicks = 2 });

            var second = new Pattern(8, song.Patterns[0].ChannelCount) { Name = "Second" };
            second.SetNote(0, 0, new Note { Pitch = 67, InstrumentIndex = 1, Volume = 48, DurationTicks = 2 });
            song.Patterns.Add(second);

            MidiFile.ExportPatternsChannels(song, [0, 1], [0], path);
            var imported = MidiFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Contains(imported, note => note.Pitch == 60 && note.StartTick == 0);
            Assert.Contains(imported, note => note.Pitch == 67 && note.StartTick == 8);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void MidiExport_InfersTrackerNoteDurationFromNoteOff()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.mid");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Patterns = 1 });
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 64, InstrumentIndex = 1, Volume = 50, DurationTicks = 1 });
            song.Patterns[0].SetNote(8, 0, new Note { Pitch = (byte)SpecialNote.NoteOff });

            MidiFile.ExportPatternChannel(song, 0, 0, path);
            var imported = MidiFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Single(imported);
            Assert.Equal(64, imported[0].Pitch);
            Assert.Equal(0, imported[0].StartTick);
            Assert.Equal(8, imported[0].DurationTicks);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void MidiExport_InfersTrackerNoteDurationAcrossSelectedPatterns()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.mid");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Patterns = 1 });
            song.Patterns[0].Resize(8, song.Patterns[0].ChannelCount);
            song.Patterns[0].SetNote(6, 0, new Note { Pitch = 64, InstrumentIndex = 1, Volume = 50, DurationTicks = 1 });

            var second = new Pattern(8, song.Patterns[0].ChannelCount) { Name = "Second" };
            second.SetNote(4, 0, new Note { Pitch = (byte)SpecialNote.NoteOff });
            song.Patterns.Add(second);

            MidiFile.ExportPatternsChannels(song, [0, 1], [0], path);
            var imported = MidiFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Single(imported);
            Assert.Equal(64, imported[0].Pitch);
            Assert.Equal(6, imported[0].StartTick);
            Assert.Equal(6, imported[0].DurationTicks);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmExport_WritesFastTrackerHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Title = "XM Export";
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 64, DurationTicks = 4 });

            XmModuleExporter.Save(song, path);

            byte[] header = File.ReadAllBytes(path).Take(17).ToArray();
            Assert.Equal("Extended Module: ", System.Text.Encoding.ASCII.GetString(header));
            Assert.True(new FileInfo(path).Length > 336);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmExport_WritesFastTrackerNoteNumberWithoutOctaveShift()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 64, DurationTicks = 4 });

            XmModuleExporter.Save(song, path);

            byte[] bytes = File.ReadAllBytes(path);
            int patternOffset = 336;
            uint headerLength = BitConverter.ToUInt32(bytes, patternOffset);
            int dataOffset = patternOffset + (int)headerLength;
            Assert.Equal(37, bytes[dataOffset]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmExport_ConvertsSidTraceCellsToXmSafeNotes()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault(new NewSongOptions { Channels = 4, Patterns = 1, RowsPerPattern = 64, IncludeDefaultSamples = false });
            song.Format = ModuleFormat.SID;
            song.Patterns[0].Resize(64, 4);
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                Volume = 48,
                VolumeColumn = 0x40,
                EffectColumn = 0x88,
                EffectParam = 0x05
            });
            song.Patterns[0].SetNote(0, 3, new Note
            {
                Pitch = 36,
                InstrumentIndex = 4,
                Volume = 40,
                VolumeColumn = 0x07,
                EffectColumn = 0x80,
                EffectParam = 0x7F
            });
            song.Patterns[0].SetNote(1, 1, new Note
            {
                Pitch = (byte)SpecialNote.NoteOff,
                InstrumentIndex = 2,
                VolumeColumn = 0x10,
                EffectColumn = 0x10,
                EffectParam = 0x00
            });

            XmModuleExporter.Save(song, path);

            byte[] bytes = File.ReadAllBytes(path);
            int patternOffset = 336;
            int dataOffset = patternOffset + (int)BitConverter.ToUInt32(bytes, patternOffset);
            Assert.Equal(37, bytes[dataOffset]);
            Assert.InRange(bytes[dataOffset + 1], (byte)1, (byte)6);
            Assert.Equal(0x40, bytes[dataOffset + 2]);
            Assert.NotEqual(0x88, bytes[dataOffset + 3]);

            int channel4 = dataOffset + 3 * 5;
            Assert.Equal(0, bytes[channel4]);
            Assert.Equal(0, bytes[channel4 + 1]);
            Assert.Equal(0, bytes[channel4 + 2]);
            Assert.Equal(0, bytes[channel4 + 3]);
            Assert.Equal(0, bytes[channel4 + 4]);

            int row1Channel2 = dataOffset + ((1 * 4) + 1) * 5;
            Assert.Equal(97, bytes[row1Channel2]);
            Assert.Equal(0, bytes[row1Channel2 + 1]);
            Assert.Equal(0, bytes[row1Channel2 + 2]);
            Assert.Equal(0, bytes[row1Channel2 + 3]);
            Assert.Equal(0, bytes[row1Channel2 + 4]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmExport_AllowsCurrentDirectoryFileName()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string tempDirectory = Path.Combine(Path.GetTempPath(), "amChipper-tests", Guid.NewGuid().ToString("N"));
        string fileName = "current-dir-export.xm";
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Environment.CurrentDirectory = tempDirectory;
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 60, InstrumentIndex = 1, Volume = 64, DurationTicks = 4 });

            XmModuleExporter.Save(song, fileName);

            Assert.True(File.Exists(Path.Combine(tempDirectory, fileName)));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void XmExport_PreservesRawVolumeColumnEffect()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                Volume = 255,
                VolumeColumn = 0x6F,
                DurationTicks = 4
            });

            XmModuleExporter.Save(song, path);

            byte[] bytes = File.ReadAllBytes(path);
            int patternOffset = 336;
            uint headerLength = BitConverter.ToUInt32(bytes, patternOffset);
            int dataOffset = patternOffset + (int)headerLength;
            Assert.Equal(0x6F, bytes[dataOffset + 2]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmExport_PreservesRawMainEffectColumn()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                EffectColumn = 0x21,
                EffectParam = 0x34,
                DurationTicks = 4
            });

            XmModuleExporter.Save(song, path);

            byte[] bytes = File.ReadAllBytes(path);
            int patternOffset = 336;
            uint headerLength = BitConverter.ToUInt32(bytes, patternOffset);
            int dataOffset = patternOffset + (int)headerLength;
            Assert.Equal(0x21, bytes[dataOffset + 3]);
            Assert.Equal(0x34, bytes[dataOffset + 4]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void XmPatternPatcher_KeepsOriginalInstrumentTail()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.xm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Format = ModuleFormat.XM;
            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 60,
                InstrumentIndex = 1,
                EffectColumn = 0x04,
                EffectParam = 0x21,
                DurationTicks = 4
            });

            XmModuleExporter.Save(song, path);
            byte[] original = File.ReadAllBytes(path);
            Assert.True(XmModulePatternPatcher.TryGetChangeSummary(song, original, out int unchangedPatterns, out int unchangedCells));
            Assert.Equal(0, unchangedPatterns);
            Assert.Equal(0, unchangedCells);

            song.Patterns[0].SetNote(0, 0, new Note
            {
                Pitch = 64,
                InstrumentIndex = 1,
                EffectColumn = 0x04,
                EffectParam = 0x21,
                DurationTicks = 4
            });

            Assert.True(XmModulePatternPatcher.TryCreatePatchedModule(song, original, out byte[] patched));
            int originalTail = FindInstrumentTailOffset(original);
            int patchedTail = FindInstrumentTailOffset(patched);
            Assert.Equal(original.AsSpan(originalTail).ToArray(), patched.AsSpan(patchedTail).ToArray());
            Assert.NotEqual(original, patched);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FLScoreExportImport_RoundTripsCurrentLaneNotes()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.fsc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 64, InstrumentIndex = 1, Volume = 50, Panning = 128, DurationTicks = 4 });
            song.Patterns[0].SetNote(4, 0, new Note { Pitch = 67, InstrumentIndex = 1, Volume = 45, Panning = 128, DurationTicks = 4 });

            FLScoreFile.ExportPatternChannel(song, 0, 0, path);
            var imported = FLScoreFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Equal(2, imported.Count);
            Assert.Equal(64, imported[0].Pitch);
            Assert.Equal(0, imported[0].StartTick);
            Assert.Equal(4, imported[0].DurationTicks);
            Assert.Equal(67, imported[1].Pitch);
            Assert.Equal(4, imported[1].StartTick);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FLScoreExport_InfersTrackerNoteDuration()
    {
        string path = Path.Combine(Path.GetTempPath(), "amChipper-tests", $"{Guid.NewGuid():N}.fsc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            Song song = Song.CreateDefault();
            song.Patterns[0].SetNote(0, 0, new Note { Pitch = 64, InstrumentIndex = 1, Volume = 50 });
            song.Patterns[0].SetNote(8, 0, new Note { Pitch = (byte)SpecialNote.NoteOff });

            FLScoreFile.ExportPatternChannel(song, 0, 0, path);
            var imported = FLScoreFile.ImportPatternChannel(path, song.RowsPerBeat);

            Assert.Single(imported);
            Assert.Equal(8, imported[0].DurationTicks);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FLScoreImport_ReadsBundledBasicScore()
    {
        string? root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root is not null; i++)
        {
            string candidate = Path.Combine(root, "4 steps.fsc");
            if (File.Exists(candidate))
            {
                var notes = FLScoreFile.ImportPatternChannel(candidate, 4);
                Assert.Equal(4, notes.Count);
                Assert.All(notes, n => Assert.Equal(48, n.Pitch));
                Assert.Equal([0L, 4L, 8L, 12L], notes.Select(n => n.StartTick).ToArray());
                return;
            }

            root = Directory.GetParent(root)?.FullName;
        }

        throw new FileNotFoundException("Bundled FSC sample was not found.", "4 steps.fsc");
    }

    [Fact]
    public void FLScoreImport_ReadsBundledFLStudioScoreLibrary()
    {
        string? root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root is not null; i++)
        {
            string candidate = Path.Combine(root, "flstudio_fsc");
            if (Directory.Exists(candidate))
            {
                string[] files = Directory.GetFiles(candidate, "*.fsc", SearchOption.AllDirectories);
                Assert.True(files.Length > 100);

                foreach (string file in files)
                {
                    var notes = FLScoreFile.ImportPatternChannel(file, 4);
                    Assert.NotEmpty(notes);
                    Assert.All(notes, note => Assert.InRange(note.Pitch, (byte)1, (byte)119));
                    Assert.True(notes.All(note => note.DurationTicks > 0), file);
                }

                return;
            }

            root = Directory.GetParent(root)?.FullName;
        }

        throw new DirectoryNotFoundException("Bundled FL Studio FSC library was not found.");
    }

    [Fact]
    public void FLScoreImport_ReadsModernFLStudioScoreLayout()
    {
        string? root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root is not null; i++)
        {
            string candidate = Path.Combine(root, "flstudio_fsc", "Quantization", "Alt shuffle.fsc");
            if (File.Exists(candidate))
            {
                var notes = FLScoreFile.ImportPatternChannel(candidate, 4);

                Assert.True(notes.Count >= 16);
                Assert.All(notes, note => Assert.Equal(60, note.Pitch));
                Assert.All(notes, note => Assert.InRange(note.Velocity, (byte)1, (byte)127));
                Assert.True(notes.Select(note => note.StartTick).Distinct().Count() > 8);
                return;
            }

            root = Directory.GetParent(root)?.FullName;
        }

        throw new FileNotFoundException("Bundled modern FSC sample was not found.", "Alt shuffle.fsc");
    }

    private static int FindInstrumentTailOffset(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = 60;
        uint headerSize = reader.ReadUInt32();
        long headerStart = stream.Position - sizeof(uint);
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        ushort patternCount = reader.ReadUInt16();
        stream.Position = headerStart + headerSize;

        for (int pattern = 0; pattern < patternCount; pattern++)
        {
            long headerOffset = stream.Position;
            uint patternHeaderLength = reader.ReadUInt32();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            ushort packedSize = reader.ReadUInt16();
            stream.Position = headerOffset + patternHeaderLength + packedSize;
        }

        return (int)stream.Position;
    }
}
