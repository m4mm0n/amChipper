using amChipper.Core.Diagnostics;

namespace amChipper.Core.Tests;

public sealed class BugReportBuilderTests
{
    [Fact]
    public void BuildBodyIncludesUserFieldsAndRuntimeContext()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero);
        var draft = new BugReportDraft(
            BugReportBuilder.DefaultRecipient,
            "amChipper",
            "v0.2.4.0-AMC20260524.1",
            "Crash when opening a SID tune.",
            "1. Open tune.sid\n2. Press Play",
            "Playback starts.",
            "Window closes.",
            "Loaded chip tune: tune.sid",
            @"C:\Music\tune.sid",
            @"C:\Logs\amChipper.log",
            "last log line",
            capturedAt,
            "Linux 6.8 via Wine",
            ".NET 10.0.0",
            "X64");

        string body = BugReportBuilder.BuildBody(draft).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("To: admin@darkmaster.no", body);
        Assert.Contains("Version: v0.2.4.0-AMC20260524.1", body);
        Assert.Contains("Details:\nCrash when opening a SID tune.", body);
        Assert.Contains("Steps to reproduce:\n1. Open tune.sid\n2. Press Play", body);
        Assert.Contains("Expected:\nPlayback starts.", body);
        Assert.Contains("Actual:\nWindow closes.", body);
        Assert.Contains(@"Project/file: C:\Music\tune.sid", body);
        Assert.Contains("Operating system: Linux 6.8 via Wine", body);
        Assert.Contains("Recent log tail:\nlast log line", body);
    }

    [Fact]
    public void BuildMailtoUriAddressesOwnerAndEscapesSubjectAndBody()
    {
        var draft = new BugReportDraft(
            BugReportBuilder.DefaultRecipient,
            "amChipper",
            "v0.2.4.0-AMC20260524.1",
            "Unicode and query chars: å & ?",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero),
            "Windows 11",
            ".NET 10.0.0",
            "X64");

        Uri uri = BugReportBuilder.BuildMailtoUri(draft);

        Assert.Equal("mailto", uri.Scheme);
        Assert.StartsWith("mailto:admin@darkmaster.no?", uri.OriginalString);
        Assert.Contains("subject=amChipper%20bug%20report%20-%20v0.2.4.0-AMC20260524.1", uri.OriginalString);
        Assert.Contains("Unicode%20and%20query%20chars", uri.OriginalString);
        Assert.Contains("%26", uri.OriginalString);
    }

    [Fact]
    public void CreateFromEnvironmentTailsLargeLogs()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"amchipper-bug-report-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(tempFile, new string('a', 200) + "TAIL");

            var draft = BugReportBuilder.CreateFromEnvironment(
                "amChipper",
                "v0.2.4.0-AMC20260524.1",
                "details",
                "steps",
                "expected",
                "actual",
                "status",
                "project.amc",
                tempFile,
                maxLogTailChars: 16);

            Assert.Equal(BugReportBuilder.DefaultRecipient, draft.RecipientEmail);
            Assert.Equal("aaaaaaaaaaaaTAIL", draft.LogTail);
            Assert.False(string.IsNullOrWhiteSpace(draft.OperatingSystem));
            Assert.False(string.IsNullOrWhiteSpace(draft.Runtime));
            Assert.False(string.IsNullOrWhiteSpace(draft.ProcessArchitecture));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
