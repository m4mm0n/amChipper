using System.IO;

namespace amChipper.App.ViewModels;

/// <summary>
/// Represents a file shown in the sidebar chiptune library.
/// </summary>
public sealed class ChiptuneLibraryEntry
{
    public ChiptuneLibraryEntry(string path, string root)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        RelativePath = string.IsNullOrWhiteSpace(root)
            ? FileName
            : System.IO.Path.GetRelativePath(root, path);
        Extension = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        SizeText = FormatSize(new FileInfo(path).Length);
    }

    public string Path { get; }
    public string FileName { get; }
    public string Folder { get; }
    public string RelativePath { get; }
    public string Extension { get; }
    public string SizeText { get; }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}
