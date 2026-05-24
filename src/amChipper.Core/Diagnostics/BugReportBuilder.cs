using System.Runtime.InteropServices;
using System.Text;

namespace amChipper.Core.Diagnostics;

/// <summary>
/// Carries user-entered bug report content plus diagnostic context.
/// </summary>
public sealed record BugReportDraft(
    string RecipientEmail,
    string ApplicationName,
    string Version,
    string Details,
    string StepsToReproduce,
    string ExpectedBehavior,
    string ActualBehavior,
    string StatusText,
    string ProjectPath,
    string LogFilePath,
    string LogTail,
    DateTimeOffset CapturedAt,
    string OperatingSystem,
    string Runtime,
    string ProcessArchitecture);

/// <summary>
/// Builds email-ready bug reports for support requests.
/// </summary>
public static class BugReportBuilder
{
    public const string DefaultRecipient = "admin@darkmaster.no";

    public static BugReportDraft CreateFromEnvironment(
        string applicationName,
        string version,
        string details,
        string stepsToReproduce,
        string expectedBehavior,
        string actualBehavior,
        string statusText,
        string projectPath,
        string logFilePath,
        string recipientEmail = DefaultRecipient,
        int maxLogTailChars = 8_000)
    {
        return new BugReportDraft(
            Normalize(recipientEmail, DefaultRecipient),
            Normalize(applicationName, "amChipper"),
            Normalize(version, "unknown"),
            details?.Trim() ?? string.Empty,
            stepsToReproduce?.Trim() ?? string.Empty,
            expectedBehavior?.Trim() ?? string.Empty,
            actualBehavior?.Trim() ?? string.Empty,
            statusText?.Trim() ?? string.Empty,
            projectPath?.Trim() ?? string.Empty,
            logFilePath?.Trim() ?? string.Empty,
            ReadTail(logFilePath, maxLogTailChars),
            DateTimeOffset.Now,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    public static string BuildSubject(BugReportDraft draft) =>
        $"{Normalize(draft.ApplicationName, "amChipper")} bug report - {Normalize(draft.Version, "unknown")}";

    public static string BuildBody(BugReportDraft draft)
    {
        var body = new StringBuilder();
        body.AppendLine($"To: {Normalize(draft.RecipientEmail, DefaultRecipient)}");
        body.AppendLine($"Application: {Normalize(draft.ApplicationName, "amChipper")}");
        body.AppendLine($"Version: {Normalize(draft.Version, "unknown")}");
        body.AppendLine($"Captured: {draft.CapturedAt:O}");
        body.AppendLine();
        AppendSection(body, "Details", draft.Details);
        AppendSection(body, "Steps to reproduce", draft.StepsToReproduce);
        AppendSection(body, "Expected", draft.ExpectedBehavior);
        AppendSection(body, "Actual", draft.ActualBehavior);
        body.AppendLine("Context:");
        body.AppendLine($"Status: {Normalize(draft.StatusText, "(not captured)")}");
        body.AppendLine($"Project/file: {Normalize(draft.ProjectPath, "(none)")}");
        body.AppendLine($"Log file: {Normalize(draft.LogFilePath, "(none)")}");
        body.AppendLine($"Operating system: {Normalize(draft.OperatingSystem, "(unknown)")}");
        body.AppendLine($"Runtime: {Normalize(draft.Runtime, "(unknown)")}");
        body.AppendLine($"Process architecture: {Normalize(draft.ProcessArchitecture, "(unknown)")}");
        body.AppendLine();
        AppendSection(body, "Recent log tail", draft.LogTail);
        return body.ToString().TrimEnd();
    }

    public static Uri BuildMailtoUri(BugReportDraft draft)
    {
        string recipient = Normalize(draft.RecipientEmail, DefaultRecipient)
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal);
        string subject = Uri.EscapeDataString(BuildSubject(draft));
        string body = Uri.EscapeDataString(BuildBody(draft));
        return new Uri($"mailto:{recipient}?subject={subject}&body={body}");
    }

    private static void AppendSection(StringBuilder body, string title, string value)
    {
        body.AppendLine($"{title}:");
        body.AppendLine(Normalize(value, "(not provided)"));
        body.AppendLine();
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ReadTail(string? logFilePath, int maxLogTailChars)
    {
        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
            return string.Empty;

        int length = Math.Clamp(maxLogTailChars, 0, 64_000);
        if (length == 0)
            return string.Empty;

        try
        {
            using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            if (text.Length <= length)
                return text;

            return text[^length..];
        }
        catch (IOException ex)
        {
            return BuildUnreadableLogMessage(logFilePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BuildUnreadableLogMessage(logFilePath, ex);
        }
    }

    private static string BuildUnreadableLogMessage(string logFilePath, Exception ex)
    {
        string reason = ex.Message;
        if (string.IsNullOrWhiteSpace(reason))
            reason = ex.GetType().Name;

        return $"The log file could not be read because it is locked or unavailable: {logFilePath}{Environment.NewLine}{reason}";
    }
}
