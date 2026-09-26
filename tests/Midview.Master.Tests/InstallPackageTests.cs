using System.Text.Json;
using Midview.Master;
using Xunit;

public class InstallPackageTests : IDisposable
{
    private const string ThisMaster = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherMaster = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string SessionThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string AgentThumbprint = "FEDCBA9876543210FEDCBA9876543210FEDCBA98";

    private readonly string _package = Path.Combine(Path.GetTempPath(), $"midview-package-{Guid.NewGuid():N}");

    public InstallPackageTests()
    {
        Directory.CreateDirectory(_package);
        File.WriteAllText(Path.Combine(_package, InstallPackage.StudentInstallerFileName), "@echo off");
    }

    public void Dispose() => Directory.Delete(_package, recursive: true);

    private string TrustedMastersPath => Path.Combine(_package, InstallPackage.TrustedMastersFileName);

    [Fact]
    public void IsPackageDirectory_LooksForTheStudentInstaller()
    {
        Assert.True(InstallPackage.IsPackageDirectory(_package));
        Assert.False(InstallPackage.IsPackageDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void AuthorizeMaster_WritesTheListTheInstallerCopies()
    {
        InstallPackage.AuthorizeMaster(_package, ThisMaster.ToLowerInvariant());

        Assert.Equal(new[] { ThisMaster }, JsonSerializer.Deserialize<string[]>(File.ReadAllText(TrustedMastersPath)));
    }

    [Fact]
    public void AuthorizeMaster_KeepsOtherTeachers_AndNeverDuplicates()
    {
        File.WriteAllText(TrustedMastersPath, $"[\"{OtherMaster}\"]");

        InstallPackage.AuthorizeMaster(_package, ThisMaster);
        var authorized = InstallPackage.AuthorizeMaster(_package, ThisMaster);

        Assert.Equal(new[] { OtherMaster, ThisMaster }, authorized);
        Assert.Equal(authorized, JsonSerializer.Deserialize<string[]>(File.ReadAllText(TrustedMastersPath)));
    }

    [Fact]
    public void AuthorizeMaster_UnreadableFile_IsReportedAndLeftAlone()
    {
        File.WriteAllText(TrustedMastersPath, "{ não é json");

        Assert.Throws<InvalidDataException>(() => InstallPackage.AuthorizeMaster(_package, ThisMaster));
        Assert.Equal("{ não é json", File.ReadAllText(TrustedMastersPath));
    }

    [Fact]
    public void LoadInstalledMachines_NoMachinesYet_IsEmptyWithoutProblems()
    {
        var (machines, problems) = InstallPackage.LoadInstalledMachines(_package, useComputerName: false);

        Assert.Empty(machines);
        Assert.Empty(problems);
    }

    [Fact]
    public void LoadInstalledMachines_UsesTheFirstAddress_OrTheComputerName()
    {
        WriteRecord("ALUNO-01", $$"""
            { "computerName": "ALUNO-01", "addresses": ["192.168.0.21", "192.168.56.1"],
              "sessionThumbprint": "{{SessionThumbprint.ToLowerInvariant()}}", "agentThumbprint": "{{AgentThumbprint}}",
              "installedAt": "2026-09-24T10:00:00-03:00" }
            """);

        var byAddress = InstallPackage.LoadInstalledMachines(_package, useComputerName: false).Machines;
        var byName = InstallPackage.LoadInstalledMachines(_package, useComputerName: true).Machines;

        Assert.Equal(new[] { new KnownMachine("ALUNO-01", "192.168.0.21", SessionThumbprint, AgentThumbprint) }, byAddress);
        Assert.Equal("ALUNO-01", Assert.Single(byName).HostAddress);
    }

    [Fact]
    public void LoadInstalledMachines_AcceptsASingleAddressWrittenAsAString()
    {
        WriteRecord("ALUNO-01", Record("ALUNO-01", addresses: "\"10.0.0.5\""));

        Assert.Equal("10.0.0.5", Assert.Single(InstallPackage.LoadInstalledMachines(_package, false).Machines).HostAddress);
    }

    [Fact]
    public void LoadInstalledMachines_ComesInClassroomOrder()
    {
        foreach (var name in new[] { "Aluno-10", "Aluno-2", "Aluno-1", "Professor" })
        {
            WriteRecord(name, Record(name));
        }

        var machines = InstallPackage.LoadInstalledMachines(_package, false).Machines;

        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-10", "Professor" }, machines.Select(machine => machine.Name));
    }

    [Fact]
    public void LoadInstalledMachines_ReportsEachBadFile_AndImportsTheRest()
    {
        WriteRecord("ALUNO-01", Record("ALUNO-01"));
        WriteRecord("ALUNO-02", Record("ALUNO-02", sessionThumbprint: "123"));
        WriteRecord("ALUNO-03", Record("ALUNO-03", addresses: "[]"));
        WriteRecord("ALUNO-04", "isto não é json");
        WriteRecord("ALUNO-05", Record("ALUNO-05", agentThumbprint: "\"xyz\""));

        var (machines, problems) = InstallPackage.LoadInstalledMachines(_package, false);

        Assert.Equal("ALUNO-01", Assert.Single(machines).Name);
        Assert.Collection(problems,
            problem => Assert.StartsWith("ALUNO-02.json: thumbprint da Session", problem),
            problem => Assert.StartsWith("ALUNO-03.json: sem endereço IP", problem),
            problem => Assert.StartsWith("ALUNO-04.json: não pôde ser lido", problem),
            problem => Assert.StartsWith("ALUNO-05.json: thumbprint do Agent", problem));
    }

    [Fact]
    public void LoadInstalledMachines_WithoutAnAgentThumbprint_StillImportsForViewing()
    {
        WriteRecord("ALUNO-01", Record("ALUNO-01", agentThumbprint: "null"));

        Assert.Null(Assert.Single(InstallPackage.LoadInstalledMachines(_package, false).Machines).AgentThumbprint);
    }

    [Fact]
    public void LoadInstalledMachines_KeepsTheNetworkCard()
    {
        WriteRecord("ALUNO-01", Record("ALUNO-01", macAddress: "\"00:1a:2b:3c:4d:5e\""));

        Assert.Equal("00-1A-2B-3C-4D-5E", Assert.Single(InstallPackage.LoadInstalledMachines(_package, false).Machines).MacAddress);
    }

    [Theory]
    [InlineData(null)] // a machine file from before it was recorded
    [InlineData("null")]
    [InlineData("\"00-00-00-00-00-00\"")]
    public void LoadInstalledMachines_WithoutAUsableNetworkCard_StillImports(string? macAddress)
    {
        WriteRecord("ALUNO-01", Record("ALUNO-01", macAddress: macAddress));

        Assert.Null(Assert.Single(InstallPackage.LoadInstalledMachines(_package, false).Machines).MacAddress);
    }

    private static string Record(
        string name, string addresses = "[\"192.168.0.21\"]", string sessionThumbprint = SessionThumbprint,
        string agentThumbprint = $"\"{AgentThumbprint}\"", string? macAddress = null)
    {
        var macAddressField = macAddress is null ? string.Empty : $", \"macAddress\": {macAddress}";
        return $$"""
            { "computerName": "{{name}}", "addresses": {{addresses}},
              "sessionThumbprint": "{{sessionThumbprint}}", "agentThumbprint": {{agentThumbprint}}{{macAddressField}} }
            """;
    }

    private void WriteRecord(string name, string json)
    {
        var directory = Path.Combine(_package, InstallPackage.MachinesDirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.json"), json);
    }
}
