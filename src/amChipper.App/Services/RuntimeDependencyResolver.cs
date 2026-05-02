using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

namespace amChipper.App.Services;

/// <summary>
/// Represents the RuntimeDependencyResolver component.
/// </summary>
public static class RuntimeDependencyResolver
{
    /// <summary>
    /// Stores or exposes _configured.
    /// </summary>
    private static bool _configured;
    /// <summary>
    /// Stores or exposes _dllDirectoryCookie.
    /// </summary>
    private static nint _dllDirectoryCookie;
    private static readonly object Sync = new();
    /// <summary>
    /// Stores or exposes LoadEvents.
    /// </summary>
    private static readonly List<DependencyLoadInfo> LoadEvents = [];

    /// <summary>
    /// Executes the LibsDirectory operation.
    /// </summary>
    public static string LibsDirectory => Path.Combine(AppContext.BaseDirectory, "libs");
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public static event EventHandler<DependencyLoadInfo>? DependencyLoaded;

    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    public static void Configure()
    {
        if (_configured)
            return;

        _configured = true;
        Directory.CreateDirectory(LibsDirectory);
        Record("libs directory", LibsDirectory, "ready");

        AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedAssembly;
        AssemblyLoadContext.Default.Resolving += ResolveManagedAssemblyLoadContext;

        if (OperatingSystem.IsWindows())
        {
            SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs | LoadLibrarySearchUserDirs);
            _dllDirectoryCookie = AddDllDirectory(LibsDirectory);
            Record("native search path", LibsDirectory, _dllDirectoryCookie == nint.Zero ? "failed" : "ready");
        }

        var audioAssembly = Assembly.Load("amChipper.Audio");
        Record("amChipper.Audio.dll", audioAssembly.Location, "loaded");
        NativeLibrary.SetDllImportResolver(audioAssembly, ResolveNativeLibrary);
    }

    /// <summary>
    /// Executes the GetLoadEventsSnapshot operation.
    /// </summary>
    public static IReadOnlyList<DependencyLoadInfo> GetLoadEventsSnapshot()
    {
        lock (Sync)
            return LoadEvents.ToArray();
    }

    /// <summary>
    /// Executes the GetKnownDependencyFiles operation.
    /// </summary>
    public static IReadOnlyList<DependencyLoadInfo> GetKnownDependencyFiles()
    {
        if (!Directory.Exists(LibsDirectory))
            return [];

        return Directory.EnumerateFiles(LibsDirectory, "*.dll")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => new DependencyLoadInfo(Path.GetFileName(path), path, "available"))
            .ToArray();
    }

    /// <summary>
    /// Executes the ResolveManagedAssembly operation.
    /// </summary>
    private static Assembly? ResolveManagedAssembly(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        return ResolveManagedAssemblyByName(name);
    }

    /// <summary>
    /// Resolves .NET Core assembly-load-context misses from the local libs directory.
    /// </summary>
    private static Assembly? ResolveManagedAssemblyLoadContext(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        return ResolveManagedAssemblyByName(assemblyName.Name);
    }

    /// <summary>
    /// Loads a managed dependency from the local libs directory by simple assembly name.
    /// </summary>
    private static Assembly? ResolveManagedAssemblyByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string candidate = Path.Combine(LibsDirectory, $"{name}.dll");
        if (!File.Exists(candidate))
            return null;

        Record($"{name}.dll", candidate, "loaded");
        return Assembly.LoadFrom(candidate);
    }

    /// <summary>
    /// Executes the ResolveNativeLibrary operation.
    /// </summary>
    private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        string fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName
            : $"{libraryName}.dll";
        string candidate = Path.Combine(LibsDirectory, fileName);

        if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, assembly, searchPath, out nint handle))
        {
            Record(fileName, candidate, "loaded");
            return handle;
        }

        return nint.Zero;
    }

    [DllImport("kernel32", SetLastError = true)]
    /// <summary>
    /// Executes the SetDefaultDllDirectories operation.
    /// </summary>
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    /// <summary>
    /// Executes the AddDllDirectory operation.
    /// </summary>
    private static extern nint AddDllDirectory(string newDirectory);

    /// <summary>
    /// Stores or exposes uint.
    /// </summary>
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    /// <summary>
    /// Stores or exposes uint.
    /// </summary>
    private const uint LoadLibrarySearchUserDirs = 0x00000400;

    /// <summary>
    /// Executes the Record operation.
    /// </summary>
    private static void Record(string name, string path, string state)
    {
        var info = new DependencyLoadInfo(name, path, state);
        lock (Sync)
            LoadEvents.Add(info);
        DependencyLoaded?.Invoke(null, info);
    }
}

/// <summary>
/// Carries DependencyLoadInfo data.
/// </summary>
public sealed record DependencyLoadInfo(string Name, string Path, string State)
{
    /// <summary>
    /// Stores or exposes Display.
    /// </summary>
    public string Display => $"{Name} - {State}";
}
