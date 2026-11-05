using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Midview.Protocol;
using Xunit;

// Registry cases run against a throwaway HKCU subtree standing in for one
// student's Software key, so no real Downloads, Office or dialog value is touched.
public sealed class ClassSaveTargetsTests : IDisposable
{
    private const string Word = ClassSaveTargets.OfficeRoot + @"\Word\Options";
    private const string Excel = ClassSaveTargets.OfficeRoot + @"\Excel\Options";
    private const string PowerPoint = ClassSaveTargets.OfficeRoot + @"\PowerPoint\RecentFolderList";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;
    private readonly List<string> _links = new();

    public ClassSaveTargetsTests()
    {
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        // A link key is deleted as itself first: DeleteSubKeyTree would follow it.
        foreach (var link in _links)
            DeleteLink(link);
        _software.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    private static string Mru(string program, params byte[] idList) =>
        Convert.ToBase64String(LastVisitedMru.Entry(program, idList));

    [Theory]
    [InlineData(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, RegistryValueKind.ExpandString, @"%USERPROFILE%\Downloads", @"C:\Turma")]
    [InlineData(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, RegistryValueKind.String, @"C:\Downloads", @"C:\Turma")]
    [InlineData(Word, "DOC-PATH", RegistryValueKind.ExpandString, null, @"C:\Turma")]
    [InlineData(Excel, "DefaultPath", RegistryValueKind.ExpandString, @"C:\Old", @"C:\Turma")]
    [InlineData(PowerPoint, "Default", RegistryValueKind.String, null, @"C:\Turma")]
    [InlineData(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.DWord, "1", "0")]
    [InlineData(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.DWord, null, "0")]
    // The match ignores case, as the registry does.
    [InlineData(@"microsoft\windows\currentversion\explorer\user shell folders", "{374de290-123f-4565-9164-39c4925e467b}", RegistryValueKind.String, null, @"C:\Turma")]
    public void IsAllowed_TheTargets(string key, string name, RegistryValueKind kind, string? before, string written)
    {
        Assert.True(ClassSaveTargets.IsAllowed(new SavedSetting(key, name, kind, before, written)));
    }

    [Fact]
    public void IsAllowed_TheMruTargets()
    {
        foreach (var program in new[] { "notepad.exe", "MSPAINT.EXE" })
        {
            Assert.True(ClassSaveTargets.IsAllowed(new SavedSetting(
                ClassSaveTargets.MruKey, "0", RegistryValueKind.Binary, null, Mru(program, 1, 2))));
            Assert.True(ClassSaveTargets.IsAllowed(new SavedSetting(
                ClassSaveTargets.MruKey, "9999", RegistryValueKind.Binary, Mru(program, 9), Mru(program, 1, 2))));
        }
    }

    public static IEnumerable<object[]> Refused()
    {
        var mruKey = ClassSaveTargets.MruKey;
        var downloads = (ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName);
        // A key the Agent, as SYSTEM, must never write for a student's file.
        yield return new object[] { new SavedSetting(@"Policies\Microsoft\Windows\System", "Anything", RegistryValueKind.String, null, "x") };
        yield return new object[] { new SavedSetting(downloads.Item1, downloads.Item2, RegistryValueKind.Binary, null, "AAAA") };
        yield return new object[] { new SavedSetting(downloads.Item1, downloads.Item2, (RegistryValueKind)99, null, "x") };
        yield return new object[] { new SavedSetting(downloads.Item1, "{00000000-0000-0000-0000-000000000000}", RegistryValueKind.String, null, "x") };
        yield return new object[] { new SavedSetting(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.String, null, "0") };
        yield return new object[] { new SavedSetting(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.DWord, null, "zero") };
        yield return new object[] { new SavedSetting(mruKey, "x1", RegistryValueKind.Binary, null, Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "12345", RegistryValueKind.Binary, null, Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "", RegistryValueKind.Binary, null, Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, ClassSaveTargetsTestsData.Arabic, RegistryValueKind.Binary, null, Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.Binary, null, Mru("cmd.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.String, null, Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.Binary, Mru("mspaint.exe", 1), Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.Binary, "not base64!", Mru("notepad.exe", 1)) };
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.Binary, null, "not base64!") };
        // No UTF-16 null ends a program name.
        yield return new object[] { new SavedSetting(mruKey, "0", RegistryValueKind.Binary, null, Convert.ToBase64String(Encoding.Unicode.GetBytes("notepad.exe"))) };
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void IsAllowed_RefusesOthers(SavedSetting setting)
    {
        Assert.False(ClassSaveTargets.IsAllowed(setting));
    }

    [Fact]
    public void IsAllowed_RefusesNullStrings()
    {
        Assert.False(ClassSaveTargets.IsAllowed(new SavedSetting(null!, null!, RegistryValueKind.String, null, null!)));
    }

    public static IEnumerable<object[]> Kinds()
    {
        yield return new object[] { RegistryValueKind.String, @"C:\Pasta da turma – 7A", @"C:\Pasta da turma – 7A" };
        // Restoring Downloads uses the raw text, so the variable must stay unexpanded.
        yield return new object[] { RegistryValueKind.ExpandString, @"%USERPROFILE%\Downloads", @"%USERPROFILE%\Downloads" };
        yield return new object[] { RegistryValueKind.DWord, "0", "0" };
        yield return new object[] { RegistryValueKind.DWord, "-1", "-1" };
        yield return new object[] { RegistryValueKind.Binary, Convert.ToBase64String(new byte[] { 1, 2, 3, 255 }), Convert.ToBase64String(new byte[] { 1, 2, 3, 255 }) };
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void WriteThenRead_EachKind(RegistryValueKind kind, string encoded, string expected)
    {
        ClassSaveTargets.WriteEncoded(_software, @"A\B", "V", kind, encoded);

        var read = ClassSaveTargets.ReadEncoded(_software, @"A\B", "V", out var readKind);

        Assert.Equal(expected, read);
        Assert.Equal(kind, readKind);
    }

    [Fact]
    public void WriteEncoded_NullDeletesTheValue()
    {
        ClassSaveTargets.WriteEncoded(_software, "A", "V", RegistryValueKind.String, "x");
        ClassSaveTargets.WriteEncoded(_software, "A", "V", RegistryValueKind.String, null);

        Assert.Null(ClassSaveTargets.ReadEncoded(_software, "A", "V", out _));
        // A missing key is nothing to delete.
        ClassSaveTargets.WriteEncoded(_software, "Missing", "V", RegistryValueKind.String, null);
        Assert.Null(_software.OpenSubKey("Missing"));
    }

    [Fact]
    public void ReadEncoded_MissingKeyOrValueIsNull()
    {
        Assert.Null(ClassSaveTargets.ReadEncoded(_software, "Nope", "V", out _));
        _software.CreateSubKey("Here").Dispose();
        Assert.Null(ClassSaveTargets.ReadEncoded(_software, "Here", "V", out _));
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegSetValueExW(SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegCreateKeyExW(SafeRegistryHandle parent, string name, int reserved, string? cls, int options,
        int access, IntPtr security, out SafeRegistryHandle result, out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyExW(SafeRegistryHandle parent, string name, int options, int access, out SafeRegistryHandle result);

    [DllImport("ntdll.dll")]
    private static extern uint NtDeleteKey(SafeRegistryHandle key);

    // RegSetValueEx does not check the size of a REG_DWORD, so a student can plant this.
    [Fact]
    public void ReadEncoded_OversizedDWordIsNullWithoutThrowing()
    {
        using var key = _software.CreateSubKey("A");
        Assert.Equal(0, RegSetValueExW(key.Handle, "V", 0, 4, new byte[8], 8));

        var read = ClassSaveTargets.ReadEncoded(_software, "A", "V", out var kind);

        Assert.Null(read);
        Assert.Equal(RegistryValueKind.DWord, kind);
    }

    // The link is volatile, so it is gone at logoff even if a test dies.
    private bool TryMakeLink(string linkName, string targetRelative)
    {
        const int volatileLink = 0x2 | 0x1;
        const int allAccessAndCreateLink = 0xF003F | 0x20;
        var result = RegCreateKeyExW(_software.Handle, linkName, 0, null, volatileLink, allAccessAndCreateLink,
            IntPtr.Zero, out var link, out _);
        if (result != 0)
            return false;
        using (link)
        {
            _links.Add(linkName);
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            var target = $@"\REGISTRY\USER\{sid}\{_rootPath}\{targetRelative}";
            var bytes = Encoding.Unicode.GetBytes(target);
            Assert.Equal(0, RegSetValueExW(link, "SymbolicLinkValue", 0, 6, bytes, bytes.Length));
        }
        return true;
    }

    private void DeleteLink(string linkName)
    {
        if (RegOpenKeyExW(_software.Handle, linkName, 0x8, 0x10000 | 0x1, out var link) == 0)
        {
            using (link)
                NtDeleteKey(link);
        }
    }

    [Fact]
    public void RegistryLink_IsNotFollowed()
    {
        _software.CreateSubKey("target").Dispose();
        ClassSaveTargets.WriteEncoded(_software, @"target\Sub", "V", RegistryValueKind.String, "planted");
        // Without a link there is nothing to test, so that is a failure, not a pass.
        Assert.True(TryMakeLink("link", "target"),
            "Could not create a registry link under HKCU (RegCreateKeyEx with REG_OPTION_CREATE_LINK failed), so the link check was not tested.");

        // Read through the link finds nothing, though the target holds a value.
        Assert.Null(ClassSaveTargets.ReadEncoded(_software, @"link\Sub", "V", out var kind));
        Assert.Equal(RegistryValueKind.Unknown, kind);

        // Neither a write nor a delete goes through, and nothing new appears under the target.
        Assert.Throws<InvalidOperationException>(() =>
            ClassSaveTargets.WriteEncoded(_software, @"link\Sub", "V", RegistryValueKind.String, "x"));
        Assert.Throws<InvalidOperationException>(() =>
            ClassSaveTargets.WriteEncoded(_software, @"link\Sub", "V", RegistryValueKind.String, null));
        Assert.Throws<InvalidOperationException>(() =>
            ClassSaveTargets.WriteEncoded(_software, @"link\Fresh", "V", RegistryValueKind.String, "x"));
        Assert.Equal("planted", ClassSaveTargets.ReadEncoded(_software, @"target\Sub", "V", out _));
        using var target = _software.OpenSubKey("target")!;
        Assert.Null(target.OpenSubKey("Fresh"));
    }

    [Fact]
    public void NormalKeys_PassTheLinkCheck()
    {
        ClassSaveTargets.WriteEncoded(_software, @"A\B\C", "V", RegistryValueKind.String, "ok");
        Assert.Equal("ok", ClassSaveTargets.ReadEncoded(_software, @"A\B\C", "V", out _));
        ClassSaveTargets.WriteEncoded(_software, @"A\B\C", "V", RegistryValueKind.String, null);
        Assert.Null(ClassSaveTargets.ReadEncoded(_software, @"A\B\C", "V", out _));
    }

    private static SavedSetting Text(string written, RegistryValueKind kind = RegistryValueKind.ExpandString) =>
        new(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, kind, null, written);

    [Fact]
    public void SameValue_StringsIgnoreCaseAndTrailingSlash()
    {
        var setting = Text(@"c:\x\pasta da turma – 7a", RegistryValueKind.ExpandString);

        Assert.True(ClassSaveTargets.SameValue(@"C:\X\Pasta da turma – 7A\", RegistryValueKind.String, setting));
        Assert.True(ClassSaveTargets.SameValue(@"C:\X\Pasta da turma – 7A", RegistryValueKind.ExpandString, setting));
        Assert.False(ClassSaveTargets.SameValue(@"C:\X\Outra", RegistryValueKind.String, setting));
        Assert.False(ClassSaveTargets.SameValue(@"C:\X\Pasta da turma – 7A", RegistryValueKind.DWord, setting));
    }

    [Fact]
    public void SameValue_NullIsNeverEqual()
    {
        Assert.False(ClassSaveTargets.SameValue(null, RegistryValueKind.String, Text("")));
        Assert.False(ClassSaveTargets.SameValue(null, RegistryValueKind.DWord,
            new SavedSetting(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.DWord, null, "0")));
    }

    [Fact]
    public void SameValue_DWordIsExact()
    {
        var setting = new SavedSetting(ClassSaveTargets.PreferCloudKey, ClassSaveTargets.PreferCloudName, RegistryValueKind.DWord, "1", "0");

        Assert.True(ClassSaveTargets.SameValue("0", RegistryValueKind.DWord, setting));
        Assert.False(ClassSaveTargets.SameValue("1", RegistryValueKind.DWord, setting));
        Assert.False(ClassSaveTargets.SameValue("0", RegistryValueKind.String, setting));
    }

    [Fact]
    public void SameValue_BinaryExact()
    {
        var written = Convert.ToBase64String(LastVisitedMru.Entry("notepad.exe", new byte[] { 1, 2, 3 }));
        var other = Convert.ToBase64String(LastVisitedMru.Entry("notepad.exe", new byte[] { 1, 2, 4 }));
        var setting = new SavedSetting(ClassSaveTargets.MruKey, "0", RegistryValueKind.Binary, null, written);

        Assert.True(ClassSaveTargets.SameValue(written, RegistryValueKind.Binary, setting));
        Assert.False(ClassSaveTargets.SameValue(other, RegistryValueKind.Binary, setting));
        Assert.False(ClassSaveTargets.SameValue(written, RegistryValueKind.String, setting));
    }
}

// Kept out of the attribute-free helpers above: an Arabic-Indic digit is a decimal
// digit to \d, but never a name Windows gave an entry.
internal static class ClassSaveTargetsTestsData
{
    public const string Arabic = "\u0661";
}
