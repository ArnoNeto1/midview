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
