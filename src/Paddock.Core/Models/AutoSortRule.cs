namespace Paddock.Core.Models;

public class AutoSortRule
{
    public string Pattern { get; set; } = string.Empty;
    public RuleMatchType MatchType { get; set; } = RuleMatchType.Extension;
    public string TargetPaddockId { get; set; } = string.Empty;
}

public enum RuleMatchType
{
    Extension,
    NameGlob,
    NameRegex
}
