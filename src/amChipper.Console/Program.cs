using amChipper.Audio.Engine;
using amChipper.Core.Editing;
using amChipper.Core.Interfaces;
using amChipper.Core.Models;
using amChipper.Core.Persistence;
using amChipper.App.Services;

namespace amChipper.Console;

/// <summary>
/// Represents the Program component.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int SampleRate = 44100;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    private const int RenderFrames = SampleRate * 8;

    /// <summary>
    /// Executes the Main operation.
    /// </summary>
    public static int Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var options = CliOptions.Parse(args);
        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return options.Command switch
            {
                "self-test" => RunSelfTest(options),
                "sid-xm-test" => RunSidXmTest(options),
                "nsf-batch" => RunNsfBatch(options),
                "sid-batch" => RunSidBatch(options),
                "chip-batch" => RunChipBatch(options),
                "lang-export" => RunLangExport(options),
                "lang-check" => RunLangCheck(options),
                "export-amc" => RunExportAmc(options),
                "dashboard" => RunDashboard(options),
                _ => RunDashboard(options)
            };
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"amChipper.Console failed: {ex.GetType().Name}: {ex.Message}");
            System.Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    /// <summary>
    /// Executes the RunDashboard operation.
    /// </summary>
    private static int RunDashboard(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InputPath) &&
            File.Exists(options.InputPath) &&
            ChipTuneFile.IsSupported(options.InputPath))
        {
            byte[] data = File.ReadAllBytes(options.InputPath);
            var chipSong = ChipTuneFile.ImportAsSong(data, options.InputPath);
            PrintChipDashboard(chipSong, options.InputPath);
            return 0;
        }

        using var module = LoadModule(options.InputPath, out Song song);
        PrintDashboard(song, module, options.InputPath);
        return 0;
    }

    /// <summary>
    /// Executes the RunSelfTest operation.
    /// </summary>
    private static int RunSelfTest(CliOptions options)
    {
        using var module = LoadModule(options.InputPath, out Song song);
        PrintDashboard(song, module, options.InputPath);

        int patternIndex = Math.Clamp(options.Pattern, 0, Math.Max(song.Patterns.Count - 1, 0));
        int channel = Math.Clamp(options.Channel, 0, Math.Max(song.Tracks.Count - 1, 0));
        var pattern = song.Patterns[patternIndex];
        channel = Math.Clamp(channel, 0, Math.Max(pattern.ChannelCount - 1, 0));

        var originalModulePeak = RenderModulePeak(module);
        float noEditPatchedPeak = RenderNoEditPatchedPeak(song);
        var originalSongPeak = RenderSequencerPeak(song, PlaybackScope.Song, patternIndex, 0, null, 0);
        var originalPatternPeak = RenderSequencerPeak(song, PlaybackScope.Pattern, patternIndex, 0, null, 0);
        var originalPianoPeak = RenderSequencerPeak(song, PlaybackScope.PianoRoll, patternIndex, 0, channel, 0);

        var beforeEffectRows = SnapshotEffects(pattern, channel);
        var beforeEffects = beforeEffectRows.Count(kv => kv.Value.HasEffect);
        XmModulePatternPatcher.TryGetChangeSummary(song, song.OriginalModuleData ?? [], out int noEditChangedPatterns, out int noEditChangedCells);
        XmModulePatternPatcher.TryGetFirstChangeDetails(song, song.OriginalModuleData ?? [], 3, out var noEditDetails);
        ApplyPianoRollStyleEdit(song, patternIndex, channel, options.Row, options.Pitch, options.DurationRows);
        XmModulePatternPatcher.TryGetChangeSummary(song, song.OriginalModuleData ?? [], out int editChangedPatterns, out int editChangedCells);
        var afterEffectRows = SnapshotEffects(pattern, channel);
        var afterEffects = afterEffectRows.Count(kv => kv.Value.HasEffect);
        var effectDiff = DescribeEffectDiff(beforeEffectRows, afterEffectRows).ToArray();

        var editedSongPeak = RenderSequencerPeak(song, PlaybackScope.Song, patternIndex, 0, null, 0);
        var editedPatternPeak = RenderSequencerPeak(song, PlaybackScope.Pattern, patternIndex, 0, null, 0);
        var editedPianoPeak = RenderSequencerPeak(song, PlaybackScope.PianoRoll, patternIndex, 0, channel, 0);

        string exportPath = options.ExportPath;
        if (song.OriginalModuleData is null ||
            !XmModulePatternPatcher.TrySavePatchedModule(song, song.OriginalModuleData, exportPath))
        {
            XmModuleExporter.Save(song, exportPath);
        }

        using var exported = new ModulePlayer(SampleRate, new ConsoleLogger("export"));
        bool exportedLoaded = exported.Load(File.ReadAllBytes(exportPath), Path.GetFileName(exportPath));
        var exportedPeak = exportedLoaded ? RenderModulePeak(exported) : 0f;

        PrintPanel("SELF TEST",
        [
            $"edit target        pattern {patternIndex}, channel {channel}, row {options.Row}, pitch {options.Pitch}",
            $"effects preserved  before {beforeEffects}, after {afterEffects}",
            $"effect row diffs   {effectDiff.Length}",
            $"patch changes      no-edit {noEditChangedPatterns}p/{noEditChangedCells}c, edited {editChangedPatterns}p/{editChangedCells}c",
            $"original peaks     module {originalModulePeak:0.0000}, no-edit-patch {noEditPatchedPeak:0.0000}, song {originalSongPeak:0.0000}",
            $"original scopes    pattern {originalPatternPeak:0.0000}, piano {originalPianoPeak:0.0000}",
            $"edited peaks       song {editedSongPeak:0.0000}, pattern {editedPatternPeak:0.0000}, piano {editedPianoPeak:0.0000}",
            $"exported XM        {exportPath}",
            $"exported peak      {(exportedLoaded ? exportedPeak.ToString("0.0000") : "load failed")}"
        ]);
        PrintPanel("EFFECT DIFF", effectDiff.Length == 0 ? ["none"] : effectDiff);
        PrintPanel("PATCH DIFF", noEditDetails.Count == 0 ? ["none"] : noEditDetails);

        bool ok = originalModulePeak > 0.0001f
            && editedSongPeak > 0.0001f
            && editedPatternPeak > 0.0001f
            && editedPianoPeak > 0.0001f
            && exportedLoaded
            && exportedPeak > 0.0001f
            && (noEditPatchedPeak <= 0 || Math.Abs(noEditPatchedPeak - originalModulePeak) < 0.01f)
            && afterEffects >= beforeEffects;

        System.Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 2;
    }

    /// <summary>
    /// Executes the RunSidXmTest operation.
    /// </summary>
    private static int RunSidXmTest(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
            throw new ArgumentException("--input must point at a SID file.");

        byte[] data = File.ReadAllBytes(options.InputPath);
        var song = ChipTuneFile.ImportAsSong(data, options.InputPath);
        if (song.Format != ModuleFormat.SID)
            throw new InvalidOperationException($"Expected SID input, got {song.Format}.");

        string exportPath = options.ExportPath;
        XmModuleExporter.Save(song, exportPath);

        using var exported = new ModulePlayer(SampleRate, new ConsoleLogger("sid-xm"));
        bool exportedLoaded = exported.Load(File.ReadAllBytes(exportPath), Path.GetFileName(exportPath));
        var exportedStats = exportedLoaded ? RenderModuleStats(exported) : new RenderStats(0, 0, 0);
        float sidPeak = Peak(InternalChipRenderer.RenderStereoFloat(data, options.InputPath, 8, SampleRate), RenderFrames);

        PrintHeader("amChipper SID to XM test");
        PrintPanel("SID SOURCE",
        [
            $"file       {Path.GetFileName(options.InputPath)}",
            $"title      {song.Title}",
            $"structure  {song.OrderList.Count} orders, {song.Patterns.Count} patterns, {song.Tracks.Count} channels",
            $"notes      {song.Patterns.Sum(CountNotes)} playable, {song.Patterns.Sum(CountEffects)} effects, {song.Patterns.Sum(CountVolumeColumns)} volfx",
            $"first note {DescribeFirstPlayableNote(song)}",
            $"sid peak   {sidPeak:0.0000}"
        ]);
        PrintPanel("EXPORTED XM",
        [
            $"path       {exportPath}",
            $"loaded     {exportedLoaded}",
            $"peak/rms   {exportedStats.Peak:0.0000} / {exportedStats.Rms:0.0000}",
            $"frames     {exportedStats.Frames}",
            $"duration   {(exportedLoaded ? exported.DurationSecs.ToString("0.00") : "n/a")}s",
            $"structure  {(exportedLoaded ? $"{exported.OrderCount} orders, {exported.PatternCount} patterns, {exported.ChannelCount} channels" : "n/a")}"
        ]);

        bool ok = sidPeak > 0.0001f && exportedLoaded && exportedStats.Peak > 0.0001f && exportedStats.Rms > 0.00001f && song.Patterns.Sum(CountNotes) > 0;
        System.Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 2;
    }

    /// <summary>
    /// Executes a batch NSF render diagnostic over a file or directory.
    /// </summary>
    private static int RunNsfBatch(CliOptions options)
    {
        string root = string.IsNullOrWhiteSpace(options.InputPath) ? "NSF" : options.InputPath;
        IEnumerable<string> candidates = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                    Path.GetExtension(path).Equals(".nsf", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(path).Equals(".nsfe", StringComparison.OrdinalIgnoreCase))
            : [root];

        string[] files = candidates.Skip(Math.Max(options.Skip, 0)).Take(Math.Max(options.Limit, 1)).ToArray();
        int ok = 0;
        int silent = 0;
        int failed = 0;
        var lines = new List<string>();
        foreach (string file in files)
        {
            var fileWatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var metadata = ChipTuneFile.ReadMetadata(data, file);
                float[] pcm = metadata.Format == ModuleFormat.NSF
                    ? RenderStreamingChip(data, file, options.Seconds, SampleRate)
                    : InternalChipRenderer.RenderStereoFloat(data, file, options.Seconds, SampleRate);
                var stats = Stats(pcm, Math.Min(pcm.Length / 2, options.Seconds * SampleRate));
                var song = ChipTuneFile.ImportAsSong(data, file);
                bool audible = stats.Peak > 0.0005f && stats.Rms > 0.00001;
                if (audible) ok++; else silent++;
                lines.Add($"{(audible ? "OK" : "SILENT"),-6} peak {stats.Peak:0.0000} rms {stats.Rms:0.000000} songs {metadata.SongCount,2} start {metadata.StartSong,2} exp {DescribeNsfExpansion(data),-13} ch {song.Tracks.Count,2} pat {song.Patterns.Count,2} notes {song.Patterns.Sum(CountNotes),4} fx {song.Patterns.Sum(CountEffects),4} {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                failed++;
                lines.Add($"FAIL   {Path.GetFileName(file)} :: {ex.GetType().Name}: {ex.Message}");
            }
        }

        PrintHeader("amChipper NSF batch diagnostic");
        PrintPanel("SUMMARY", [$"files {files.Length}", $"audible {ok}", $"silent {silent}", $"failed {failed}", $"seconds/file {options.Seconds}"]);
        foreach (var chunk in lines.Chunk(18))
            PrintPanel("FILES", chunk.ToArray());
        return failed == 0 && ok > 0 ? 0 : 2;
    }

    /// <summary>
    /// Renders a chip file through the same bounded streaming path used by app playback.
    /// </summary>
    private static float[] RenderStreamingChip(byte[] data, string sourcePath, int seconds, int sampleRate)
    {
        seconds = Math.Clamp(seconds, 1, 120);
        var renderer = InternalChipRenderer.CreateStreamingRenderer(data, sourcePath, sampleRate);
        int frames = seconds * sampleRate;
        float[] pcm = new float[frames * 2];
        const int chunkFrames = 1024;
        float[] chunk = new float[chunkFrames * 2];
        int offset = 0;
        while (offset < frames)
        {
            int framesThisChunk = Math.Min(chunkFrames, frames - offset);
            Array.Clear(chunk, 0, framesThisChunk * 2);
            renderer.Render(chunk, framesThisChunk, 2);
            Array.Copy(chunk, 0, pcm, offset * 2, framesThisChunk * 2);
            offset += framesThisChunk;
        }

        return pcm;
    }

    /// <summary>
    /// Executes a bounded SID/NSF render diagnostic over a file tree.
    /// </summary>
    private static int RunChipBatch(CliOptions options)
    {
        string root = string.IsNullOrWhiteSpace(options.InputPath) ? "." : options.InputPath;
        string[] extensions = [".sid", ".psid", ".rsid", ".nsf", ".nsfe"];
        IEnumerable<string> candidates = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            : [root];

        string[] files = candidates.Skip(Math.Max(options.Skip, 0)).Take(Math.Max(options.Limit, 1)).ToArray();
        int sid = 0;
        int nsf = 0;
        int ok = 0;
        int silent = 0;
        int failed = 0;
        var lines = new List<string>();

        foreach (string file in files)
        {
            var fileWatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var metadata = ChipTuneFile.ReadMetadata(data, file);
                if (metadata.Format == ModuleFormat.NSF) nsf++;
                if (metadata.Format == ModuleFormat.SID) sid++;

                float[] pcm = metadata.Format == ModuleFormat.NSF
                    ? RenderStreamingChip(data, file, options.Seconds, SampleRate)
                    : InternalChipRenderer.RenderStereoFloat(data, file, options.Seconds, SampleRate);
                var stats = Stats(pcm, Math.Min(pcm.Length / 2, options.Seconds * SampleRate));
                bool audible = stats.Peak > 0.0005f && stats.Rms > 0.00001;
                if (audible) ok++; else silent++;

                string detail = metadata.Format == ModuleFormat.NSF
                    ? $"songs {metadata.SongCount,2} start {metadata.StartSong,2} exp {DescribeNsfExpansion(data),-13}"
                    : $"songs {metadata.SongCount,2} start {metadata.StartSong,2} sid {metadata.Clock}/{metadata.SidModel}";
                var song = ChipTuneFile.ImportAsSong(data, file);
                lines.Add($"{(audible ? "OK" : "SILENT"),-6} {metadata.Format,-3} peak {stats.Peak:0.0000} rms {stats.Rms:0.000000} {detail} ch {song.Tracks.Count,2} pat {song.Patterns.Count,2} notes {song.Patterns.Sum(CountNotes),4} fx {song.Patterns.Sum(CountEffects),4} {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                failed++;
                lines.Add($"FAIL   {Path.GetFileName(file)} :: {ex.GetType().Name}: {ex.Message}");
            }
        }

        PrintHeader("amChipper chip batch diagnostic");
        PrintPanel("SUMMARY", [$"files {files.Length}", $"sid {sid}", $"nsf {nsf}", $"audible {ok}", $"silent {silent}", $"failed {failed}", $"seconds/file {options.Seconds}"]);
        foreach (var chunk in lines.Chunk(18))
            PrintPanel("FILES", chunk.ToArray());
        return failed == 0 && ok > 0 ? 0 : 2;
    }

    /// <summary>
    /// Executes a bounded SID render diagnostic over a file tree without tracker-trace import overhead.
    /// </summary>
    private static int RunSidBatch(CliOptions options)
    {
        string root = string.IsNullOrWhiteSpace(options.InputPath) ? "SID" : options.InputPath;
        string[] extensions = [".sid", ".psid", ".rsid"];
        IEnumerable<string> candidates = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            : [root];

        string[] files = candidates.Skip(Math.Max(options.Skip, 0)).Take(Math.Max(options.Limit, 1)).ToArray();
        int psid = 0;
        int rsid = 0;
        int ok = 0;
        int silent = 0;
        int failed = 0;
        var lines = new List<string>();
        int sampleRate = Math.Clamp(options.SampleRate, 8000, 192000);

        foreach (string file in files)
        {
            var fileWatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var metadata = ChipTuneFile.ReadMetadata(data, file);
                if (metadata.Format != ModuleFormat.SID)
                    throw new InvalidDataException($"Expected SID data, got {metadata.Format}.");
                if (metadata.Type.Equals("RSID", StringComparison.OrdinalIgnoreCase)) rsid++; else psid++;

                float[] pcm = InternalChipRenderer.RenderStereoFloat(data, file, options.Seconds, sampleRate);
                var stats = Stats(pcm, Math.Min(pcm.Length / 2, options.Seconds * sampleRate));
                bool audible = stats.Peak > 0.0005f && stats.Rms > 0.00001;
                if (audible) ok++; else silent++;

                fileWatch.Stop();
                lines.Add($"{(audible ? "OK" : "SILENT"),-6} {metadata.Type,-4} {fileWatch.ElapsedMilliseconds,5}ms peak {stats.Peak:0.0000} rms {stats.Rms:0.000000} songs {metadata.SongCount,2} start {metadata.StartSong,2} load ${metadata.LoadAddress:X4} init ${metadata.InitAddress:X4} play ${metadata.PlayAddress:X4} {metadata.Clock}/{metadata.SidModel} {file}");
            }
            catch (Exception ex)
            {
                fileWatch.Stop();
                failed++;
                lines.Add($"FAIL   {fileWatch.ElapsedMilliseconds,5}ms {file} :: {ex.GetType().Name}: {ex.Message}");
            }
        }

        PrintHeader("amChipper SID batch diagnostic");
        PrintPanel("SUMMARY", [$"files {files.Length}", $"skip {Math.Max(options.Skip, 0)}", $"psid {psid}", $"rsid {rsid}", $"audible {ok}", $"silent {silent}", $"failed {failed}", $"seconds/file {options.Seconds}", $"sample-rate {sampleRate}"]);
        foreach (var chunk in lines.Chunk(18))
            PrintPanel("FILES", chunk.ToArray());
        return failed == 0 && ok > 0 ? 0 : 2;
    }

    /// <summary>
    /// Exports editable JSON language packs.
    /// </summary>
    private static int RunLangExport(CliOptions options)
    {
        string output = string.IsNullOrWhiteSpace(options.InputPath) ? Path.Combine(AppContext.BaseDirectory, "lang") : options.InputPath;
        AppHelpContent.ExportLanguageFiles(output, options.Language, overwrite: true);
        PrintHeader("amChipper language export");
        PrintPanel("LANG", [$"output {output}", $"language {(string.IsNullOrWhiteSpace(options.Language) ? "all built-ins" : options.Language)}"]);
        return 0;
    }

    /// <summary>
    /// Validates editable JSON language packs.
    /// </summary>
    private static int RunLangCheck(CliOptions options)
    {
        string input = string.IsNullOrWhiteSpace(options.InputPath) ? Path.Combine(AppContext.BaseDirectory, "lang") : options.InputPath;
        var lines = AppHelpContent.ValidateLanguageFiles(input);
        PrintHeader("amChipper language pack check");
        foreach (var chunk in lines.Chunk(18))
            PrintPanel("LANG", chunk.ToArray());
        return lines.Any(line => line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
    }

    /// <summary>
    /// Executes the RunExportAmc operation.
    /// </summary>
    private static int RunExportAmc(CliOptions options)
    {
        using var module = LoadModule(options.InputPath, out Song song);
        string exportPath = Path.ChangeExtension(options.ExportPath, NativeChipModuleFile.Extension);
        NativeChipModuleFile.Save(song, exportPath);

        var loaded = NativeChipModuleFile.Load(exportPath);
        PrintHeader("amChipper AMC export");
        PrintPanel("SOURCE",
        [
            $"file       {Path.GetFileName(options.InputPath)}",
            $"title      {song.Title}",
            $"format     {song.Format}",
            $"duration   {module.DurationSecs:0.00}s",
            $"structure  {song.OrderList.Count} orders, {song.Patterns.Count} patterns, {song.Tracks.Count} channels"
        ]);
        PrintPanel("AMC OUTPUT",
        [
            $"path       {exportPath}",
            $"loaded     yes",
            $"title      {loaded.Title}",
            $"container  amChipper AMC (.amc)",
            $"embedded   {loaded.Format} {loaded.SourceModuleExtension} source",
            $"structure  {loaded.OrderList.Count} orders, {loaded.Patterns.Count} patterns, {loaded.Tracks.Count} channels",
            $"bytes      {new FileInfo(exportPath).Length}"
        ]);
        return 0;
    }

    /// <summary>
    /// Executes the LoadModule operation.
    /// </summary>
    private static ModulePlayer LoadModule(string path, out Song song)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = "Outlive no2.xm";

        var logger = new ConsoleLogger("module");
        if (Path.GetExtension(path).Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            song = NativeChipModuleFile.Load(path);
            byte[] embedded = song.OriginalModuleData
                ?? throw new InvalidOperationException("The AMC file does not contain an embedded source module for libopenmpt playback.");
            var nativeModule = new ModulePlayer(SampleRate, logger);
            string sourceName = $"{Path.GetFileNameWithoutExtension(path)}{song.SourceModuleExtension}";
            if (!nativeModule.Load(embedded, sourceName))
                throw new InvalidOperationException($"Could not load embedded module from AMC: {path}");
            return nativeModule;
        }

        byte[] data = File.ReadAllBytes(path);
        var module = new ModulePlayer(SampleRate, logger);
        if (!module.Load(data, Path.GetFileName(path)))
            throw new InvalidOperationException($"Could not load module: {path}");

        song = module.ImportAsSong() ?? throw new InvalidOperationException("Module import returned no song.");
        song.OriginalModuleData = data;
        return module;
    }

    /// <summary>
    /// Executes the PrintDashboard operation.
    /// </summary>
    private static void PrintDashboard(Song song, ModulePlayer module, string inputPath)
    {
        var vu = RenderModuleVuSnapshot(module, Math.Min(song.Tracks.Count, module.ChannelCount));
        bool isAmc = Path.GetExtension(inputPath).Equals(NativeChipModuleFile.Extension, StringComparison.OrdinalIgnoreCase);
        string formatLine = isAmc
            ? $"amChipper AMC (.amc), embedded {song.Format} {song.SourceModuleExtension} source"
            : song.Format.ToString();
        PrintHeader("amChipper headless console");
        PrintPanel("TRANSPORT",
        [
            $"module     {song.Title}",
            $"format     {formatLine}",
            $"duration   {module.DurationSecs:0.00}s",
            $"tempo      initial {song.Bpm} bpm, live {module.CurrentTempo} bpm, {song.RowsPerBeat} rows/beat, speed {module.CurrentSpeed}",
            $"structure  {song.OrderList.Count} orders, {song.Patterns.Count} patterns, {song.Tracks.Count} channels, {song.Instruments.Count} instruments"
        ]);

        PrintPanel("CHANNEL RACK",
            song.Tracks.Take(12).Select((track, index) =>
                $"{index + 1,2}. {track.Name,-18} inst {track.InstrumentIndex + 1,2} vol {track.Volume,3} pan {track.Panning,3} blocks {track.Blocks.Count,3}").ToArray());

        PrintPanel("PATTERN GRID",
            song.Patterns.Take(12).Select((pattern, index) =>
                $"{index,2}. {pattern.Name,-16} rows {pattern.RowCount,3} ch {pattern.ChannelCount,2} notes {CountNotes(pattern),4} effects {CountEffects(pattern),4} volfx {CountVolumeColumns(pattern),4}").ToArray());

        PrintPanel("MIXER",
            song.Tracks.Take(12).Select((track, index) =>
                $"{index + 1,2}. meter {(index < vu.Length ? vu[index] : 0),5:0.000}  mute {(track.Muted ? "yes" : " no")}  solo {(track.Solo ? "yes" : " no")}  vol {track.Volume / 128.0,5:0.00}  pan {(track.Panning / 255.0) * 2.0 - 1.0,5:0.00}").ToArray());
    }

    /// <summary>
    /// Prints an imported SID/NSF chip dashboard without routing through libopenmpt.
    /// </summary>
    private static void PrintChipDashboard(Song song, string inputPath)
    {
        PrintHeader("amChipper chip source dashboard");
        PrintPanel("TRANSPORT",
        [
            $"source     {Path.GetFileName(inputPath)}",
            $"title      {song.Title}",
            $"format     {song.Format} / {song.SourceModuleType}",
            $"structure  {song.OrderList.Count} orders, {song.Patterns.Count} patterns, {song.Tracks.Count} channels, {song.Instruments.Count} instruments",
            $"notes      {song.Patterns.Sum(CountNotes)} playable, {song.Patterns.Sum(CountEffects)} effects, {song.Patterns.Sum(CountVolumeColumns)} volfx",
            $"comment    {song.Comment}"
        ]);

        PrintPanel("CHANNEL RACK",
            song.Tracks.Take(16).Select((track, index) =>
                $"{index + 1,2}. {track.Name,-18} inst {track.InstrumentIndex + 1,2} vol {track.Volume,3} pan {track.Panning,3} blocks {track.Blocks.Count,3} fx {track.EffectSummary}").ToArray());

        PrintPanel("PATTERN GRID",
            song.Patterns.Take(16).Select((pattern, index) =>
                $"{index,2}. {pattern.Name,-18} rows {pattern.RowCount,3} ch {pattern.ChannelCount,2} notes {CountNotes(pattern),4} effects {CountEffects(pattern),4} volfx {CountVolumeColumns(pattern),4}").ToArray());
    }

    /// <summary>
    /// Executes the RenderModuleVuSnapshot operation.
    /// </summary>
    private static double[] RenderModuleVuSnapshot(ModulePlayer module, int channelCount)
    {
        var buffer = new float[1024 * 2];
        var vu = new double[Math.Max(0, channelCount)];
        for (int pass = 0; pass < 24; pass++)
        {
            int rendered = module.Render(buffer, 1024);
            if (rendered <= 0)
                break;

            for (int channel = 0; channel < vu.Length; channel++)
                vu[channel] = Math.Max(vu[channel], module.GetCurrentChannelVuMono(channel));
        }

        module.SeekToOrder(0, 0);
        return vu;
    }

    /// <summary>
    /// Executes the ApplyPianoRollStyleEdit operation.
    /// </summary>
    private static void ApplyPianoRollStyleEdit(Song song, int patternIndex, int channel, int row, byte pitch, int durationRows)
    {
        var pattern = song.Patterns[patternIndex];
        row = Math.Clamp(row, 0, Math.Max(pattern.RowCount - 1, 0));
        durationRows = Math.Max(1, durationRows);

        var sources = new Dictionary<Note, PianoRollNoteSource>(ReferenceEqualityComparer.Instance);
        var notes = PianoRollLaneCommitter.LoadNotes(pattern, channel, Math.Max(1, song.RowsPerBeat), sources)
            .ToList();

        var original = notes.FirstOrDefault(n => n.StartTick == row)
            ?? new Note { StartTick = row, DurationTicks = durationRows, InstrumentIndex = ResolveChannelInstrument(song, pattern, channel) };
        if (!notes.Contains(original))
            notes.Add(original);

        var edited = original;
        edited.Pitch = pitch;
        edited.InstrumentIndex = edited.InstrumentIndex > 0
            ? edited.InstrumentIndex
            : ResolveChannelInstrument(song, pattern, channel);
        edited.Volume = edited.Volume <= 64 ? edited.Volume : (byte)48;
        edited.Velocity = (byte)Math.Clamp(edited.Volume * 2, 1, 127);
        edited.StartTick = row;
        edited.DurationTicks = durationRows;

        PianoRollLaneCommitter.Commit(
            pattern,
            channel,
            notes,
            sources,
            () => ResolveChannelInstrument(song, pattern, channel));
    }

    /// <summary>
    /// Executes the ResolveChannelInstrument operation.
    /// </summary>
    private static byte ResolveChannelInstrument(Song song, Pattern pattern, int channel)
    {
        for (int row = 0; row < pattern.RowCount; row++)
        {
            var note = pattern.GetNote(row, channel);
            if (note.InstrumentIndex > 0)
                return note.InstrumentIndex;
        }

        int trackInstrument = channel >= 0 && channel < song.Tracks.Count ? song.Tracks[channel].InstrumentIndex + 1 : 1;
        return (byte)Math.Clamp(trackInstrument, 1, Math.Max(song.Instruments.Count, 1));
    }

    /// <summary>
    /// Executes the RenderModulePeak operation.
    /// </summary>
    private static float RenderModulePeak(ModulePlayer module)
    {
        var buffer = new float[1024 * 2];
        float peak = 0;
        int remaining = RenderFrames;
        while (remaining > 0)
        {
            int frames = Math.Min(1024, remaining);
            int rendered = module.Render(buffer, frames);
            if (rendered <= 0)
                break;

            peak = Math.Max(peak, Peak(buffer, rendered));
            remaining -= rendered;
        }

        return peak;
    }

    /// <summary>
    /// Executes the RenderModuleStats operation.
    /// </summary>
    private static RenderStats RenderModuleStats(ModulePlayer module)
    {
        var buffer = new float[1024 * 2];
        float peak = 0;
        double sumSquares = 0;
        long samples = 0;
        int framesRendered = 0;
        int remaining = RenderFrames;
        while (remaining > 0)
        {
            int frames = Math.Min(1024, remaining);
            int rendered = module.Render(buffer, frames);
            if (rendered <= 0)
                break;

            int limit = Math.Min(buffer.Length, rendered * 2);
            for (int i = 0; i < limit; i++)
            {
                float value = Math.Abs(buffer[i]);
                peak = Math.Max(peak, value);
                sumSquares += value * value;
            }

            samples += limit;
            framesRendered += rendered;
            remaining -= rendered;
        }

        return new RenderStats(peak, samples > 0 ? Math.Sqrt(sumSquares / samples) : 0, framesRendered);
    }

    /// <summary>
    /// Executes the RenderNoEditPatchedPeak operation.
    /// </summary>
    private static float RenderNoEditPatchedPeak(Song song)
    {
        if (song.OriginalModuleData is null ||
            !XmModulePatternPatcher.TryCreatePatchedModule(song, song.OriginalModuleData, out byte[] patched))
        {
            return 0;
        }

        using var module = new ModulePlayer(SampleRate, new ConsoleLogger("noop"));
        return module.Load(patched, "no-edit-patched.xm") ? RenderModulePeak(module) : 0;
    }

    /// <summary>
    /// Executes the RenderSequencerPeak operation.
    /// </summary>
    private static float RenderSequencerPeak(Song song, PlaybackScope scope, int pattern, int row, int? channel, double startBeat)
    {
        var sequencer = new InternalSequencer(SampleRate, new ConsoleLogger("seq"));
        sequencer.SetSong(song);
        sequencer.Play(scope, pattern, row, channel, startBeat);

        var buffer = new float[1024 * 2];
        float peak = 0;
        int remaining = RenderFrames;
        while (remaining > 0)
        {
            int frames = Math.Min(1024, remaining);
            sequencer.Render(buffer, frames);
            peak = Math.Max(peak, Peak(buffer, frames));
            remaining -= frames;
        }

        sequencer.Stop();
        return peak;
    }

    /// <summary>
    /// Executes the Peak operation.
    /// </summary>
    private static float Peak(float[] buffer, int frames)
    {
        float peak = 0;
        int limit = Math.Min(buffer.Length, frames * 2);
        for (int i = 0; i < limit; i++)
            peak = Math.Max(peak, Math.Abs(buffer[i]));
        return peak;
    }

    /// <summary>
    /// Calculates peak/RMS stats for an interleaved stereo float buffer.
    /// </summary>
    private static RenderStats Stats(float[] buffer, int frames)
    {
        float peak = 0;
        double sumSquares = 0;
        int limit = Math.Min(buffer.Length, frames * 2);
        for (int i = 0; i < limit; i++)
        {
            float value = Math.Abs(buffer[i]);
            if (!float.IsFinite(value))
                continue;
            peak = Math.Max(peak, value);
            sumSquares += value * value;
        }

        return new RenderStats(peak, limit > 0 ? Math.Sqrt(sumSquares / limit) : 0, frames);
    }

    /// <summary>
    /// Describes NSF expansion-chip flags for console diagnostics.
    /// </summary>
    private static string DescribeNsfExpansion(byte[] data)
    {
        if (data.Length <= 0x7B)
            return "2A03";

        byte flags = data[0x7B];
        if (flags == 0)
            return "2A03";

        var parts = new List<string>(6);
        if ((flags & 0x01) != 0) parts.Add("VRC6");
        if ((flags & 0x02) != 0) parts.Add("VRC7");
        if ((flags & 0x04) != 0) parts.Add("FDS");
        if ((flags & 0x08) != 0) parts.Add("MMC5");
        if ((flags & 0x10) != 0) parts.Add("N163");
        if ((flags & 0x20) != 0) parts.Add("S5B");
        return parts.Count == 0 ? $"0x{flags:X2}" : string.Join("+", parts);
    }

    /// <summary>
    /// Executes the CountNotes operation.
    /// </summary>
    private static int CountNotes(Pattern pattern)
    {
        int count = 0;
        foreach (var note in pattern.Notes)
        {
            if (note.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the CountEffects operation.
    /// </summary>
    private static int CountEffects(Pattern pattern)
    {
        int count = 0;
        foreach (var note in pattern.Notes)
        {
            if (note.EffectColumn != 0 || note.Effect != EffectCommand.None || note.EffectParam != 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the CountVolumeColumns operation.
    /// </summary>
    private static int CountVolumeColumns(Pattern pattern)
    {
        int count = 0;
        foreach (var note in pattern.Notes)
        {
            if (note.VolumeColumn != 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the DescribeFirstPlayableNote operation.
    /// </summary>
    private static string DescribeFirstPlayableNote(Song song)
    {
        for (int order = 0; order < song.OrderList.Count; order++)
        {
            int patternIndex = song.OrderList[order];
            if ((uint)patternIndex >= (uint)song.Patterns.Count)
                continue;

            var pattern = song.Patterns[patternIndex];
            for (int row = 0; row < pattern.RowCount; row++)
            {
                for (int channel = 0; channel < pattern.ChannelCount; channel++)
                {
                    var note = pattern.GetNote(row, channel);
                    if (note.Pitch is > 0 and < (byte)SpecialNote.NoteOff)
                        return $"order {order}, pattern {patternIndex}, row {row}, channel {channel}, {note.NoteName}, inst {note.InstrumentIndex}, vol {note.Volume}, vfx {note.VolumeColumn:X2}, fx {note.EffectColumn:X2}{note.EffectParam:X2}";
                }
            }
        }

        return "none";
    }

    /// <summary>
    /// Executes the CountEffects operation.
    /// </summary>
    private static int CountEffects(Pattern pattern, int channel)
    {
        int count = 0;
        for (int row = 0; row < pattern.RowCount; row++)
        {
            var note = pattern.GetNote(row, channel);
            if (note.EffectColumn != 0 || note.Effect != EffectCommand.None || note.EffectParam != 0 || note.VolumeColumn != 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Executes the EffectSnapshot operation.
    /// </summary>
    private static Dictionary<int, EffectSnapshot> SnapshotEffects(Pattern pattern, int channel)
    {
        var rows = new Dictionary<int, EffectSnapshot>();
        for (int row = 0; row < pattern.RowCount; row++)
        {
            var note = pattern.GetNote(row, channel);
            rows[row] = new EffectSnapshot(note.Effect, note.EffectColumn, note.EffectParam, note.VolumeColumn);
        }

        return rows;
    }

    /// <summary>
    /// Executes the DescribeEffectDiff operation.
    /// </summary>
    private static IEnumerable<string> DescribeEffectDiff(
        IReadOnlyDictionary<int, EffectSnapshot> before,
        IReadOnlyDictionary<int, EffectSnapshot> after)
    {
        foreach (int row in before.Keys.Concat(after.Keys).Distinct().OrderBy(r => r))
        {
            before.TryGetValue(row, out var oldFx);
            after.TryGetValue(row, out var newFx);
            if (!oldFx.Equals(newFx))
                yield return $"{row:D3}:{oldFx}->{newFx}";
        }
    }

    /// <summary>
    /// Executes the PrintHelp operation.
    /// </summary>
    private static void PrintHelp()
    {
        PrintHeader("amChipper.Console");
        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- dashboard --input \"Outlive no2.xm\"");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- self-test --input \"Outlive no2.xm\" --pattern 0 --channel 1");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- sid-xm-test --input \"c64\\Android\\Boppy.sid\" --export \"%TEMP%\\boppy.xm\"");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- nsf-batch --input \"NSF\" --limit 40 --seconds 3");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- sid-batch --input \"C:\\Music\\HVSC\" --limit 1000 --skip 0 --seconds 1 --sample-rate 8000");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- chip-batch --input \"C:\\Music\\Chiptunes\" --limit 80 --seconds 3");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- lang-export --input \"Ready2Release\\lang\"");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- lang-check --input \"Ready2Release\\lang\"");
        System.Console.WriteLine("  dotnet run --project src/amChipper.Console -c Release -p:Platform=x64 -- export-amc --input \"Outlive no2.xm\" --export \"examples\\Outlive no2.amc\"");
    }

    /// <summary>
    /// Executes the PrintHeader operation.
    /// </summary>
    private static void PrintHeader(string title)
    {
        System.Console.WriteLine("╔" + new string('═', 76) + "╗");
        System.Console.WriteLine($"║ {title,-74} ║");
        System.Console.WriteLine("╚" + new string('═', 76) + "╝");
    }

    /// <summary>
    /// Executes the PrintPanel operation.
    /// </summary>
    private static void PrintPanel(string title, IReadOnlyList<string> lines)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("┌─ " + title + " " + new string('─', Math.Max(0, 73 - title.Length)) + "┐");
        foreach (string line in lines.DefaultIfEmpty("(empty)"))
            System.Console.WriteLine($"│ {Truncate(line, 74),-74} │");
        System.Console.WriteLine("└" + new string('─', 76) + "┘");
    }

    /// <summary>
    /// Executes the Truncate operation.
    /// </summary>
    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..Math.Max(0, width - 1)] + "…";
}

/// <summary>
/// Represents the ConsoleLogger component.
/// </summary>
internal sealed class ConsoleLogger(string scope) : IAppLogger
{
    /// <summary>
    /// Executes the Debug operation.
    /// </summary>
    public void Debug(string message, string member = "", string file = "", int line = 0) { }
    /// <summary>
    /// Executes the Info operation.
    /// </summary>
    public void Info(string message, string member = "", string file = "", int line = 0) { }
    /// <summary>
    /// Executes the Warning operation.
    /// </summary>
    public void Warning(string message, string member = "", string file = "", int line = 0) => System.Console.Error.WriteLine($"[{scope}] warn: {message}");
    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(string message, string member = "", string file = "", int line = 0) => System.Console.Error.WriteLine($"[{scope}] error: {message}");
    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(Exception ex, string? message = null, string member = "", string file = "", int line = 0) => System.Console.Error.WriteLine($"[{scope}] error: {message ?? ex.Message}: {ex.Message}");
    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(string message, string member = "", string file = "", int line = 0) => System.Console.Error.WriteLine($"[{scope}] fatal: {message}");
    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(Exception ex, string? message = null, string member = "", string file = "", int line = 0) => System.Console.Error.WriteLine($"[{scope}] fatal: {message ?? ex.Message}: {ex.Message}");
}

/// <summary>
/// Carries CliOptions data.
/// </summary>
internal sealed record CliOptions(
    string Command,
    string InputPath,
    string ExportPath,
    int Pattern,
    int Channel,
    int Row,
    byte Pitch,
    int DurationRows,
    int Limit,
    int Skip,
    int Seconds,
    string Language,
    int SampleRate,
    bool Help)
{
    /// <summary>
    /// Executes the Parse operation.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        string command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "dashboard";
        string input = Get(args, "--input", "Outlive no2.xm");
        string export = Get(args, "--export", Path.Combine(Path.GetTempPath(), "amChipper-headless-export.xm"));
        int pattern = GetInt(args, "--pattern", 0);
        int channel = GetInt(args, "--channel", 0);
        int row = GetInt(args, "--row", 0);
        byte pitch = (byte)Math.Clamp(GetInt(args, "--pitch", 64), 1, 127);
        int duration = GetInt(args, "--duration", 4);
        int limit = GetInt(args, "--limit", 40);
        int skip = GetInt(args, "--skip", 0);
        int seconds = Math.Clamp(GetInt(args, "--seconds", 3), 1, 30);
        string language = Get(args, "--language", string.Empty);
        int sampleRate = Math.Clamp(GetInt(args, "--sample-rate", 44100), 8000, 192000);
        bool help = args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase);
        return new CliOptions(command, input, export, pattern, channel, row, pitch, duration, limit, skip, seconds, language, sampleRate, help);
    }

    /// <summary>
    /// Executes the Get operation.
    /// </summary>
    private static string Get(string[] args, string name, string fallback)
    {
        int index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    /// <summary>
    /// Executes the GetInt operation.
    /// </summary>
    private static int GetInt(string[] args, string name, int fallback) =>
        int.TryParse(Get(args, name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), out int value) ? value : fallback;
}

/// <summary>
/// Carries struct data.
/// </summary>
internal readonly record struct EffectSnapshot(
    EffectCommand Effect,
    byte EffectColumn,
    byte EffectParam,
    byte VolumeColumn)
{
    /// <summary>
    /// Stores or exposes HasEffect.
    /// </summary>
    public bool HasEffect =>
        Effect != EffectCommand.None || EffectColumn != 0 || EffectParam != 0 || VolumeColumn != 0;

    /// <summary>
    /// Executes the ToString operation.
    /// </summary>
    public override string ToString() =>
        HasEffect ? $"{EffectColumn:X2}{EffectParam:X2}/V{VolumeColumn:X2}" : "----/V00";
}

/// <summary>
/// Carries struct data.
/// </summary>
internal readonly record struct RenderStats(float Peak, double Rms, int Frames);
