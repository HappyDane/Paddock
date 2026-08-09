using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class DesktopItemFilterTests : IDisposable
{
    private readonly string _root;

    public DesktopItemFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_filter_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string File_(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData("app.lnk", DesktopItemCategory.Shortcuts)]
    [InlineData("site.url", DesktopItemCategory.Shortcuts)]
    [InlineData("report.pdf", DesktopItemCategory.Documents)]
    [InlineData("notes.TXT", DesktopItemCategory.Documents)]
    [InlineData("holiday.jpeg", DesktopItemCategory.Images)]
    [InlineData("song.flac", DesktopItemCategory.Media)]
    [InlineData("backup.7z", DesktopItemCategory.Archives)]
    [InlineData("installer.exe", DesktopItemCategory.Other)]
    public void Matches_ClassifiesFilesByExtension(string name, DesktopItemCategory expected)
    {
        var path = File_(name);

        Assert.True(DesktopItemFilter.Matches(path, expected));
        Assert.True(DesktopItemFilter.Matches(path, DesktopItemCategory.All));
        Assert.False(DesktopItemFilter.Matches(path, DesktopItemCategory.Folders));
    }

    [Fact]
    public void Matches_TreatsDirectoriesAsFoldersOnly()
    {
        // A folder whose name looks like a document must not be classified by it.
        var folder = Folder("Reports.pdf");

        Assert.True(DesktopItemFilter.Matches(folder, DesktopItemCategory.Folders));
        Assert.False(DesktopItemFilter.Matches(folder, DesktopItemCategory.Documents));
        Assert.False(DesktopItemFilter.Matches(folder, DesktopItemCategory.Other));
    }

    [Fact]
    public void Matches_RejectsEmptyPaths()
    {
        Assert.False(DesktopItemFilter.Matches("", DesktopItemCategory.All));
        Assert.False(DesktopItemFilter.Matches("   ", DesktopItemCategory.Shortcuts));
    }

    [Fact]
    public void Filter_SelectsOnlyTheRequestedCategory()
    {
        var paths = new[]
        {
            File_("a.lnk"),
            File_("b.pdf"),
            File_("c.png"),
            Folder("Projects"),
        };

        Assert.Single(DesktopItemFilter.Filter(paths, DesktopItemCategory.Shortcuts));
        Assert.Single(DesktopItemFilter.Filter(paths, DesktopItemCategory.Documents));
        Assert.Single(DesktopItemFilter.Filter(paths, DesktopItemCategory.Folders));
        Assert.Equal(4, DesktopItemFilter.Filter(paths, DesktopItemCategory.All).Count);
        Assert.Empty(DesktopItemFilter.Filter(paths, DesktopItemCategory.Archives));
    }

    [Fact]
    public void Describe_CoversEveryCategory()
    {
        foreach (var category in Enum.GetValues<DesktopItemCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DesktopItemFilter.Describe(category)));
        }
    }
}
