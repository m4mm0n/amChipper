using amChipper.Core.Interfaces;

namespace amChipper.Audio.Engine;

public static class ModulePlayerFactory
{
    public const string BackendEnvironmentVariable = "AMCHIPPER_OPENMPT_BACKEND";
    public const string BackendMarkerFileName = "amChipper.openmpt-backend";
    public const string NativeBackendName = "native";
    public const string ManagedBackendName = "managed";

    public static IModulePlayer Create(int sampleRate = 44100, IAppLogger? logger = null)
    {
        if (IsManagedBackendRequested())
        {
#if AMCHIPPER_LIBOPENMPT_NET
            return new ManagedModulePlayer(sampleRate, logger);
#else
            logger?.Warning("Managed libopenmpt.net backend was requested, but libopenmpt.net is not available at build time. Falling back to native libopenmpt.");
#endif
        }

        return new ModulePlayer(sampleRate, logger);
    }

    public static bool IsManagedBackendRequested() =>
        string.Equals(ResolveBackendName(), ManagedBackendName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ResolveBackendName(), "libopenmpt.net", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ResolveBackendName(), "dotnet", StringComparison.OrdinalIgnoreCase);

    public static string ResolveBackendName()
    {
        string? environment = Environment.GetEnvironmentVariable(BackendEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environment))
            return environment.Trim();

        string markerPath = Path.Combine(AppContext.BaseDirectory, BackendMarkerFileName);
        if (File.Exists(markerPath))
        {
            string marker = File.ReadLines(markerPath).FirstOrDefault() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(marker))
                return marker.Trim();
        }

        return NativeBackendName;
    }
}
