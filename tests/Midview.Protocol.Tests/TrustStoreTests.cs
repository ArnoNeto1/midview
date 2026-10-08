using Midview.Protocol;
using Xunit;

public class TrustStoreTests : IDisposable
{
    private static readonly string A = new('A', 40);
    private static readonly string B = new('B', 40);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trust-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void AFixedList_TrustsItsThumbprints_InAnyCase()
    {
        var store = new TrustStore(new[] { A.ToLowerInvariant() });

        Assert.True(store.IsTrusted(A));
        Assert.False(store.IsTrusted(B));
    }

    [Fact]
    public void FromFile_NoFile_TrustsNobody()
    {
        var store = TrustStore.FromFile(_path);

        Assert.Equal(0, store.Count);
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_SeesATeacherAddedLater()
    {
        var store = TrustStore.FromFile(_path);
        Assert.False(store.IsTrusted(A));

        TrustedMastersFile.Add(_path, A);

        Assert.True(store.IsTrusted(A));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void FromFile_ReadsTheFileAgainOnlyWhenItChanges()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));

        // Same size, same time: taken as unchanged, so not read again.
        var stamp = File.GetLastWriteTimeUtc(_path);
        File.WriteAllText(_path, $"[\"{B}\"]");
        File.SetLastWriteTimeUtc(_path, stamp);
        Assert.True(store.IsTrusted(A));
        Assert.False(store.IsTrusted(B));

        File.SetLastWriteTimeUtc(_path, stamp.AddSeconds(1));
        Assert.True(store.IsTrusted(B));
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_ABrokenFile_KeepsWhatWasTrusted()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));
        var stamp = File.GetLastWriteTimeUtc(_path);

        File.WriteAllText(_path, "[\"AAA");
        Assert.True(store.IsTrusted(A));

        // TrustStore tells a change by the file's write time and size, and
        // writes this close together can land in the same tick of the file
        // clock (forced here, so the test doesn't depend on how fast it runs):
        // the good list must differ in size from the first one to be seen.
        File.WriteAllText(_path, $"[ \"{B}\" ]");
        File.SetLastWriteTimeUtc(_path, stamp);
        Assert.True(store.IsTrusted(B));
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_TheFileGone_TrustsNobody()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));

        File.Delete(_path);

        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void ACertificate_IsTrustedByItsThumbprint()
    {
        using var certificate = SelfSignedCertificate.Create("teacher");
        File.WriteAllText(_path, $"[\"{certificate.Thumbprint.ToLowerInvariant()}\"]");

        Assert.True(TrustStore.FromFile(_path).IsTrusted(certificate));
    }
}
