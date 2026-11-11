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
                new KnownMachine("Aluno-2", "192.168.0.12", "SESSIONTHUMB2", "AGENTTHUMB2", "00-1A-2B-3C-4D-5E"),
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

    [Fact]
    public void Load_NormalizesTheMacAddress_AndDropsABadOne()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                [
                  { "Name": "Aluno-1", "HostAddress": "10.0.0.1", "SessionThumbprint": "AAA", "MacAddress": "00:1a:2b:3c:4d:5e" },
                  { "Name": "Aluno-2", "HostAddress": "10.0.0.2", "SessionThumbprint": "BBB", "MacAddress": "não é um MAC" }
                ]
                """);

            var machines = KnownMachinesFile.Load(path, out _);

            Assert.Equal(new[] { "00-1A-2B-3C-4D-5E", null }, machines.Select(machine => machine.MacAddress));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // In its own directory, so Save also has to create that directory.
    [Fact]
    public void Old_roster_without_kind_loads_as_computers()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                [ { "Name": "Aluno-1", "HostAddress": "10.0.0.1", "SessionThumbprint": "AAA", "AgentThumbprint": "BBB", "MacAddress": "00-1A-2B-3C-4D-5E" } ]
                """);

            var machine = Assert.Single(KnownMachinesFile.Load(path, out _));

            Assert.Equal(Midview.Protocol.DeviceKind.Windows, machine.Kind);
            Assert.False(machine.IsPhone);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Phone_kind_survives_save_and_load()
    {
        var path = TempPath();
        try
        {
            var phone = new KnownMachine("Celular-1", "10.0.0.5", "SESS", "AGENT", null, Midview.Protocol.DeviceKind.Android);

            KnownMachinesFile.Save(path, new[] { phone });
            var loaded = Assert.Single(KnownMachinesFile.Load(path, out _));

            Assert.Equal(phone, loaded);
            Assert.True(loaded.IsPhone);
            Assert.DoesNotContain("IsPhone", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void IsSameEntryAs_ignores_nothing_but_mac()
    {
        var pc = new KnownMachine("A", "10.0.0.1", "S", "T");
        var phone = pc with { Kind = Midview.Protocol.DeviceKind.Android };

        Assert.False(pc.IsSameEntryAs(phone));
        Assert.True(pc.IsSameEntryAs(pc with { MacAddress = "00-1A-2B-3C-4D-5E" }));
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"midview-roster-{Guid.NewGuid():N}", "known-machines.json");
}
