using Midview.Master;
using Midview.Protocol;
using Xunit;

public class UpdateTextTests
{
    private const string PackageFingerprint = "AB12";
    private const string PackageBuild = "2026-09-26 19:40 a1b2c3d";

    private static StatusResult Installed(string? fingerprint, string? build, bool ready = true, string? attemptState = null) =>
        new(new UpdateStatusReport(fingerprint, build, ready,
            attemptState is null ? null : new UpdateAttemptReport("00", attemptState, string.Empty, DateTimeOffset.UnixEpoch)), false);

    [Theory]
    [InlineData("2026-09-26 19:40 a1b2c3d", "26/09 19:40 (a1b2c3d)")]
    [InlineData("2026-09-26 19:40", "26/09 19:40")]
    [InlineData("feito à mão", "feito à mão")]
    [InlineData(null, "desconhecida")]
    public void ShortBuild_ShowsDayTimeAndCommit(string? build, string expected) =>
        Assert.Equal(expected, UpdateText.ShortBuild(build));

    [Theory]
    [InlineData("2026-09-27 08:00 ffffff0", true)]
    [InlineData("2026-09-26 19:40 0000000", false)]
    [InlineData("2026-09-25 23:59 a1b2c3d", false)]
    [InlineData(null, false)]
    [InlineData("curto", false)]
    public void IsNewer_ComparesTheBuildTimes(string? installed, bool expected) =>
        Assert.Equal(expected, UpdateText.IsNewer(installed, PackageBuild));

    [Fact]
    public void DescribeStatus_OldAgent_AsksForTheUsbStick() =>
        Assert.Contains("pendrive", UpdateText.DescribeStatus(new StatusResult(null, true), PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_SameFingerprint_IsAlreadyThere() =>
        Assert.Equal("✓ já nesta versão (26/09 19:40 (a1b2c3d))",
            UpdateText.DescribeStatus(Installed("ab12", PackageBuild), PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_OlderBuild_ShowsBoth() =>
        Assert.Equal("• instalada: 20/09 10:00 (0000001) → pacote: 26/09 19:40 (a1b2c3d)",
            UpdateText.DescribeStatus(Installed("CD34", "2026-09-20 10:00 0000001"), PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_NewerBuild_Warns() =>
        Assert.StartsWith("⚠ instalada: 27/09 08:00 (fffffff), mais nova que o pacote",
            UpdateText.DescribeStatus(Installed("CD34", "2026-09-27 08:00 fffffff"), PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_NotReady_AsksToCheckAgain() =>
        Assert.StartsWith("• calculando",
            UpdateText.DescribeStatus(Installed(null, null, ready: false), PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_AnUnfinishedAttempt_IsMentioned() =>
        Assert.EndsWith("há uma atualização em andamento nela",
            UpdateText.DescribeStatus(Installed("CD34", "2026-09-20 10:00 0000001", attemptState: UpdateStates.Provisional),
                PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeStatus_AFinishedAttempt_IsNot() =>
        Assert.DoesNotContain("andamento",
            UpdateText.DescribeStatus(Installed("CD34", "2026-09-20 10:00 0000001", attemptState: UpdateStates.RolledBack),
                PackageFingerprint, PackageBuild));

    [Fact]
    public void DescribeProgress_NamesEachStep()
    {
        Assert.Equal("recebendo...", UpdateText.DescribeProgress(UpdateStates.Receiving));
        Assert.Equal("conferindo o pacote...", UpdateText.DescribeProgress(UpdateStates.Verifying));
        Assert.Equal("trocando de versão...", UpdateText.DescribeProgress(UpdateStates.Applying));
        Assert.Equal("reiniciando na versão nova...", UpdateText.DescribeProgress(UpdateStates.Provisional));
    }
}
