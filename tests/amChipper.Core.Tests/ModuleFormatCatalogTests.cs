using amChipper.Core.Models;
using amChipper.Core.Persistence;

namespace amChipper.Core.Tests;

public sealed class ModuleFormatCatalogTests
{
    [Theory]
    [InlineData(".xm", ModuleFormat.XM)]
    [InlineData("mod", ModuleFormat.MOD)]
    [InlineData(".it", ModuleFormat.IT)]
    [InlineData(".s3m", ModuleFormat.S3M)]
    [InlineData(".mptm", ModuleFormat.OpenMpt)]
    [InlineData(".med", ModuleFormat.OpenMpt)]
    [InlineData(".669", ModuleFormat.OpenMpt)]
    [InlineData(".ahx", ModuleFormat.OpenMpt)]
    [InlineData(".hvl", ModuleFormat.OpenMpt)]
    [InlineData(".ptm", ModuleFormat.OpenMpt)]
    [InlineData(".umx", ModuleFormat.OpenMpt)]
    [InlineData(".okt", ModuleFormat.OpenMpt)]
    [InlineData(".mo3", ModuleFormat.OpenMpt)]
    [InlineData(".ult", ModuleFormat.OpenMpt)]
    [InlineData(".sid", ModuleFormat.SID)]
    [InlineData(".nsf", ModuleFormat.NSF)]
    public void ResolvesKnownTrackerModuleExtensions(string extension, ModuleFormat expected)
    {
        Assert.True(ModuleFormatCatalog.IsSupportedExtension(extension));
        Assert.Equal(expected, ModuleFormatCatalog.ResolveModuleFormat(null, extension));
    }

    [Fact]
    public void OpenDialogFilterIncludesBroadTrackerCatalog()
    {
        string filter = ModuleFormatCatalog.OpenDialogFilter;

        Assert.Contains("*.mptm", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.med", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.669", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.ahx", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.hvl", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.ptm", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.umx", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.okt", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mo3", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.ult", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.sid", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.nsf", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreferredExtensionPreservesImportedOpenMptSourceExtension()
    {
        Assert.Equal(".med", ModuleFormatCatalog.GetPreferredExtension(ModuleFormat.OpenMpt, "MED"));
        Assert.Equal(".mptm", ModuleFormatCatalog.GetPreferredExtension(ModuleFormat.OpenMpt, ".mptm"));
    }

    [Fact]
    public void ProjectNormalizeKeepsSourceFormatMetadataUsable()
    {
        var song = Song.CreateDefault();
        song.Format = ModuleFormat.OpenMpt;
        song.SourceModuleType = " med ";
        song.SourceModuleExtension = "MED";

        SongProjectSerializer.Normalize(song);

        Assert.Equal("med", song.SourceModuleType);
        Assert.Equal(".med", song.SourceModuleExtension);
        Assert.Equal("MED", ModuleFormatCatalog.GetDisplayLabel(song));
    }
}
