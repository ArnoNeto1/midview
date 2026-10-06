using Midview.Master;
using Xunit;

public class SavedMenuTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-menu-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Add_PutsItAtTheEnd()
    {
        var menu = SavedMenu.Add(new[] { new MenuEntry("YouTube", "https://youtube.com/") }, new MenuEntry(" Scratch ", "https://scratch.mit.edu/"));

        Assert.Equal(new[] { new MenuEntry("YouTube", "https://youtube.com/"), new MenuEntry("Scratch", "https://scratch.mit.edu/") }, menu);
    }

    // Saving again under a name already in the menu changes that item.
    [Fact]
    public void Add_AnExistingName_ReplacesWhatItOpens_InItsPlace()
    {
        var menu = SavedMenu.Add(
            new[] { new MenuEntry("Editor", "notepad"), new MenuEntry("Terminal", "powershell") },
            new MenuEntry("editor", "notepad++"));

        Assert.Equal(new[] { new MenuEntry("editor", "notepad++"), new MenuEntry("Terminal", "powershell") }, menu);
    }

    [Fact]
    public void Remove_TakesOutThatName()
    {
        var menu = SavedMenu.Remove(new[] { new MenuEntry("Editor", "notepad"), new MenuEntry("Terminal", "powershell") }, "EDITOR");

        Assert.Equal(new[] { new MenuEntry("Terminal", "powershell") }, menu);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", "youtube.com")]
    [InlineData("https://scratch.mit.edu/", "scratch.mit.edu")]
    [InlineData("http://192.168.0.10/prova", "192.168.0.10")]
    public void NameFor_AWebsite_IsItsHost(string address, string expected)
    {
        Assert.Equal(expected, SavedMenu.NameFor(new Uri(address)));
    }

    [Theory]
    [InlineData("powershell -NoExit", "powershell")]
    [InlineData("\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\" aula.mp4\nnotepad", "vlc")]
    [InlineData("%USERPROFILE%\\Desktop\\aula.pptx", "aula")]
    public void NameFor_Programs_IsTheFirstOnesName(string commands, string expected)
    {
        Assert.Equal(expected, SavedMenu.NameFor(commands));
    }

    [Fact]
    public void Save_ThenLoad_GivesThemBackInOrder()
    {
        var menu = new[] { new MenuEntry("VLC", "\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\""), new MenuEntry("Dois", "notepad\ncalc") };

        SavedMenu.Save(_path, menu);

        Assert.Equal(menu, SavedMenu.Load(_path));
    }

    [Fact]
    public void Load_MissingBrokenOrBlank_LeavesThoseOut()
    {
        Assert.Empty(SavedMenu.Load(_path));

        File.WriteAllText(_path, "{ isto não é um menu");
        Assert.Empty(SavedMenu.Load(_path));

        File.WriteAllText(_path, "[{\"Name\":\"\",\"Target\":\"notepad\"},{\"Name\":\"Calc\",\"Target\":\" \"},{\"Name\":\"Paint\",\"Target\":\"mspaint\"}]");
        Assert.Equal(new[] { new MenuEntry("Paint", "mspaint") }, SavedMenu.Load(_path));
    }
}
