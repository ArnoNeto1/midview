using System.Text;
using Midview.Master;
using Xunit;

public class AdminPasswordStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-pw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Senha@123")]
    [InlineData("açaí-ção-ñ-€-日本")]
    public void Save_then_Load_round_trips(string password)
    {
        var store = new AdminPasswordStore(_folder);

        store.Save(Encoding.UTF8.GetBytes(password));

        Assert.True(store.HasPassword);
        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(password, Encoding.UTF8.GetString(loaded!));
    }

    [Fact]
    public void The_stored_bytes_are_not_the_plaintext()
    {
        var store = new AdminPasswordStore(_folder);
        var plain = Encoding.UTF8.GetBytes("Senha@123-segredo");

        store.Save(plain);

        var onDisk = File.ReadAllBytes(Path.Combine(_folder, "admin-password.bin"));
        Assert.NotEqual(plain, onDisk);
        Assert.DoesNotContain("segredo", Encoding.UTF8.GetString(onDisk));
    }

    [Fact]
    public void Load_returns_null_when_absent()
    {
        var store = new AdminPasswordStore(_folder);

        Assert.False(store.HasPassword);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_returns_null_on_a_corrupt_blob_without_throwing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "admin-password.bin"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        var store = new AdminPasswordStore(_folder);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_removes_the_password()
    {
        var store = new AdminPasswordStore(_folder);
        store.Save(Encoding.UTF8.GetBytes("x"));

        store.Clear();

        Assert.False(store.HasPassword);
        Assert.Null(store.Load());
    }
}
