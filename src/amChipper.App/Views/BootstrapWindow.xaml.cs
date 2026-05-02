using System.IO;
using System.Windows;
using amChipper.App.Services;

namespace amChipper.App.Views;

/// <summary>
/// Represents the BootstrapWindow component.
/// </summary>
public partial class BootstrapWindow : Window
{
    /// <summary>
    /// Executes the MinimumSplashDuration operation.
    /// </summary>
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromSeconds(2);

    private readonly DependencyBootstrapper _bootstrapper = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _language = AppConfigurationStore.Load().Language;
    /// <summary>
    /// Stores or exposes Succeeded.
    /// </summary>
    public bool Succeeded { get; private set; }

    public BootstrapWindow()
    {
        InitializeComponent();
        ApplyStaticTranslations();
        _bootstrapper.StatusChanged += OnStatus;
        _bootstrapper.ProgressChanged += OnProgress;
        RuntimeDependencyResolver.DependencyLoaded += OnDependencyLoaded;
    }

    /// <summary>
    /// Applies translated splash-screen labels before the longer startup stages begin.
    /// </summary>
    private void ApplyStaticTranslations()
    {
        TxtStatus.Text = T("SplashStarting");
        TxtDetail.Text = T("SplashPreparing");
        TxtTagline.Text = T("SplashTagline");
        BtnCancel.Content = T("Cancel");
    }

    /// <summary>
    /// Executes the RunStartupAsync operation.
    /// </summary>
    public static async Task<bool> RunStartupAsync(bool downloadLibOpenMpt)
    {
        var win = new BootstrapWindow();
        win.Show();
        await win.ExecuteStartupAsync(downloadLibOpenMpt);
        RuntimeDependencyResolver.DependencyLoaded -= win.OnDependencyLoaded;
        if (win.Succeeded)
            win.Close();
        return win.Succeeded;
    }

    /// <summary>
    /// Executes the ExecuteStartupAsync operation.
    /// </summary>
    private async Task ExecuteStartupAsync(bool downloadLibOpenMpt)
    {
        try
        {
            var started = DateTime.UtcNow;
            await SetStageAsync(T("SplashLoadingTheme"), T("SplashLoadingThemeDetail"), 8);
            await ShowDependencyLoadsAsync(10, 32);
            await SetStageAsync(T("SplashPreparingAudio"), T("SplashPreparingAudioDetail"), 34);
            await SetStageAsync(T("SplashCheckingTracker"), T("SplashCheckingTrackerDetail"), 40);

            if (downloadLibOpenMpt)
            {
                TxtDetail.Text = "Downloading libopenmpt so XM, MOD, S3M, IT, MPTM, MED, OKT, 669, MTM and the broader tracker-module catalog can open correctly.";
                SetProgress(44, T("SplashDownloadingTracker"));
                await _bootstrapper.DownloadLibOpenMptAsync(_cts.Token);
                SetProgress(82, T("SplashTrackerReady"));
            }
            else
            {
                await SetStageAsync(T("SplashTrackerReady"), T("SplashTrackerReadyDetail"), 74);
            }

            await SetStageAsync(T("SplashBuildingWorkspace"), T("SplashBuildingWorkspaceDetail"), 88);
            await SetStageAsync(T("SplashOpeningDaw"), T("SplashOpeningDawDetail"), 100);

            var elapsed = DateTime.UtcNow - started;
            if (elapsed < MinimumSplashDuration)
                await Task.Delay(MinimumSplashDuration - elapsed, _cts.Token);

            Succeeded = true;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warning("Startup cancelled by user.");
            TxtStatus.Text = T("SplashStartupCancelled");
            TxtDetail.Text = T("SplashStartupCancelled");
            BtnCancel.Content = T("Close");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Startup failed");
            TxtStatus.Text = T("SplashStartupFailed");
            BtnCancel.Content = T("Close");

            string inner = ex.InnerException is not null
                ? $"\n\nInner: {ex.InnerException.Message}"
                : string.Empty;
            string logHint = $"\n\nSee log: %LOCALAPPDATA%\\amChipper\\Logs\\amChipper.log";
            TxtDetail.Text = $"{ex.GetType().Name}: {ex.Message}{inner}{logHint}";
        }
    }

    /// <summary>
    /// Executes the ShowDependencyLoadsAsync operation.
    /// </summary>
    private async Task ShowDependencyLoadsAsync(double startPercent, double endPercent)
    {
        var dependencies = RuntimeDependencyResolver.GetLoadEventsSnapshot()
            .Concat(RuntimeDependencyResolver.GetKnownDependencyFiles())
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(d => d.State == "loaded") ?? g.First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (dependencies.Count == 0)
        {
            await SetStageAsync(T("SplashScanningDependencies"), T("SplashNoDependencies"), endPercent);
            return;
        }

        for (int i = 0; i < dependencies.Count; i++)
        {
            var dll = dependencies[i];
            double percent = startPercent + ((i + 1) / (double)dependencies.Count) * (endPercent - startPercent);
            TxtStatus.Text = string.Format(T("SplashLoadingDependency"), i + 1, dependencies.Count);
            TxtDetail.Text = $"{dll.Name}  [{dll.State}]\n{dll.Path}";
            SetProgress(percent, Path.GetFileName(dll.Path));
            AppLogger.Info($"[StartupDependency] name=\"{dll.Name}\" state={dll.State} path=\"{dll.Path}\"");
            await Task.Delay(180, _cts.Token);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the OnStatus operation.
    /// </summary>
    private void OnStatus(object? sender, string msg)
        => Dispatcher.Invoke(() => TxtStatus.Text = msg);

    /// <summary>
    /// Executes the OnProgress operation.
    /// </summary>
    private void OnProgress(object? sender, DownloadProgressArgs e) =>
        Dispatcher.Invoke(() =>
        {
            SetIndeterminate(false);
            ProgressBar.Value = 42 + e.Percent * 0.38;
            TxtProgress.Text = e.Display;
        });

    /// <summary>
    /// Executes the OnDependencyLoaded operation.
    /// </summary>
    private void OnDependencyLoaded(object? sender, DependencyLoadInfo info)
        => Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = $"{T("Loaded")} {info.Name}";
            TxtDetail.Text = info.Path;
        });

    /// <summary>
    /// Executes the BtnCancel_Click operation.
    /// </summary>
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (BtnCancel.Content?.ToString() == T("Close"))
        {
            Close();
            return;
        }

        _cts.Cancel();
    }

    /// <summary>
    /// Executes the SetIndeterminate operation.
    /// </summary>
    private void SetIndeterminate(bool on)
    {
        ProgressBar.IsIndeterminate = on;
        if (on) TxtProgress.Text = string.Empty;
    }

    /// <summary>
    /// Executes the SetStageAsync operation.
    /// </summary>
    private async Task SetStageAsync(string status, string detail, double percent)
    {
        TxtStatus.Text = status;
        TxtDetail.Text = detail;
        SetProgress(percent, $"{percent:0}%");
        await Task.Delay(350, _cts.Token);
    }

    /// <summary>
    /// Executes the SetProgress operation.
    /// </summary>
    private void SetProgress(double percent, string display)
    {
        SetIndeterminate(false);
        ProgressBar.Value = Math.Clamp(percent, 0, 100);
        TxtProgress.Text = display;
    }

    /// <summary>
    /// Translates splash-screen text using the saved configuration language.
    /// </summary>
    private string T(string key) => AppHelpContent.Translate(_language, key);
}
