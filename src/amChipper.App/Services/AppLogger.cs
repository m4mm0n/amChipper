using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using amChipper.Core.Interfaces;
using QuickLog;
using QuickLog.Loggers;

namespace amChipper.App.Services;

/// <summary>
/// Static logger facade wired to QuickLog (LogManager / IQuickLog).
/// Call <see cref="Initialise"/> once in App.OnStartup, then use the
/// static methods anywhere, or inject <see cref="Instance"/> as <see cref="IAppLogger"/>.
/// Call <see cref="Shutdown"/> in App.OnExit.
/// </summary>
public static class AppLogger
{
    /// <summary>Injectable IAppLogger instance — starts as a no-op until Initialise() is called.</summary>
    public static IAppLogger Instance { get; private set; } = new NullLogger();
    /// <summary>
    /// Stores or exposes LogFilePath.
    /// </summary>
    public static string LogFilePath { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes LogDirectory.
    /// </summary>
    public static string LogDirectory { get; private set; } = string.Empty;
    /// <summary>
    /// Stores or exposes VerboseEnabled.
    /// </summary>
    public static bool VerboseEnabled { get; set; } = true;

    /// <summary>
    /// Initialise QuickLog file logging. Call once before anything else logs.
    /// </summary>
    /// <param name="logDirectory">Folder where amChipper.log will be written.</param>
    public static void Initialise(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        string logFile = Path.Combine(logDirectory, "amChipper.log");
        if (string.Equals(LogFilePath, logFile, StringComparison.OrdinalIgnoreCase) &&
            Instance is not NullLogger)
        {
            Info($"amChipper logger already active. Log -> {logFile}");
            return;
        }

        if (Instance is IDisposable current)
            current.Dispose();

        LogManager.Shutdown();
        LogDirectory = logDirectory;
        LogFilePath = logFile;

        var quickLogger = new QuickLogger(logFile)
        {
            EnableConsoleLogging = false,
            EnableFileLogging = true,
            EnableEventLogging = false,
            EnableTraceLogging = false
        };
        LogManager.ConfigureDefault(quickLogger);
        Instance = new QuickLogAdapter(LogManager.GetDefaultLogger());

        Info($"========== amChipper session started {DateTimeOffset.Now:O} ==========");
        Info($"amChipper logger started. Log -> {logFile}");
    }

    /// <summary>Flush and release all QuickLog resources. Call from App.OnExit.</summary>
    public static void Shutdown()
    {
        if (Instance is IDisposable disposable)
            disposable.Dispose();

        LogManager.Shutdown();
    }

    // ── Static pass-throughs ─────────────────────────────────────────────────

    /// <summary>
    /// Executes the Debug operation.
    /// </summary>
    public static void Debug(string msg,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
    {
        if (VerboseEnabled)
            Instance.Debug(msg, m, f, l);
    }

    /// <summary>
    /// Executes the Info operation.
    /// </summary>
    public static void Info(string msg,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Info(msg, m, f, l);

    /// <summary>
    /// Executes the Warning operation.
    /// </summary>
    public static void Warning(string msg,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Warning(msg, m, f, l);

    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public static void Error(string msg,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Error(msg, m, f, l);

    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public static void Error(Exception ex, string? msg = null,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Error(ex, msg, m, f, l);

    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public static void Fatal(string msg,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Fatal(msg, m, f, l);

    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public static void Fatal(Exception ex, string? msg = null,
        [CallerMemberName] string m = "", [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
        => Instance.Fatal(ex, msg, m, f, l);
}

// ─────────────────────────────────────────────────────────────────────────────
// QuickLog adapter — maps IAppLogger → IQuickLog
// ─────────────────────────────────────────────────────────────────────────────

file sealed class QuickLogAdapter : IAppLogger, IDisposable
{
    /// <summary>
    /// Stores or exposes _log.
    /// </summary>
    private readonly IQuickLog _log;
    private readonly BlockingCollection<QueuedLogEntry> _queue = new(new ConcurrentQueue<QueuedLogEntry>(), 16_384);
    /// <summary>
    /// Stores or exposes _worker.
    /// </summary>
    private readonly Thread _worker;
    /// <summary>
    /// Stores or exposes _dropped.
    /// </summary>
    private long _dropped;
    /// <summary>
    /// Stores or exposes _disposed.
    /// </summary>
    private bool _disposed;

    public QuickLogAdapter(IQuickLog log)
    {
        _log = log;
        _worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "amChipper logger"
        };
        _worker.Start();
    }

    /// <summary>
    /// Executes the Debug operation.
    /// </summary>
    public void Debug(string message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Debug, message, null, member, file, line));

    /// <summary>
    /// Executes the Info operation.
    /// </summary>
    public void Info(string message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Info, message, null, member, file, line));

    /// <summary>
    /// Executes the Warning operation.
    /// </summary>
    public void Warning(string message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Warn, message, null, member, file, line));

    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(string message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Error, message, null, member, file, line));

    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(Exception ex, string? message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Error, message, ex, member, file, line));

    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(string message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Crit, message, null, member, file, line));

    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(Exception ex, string? message, string member, string file, int line)
        => Enqueue(new QueuedLogEntry(LogType.Crit, message, ex, member, file, line));

    /// <summary>
    /// Executes the Enqueue operation.
    /// </summary>
    private void Enqueue(QueuedLogEntry entry)
    {
        if (_disposed)
            return;

        try
        {
            if (!_queue.TryAdd(entry))
                Interlocked.Increment(ref _dropped);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Executes the ProcessQueue operation.
    /// </summary>
    private void ProcessQueue()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
            Write(entry);

        long dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
            _log.Log(LogType.Warn, $"[Logger] Dropped {dropped} diagnostic log entries because the queue was full.", nameof(ProcessQueue), string.Empty, 0);
    }

    /// <summary>
    /// Executes the Write operation.
    /// </summary>
    private void Write(QueuedLogEntry entry)
    {
        try
        {
            if (entry.Exception is not null && entry.Message is not null)
                _log.Log(entry.Type, entry.Message, entry.Exception, entry.Member, entry.File, entry.Line);
            else if (entry.Exception is not null)
                _log.Log(entry.Type, entry.Exception, entry.Member, entry.File, entry.Line);
            else
                _log.Log(entry.Type, entry.Message ?? string.Empty, entry.Member, entry.File, entry.Line);
        }
        catch
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Executes the Dispose operation.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.CompleteAdding();
        if (!_worker.Join(TimeSpan.FromSeconds(3)))
            Interlocked.Increment(ref _dropped);

        if (_log is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Carries struct data.
    /// </summary>
    private readonly record struct QueuedLogEntry(
        LogType Type,
        string? Message,
        Exception? Exception,
        string Member,
        string File,
        int Line);
}

// ─────────────────────────────────────────────────────────────────────────────
// Null logger — used before Initialise() and in unit tests
// ─────────────────────────────────────────────────────────────────────────────

file sealed class NullLogger : IAppLogger
{
    /// <summary>
    /// Executes the Debug operation.
    /// </summary>
    public void Debug(string msg, string m, string f, int l) => Trace(msg);
    /// <summary>
    /// Executes the Info operation.
    /// </summary>
    public void Info(string msg, string m, string f, int l) => Trace(msg);
    /// <summary>
    /// Executes the Warning operation.
    /// </summary>
    public void Warning(string msg, string m, string f, int l) => Trace(msg);
    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(string msg, string m, string f, int l) => Trace(msg);
    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(string msg, string m, string f, int l) => Trace(msg);
    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(Exception ex, string? msg, string m, string f, int l)
        => Trace($"{msg}: {ex.Message}");
    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(Exception ex, string? msg, string m, string f, int l)
        => Trace($"FATAL {msg}: {ex.Message}");
    /// <summary>
    /// Executes the Trace operation.
    /// </summary>
    private static void Trace(string msg) =>
        System.Diagnostics.Trace.WriteLine($"[amChipper] {msg}");
}
