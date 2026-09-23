using Midview.Master;
using Xunit;

public class KnownMachinesFileTests
{
    [Fact]
    public void Load_NoExistingFile_ReturnsEmptyList()
    {
        var machines = KnownMachinesFile.Load(TempPath(), out var corruptCopyPath);

        Assert.Empty(machines);
        Assert.Null(corruptCopyPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMachinesInOrder()
    {
        var path = TempPath();
        try
        {
            var saved = new[]
            {
                new KnownMachine("Aluno-2", "192.168.0.12", "SESSIONTHUMB2", "AGENTTHUMB2"),
                new KnownMachine("Aluno-1", "192.168.0.11", "SESSIONTHUMB1", null),
            };

            KnownMachinesFile.Save(path, saved);
            var loaded = KnownMachinesFile.Load(path, out _);

            Assert.Equal(saved, loaded);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyAndKeepsTheFileAside()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "[{\"Name\": \"Aluno-1\", \"Host");

            var machines = KnownMachinesFile.Load(path, out var corruptCopyPath);

            Assert.Empty(machines);
            Assert.Equal(path + ".corrupt", corruptCopyPath);
            Assert.Equal("[{\"Name\": \"Aluno-1\", \"Host", File.ReadAllText(corruptCopyPath!));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_DropsEntriesMissingARequiredField()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                [
                  { "Name": "Aluno-1", "HostAddress": "10.0.0.1", "SessionThumbprint": "AAA" },
                  { "Name": "Aluno-2", "SessionThumbprint": "BBB" },
                  null
                ]
                """);

            var machines = KnownMachinesFile.Load(path, out _);

            Assert.Equal(new[] { new KnownMachine("Aluno-1", "10.0.0.1", "AAA", null) }, machines);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // In its own directory, so Save also has to create that directory.
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"midview-roster-{Guid.NewGuid():N}", "known-machines.json");
}
