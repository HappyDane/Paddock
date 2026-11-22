using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.ViewModels;

/// <summary>A paddock a rule can send items to.</summary>
public sealed record PaddockChoice(string Id, string Title);

/// <summary>One kind of condition in the rule editor's "When" list.</summary>
public sealed record ConditionOption(RuleMatchType Type, string Label);

/// <summary>One value in a condition's drop-down (a kind of item, a kind of app).</summary>
public sealed record ChoiceOption(string Value, string Label);

/// <summary>A ready-made rule from the Add Rule menu, with the paddock it fills.</summary>
public sealed record RulePreset(string Label, string PaddockTitle, RuleMatchType Type, string Pattern);

/// <summary>
/// The Auto-Organize window: the rules, top to bottom, and the switch that
/// turns them on. Every change is saved (once typing settles) and takes
/// effect at once.
/// </summary>
public sealed class AutoOrganizeViewModel : INotifyPropertyChanged
{
    private readonly App _app;
    private readonly DispatcherTimer _saveSoon;
    private IReadOnlyList<PaddockChoice> _paddocks = [];

    public AutoOrganizeViewModel(App app, string? defaultTargetId)
    {
        _app = app;
        DefaultTargetId = defaultTargetId;

        _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveSoon.Tick += (_, _) => SaveNow();

        Rules = new ObservableCollection<RuleViewModel>(
            app.Settings.Settings.AutoSortRules.Select(r => new RuleViewModel(r, this)));
        Rules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EmptyVisibility));
        RefreshPaddocks();
    }

    /// <summary>The paddock the window was opened from: new custom rules fill it.</summary>
    public string? DefaultTargetId { get; set; }

    public ObservableCollection<RuleViewModel> Rules { get; }

    public IReadOnlyList<PaddockChoice> Paddocks => _paddocks;

    public bool Enabled
    {
        get => _app.Settings.Settings.AutoOrganizeEnabled;
        set
        {
            _app.Settings.Settings.AutoOrganizeEnabled = value;
            OnPropertyChanged();
            SaveNow();
        }
    }

    public Visibility EmptyVisibility => Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static IReadOnlyList<RulePreset> Presets { get; } =
    [
        new("Installers → Installers", "Installers", RuleMatchType.Kind, nameof(DesktopItemCategory.Installers)),
        new("PDFs → Documents", "Documents", RuleMatchType.Extension, ".pdf"),
        new("Images → Screenshots", "Screenshots", RuleMatchType.Kind, nameof(DesktopItemCategory.Images)),
        new("Anything from Downloads → Downloads", "Downloads", RuleMatchType.FromFolder, ""),
        new("Game shortcuts → Games", "Games", RuleMatchType.AppCategory, nameof(AppCategory.Games)),
        new("Creative app shortcuts → Creative", "Creative", RuleMatchType.AppCategory, nameof(AppCategory.Creative)),
        new("Everything else → Inbox", "Inbox", RuleMatchType.Anything, ""),
    ];

    /// <summary>Re-reads the paddocks (they can be created, renamed or closed while the window is open).</summary>
    public void RefreshPaddocks()
    {
        _paddocks = _app.PaddockManager.GetPaddocks().Select(p => new PaddockChoice(p.Id, p.Title)).ToList();
        OnPropertyChanged(nameof(Paddocks));

        foreach (var rule in Rules)
            rule.PaddocksChanged();
    }

    /// <summary>
    /// Adds a ready-made rule. Its paddock is the one with that name, or a new
    /// one if there is none. It goes above any catch-all, which must stay last
    /// to make sense.
    /// </summary>
    public RuleViewModel AddPreset(RulePreset preset)
    {
        var target = _app.PaddockManager.GetPaddocks()
                         .FirstOrDefault(p => string.Equals(p.Title.Trim(), preset.PaddockTitle, StringComparison.OrdinalIgnoreCase))
                     ?? _app.Host?.CreateNamedPaddock(preset.PaddockTitle);

        RefreshPaddocks();

        var pattern = preset.Type == RuleMatchType.FromFolder && string.IsNullOrEmpty(preset.Pattern)
            ? DesktopIconService.DownloadsFolder
            : preset.Pattern;

        return Add(new AutoSortRule { MatchType = preset.Type, Pattern = pattern, TargetPaddockId = target?.Id ?? string.Empty });
    }

    /// <summary>Adds an empty "file type is …" rule for the user to fill in.</summary>
    public RuleViewModel AddCustom()
    {
        var target = DefaultTargetId is not null && _paddocks.Any(p => p.Id == DefaultTargetId)
            ? DefaultTargetId
            : _paddocks.FirstOrDefault()?.Id ?? string.Empty;

        return Add(new AutoSortRule { MatchType = RuleMatchType.Extension, Pattern = string.Empty, TargetPaddockId = target });
    }

    public void Remove(RuleViewModel rule)
    {
        Rules.Remove(rule);
        SaveNow();
    }

    public void Move(RuleViewModel rule, int delta)
    {
        var from = Rules.IndexOf(rule);
        var to = Math.Clamp(from + delta, 0, Rules.Count - 1);
        if (from < 0 || from == to)
            return;

        Rules.Move(from, to);
        SaveNow();
    }

    /// <summary>A rule changed: save shortly (typing sends a change per key).</summary>
    internal void Changed()
    {
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Writes the rules now and puts them to work.</summary>
    public void SaveNow()
    {
        _saveSoon.Stop();
        _app.Settings.Settings.AutoSortRules = Rules.Select(r => r.Model).ToList();
        _app.Settings.Save();
        _app.AutoOrganize?.Refresh();
    }

    private RuleViewModel Add(AutoSortRule rule)
    {
        var viewModel = new RuleViewModel(rule, this);
        var catchAll = rule.MatchType == RuleMatchType.Anything
            ? -1
            : Rules.ToList().FindIndex(r => r.Model.MatchType == RuleMatchType.Anything);

        Rules.Insert(catchAll < 0 ? Rules.Count : catchAll, viewModel);
        SaveNow();
        return viewModel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One rule in the Auto-Organize window: "When [condition] [value], put it in [paddock]".</summary>
public sealed class RuleViewModel : INotifyPropertyChanged
{
    private readonly AutoSortRule _rule;
    private readonly AutoOrganizeViewModel _owner;

    public RuleViewModel(AutoSortRule rule, AutoOrganizeViewModel owner)
    {
        _rule = rule;
        _owner = owner;
    }

    public static IReadOnlyList<ConditionOption> Conditions { get; } =
    [
        new(RuleMatchType.Extension, "File type is"),
        new(RuleMatchType.Kind, "Kind of item is"),
        new(RuleMatchType.NameGlob, "Name matches"),
        new(RuleMatchType.NameRegex, "Name matches regex"),
        new(RuleMatchType.AppCategory, "Shortcut to an app for"),
        new(RuleMatchType.Downloaded, "Downloaded from the internet"),
        new(RuleMatchType.FromFolder, "Came from the folder"),
        new(RuleMatchType.Anything, "Anything else"),
    ];

    private static readonly IReadOnlyList<ChoiceOption> Kinds =
    [
        new(nameof(DesktopItemCategory.Documents), "Documents"),
        new(nameof(DesktopItemCategory.Images), "Images"),
        new(nameof(DesktopItemCategory.Media), "Music and video"),
        new(nameof(DesktopItemCategory.Archives), "Archives"),
        new(nameof(DesktopItemCategory.Installers), "Installers and programs"),
        new(nameof(DesktopItemCategory.Shortcuts), "Shortcuts"),
        new(nameof(DesktopItemCategory.Folders), "Folders"),
        new(nameof(DesktopItemCategory.Other), "Any other file"),
    ];

    private static readonly IReadOnlyList<ChoiceOption> Apps =
    [
        new(nameof(AppCategory.Games), "Games"),
        new(nameof(AppCategory.Creative), "Creative work"),
        new(nameof(AppCategory.Development), "Development"),
        new(nameof(AppCategory.Office), "Office and documents"),
        new(nameof(AppCategory.Internet), "Browsers and the web"),
        new(nameof(AppCategory.Communication), "Chat and calls"),
        new(nameof(AppCategory.Media), "Music and video"),
    ];

    public AutoSortRule Model => _rule;

    public bool Enabled
    {
        get => _rule.Enabled;
        set
        {
            _rule.Enabled = value;
            OnPropertyChanged();
            _owner.Changed();
        }
    }

    public ConditionOption Condition
    {
        get => Conditions.FirstOrDefault(c => c.Type == _rule.MatchType) ?? Conditions[0];
        set
        {
            if (value is null || value.Type == _rule.MatchType)
                return;

            _rule.MatchType = value.Type;
            _rule.Pattern = value.Type switch
            {
                RuleMatchType.Kind => Kinds[0].Value,
                RuleMatchType.AppCategory => Apps[0].Value,
                RuleMatchType.FromFolder => DesktopIconService.DownloadsFolder,
                _ => string.Empty,
            };

            OnPropertyChanged(string.Empty);
            _owner.Changed();
        }
    }

    /// <summary>The typed value: file types, a name pattern or a regex.</summary>
    public string Text
    {
        get => _rule.Pattern;
        set
        {
            _rule.Pattern = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(ProblemVisibility));
            _owner.Changed();
        }
    }

    public string Hint => _rule.MatchType switch
    {
        RuleMatchType.Extension => "e.g. .pdf, .docx",
        RuleMatchType.NameGlob => "e.g. Screenshot*; Scan *",
        RuleMatchType.NameRegex => @"e.g. ^IMG_\d+",
        _ => string.Empty,
    };

    public IReadOnlyList<ChoiceOption> Choices => _rule.MatchType switch
    {
        RuleMatchType.Kind => Kinds,
        RuleMatchType.AppCategory => Apps,
        _ => [],
    };

    public ChoiceOption? Choice
    {
        get => Choices.FirstOrDefault(c => string.Equals(c.Value, _rule.Pattern, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
                return;

            _rule.Pattern = value.Value;
            OnPropertyChanged();
            _owner.Changed();
        }
    }

    /// <summary>The folder of a "came from" rule.</summary>
    public string Folder
    {
        get => _rule.Pattern;
        set
        {
            _rule.Pattern = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FolderName));
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(ProblemVisibility));
            _owner.SaveNow();
        }
    }

    public string FolderName
    {
        get
        {
            var trimmed = _rule.Pattern.TrimEnd('\\', '/');
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    public Visibility TextVisibility => Shows(RuleMatchType.Extension, RuleMatchType.NameGlob, RuleMatchType.NameRegex);

    public Visibility ChoiceVisibility => Shows(RuleMatchType.Kind, RuleMatchType.AppCategory);

    public Visibility FolderVisibility => Shows(RuleMatchType.FromFolder);

    public IReadOnlyList<PaddockChoice> Paddocks => _owner.Paddocks;

    public string TargetPaddockId
    {
        get => _rule.TargetPaddockId;
        set
        {
            if (value is null)
                return;

            _rule.TargetPaddockId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(ProblemVisibility));
            _owner.Changed();
        }
    }

    /// <summary>Why this rule will not do anything, or null when it is fine.</summary>
    public string? Problem
    {
        get
        {
            if (!_owner.Paddocks.Any(p => p.Id == _rule.TargetPaddockId))
                return "Choose a paddock for it to fill.";

            if (AutoSortRules.IsValid(_rule))
                return null;

            return _rule.MatchType switch
            {
                RuleMatchType.Extension => "Type at least one file type.",
                RuleMatchType.NameGlob => "Type a name to match.",
                RuleMatchType.NameRegex => "That is not a regular expression Paddock can read.",
                RuleMatchType.FromFolder => "Choose a folder.",
                _ => "This rule is incomplete.",
            };
        }
    }

    public Visibility ProblemVisibility => Problem is null ? Visibility.Collapsed : Visibility.Visible;

    internal void PaddocksChanged()
    {
        OnPropertyChanged(nameof(Paddocks));
        OnPropertyChanged(nameof(TargetPaddockId));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(ProblemVisibility));
    }

    private Visibility Shows(params RuleMatchType[] types)
        => types.Contains(_rule.MatchType) ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
