namespace amChipper.Core.Interfaces;

/// <summary>
/// No-op IAppLogger used as a default when no logger is injected.
/// Forwards to System.Diagnostics.Trace so messages still appear in
/// the VS Output window during development.
/// </summary>
public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();
    private NullAppLogger() { }

    /// <summary>
    /// Executes the Debug operation.
    /// </summary>
    public void Debug(string msg, string m, string f, int l) => Write("DBG", msg, m);
    /// <summary>
    /// Executes the Info operation.
    /// </summary>
    public void Info(string msg, string m, string f, int l) => Write("INF", msg, m);
    /// <summary>
    /// Executes the Warning operation.
    /// </summary>
    public void Warning(string msg, string m, string f, int l) => Write("WRN", msg, m);
    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(string msg, string m, string f, int l) => Write("ERR", msg, m);
    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(string msg, string m, string f, int l) => Write("FTL", msg, m);

    /// <summary>
    /// Executes the Error operation.
    /// </summary>
    public void Error(Exception ex, string? msg, string m, string f, int l)
        => Write("ERR", $"{msg ?? ex.Message}\n{ex}", m);

    /// <summary>
    /// Executes the Fatal operation.
    /// </summary>
    public void Fatal(Exception ex, string? msg, string m, string f, int l)
        => Write("FTL", $"{msg ?? ex.Message}\n{ex}", m);

    /// <summary>
    /// Executes the Write operation.
    /// </summary>
    private static void Write(string level, string msg, string member) =>
        System.Diagnostics.Trace.WriteLine($"[amChipper][{level}][{member}] {msg}");
}
