using Midview.Master;
using Xunit;

public class KnownMachinesFileTests
{
    [Fact]
    public void Load_NoFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");

        var result = KnownMachinesFile.Load(path);

        Assert.Empty(result);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");
        var machines = new[]
        {
            new KnownMachine("Aluno-25", "192.168.0.125", "AAAA1111", "BBBB2222"),
            new KnownMachine("Aluno-26", "192.168.0.126", "CCCC3333", null),
        };
        try
        {
            KnownMachinesFile.Save(path, machines);
            var result = KnownMachinesFile.Load(path);

            Assert.Equal(machines, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_MovesItAsideAndThrows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json");

            Assert.Throws<InvalidDataException>(() => KnownMachinesFile.Load(path));
            Assert.False(File.Exists(path));
            Assert.Equal("{ not valid json", File.ReadAllText(path + ".corrupt"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".corrupt");
        }
    }

    [Fact]
    public void Save_MissingDirectory_CreatesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}");
        var path = Path.Combine(directory, "known-machines.json");
        try
        {
            KnownMachinesFile.Save(path, new[] { new KnownMachine("Aluno-25", "192.168.0.125", "AAAA1111", null) });

            Assert.Single(KnownMachinesFile.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
