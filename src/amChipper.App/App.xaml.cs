using System.IO;
using System.Windows;
using amChipper.App.Services;
using amChipper.App.Views;

namespace amChipper.App;

/// <summary>
/// Represents the App component.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Executes the OnStartup operation.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 1. Enable libs/ probing before QuickLog or audio assemblies load ──
        RuntimeDependencyResolver.Configure();

        // ── 2. Initialise QuickLog ────────────────────────────────────────────
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "amChipper", "Logs");
        AppLogger.Initialise(logDir);
        AppLogger.Info("amChipper starting up.");
        AppHelpContent.EnsureLanguageFiles();

        // ── 3. Global unhandled-exception hook ────────────────────────────────
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLogger.Fatal(ex.Exception, "Unhandled UI exception");
            ex.Handled = true; // prevent WPF crash dialog
            MessageBox.Show($"Unhandled error:\n{ex.Exception.Message}",
                "amChipper", MessageBoxButton.OK, MessageBoxImage.Error);
            // If no window is visible yet, shut down cleanly instead of hanging
            if (MainWindow is null || !MainWindow.IsVisible)
                Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception exc)
                AppLogger.Fatal(exc, "Unhandled domain exception");
        };

        // ── 4. Check / download libopenmpt.dll ───────────────────────────────
        // Must happen BEFORE MainWindow is created: constructing MainWindow creates
        // AudioEngine → ModulePlayer → CheckDll(), which loads the DLL via P/Invoke
        // and permanently caches whether it's usable. Running the check afterward
        // means the cache reflects the pre-download state.
        //
        // Use OnExplicitShutdown so BootstrapWindow closing doesn't trigger
        // OnMainWindowClose and kill the process before MainWindow ever appears.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool downloadLibOpenMpt = false;
        if (!DependencyBootstrapper.IsLibOpenMptPresent())
        {
            AppLogger.Info("libopenmpt.dll missing or invalid — prompting user.");

            var result = MessageBox.Show(
                "libopenmpt.dll was not found or is invalid.\n\n" +
                "This library is required to open MOD / XM / IT / S3M files.\n\n" +
                "Download it now automatically? (~10 MB from lib.openmpt.org)\n\n" +
                "Choose No to skip — module file playback will be unavailable.",
                "amChipper — Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DependencyBootstrapper.DeleteInvalidDll();
                downloadLibOpenMpt = true;
            }
            else
            {
                AppLogger.Info("User chose to skip libopenmpt download. Module playback unavailable.");
            }
        }

        bool startupOk = await BootstrapWindow.RunStartupAsync(downloadLibOpenMpt);
        if (!startupOk)
        {
            AppLogger.Warning("Startup splash did not complete successfully; shutting down.");
            Shutdown(1);
            return;
        }

        if (downloadLibOpenMpt)
            AppLogger.Info("libopenmpt.dll downloaded successfully.");

        // ── 5. Create and show MainWindow ─────────────────────────────────────
        // DLL is now in place (or intentionally skipped). ModulePlayer.CheckDll()
        // will run here and see the correct library for the first time.
        AppLogger.Info("Launching MainWindow.");
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose; // Normal lifetime from here
        mainWindow.Show();
    }

    /// <summary>
    /// Executes the OnExit operation.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"amChipper exiting (code {e.ApplicationExitCode}).");
        AppLogger.Shutdown();
        base.OnExit(e);
    }
}
