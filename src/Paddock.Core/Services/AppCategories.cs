namespace Paddock.Core.Services;

/// <summary>Broad kinds of application a desktop shortcut can start.</summary>
public enum AppCategory
{
    Games,
    Creative,
    Development,
    Office,
    Internet,
    Communication,
    Media,
}

/// <summary>
/// Tells what kind of app a shortcut starts, from where it points: a game
/// launcher's link (<c>steam://rungameid/…</c>), a game library folder
/// (<c>…\steamapps\common\…</c>), or a well-known program
/// (<c>photoshop.exe</c>, <c>code.exe</c>, …).
///
/// It is a curated list, not magic: an app it does not know is simply not
/// categorised (and a name or folder rule can still route it).
/// </summary>
public static class AppCategories
{
    private static readonly Dictionary<string, AppCategory> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steam"] = AppCategory.Games,
        ["com.epicgames.launcher"] = AppCategory.Games,
        ["uplay"] = AppCategory.Games,
        ["origin"] = AppCategory.Games,
        ["origin2"] = AppCategory.Games,
        ["link2ea"] = AppCategory.Games,
        ["goggalaxy"] = AppCategory.Games,
        ["battlenet"] = AppCategory.Games,
        ["riotclient"] = AppCategory.Games,
        ["roblox"] = AppCategory.Games,
        ["roblox-player"] = AppCategory.Games,
        ["minecraft"] = AppCategory.Games,
        ["http"] = AppCategory.Internet,
        ["https"] = AppCategory.Internet,
        ["spotify"] = AppCategory.Media,
        ["ms-teams"] = AppCategory.Communication,
        ["msteams"] = AppCategory.Communication,
        ["slack"] = AppCategory.Communication,
        ["discord"] = AppCategory.Communication,
        ["zoommtg"] = AppCategory.Communication,
        ["tg"] = AppCategory.Communication,
        ["whatsapp"] = AppCategory.Communication,
        ["vscode"] = AppCategory.Development,
    };

    private static readonly Dictionary<string, AppCategory> Programs = Build(
        (AppCategory.Games,
        [
            "steam.exe", "epicgameslauncher.exe", "galaxyclient.exe", "battle.net.exe", "battle.net launcher.exe",
            "eadesktop.exe", "origin.exe", "upc.exe", "ubisoftconnect.exe", "riotclientservices.exe",
            "minecraftlauncher.exe", "minecraft.exe", "robloxplayerbeta.exe", "playnite.desktopapp.exe",
            "playnite.fullscreenapp.exe", "heroic.exe", "itch.exe", "amazon games.exe", "rockstarlauncher.exe",
        ]),
        (AppCategory.Creative,
        [
            "photoshop.exe", "illustrator.exe", "indesign.exe", "afterfx.exe", "adobe premiere pro.exe",
            "lightroom.exe", "lightroomclassic.exe", "adobe xd.exe", "animate.exe", "audition.exe",
            "figma.exe", "blender.exe", "krita.exe", "inkscape.exe", "gimp.exe", "paintdotnet.exe",
            "designer.exe", "photo.exe", "resolve.exe", "obs64.exe", "audacity.exe", "canva.exe", "capcut.exe",
            "clipstudiopaint.exe", "sketchup.exe", "maya.exe", "3dsmax.exe", "cinema 4d.exe", "houdini.exe",
            "zbrush.exe", "fl64.exe", "reaper.exe", "studio one.exe", "cubase13.exe", "affinity.exe",
            "substance painter.exe", "darktable.exe", "rawtherapee.exe", "kdenlive.exe", "shotcut.exe",
            "vegas.exe", "handbrake.exe",
        ]),
        (AppCategory.Development,
        [
            "code.exe", "code - insiders.exe", "cursor.exe", "devenv.exe", "rider64.exe", "idea64.exe",
            "pycharm64.exe", "webstorm64.exe", "clion64.exe", "goland64.exe", "datagrip64.exe",
            "phpstorm64.exe", "rubymine64.exe", "studio64.exe", "sublime_text.exe", "notepad++.exe",
            "githubdesktop.exe", "gitkraken.exe", "sourcetree.exe", "postman.exe", "insomnia.exe",
            "docker desktop.exe", "wt.exe", "windowsterminal.exe", "unity hub.exe", "unity.exe",
            "unrealeditor.exe", "eclipse.exe", "zed.exe", "dbeaver.exe", "ssms.exe", "putty.exe", "winscp.exe",
        ]),
        (AppCategory.Office,
        [
            "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe", "onenote.exe", "msaccess.exe",
            "mspub.exe", "visio.exe", "winproj.exe", "acrobat.exe", "acrord32.exe", "foxitpdfreader.exe",
            "sumatrapdf.exe", "soffice.exe", "swriter.exe", "scalc.exe", "simpress.exe", "notion.exe",
            "obsidian.exe", "evernote.exe", "wps.exe", "et.exe", "wpp.exe",
        ]),
        (AppCategory.Internet,
        [
            "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe", "vivaldi.exe", "arc.exe",
            "iexplore.exe", "thunderbird.exe", "tor.exe", "qbittorrent.exe", "filezilla.exe",
        ]),
        (AppCategory.Communication,
        [
            "teams.exe", "ms-teams.exe", "slack.exe", "discord.exe", "zoom.exe", "skype.exe", "telegram.exe",
            "whatsapp.exe", "signal.exe", "element.exe", "webex.exe", "mattermost.exe",
        ]),
        (AppCategory.Media,
        [
            "spotify.exe", "vlc.exe", "itunes.exe", "wmplayer.exe", "potplayermini64.exe", "mpc-hc64.exe",
            "mpc-be64.exe", "foobar2000.exe", "musicbee.exe", "aimp.exe", "plex.exe", "plex htpc.exe",
            "kodi.exe", "mpv.exe", "tidal.exe", "applemusic.exe",
        ]));

    /// <summary>Game library folders. A shortcut pointing into one of these starts a game.</summary>
    private static readonly string[] GameFolders =
    [
        @"\steamapps\common\", @"\epic games\", @"\gog games\", @"\gog galaxy\games\", @"\xboxgames\",
        @"\riot games\", @"\ubisoft game launcher\games\", @"\ea games\", @"\origin games\",
        @"\rockstar games\", @"\amazon games\library\", @"\itch\apps\",
    ];

    /// <summary>
    /// The category of the app a shortcut starts, or <c>null</c> when it is
    /// not one Paddock recognises. <paramref name="target"/> is the program
    /// path a <c>.lnk</c> points at, or the URL of a <c>.url</c>.
    /// </summary>
    public static AppCategory? Classify(string? target, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        target = target.Trim().Trim('"');

        var schemeEnd = target.IndexOf(':');
        if (schemeEnd > 1 && !IsDriveOrUncPath(target))
        {
            var scheme = target[..schemeEnd];
            return Schemes.TryGetValue(scheme, out var byScheme) ? byScheme : null;
        }

        var program = ProgramName(target, arguments);
        if (Programs.TryGetValue(program, out var byName))
            return byName;

        var folder = target.Replace('/', '\\');
        if (GameFolders.Any(f => folder.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return AppCategory.Games;

        // GOG Galaxy and Battle.net start their games through the launcher.
        if (arguments is not null
            && (arguments.Contains("/command=runGame", StringComparison.OrdinalIgnoreCase)
                || arguments.Contains("--exec=\"launch", StringComparison.OrdinalIgnoreCase)
                || arguments.Contains("--exec=launch", StringComparison.OrdinalIgnoreCase)))
        {
            return AppCategory.Games;
        }

        if (folder.Contains(@"\adobe\", StringComparison.OrdinalIgnoreCase))
            return AppCategory.Creative;

        if (folder.Contains(@"\jetbrains\", StringComparison.OrdinalIgnoreCase))
            return AppCategory.Development;

        return null;
    }

    /// <summary>
    /// The program's file name. Apps installed by Squirrel (Slack, Discord,
    /// Teams classic…) are started as <c>Update.exe --processStart "Slack.exe"</c>:
    /// for those it is the program named in the arguments.
    /// </summary>
    private static string ProgramName(string target, string? arguments)
    {
        var name = FileName(target);

        if (!string.Equals(name, "update.exe", StringComparison.OrdinalIgnoreCase) || arguments is null)
            return name;

        const string Switch = "--processStart";
        var at = arguments.IndexOf(Switch, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return name;

        var rest = arguments[(at + Switch.Length)..].TrimStart(' ', '=');
        var started = rest.StartsWith('"')
            ? rest[1..].Split('"')[0]
            : rest.Split(' ')[0];

        return string.IsNullOrWhiteSpace(started) ? name : FileName(started);
    }

    private static string FileName(string path)
    {
        var slash = path.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static bool IsDriveOrUncPath(string target)
        => target.StartsWith(@"\\", StringComparison.Ordinal)
           || (target.Length >= 2 && char.IsLetter(target[0]) && target[1] == ':');

    private static Dictionary<string, AppCategory> Build(params (AppCategory Category, string[] Names)[] groups)
    {
        var map = new Dictionary<string, AppCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, names) in groups)
        {
            foreach (var name in names)
                map[name] = category;
        }

        return map;
    }
}
