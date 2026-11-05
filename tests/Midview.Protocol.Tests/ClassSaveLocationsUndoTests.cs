using Microsoft.Win32;
using Midview.Protocol;
using Xunit;

// Runs against a throwaway HKCU subtree standing in for one student's Software
// key, so no real Downloads, Office or dialog value is touched.
public sealed class ClassSaveLocationsUndoTests : IDisposable
{
    private const string ClassFolder = @"C:\U\Desktop\Pasta da turma – 7A";
    private const string Excel = ClassSaveTargets.OfficeRoot + @"\Excel\Options";
    private const string DownloadsBefore = @"%USERPROFILE%\Downloads";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;

    public ClassSaveLocationsUndoTests()
    {
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _software.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    private void Put(string key, string name, object value, RegistryValueKind kind)
    {
        using var k = _software.CreateSubKey(key, writable: true);
        k.SetValue(name, value, kind);
    }

    private object? Raw(string key, string name, out RegistryValueKind kind)
    {
        kind = RegistryValueKind.Unknown;
        using var k = _software.OpenSubKey(key);
        var value = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null)
            kind = k!.GetValueKind(name);
        return value;
    }

    private static string Mru(string program, params byte[] idList) =>
        Convert.ToBase64String(LastVisitedMru.Entry(program, idList));

    private static SavedSetting Downloads(string? before) => new(
        ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, RegistryValueKind.ExpandString, before, ClassFolder);

    private static SavedSetting ExcelSetting(string? before) => new(
        Excel, "DefaultPath", RegistryValueKind.ExpandString, before, ClassFolder);

    private void PutDownloads(string value) =>
        Put(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, value, RegistryValueKind.ExpandString);

    private void PutOrder(params int[] order) =>
        Put(ClassSaveTargets.MruKey, LastVisitedMru.OrderName, LastVisitedMru.FormatOrder(order), RegistryValueKind.Binary);

    private byte[]? Order() => (byte[]?)Raw(ClassSaveTargets.MruKey, LastVisitedMru.OrderName, out _);

    [Fact]
    public void PutsBackWhatWasThere()
    {
        PutDownloads(ClassFolder);

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { Downloads(DownloadsBefore) });

        Assert.Empty(problems);
        Assert.Equal(DownloadsBefore, Raw(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, out var kind));
        Assert.Equal(RegistryValueKind.ExpandString, kind);
    }

    [Fact]
    public void DeletesWhatWasNotThere()
    {
        Put(Excel, "DefaultPath", ClassFolder, RegistryValueKind.ExpandString);

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { ExcelSetting(null) });

        Assert.Empty(problems);
        Assert.Null(Raw(Excel, "DefaultPath", out _));
    }

    [Fact]
    public void LeavesWhatTheStudentChanged()
    {
        PutDownloads(@"D:\Meus downloads");

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { Downloads(DownloadsBefore) });

        Assert.Empty(problems);
        Assert.Equal(@"D:\Meus downloads", Raw(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, out _));
    }

    [Fact]
    public void SetDownloads_IsUsedWhenGiven()
    {
        PutDownloads(ClassFolder);
        string? received = null;

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { Downloads(DownloadsBefore) }, s => received = s);

        Assert.Empty(problems);
        Assert.Equal(DownloadsBefore, received);
        // The shell call may store the path expanded, so the raw value is written after it.
        Assert.Equal(DownloadsBefore, Raw(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, out var kind));
        Assert.Equal(RegistryValueKind.ExpandString, kind);
    }

    [Fact]
    public void SetDownloads_IsNotUsedWhenThereWasNoBefore()
    {
        PutDownloads(ClassFolder);
        var called = false;

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { Downloads(null) }, _ => called = true);

        Assert.Empty(problems);
        Assert.False(called);
        Assert.Null(Raw(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, out _));
    }

    [Fact]
    public void MruNewEntry_LeavesTheOrder()
    {
        var written = Mru("notepad.exe", 1, 2);
        Put(ClassSaveTargets.MruKey, "3", Convert.FromBase64String(written), RegistryValueKind.Binary);
        PutOrder(3, 0);
        var setting = new SavedSetting(ClassSaveTargets.MruKey, "3", RegistryValueKind.Binary, null, written);

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { setting });

        Assert.Empty(problems);
        Assert.Null(Raw(ClassSaveTargets.MruKey, "3", out _));
        Assert.Equal(LastVisitedMru.FormatOrder(new[] { 0 }), Order());
    }

    [Fact]
    public void MruExistingEntry_PutsBytesBack()
    {
        var before = Mru("notepad.exe", 7, 7);
        var written = Mru("notepad.exe", 1, 2);
        Put(ClassSaveTargets.MruKey, "0", Convert.FromBase64String(written), RegistryValueKind.Binary);
        PutOrder(0);
        var setting = new SavedSetting(ClassSaveTargets.MruKey, "0", RegistryValueKind.Binary, before, written);

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { setting });

        Assert.Empty(problems);
        Assert.Equal(Convert.FromBase64String(before), (byte[]?)Raw(ClassSaveTargets.MruKey, "0", out _));
        Assert.Equal(LastVisitedMru.FormatOrder(new[] { 0 }), Order());
    }

    [Fact]
    public void MruMalformedOrder_StillDeletesEntry_LeavesOrder()
    {
        var written = Mru("mspaint.exe", 1, 2);
        Put(ClassSaveTargets.MruKey, "3", Convert.FromBase64String(written), RegistryValueKind.Binary);
        var malformed = new byte[] { 3, 0, 0, 0, 9 };
        Put(ClassSaveTargets.MruKey, LastVisitedMru.OrderName, malformed, RegistryValueKind.Binary);
        var setting = new SavedSetting(ClassSaveTargets.MruKey, "3", RegistryValueKind.Binary, null, written);

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { setting });

        Assert.Empty(problems);
        Assert.Null(Raw(ClassSaveTargets.MruKey, "3", out _));
        Assert.Equal(malformed, Order());
    }

    [Fact]
    public void DisallowedSetting_IsSkipped()
    {
        const string key = @"Policies\Microsoft\Windows\System";
        Put(key, "Anything", "x", RegistryValueKind.String);
        var setting = new SavedSetting(key, "Anything", RegistryValueKind.String, null, "x");

        var problems = ClassSaveLocationsUndo.Run(_software, new[] { setting });

        Assert.Empty(problems);
        Assert.Equal("x", Raw(key, "Anything", out _));
    }

    [Fact]
    public void OneFailure_DoesNotStopTheOthers()
    {
        PutDownloads(ClassFolder);
        Put(Excel, "DefaultPath", ClassFolder, RegistryValueKind.ExpandString);

        var problems = ClassSaveLocationsUndo.Run(
            _software,
            new[] { Downloads(DownloadsBefore), ExcelSetting(@"C:\Old") },
            _ => throw new InvalidOperationException("shell"));

        Assert.Single(problems);
        Assert.Equal(@"C:\Old", Raw(Excel, "DefaultPath", out _));
        // The stored text is still put right when only the shell call failed.
        Assert.Equal(DownloadsBefore, Raw(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName, out _));
    }

    [Fact]
    public void NullSettings_ReturnsNoProblems()
    {
        Assert.Empty(ClassSaveLocationsUndo.Run(_software, null!));
    }

    [Fact]
    public void NullEntry_IsSkipped()
    {
        Put(Excel, "DefaultPath", ClassFolder, RegistryValueKind.ExpandString);

        var problems = ClassSaveLocationsUndo.Run(_software, new SavedSetting?[] { null, ExcelSetting(null) }!);

        Assert.Empty(problems);
        Assert.Null(Raw(Excel, "DefaultPath", out _));
    }

    private static IEnumerable<SavedSetting> ThenThrows(SavedSetting first)
    {
        yield return first;
        throw new IOException("lista ilegível");
    }

    [Fact]
    public void ThrowingEnumerable_DoesNotEscape()
    {
        Put(Excel, "DefaultPath", ClassFolder, RegistryValueKind.ExpandString);

        var problems = ClassSaveLocationsUndo.Run(_software, ThenThrows(ExcelSetting(@"C:\Old")));

        Assert.Single(problems);
        Assert.Equal(@"C:\Old", Raw(Excel, "DefaultPath", out _));
    }
}
