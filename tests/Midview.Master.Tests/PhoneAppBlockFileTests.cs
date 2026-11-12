using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneAppBlockFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-phone-appblock-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".tmp", _path + ".corrupt" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RoundTrips()
    {
        PhoneAppBlockFile.Save(_path, new PhoneAppPolicy(1234, AppBlockMode.AllowOnly, new[] { "com.android.chrome", "com.miui.calculator" }));

        var loaded = PhoneAppBlockFile.Load(_path)!;

        Assert.Equal(1234, loaded.SavedAt);
        Assert.Equal(AppBlockMode.AllowOnly, loaded.Mode);
        Assert.Equal(new[] { "com.android.chrome", "com.miui.calculator" }, loaded.Packages);
    }

    [Fact]
    public void ALiftedPolicy_IsKept_SoPhonesThatWereOffGetTheLiftToo()
    {
        PhoneAppBlockFile.Save(_path, PhoneAppPolicy.Lifted(99));

        var loaded = PhoneAppBlockFile.Load(_path);

        Assert.NotNull(loaded);
        Assert.False(loaded!.IsActive);
        Assert.Equal(99, loaded.SavedAt);
    }

    [Fact]
    public void Missing_IsNull() => Assert.Null(PhoneAppBlockFile.Load(_path));

    [Fact]
    public void Corrupt_IsMovedAside()
    {
        File.WriteAllText(_path, "{ nope");

        Assert.Null(PhoneAppBlockFile.Load(_path));
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    // A file edited by hand with something that is not a package name is
    // not a policy to send to the phones.
    [Fact]
    public void AnInvalidPackage_IsNoPolicy()
    {
        File.WriteAllText(_path, "{\"SavedAt\":5,\"Mode\":1,\"Packages\":[\"youtube\"]}");

        Assert.Null(PhoneAppBlockFile.Load(_path));
    }

    // The phones' block is not the PCs' one: each has its own file (both in
    // MasterData.Folder, see MasterDataTests).
    [Fact]
    public void HasItsOwnFile_NotThePcBlocks()
    {
        Assert.Equal("phone-app-block.json", Path.GetFileName(PhoneAppBlockFile.DefaultPath));
        Assert.NotEqual(Path.GetFileName(AppBlockFile.DefaultPath), Path.GetFileName(PhoneAppBlockFile.DefaultPath));
    }
}
