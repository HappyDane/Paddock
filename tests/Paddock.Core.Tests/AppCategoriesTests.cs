using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>Which kind of app a desktop shortcut starts.</summary>
public class AppCategoriesTests
{
    [Theory]
    [InlineData("steam://rungameid/570", null, AppCategory.Games)]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch", null, AppCategory.Games)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\Hades.exe", null, AppCategory.Games)]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe", "/command=runGame /gameId=1207658924", AppCategory.Games)]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe", "--exec=\"launch Pro\"", AppCategory.Games)]
    [InlineData(@"C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe", null, AppCategory.Creative)]
    [InlineData(@"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe", null, AppCategory.Creative)]
    [InlineData(@"C:\Program Files\Adobe\Adobe Substance 3D Designer\Something.exe", null, AppCategory.Creative)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", null, AppCategory.Development)]
    [InlineData(@"C:\Program Files\JetBrains\Fleet\fleet.exe", null, AppCategory.Development)]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", null, AppCategory.Office)]
    [InlineData(@"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe", null, AppCategory.Office)]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", null, AppCategory.Internet)]
    [InlineData("https://example.com/", null, AppCategory.Internet)]
    [InlineData(@"C:\Users\me\AppData\Local\Discord\Update.exe", "--processStart Discord.exe", AppCategory.Communication)]
    [InlineData(@"C:\Users\me\AppData\Local\slack\Update.exe", "--processStart \"slack.exe\"", AppCategory.Communication)]
    [InlineData(@"C:\Users\me\AppData\Roaming\Spotify\Spotify.exe", null, AppCategory.Media)]
    public void Classify_KnowsCommonApps(string target, string? arguments, AppCategory expected)
        => Assert.Equal(expected, AppCategories.Classify(target, arguments));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Tools\mytool.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Something\Update.exe")]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    public void Classify_LeavesUnknownAppsAlone(string? target)
        => Assert.Null(AppCategories.Classify(target));
}
