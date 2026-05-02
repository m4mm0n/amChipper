using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using amChipper.Core.Models;

namespace amChipper.App.Services;

/// <summary>
/// Registers amChipper as a per-user Open With target for supported music/project files.
/// </summary>
public static class FileAssociationService
{
    private const int ShcneAssocchanged = 0x08000000;
    private const int ShcnfIdlist = 0x0000;

    /// <summary>
    /// Registers the current executable as an Open With handler without requiring administrator rights.
    /// </summary>
    public static int RegisterCurrentExecutable()
    {
        string executable = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not resolve the current amChipper executable path.");

        string command = $"\"{executable}\" \"%1\"";
        string progId = "ZeroLinez.amChipper.File";

        using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
        {
            progKey?.SetValue(string.Empty, "amChipper supported file");
            progKey?.SetValue("FriendlyTypeName", "amChipper supported file");
        }

        using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon"))
            iconKey?.SetValue(string.Empty, $"\"{executable}\",0");

        using (var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
            commandKey?.SetValue(string.Empty, command);

        using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\amChipper.exe"))
        {
            appKey?.SetValue("FriendlyAppName", "amChipper");
        }

        using (var appCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\amChipper.exe\shell\open\command"))
            appCommandKey?.SetValue(string.Empty, command);

        var extensions = GetSupportedExtensions();
        foreach (string extension in extensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith?.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        SHChangeNotify(ShcneAssocchanged, ShcnfIdlist, nint.Zero, nint.Zero);
        return extensions.Count;
    }

    /// <summary>
    /// Returns every extension amChipper should expose in the Windows Open With list.
    /// </summary>
    private static IReadOnlyList<string> GetSupportedExtensions()
    {
        return ModuleFormatCatalog.Formats
            .Select(format => format.Extension)
            .Concat([".amchip", ".fsc", ".mid", ".midi"])
            .Where(extension => extension.StartsWith(".", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, nint item1, nint item2);
}
