using System.Runtime.InteropServices;

namespace amChipper.Audio.Interop;

/// <summary>
/// P/Invoke bindings for libopenmpt (x64 Windows DLL).
/// Download libopenmpt.dll from https://lib.openmpt.org/ and place it
/// alongside the application executable, or in src\amChipper.App\lib\.
///
/// libopenmpt supports MOD, XM, IT, S3M and many other formats.
/// </summary>
internal static partial class LibOpenMpt
{
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    private const string Dll = "libopenmpt";
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    internal const string ExtInterfaceInteractive = "interactive";
    /// <summary>
    /// Stores or exposes string.
    /// </summary>
    internal const string ExtInterfaceInteractive2 = "interactive2";

    // ── Module creation / destruction ─────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_create_from_memory2",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the CreateFromMemory operation.
    /// </summary>
    internal static partial nint CreateFromMemory(
        nint data, nuint size, nint logFunc, nint logUser,
        nint errFunc, nint errUser, nint errorOut,
        nint exceptionOut, nint ctls);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ext_create_from_memory",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the CreateExtFromMemory operation.
    /// </summary>
    internal static partial nint CreateExtFromMemory(
        nint data, nuint size, nint logFunc, nint logUser,
        nint errFunc, nint errUser, nint errorOut,
        nint exceptionOut, nint ctls);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_destroy")]
    /// <summary>
    /// Executes the Destroy operation.
    /// </summary>
    internal static partial void Destroy(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ext_destroy")]
    /// <summary>
    /// Executes the DestroyExt operation.
    /// </summary>
    internal static partial void DestroyExt(nint moduleExt);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ext_get_module")]
    /// <summary>
    /// Executes the GetExtModule operation.
    /// </summary>
    internal static partial nint GetExtModule(nint moduleExt);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ext_get_interface",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the GetExtInterface operation.
    /// </summary>
    internal static partial int GetExtInterface(
        nint moduleExt, string interfaceId, ref OpenMptInteractiveInterface interfaceData, nuint interfaceSize);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ext_get_interface",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the GetExtInterface2 operation.
    /// </summary>
    internal static partial int GetExtInterface2(
        nint moduleExt, string interfaceId, ref OpenMptInteractiveInterface2 interfaceData, nuint interfaceSize);

    // ── Metadata ──────────────────────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_metadata",
        StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    /// <summary>
    /// Executes the GetMetadata operation.
    /// </summary>
    internal static partial string? GetMetadata(nint module, string key);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_num_orders")]
    /// <summary>
    /// Executes the GetNumOrders operation.
    /// </summary>
    internal static partial int GetNumOrders(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_num_patterns")]
    /// <summary>
    /// Executes the GetNumPatterns operation.
    /// </summary>
    internal static partial int GetNumPatterns(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_num_channels")]
    /// <summary>
    /// Executes the GetNumChannels operation.
    /// </summary>
    internal static partial int GetNumChannels(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_channel_name",
        StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    /// <summary>
    /// Executes the GetChannelName operation.
    /// </summary>
    internal static partial string? GetChannelName(nint module, int index);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_num_instruments")]
    /// <summary>
    /// Executes the GetNumInstruments operation.
    /// </summary>
    internal static partial int GetNumInstruments(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_num_samples")]
    /// <summary>
    /// Executes the GetNumSamples operation.
    /// </summary>
    internal static partial int GetNumSamples(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_duration_seconds")]
    /// <summary>
    /// Executes the GetDuration operation.
    /// </summary>
    internal static partial double GetDuration(nint module);

    // ── Position ──────────────────────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_position_seconds")]
    /// <summary>
    /// Executes the GetPosition operation.
    /// </summary>
    internal static partial double GetPosition(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_set_position_seconds")]
    /// <summary>
    /// Executes the SetPosition operation.
    /// </summary>
    internal static partial double SetPosition(nint module, double seconds);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_order")]
    /// <summary>
    /// Executes the GetCurrentOrder operation.
    /// </summary>
    internal static partial int GetCurrentOrder(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_row")]
    /// <summary>
    /// Executes the GetCurrentRow operation.
    /// </summary>
    internal static partial int GetCurrentRow(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_pattern")]
    /// <summary>
    /// Executes the GetCurrentPattern operation.
    /// </summary>
    internal static partial int GetCurrentPattern(nint module);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_set_position_order_row")]
    /// <summary>
    /// Executes the SetPositionOrderRow operation.
    /// </summary>
    internal static partial double SetPositionOrderRow(nint module, int order, int row);

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>Render interleaved stereo float32 audio. Returns frames rendered.</summary>
    [LibraryImport(Dll, EntryPoint = "openmpt_module_read_interleaved_float_stereo")]
    /// <summary>
    /// Executes the ReadInterleavedFloatStereo operation.
    /// </summary>
    internal static partial nuint ReadInterleavedFloatStereo(
        nint module, int sampleRate, nuint count, nint output);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_channel_vu_mono")]
    /// <summary>
    /// Executes the GetCurrentChannelVuMono operation.
    /// </summary>
    internal static partial double GetCurrentChannelVuMono(nint module, int channel);

    // ── Pattern data (for import) ─────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_pattern_num_rows")]
    /// <summary>
    /// Executes the GetPatternNumRows operation.
    /// </summary>
    internal static partial int GetPatternNumRows(nint module, int pattern);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_pattern_row_channel_command",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the GetPatternRowChannelCommand operation.
    /// </summary>
    internal static partial int GetPatternRowChannelCommand(
        nint module, int pattern, int row, int channel, int command);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_format_pattern_row_channel_command",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the FormatPatternRowChannelCommand operation.
    /// </summary>
    internal static partial nint FormatPatternRowChannelCommand(
        nint module, int pattern, int row, int channel, int command);

    // ── Instrument / sample names ─────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_instrument_name",
        StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    /// <summary>
    /// Executes the GetInstrumentName operation.
    /// </summary>
    internal static partial string? GetInstrumentName(nint module, int index);

    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_sample_name",
        StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    /// <summary>
    /// Executes the GetSampleName operation.
    /// </summary>
    internal static partial string? GetSampleName(nint module, int index);

    // ── CTL (render settings) ─────────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_module_ctl_set",
        StringMarshalling = StringMarshalling.Utf8)]
    /// <summary>
    /// Executes the CtlSet operation.
    /// </summary>
    internal static partial int CtlSet(nint module, string ctl, string value);

    // ── Order list ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the pattern index for a given order position.
    /// Returns -1 for separator markers (+++ / ---) that should be skipped.
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_order_pattern")]
    /// <summary>
    /// Executes the GetOrderPattern operation.
    /// </summary>
    internal static partial int GetOrderPattern(nint module, int order);

    // ── Timing helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of rows that make up one beat in the loaded module,
    /// as stored/inferred by libopenmpt (0 if unknown / not supported).
    /// Available since libopenmpt 0.3.
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_rows_per_beat")]
    /// <summary>
    /// Executes the GetCurrentRowsPerBeat operation.
    /// </summary>
    internal static partial int GetCurrentRowsPerBeat(nint module);

    /// <summary>Returns the initial global tempo (BPM) of the module.</summary>
    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_tempo")]
    /// <summary>
    /// Executes the GetCurrentTempo operation.
    /// </summary>
    internal static partial int GetCurrentTempo(nint module);

    /// <summary>Returns the current tracker speed, i.e. ticks per row.</summary>
    [LibraryImport(Dll, EntryPoint = "openmpt_module_get_current_speed")]
    /// <summary>
    /// Executes the GetCurrentSpeed operation.
    /// </summary>
    internal static partial int GetCurrentSpeed(nint module);

    // ── Version ───────────────────────────────────────────────────────────────

    [LibraryImport(Dll, EntryPoint = "openmpt_get_library_version")]
    /// <summary>
    /// Executes the GetLibraryVersion operation.
    /// </summary>
    internal static partial uint GetLibraryVersion();

    // ── openmpt_module_get_pattern_row_channel_command constants ──────────────
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdNote = 0;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdInstrument = 1;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdVolumeEffect = 2;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdEffect = 3;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdVolume = 4;
    /// <summary>
    /// Stores or exposes int.
    /// </summary>
    internal const int PatCmdParam = 5;

    [StructLayout(LayoutKind.Sequential)]
    /// <summary>
    /// Represents OpenMptInteractiveInterface value data.
    /// </summary>
    internal struct OpenMptInteractiveInterface
    {
        /// <summary>
        /// Stores or exposes SetCurrentSpeed.
        /// </summary>
        public nint SetCurrentSpeed;
        /// <summary>
        /// Stores or exposes SetCurrentTempo.
        /// </summary>
        public nint SetCurrentTempo;
        /// <summary>
        /// Stores or exposes SetTempoFactor.
        /// </summary>
        public nint SetTempoFactor;
        /// <summary>
        /// Stores or exposes GetTempoFactor.
        /// </summary>
        public nint GetTempoFactor;
        /// <summary>
        /// Stores or exposes SetPitchFactor.
        /// </summary>
        public nint SetPitchFactor;
        /// <summary>
        /// Stores or exposes GetPitchFactor.
        /// </summary>
        public nint GetPitchFactor;
        /// <summary>
        /// Stores or exposes SetGlobalVolume.
        /// </summary>
        public nint SetGlobalVolume;
        /// <summary>
        /// Stores or exposes GetGlobalVolume.
        /// </summary>
        public nint GetGlobalVolume;
        /// <summary>
        /// Stores or exposes SetChannelVolume.
        /// </summary>
        public nint SetChannelVolume;
        /// <summary>
        /// Stores or exposes GetChannelVolume.
        /// </summary>
        public nint GetChannelVolume;
        /// <summary>
        /// Stores or exposes SetChannelMuteStatus.
        /// </summary>
        public nint SetChannelMuteStatus;
        /// <summary>
        /// Stores or exposes GetChannelMuteStatus.
        /// </summary>
        public nint GetChannelMuteStatus;
        /// <summary>
        /// Stores or exposes SetInstrumentMuteStatus.
        /// </summary>
        public nint SetInstrumentMuteStatus;
        /// <summary>
        /// Stores or exposes GetInstrumentMuteStatus.
        /// </summary>
        public nint GetInstrumentMuteStatus;
        /// <summary>
        /// Stores or exposes PlayNote.
        /// </summary>
        public nint PlayNote;
        /// <summary>
        /// Stores or exposes StopNote.
        /// </summary>
        public nint StopNote;
    }

    [StructLayout(LayoutKind.Sequential)]
    /// <summary>
    /// Represents OpenMptInteractiveInterface2 value data.
    /// </summary>
    internal struct OpenMptInteractiveInterface2
    {
        /// <summary>
        /// Stores or exposes NoteOff.
        /// </summary>
        public nint NoteOff;
        /// <summary>
        /// Stores or exposes NoteFade.
        /// </summary>
        public nint NoteFade;
        /// <summary>
        /// Stores or exposes SetChannelPanning.
        /// </summary>
        public nint SetChannelPanning;
        /// <summary>
        /// Stores or exposes GetChannelPanning.
        /// </summary>
        public nint GetChannelPanning;
        /// <summary>
        /// Stores or exposes SetNoteFinetune.
        /// </summary>
        public nint SetNoteFinetune;
        /// <summary>
        /// Stores or exposes GetNoteFinetune.
        /// </summary>
        public nint GetNoteFinetune;
    }
}
