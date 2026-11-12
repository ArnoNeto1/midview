using Midview.Master;
using Midview.Protocol;
using Xunit;

public class LabConfigPhonesTests
{
    private static readonly KnownMachine Pc1 = new("PC 01", "192.0.2.1", "ANY", "AGENT");
    private static readonly KnownMachine Pc2 = new("PC 02", "192.0.2.2", "ANY", "AGENT");
    private static readonly KnownMachine Cel1 = new("Cel 01", "192.0.2.9", "ANY", "AGENT", Kind: DeviceKind.Android);
    private static readonly IReadOnlyList<KnownMachine> Room = new[] { Pc1, Cel1, Pc2 };

    // The functions a phone takes: the ad blocker (its DNS), the download
    // block, the distraction sites, the reset and the reading.
    [Theory]
    [InlineData("adblock", true)]
    [InlineData("downloads", true)]
    [InlineData(LabConfigPhones.Distraction, true)]
    [InlineData(LabConfigPhones.Reset, true)]
    [InlineData(LabConfigPhones.ShowApplied, true)]
    [InlineData("clean", false)]
    [InlineData("lockdown", false)]
    [InlineData("edge", false)]
    [InlineData("guest", false)]
    [InlineData("taskbar", false)]
    [InlineData("desktop", false)]
    [InlineData(LabConfigPhones.ClearProfiles, false)]
    [InlineData(LabConfigPhones.ClearDownloads, false)]
    public void A_function_reaches_the_phones_only_when_it_means_something_on_them(string function, bool phones)
    {
        Assert.Equal(phones, LabConfigPhones.ReachesPhones(function));
        var reach = LabConfigPhones.Reach(function, Room);
        Assert.Equal(phones ? new[] { "PC 01", "Cel 01", "PC 02" } : new[] { "PC 01", "PC 02" }, reach.Select(machine => machine.Name));
    }

    // A computers-only function is never sent to a phone.
    [Fact]
    public void A_computers_only_function_on_phones_alone_reaches_nobody() =>
        Assert.Empty(LabConfigPhones.Reach("taskbar", new[] { Cel1 }));

    // Every lab-config flag the window toggles is either sent to phones or
    // said to be for computers only: none is left without a word.
    [Theory]
    [InlineData("adblock")]
    [InlineData("clean")]
    [InlineData("lockdown")]
    [InlineData("edge")]
    [InlineData("guest")]
    [InlineData("taskbar")]
    [InlineData("desktop")]
    [InlineData("downloads")]
    [InlineData(LabConfigPhones.Distraction)]
    [InlineData(LabConfigPhones.ClearProfiles)]
    [InlineData(LabConfigPhones.ClearDownloads)]
    [InlineData(LabConfigPhones.Reset)]
    public void Every_row_says_what_it_does_on_a_phone(string function)
    {
        var note = LabConfigPhones.Note(function);
        Assert.NotNull(note);
        Assert.Equal(!LabConfigPhones.ReachesPhones(function), note!.StartsWith("Só nos computadores", StringComparison.Ordinal));
    }

    [Fact]
    public void The_notes_name_what_a_phone_already_has()
    {
        Assert.Contains("DNS sem anúncios", LabConfigPhones.Note("adblock"));
        Assert.Contains("\"Otimizar\" respeita", LabConfigPhones.Note("adblock"));
        Assert.Contains("Chrome 131", LabConfigPhones.Note("downloads"));
        Assert.Contains("\"Enviar arquivo\" continua chegando", LabConfigPhones.Note("downloads"));
        Assert.Contains("só o Chrome abre sites", LabConfigPhones.Note("edge"));
        Assert.Contains("não instala apps de fora", LabConfigPhones.Note("lockdown"));
        Assert.Null(LabConfigPhones.Note(LabConfigPhones.ShowApplied));
    }

    [Fact]
    public void The_phones_left_out_are_counted()
    {
        Assert.Null(LabConfigPhones.LeftOut(0));
        Assert.Equal("O celular ficou de fora: essa função é só dos computadores.", LabConfigPhones.LeftOut(1));
        Assert.Equal("Os 3 celulares ficaram de fora: essa função é só dos computadores.", LabConfigPhones.LeftOut(3));
    }

    [Fact]
    public void A_phone_is_described_by_its_two_functions()
    {
        Assert.Equal("nada aplicado", LabConfigPhones.Describe(LabConfig.Off(1)));
        Assert.Equal("bloqueador de anúncios (DNS)", LabConfigPhones.Describe(LabConfig.Off(1) with { Adblock = true }));
        Assert.Equal("travar downloads, bloqueador de anúncios (DNS)",
            LabConfigPhones.Describe(LabConfig.Off(1) with { Adblock = true, BlockDownloads = true }));
    }

    [Fact]
    public void An_older_phone_agent_is_told_to_update() =>
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não usa o Painel do laboratório: atualize-o",
            LabConfigPhones.OldAgent);

    // The phone's answer to the read-modify-write: a toggle of a phone's
    // function keeps the PC-only flags off, so none of them is ever sent.
    [Theory]
    [InlineData("adblock")]
    [InlineData("downloads")]
    public void A_phone_toggle_sends_no_computer_only_flag(string function)
    {
        var phoneHas = LabConfig.Off(10) with { Adblock = true };
        foreach (var on in new[] { true, false })
        {
            var sent = LabConfigChange.With(phoneHas, function, on);
            Assert.False(sent.CleanOnExit || sent.Lockdown || sent.ForceGuest || sent.BlockEdge || sent.LockTaskbar || sent.LockDesktop);
        }
    }

    // The bytes the Android agent writes for SavedAt 0x0102030405060708 with
    // the download block and the ad blocker on (LabConfigTest on the phone).
    [Fact]
    public void A_phones_status_decodes_as_a_lab_config()
    {
        Assert.True(LabConfig.TryDecode(new byte[] { 1, 1, 2, 3, 4, 5, 6, 7, 8, 20 }, out var config));
        Assert.Equal(new LabConfig(0x0102030405060708, false, false, true, false, true, false, false), config);
    }
}
