using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>
/// What is known about an item that arrived on the desktop — gathered by the
/// shell layer (which can read shortcuts and download marks) so the rules
/// themselves stay plain, testable logic.
/// </summary>
/// <param name="Path">Full path of the item on the desktop.</param>
public sealed record DesktopItemFacts(string Path)
{
    public bool IsFolder { get; init; }

    /// <summary>For a shortcut: the program it starts (<c>.lnk</c>) or its URL (<c>.url</c>).</summary>
    public string? ShortcutTarget { get; init; }

    /// <summary>For a <c>.lnk</c>: the arguments it passes.</summary>
    public string? ShortcutArguments { get; init; }

    /// <summary>Carries Windows' "downloaded from the internet" mark (a Zone.Identifier of Internet or worse).</summary>
    public bool DownloadedFromInternet { get; init; }

    /// <summary>The folder it was moved or copied from, when Paddock saw it leave or recognised the copy.</summary>
    public string? CameFromFolder { get; init; }
}

/// <summary>
/// Decides which paddock a new desktop item belongs in. Rules are checked in
/// order and the first enabled rule that matches — and whose paddock still
/// exists — wins.
/// </summary>
public static class AutoSortRules
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<(RuleMatchType, string), Regex?> Patterns = new();

    /// <summary>The rule that sends <paramref name="item"/> somewhere, or <c>null</c> if none does.</summary>
    public static AutoSortRule? FindMatch(
        IEnumerable<AutoSortRule> rules,
        DesktopItemFacts item,
        Func<string, bool> paddockExists)
        => rules.FirstOrDefault(rule =>
            rule.Enabled
            && !string.IsNullOrEmpty(rule.TargetPaddockId)
            && paddockExists(rule.TargetPaddockId)
            && Matches(rule, item));

    /// <summary>True when the rule's condition holds for the item (enabled or not).</summary>
    public static bool Matches(AutoSortRule rule, DesktopItemFacts item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
            return false;

        var name = System.IO.Path.GetFileName(item.Path);

        try
        {
            return rule.MatchType switch
            {
                RuleMatchType.Extension => !item.IsFolder
                                           && ParseExtensions(rule.Pattern).Contains(
                                               System.IO.Path.GetExtension(name), StringComparer.OrdinalIgnoreCase),
                RuleMatchType.NameGlob or RuleMatchType.NameRegex => PatternFor(rule) is { } regex && regex.IsMatch(name),
                RuleMatchType.Kind => Enum.TryParse<DesktopItemCategory>(rule.Pattern, ignoreCase: true, out var kind)
                                      && DesktopItemFilter.Matches(item.Path, item.IsFolder, kind),
                RuleMatchType.AppCategory => Enum.TryParse<AppCategory>(rule.Pattern, ignoreCase: true, out var app)
                                             && AppCategories.Classify(item.ShortcutTarget, item.ShortcutArguments) == app,
                RuleMatchType.Downloaded => item.DownloadedFromInternet,
                RuleMatchType.FromFolder => item.CameFromFolder is not null
                                            && !string.IsNullOrWhiteSpace(rule.Pattern)
                                            && SameFolder(item.CameFromFolder, rule.Pattern),
                RuleMatchType.Anything => true,
                _ => false,
            };
        }
        catch (RegexMatchTimeoutException)
        {
            Log.Warn($"Rule pattern '{rule.Pattern}' took too long on '{name}'; treated as no match.");
            return false;
        }
    }

    /// <summary>
    /// False for a rule that can never match as written: no extensions, an
    /// unknown kind, a regular expression that does not parse, and so on.
    /// </summary>
    public static bool IsValid(AutoSortRule rule) => rule.MatchType switch
    {
        RuleMatchType.Extension => ParseExtensions(rule.Pattern).Count > 0,
        RuleMatchType.NameGlob or RuleMatchType.NameRegex => PatternFor(rule) is not null,
        RuleMatchType.Kind => Enum.TryParse<DesktopItemCategory>(rule.Pattern, true, out var kind) && kind != DesktopItemCategory.All,
        RuleMatchType.AppCategory => Enum.TryParse<AppCategory>(rule.Pattern, true, out _),
        RuleMatchType.FromFolder => !string.IsNullOrWhiteSpace(rule.Pattern),
        RuleMatchType.Downloaded or RuleMatchType.Anything => true,
        _ => false,
    };

    /// <summary>
    /// Reads an extension list such as <c>.exe, .msi</c> or <c>pdf docx</c>
    /// into normalised <c>.ext</c> entries.
    /// </summary>
    public static IReadOnlyList<string> ParseExtensions(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return [];

        return pattern
            .Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('*'))
            .Where(e => e.Length > 0 && e != ".")
            .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The folder paths of every enabled "came from folder" rule — the folders worth watching.</summary>
    public static IReadOnlyList<string> SourceFolders(IEnumerable<AutoSortRule> rules)
        => rules
            .Where(r => r.Enabled && r.MatchType == RuleMatchType.FromFolder && !string.IsNullOrWhiteSpace(r.Pattern))
            .Select(r => NormalizeFolder(r.Pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Expands environment variables and tidies a folder path for comparison.</summary>
    public static string NormalizeFolder(string folder)
    {
        var expanded = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));

        try
        {
            expanded = System.IO.Path.GetFullPath(expanded);
        }
        catch (Exception)
        {
            // Keep it as typed; it simply will not match anything.
        }

        return expanded.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    /// <summary>A short, human description of the condition, for logs.</summary>
    public static string Describe(AutoSortRule rule) => rule.MatchType switch
    {
        RuleMatchType.Extension => $"type is {string.Join(", ", ParseExtensions(rule.Pattern))}",
        RuleMatchType.NameGlob => $"name matches \"{rule.Pattern}\"",
        RuleMatchType.NameRegex => $"name matches /{rule.Pattern}/",
        RuleMatchType.Kind => $"kind is {rule.Pattern}",
        RuleMatchType.AppCategory => $"shortcut to {rule.Pattern}",
        RuleMatchType.Downloaded => "downloaded from the internet",
        RuleMatchType.FromFolder => $"came from {rule.Pattern}",
        RuleMatchType.Anything => "anything",
        _ => rule.MatchType.ToString(),
    };

    private static bool SameFolder(string a, string b)
        => string.Equals(NormalizeFolder(a), NormalizeFolder(b), StringComparison.OrdinalIgnoreCase);

    private static Regex? PatternFor(AutoSortRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return null;

        return Patterns.GetOrAdd((rule.MatchType, rule.Pattern), key => Compile(key.Item1, key.Item2));
    }

    private static Regex? Compile(RuleMatchType type, string pattern)
    {
        const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        try
        {
            if (type == RuleMatchType.NameRegex)
                return new Regex(pattern, Options, RegexTimeout);

            // Wildcards: '*' any run, '?' one character, ';' between alternatives.
            var alternatives = pattern
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(glob => "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$")
                .ToList();

            return alternatives.Count == 0 ? null : new Regex(string.Join("|", alternatives), Options, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
