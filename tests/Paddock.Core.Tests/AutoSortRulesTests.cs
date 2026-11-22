using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Auto-organize rules: which paddock a new desktop item goes to. The first
/// enabled rule that matches and still has a paddock wins.
/// </summary>
public class AutoSortRulesTests
{
    private const string Desktop = @"C:\Users\me\Desktop";

    private static DesktopItemFacts Item(string name, bool folder = false) => new(Path.Combine(Desktop, name)) { IsFolder = folder };

    private static AutoSortRule Rule(RuleMatchType type, string pattern, string target = "p1", bool enabled = true)
        => new() { MatchType = type, Pattern = pattern, TargetPaddockId = target, Enabled = enabled };

    private static readonly Func<string, bool> AllExist = _ => true;

    [Theory]
    [InlineData(".exe, .msi", "setup.MSI", true)]
    [InlineData("exe msi", "tool.exe", true)]
    [InlineData("*.pdf", "report.pdf", true)]
    [InlineData(".pdf", "report.pdf.lnk", false)]
    [InlineData(".pdf", "noextension", false)]
    public void Extension_MatchesTheFileType(string pattern, string name, bool expected)
        => Assert.Equal(expected, AutoSortRules.Matches(Rule(RuleMatchType.Extension, pattern), Item(name)));

    [Fact]
    public void Extension_NeverMatchesAFolder()
        => Assert.False(AutoSortRules.Matches(Rule(RuleMatchType.Extension, ".pdf"), Item("Reports.pdf", folder: true)));

    [Theory]
    [InlineData("Screenshot*", "Screenshot 2026-11-22 093301.png", true)]
    [InlineData("screenshot*", "Screenshot 1.png", true)]
    [InlineData("Scan ???.pdf", "Scan 001.pdf", true)]
    [InlineData("Scan ???.pdf", "Scan 0001.pdf", false)]
    [InlineData("Invoice*; Receipt*", "Receipt May.pdf", true)]
    [InlineData("a.b", "aXb", false)]
    public void NameGlob_UsesWildcards(string pattern, string name, bool expected)
        => Assert.Equal(expected, AutoSortRules.Matches(Rule(RuleMatchType.NameGlob, pattern), Item(name)));

    [Fact]
    public void NameRegex_MatchesAndBadPatternsAreInvalid()
    {
        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.NameRegex, @"^IMG_\d{4}"), Item("img_1234.jpg")));
        Assert.False(AutoSortRules.Matches(Rule(RuleMatchType.NameRegex, "(unclosed"), Item("(unclosed")));
        Assert.False(AutoSortRules.IsValid(Rule(RuleMatchType.NameRegex, "(unclosed")));
        Assert.True(AutoSortRules.IsValid(Rule(RuleMatchType.NameRegex, "ok")));
    }

    [Fact]
    public void Kind_UsesTheDesktopCategories()
    {
        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.Kind, "Images"), Item("cat.HEIC")));
        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.Kind, "installers"), Item("setup.exe")));
        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.Kind, "Folders"), Item("Stuff", folder: true)));
        Assert.False(AutoSortRules.Matches(Rule(RuleMatchType.Kind, "Images"), Item("photos.jpg", folder: true)));
        Assert.False(AutoSortRules.IsValid(Rule(RuleMatchType.Kind, "Nonsense")));
    }

    [Fact]
    public void AppCategory_LooksAtWhereAShortcutPoints()
    {
        var game = Item("Hades.url") with { ShortcutTarget = "steam://rungameid/1145360" };
        var editor = Item("GIMP.lnk") with { ShortcutTarget = @"C:\Program Files\GIMP 2\bin\gimp.exe" };

        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.AppCategory, "Games"), game));
        Assert.False(AutoSortRules.Matches(Rule(RuleMatchType.AppCategory, "Games"), editor));
        Assert.True(AutoSortRules.Matches(Rule(RuleMatchType.AppCategory, "Creative"), editor));
        Assert.False(AutoSortRules.Matches(Rule(RuleMatchType.AppCategory, "Creative"), Item("plain.txt")));
    }

    [Fact]
    public void Downloaded_FollowsTheMarkOfTheWeb()
    {
        var rule = Rule(RuleMatchType.Downloaded, "");
        Assert.True(AutoSortRules.Matches(rule, Item("a.pdf") with { DownloadedFromInternet = true }));
        Assert.False(AutoSortRules.Matches(rule, Item("a.pdf")));
    }

    private static readonly string Downloads = Path.Combine(Path.GetTempPath(), "PaddockRules", "Downloads");

    [Fact]
    public void FromFolder_ComparesFoldersLoosely()
    {
        var rule = Rule(RuleMatchType.FromFolder, Downloads + Path.DirectorySeparatorChar);
        Assert.True(AutoSortRules.Matches(rule, Item("a.zip") with { CameFromFolder = Downloads.ToUpperInvariant() }));
        Assert.False(AutoSortRules.Matches(rule, Item("a.zip") with { CameFromFolder = Path.Combine(Downloads, "..", "Documents") }));
        Assert.False(AutoSortRules.Matches(rule, Item("a.zip")));
    }

    [Fact]
    public void FindMatch_FirstEnabledRuleWithAPaddockWins()
    {
        var rules = new List<AutoSortRule>
        {
            Rule(RuleMatchType.Extension, ".pdf", target: "disabled", enabled: false),
            Rule(RuleMatchType.Extension, ".pdf", target: "gone"),
            Rule(RuleMatchType.Kind, "Documents", target: "docs"),
            Rule(RuleMatchType.Anything, "", target: "inbox"),
        };

        var exists = (string id) => id != "gone";

        Assert.Equal("docs", AutoSortRules.FindMatch(rules, Item("a.pdf"), exists)?.TargetPaddockId);
        Assert.Equal("inbox", AutoSortRules.FindMatch(rules, Item("a.png"), exists)?.TargetPaddockId);
        Assert.Null(AutoSortRules.FindMatch(rules.Take(3), Item("a.png"), exists));
        Assert.Null(AutoSortRules.FindMatch([Rule(RuleMatchType.Anything, "", target: "")], Item("a.png"), AllExist));
    }

    [Fact]
    public void SourceFolders_ListsTheFoldersEnabledRulesWatch()
    {
        var rules = new[]
        {
            Rule(RuleMatchType.FromFolder, Downloads),
            Rule(RuleMatchType.FromFolder, Downloads.ToLowerInvariant() + Path.DirectorySeparatorChar),
            Rule(RuleMatchType.FromFolder, Path.Combine(Downloads, "..", "Elsewhere"), enabled: false),
            Rule(RuleMatchType.Extension, ".pdf"),
        };

        Assert.Equal([Downloads], AutoSortRules.SourceFolders(rules));
    }

    [Fact]
    public void ParseExtensions_NormalisesWhatPeopleType()
        => Assert.Equal([".exe", ".msi", ".pdf"], AutoSortRules.ParseExtensions(" exe, .MSI;*.pdf .exe "));

    [Fact]
    public void OldSettings_StillLoad()
    {
        // settings.json written before rules had ids or an on/off switch.
        var path = Path.Combine(Path.GetTempPath(), $"paddock_rules_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "autoSortRules": [ { "pattern": ".pdf", "matchType": "extension", "targetPaddockId": "p1" } ] }""");

        try
        {
            var rule = Assert.Single(new SettingsService(path).Settings.AutoSortRules);
            Assert.True(rule.Enabled);
            Assert.Equal(RuleMatchType.Extension, rule.MatchType);
            Assert.False(string.IsNullOrEmpty(rule.Id));
            Assert.True(AutoSortRules.Matches(rule, Item("x.pdf")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
