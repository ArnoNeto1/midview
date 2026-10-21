using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Against a throwaway HKCU subtree standing in for one student's
// HKEY_USERS\<SID>\Software, so the real Chrome and Edge policy keys are never
// touched.
public sealed class DownloadFolderPolicyTests : IDisposable
{
    private const string ChromePath = @"Policies\Google\Chrome";
    private const string EdgePath = @"Policies\Microsoft\Edge";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;

    public DownloadFolderPolicyTests()
    {
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _software.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    private object? Read(string path, string name)
    {
        using var key = _software.OpenSubKey(path);
        return key?.GetValue(name);
    }

    [Fact]
    public void Apply_WritesChromeAndEdge()
    {
        var folder = @"C:\Users\aluno\Desktop\Pasta da turma – 7A";

        DownloadFolderPolicy.Apply(_software, folder);

        Assert.Equal(folder, Read(ChromePath, "DownloadDirectory"));
        Assert.Equal(folder, Read(EdgePath, "DownloadDirectory"));
        using var chrome = _software.OpenSubKey(ChromePath)!;
        Assert.Equal(RegistryValueKind.String, chrome.GetValueKind("DownloadDirectory"));
        Assert.Equal(folder, DownloadFolderPolicy.Current(_software));
    }

    // Only a class folder is Midview's: a download folder set by anyone else stays, browser by browser.
    [Fact]
    public void Remove_LeavesADownloadFolderThatIsNoClassFolder()
    {
        using (var chrome = _software.CreateSubKey(ChromePath, writable: true))
        {
            chrome.SetValue("DownloadDirectory", @"D:\Downloads da escola", RegistryValueKind.String);
        }
        using (var edge = _software.CreateSubKey(EdgePath, writable: true))
        {
            edge.SetValue("DownloadDirectory", @"C:\Users\aluno\Desktop\Pasta da turma – 7A", RegistryValueKind.String);
        }

        DownloadFolderPolicy.Remove(_software);

        Assert.Equal(@"D:\Downloads da escola", Read(ChromePath, "DownloadDirectory"));
        Assert.Null(Read(EdgePath, "DownloadDirectory"));
    }

    [Theory]
    [InlineData(@"C:\Users\aluno\Desktop\Pasta da turma – 7A", true)]
    [InlineData(@"C:\Users\aluno\Desktop\Pasta da turma – 7A\", true)]
    [InlineData(@"D:\Downloads", false)]
    [InlineData(@"C:\Users\aluno\Desktop\Pasta da turma - 7A", false)] // a hyphen, not the en dash
    [InlineData(@"Pasta da turma – 7A", false)]
    [InlineData(@"${user_home}\Pasta da turma – 7A", false)]
    [InlineData("", false)]
    public void IsClassFolder_IsWhatApplyWrites(string folder, bool expected)
    {
        Assert.Equal(expected, DownloadFolderPolicy.IsClassFolder(folder));
    }

    [Fact]
    public void Remove_DeletesOnlyDownloadDirectory()
    {
        DownloadFolderPolicy.Apply(_software, @"C:\Users\aluno\Desktop\Pasta da turma – 7A");
        using (var chrome = _software.OpenSubKey(ChromePath, writable: true)!)
        {
            chrome.SetValue("DownloadRestriction", 3, RegistryValueKind.DWord);
        }

        DownloadFolderPolicy.Remove(_software);

        Assert.Null(Read(ChromePath, "DownloadDirectory"));
        Assert.Null(Read(EdgePath, "DownloadDirectory"));
        Assert.Null(DownloadFolderPolicy.Current(_software));
        Assert.Equal(3, Read(ChromePath, "DownloadRestriction"));
    }

    [Fact]
    public void Remove_WithNothingToRemove_DoesNothing()
    {
        DownloadFolderPolicy.Remove(_software);

        Assert.Null(_software.OpenSubKey(ChromePath));
        Assert.Null(_software.OpenSubKey(EdgePath));
    }

    [Fact]
    public void Current_IsNullWhenAbsent()
    {
        Assert.Null(DownloadFolderPolicy.Current(_software));
    }
}
