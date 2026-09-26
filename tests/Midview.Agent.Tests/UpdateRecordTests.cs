using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class UpdateRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-record-{Guid.NewGuid():N}");
    private readonly UpdatePaths _paths;

    public UpdateRecordTests()
    {
        _paths = new UpdatePaths(Path.Combine(_root, "Midview.update"), Path.Combine(_root, "data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static UpdateRecord Record(string state) =>
        new("AA", "FP", null, state, "", DateTimeOffset.UnixEpoch, null, null);

    [Fact]
    public void TheStateFiles_LiveInTheirOwnFolder()
    {
        Assert.Equal(Path.Combine(_root, "data", "update"), _paths.StateDirectory);
        foreach (var path in new[] { _paths.StatePath, _paths.ConfirmationPath, _paths.LockPath })
        {
            Assert.Equal(_paths.StateDirectory, Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void Save_WhileTheRecordIsBeingRead_StillReplacesIt()
    {
        UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Provisional));

        // How Load and a status question open it.
        using (new FileStream(_paths.StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Committed));
        }

        Assert.Equal(UpdateStates.Committed, UpdateRecordFile.Load(_paths.StatePath)!.State);
    }

    [Fact]
    public void Save_ThatCantWrite_Throws()
    {
        Directory.CreateDirectory(_paths.StatePath + ".tmp");

        Assert.ThrowsAny<Exception>(() => UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Committed)));
    }

    [Fact]
    public void Load_ReadsWhileTheRecordIsHeldOpen()
    {
        UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Applying));

        using (new FileStream(_paths.StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.Equal(UpdateStates.Applying, UpdateRecordFile.Load(_paths.StatePath)!.State);
        }
    }

    [Fact]
    public void EnsureStateDirectory_ShutsOutEveryoneButSystemAndAdministrators()
    {
        // On Windows, only an administrator can still open the folder (and
        // delete it) afterwards.
        if (OperatingSystem.IsWindows() && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return;
        }

        _paths.EnsureStateDirectory();
        _paths.EnsureStateDirectory(); // again on every start

        Assert.True(Directory.Exists(_paths.StateDirectory));
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var security = new DirectoryInfo(_paths.StateDirectory).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var allowed = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToList();
        Assert.NotEmpty(allowed);
        Assert.All(allowed, sid => Assert.True(
            sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid),
            $"{sid} can open the update folder"));
    }
}
