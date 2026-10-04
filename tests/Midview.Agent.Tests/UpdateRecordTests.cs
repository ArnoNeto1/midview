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

    // Retried: on Windows, a file another thread was still reading can't be
    // deleted for a moment.
    public void Dispose()
    {
        for (var attempt = 1; Directory.Exists(_root); attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) when (attempt < 25)
            {
                Thread.Sleep(200);
            }
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
    public void Save_WhileTheRecordIsBeingRead_WaitsForTheReader_AndReplacesIt()
    {
        UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Provisional));

        // A status question reading it (how Load opens it), for longer than
        // one retry. Let go from a thread of its own: the test thread blocks
        // in Save's retries, and with the other tests running in parallel a
        // thread-pool continuation could be starved for longer than they last.
        var reader = new FileStream(_paths.StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = new Thread(() =>
        {
            Thread.Sleep(300);
            reader.Dispose();
        });
        release.Start();

        UpdateRecordFile.Save(_paths.StatePath, Record(UpdateStates.Committed));
        release.Join();

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

        using (new FileStream(_paths.StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
