using System.Runtime.CompilerServices;

namespace amChipper.Core.Interfaces;

/// <summary>
/// Logging contract used throughout amChipper.
/// Keeps Core free of any logging framework dependency.
/// Implemented by AppLogger in amChipper.App using QuickLog.
/// </summary>
public interface IAppLogger
{
    void Debug(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Info(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Warning(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Error(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Error(Exception ex, string? message = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Fatal(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);

    void Fatal(Exception ex, string? message = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);
}
