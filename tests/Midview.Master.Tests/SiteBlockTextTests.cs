using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// The sites window's words follow what it acts on: computers ("máquina"),
// phones ("celular") or both ("aparelho").
public class SiteBlockTextTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", "AGENT");

    private static KnownMachine Phone(string name) => new(name, "192.0.2.9", "ANY", "AGENT", Kind: DeviceKind.Android);

    private static readonly KnownMachine[] Pcs = { Pc("PC 01"), Pc("PC 02"), Pc("PC 03") };
    private static readonly KnownMachine[] Phones = { Phone("Cel 01"), Phone("Cel 02"), Phone("Cel 03") };
    private static readonly KnownMachine[] Both = { Pc("PC 01"), Phone("Cel 01"), Phone("Cel 02") };

    [Fact]
    public void Targets_NamePhonesAsPhones_AndBothAsDevices()
    {
        Assert.Equal("Em 3 máquinas: PC 01, PC 02, PC 03", SiteBlockText.Targets(Pcs, wholeClass: false));
        Assert.Equal("Em 3 celulares: Cel 01, Cel 02, Cel 03", SiteBlockText.Targets(Phones, wholeClass: false));
        Assert.Equal("Em 3 aparelhos: PC 01, Cel 01, Cel 02", SiteBlockText.Targets(Both, wholeClass: false));
        Assert.Equal("Na turma toda (3 celulares)", SiteBlockText.Targets(Phones, wholeClass: true));
        Assert.Equal("Na turma toda (3 aparelhos)", SiteBlockText.Targets(Both, wholeClass: true));
        Assert.Equal("Em: Cel 01", SiteBlockText.Targets(new[] { Phones[0] }, wholeClass: false));
    }

    [Fact]
    public void ItHolds_AfterARestartOfWhatItActsOn()
    {
        Assert.Equal(
            "O bloqueio continua valendo se a máquina reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.",
            SiteBlockText.Persistence(Pcs));
        Assert.Equal(
            "O bloqueio continua valendo se o celular reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.",
            SiteBlockText.Persistence(Phones));
        Assert.Equal(
            "O bloqueio continua valendo se o aparelho reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.",
            SiteBlockText.Persistence(Both));
    }

    // On a PC the hosts file blocks only the name and its "www." form; in a
    // phone's Chrome the entry for a site covers its other addresses too
    // (Chrome's URLBlocklist format), so the m.youtube.com caveat is the PCs'.
    [Fact]
    public void TheOtherAddressesCaveat_IsOnlyForComputers()
    {
        Assert.Equal(
            "Bloqueia o endereço com e sem www (youtube.com e www.youtube.com). Outros endereços do mesmo site, como m.youtube.com, e abas que já estavam abertas podem continuar funcionando.",
            SiteBlockText.SiteHint(Pcs));
        Assert.Equal(
            "Bloqueia o endereço com e sem www (youtube.com e www.youtube.com). Abas que já estavam abertas podem continuar funcionando.",
            SiteBlockText.SiteHint(Phones));
        Assert.Equal(
            "Bloqueia o endereço com e sem www (youtube.com e www.youtube.com). Abas que já estavam abertas podem continuar funcionando e, nos computadores, outros endereços do mesmo site, como m.youtube.com.",
            SiteBlockText.SiteHint(Both));
    }

    // A phone blocks the sites in Chrome: the YouTube app keeps opening, so
    // the window says where to block it.
    [Fact]
    public void WithPhones_ItSaysTheBlockIsInChrome_AndWhereToBlockTheApps()
    {
        Assert.Null(SiteBlockText.PhoneNote(Pcs));
        Assert.Equal(
            "No celular, o bloqueio de sites vale no Chrome; apps como o do YouTube continuam abrindo: bloqueie-os em \"Bloquear programas\".",
            SiteBlockText.PhoneNote(new[] { Phones[0] }));
        Assert.Equal(
            "Nos celulares, o bloqueio de sites vale no Chrome; apps como o do YouTube continuam abrindo: bloqueie-os em \"Bloquear programas\".",
            SiteBlockText.PhoneNote(Phones));
        Assert.Equal(
            "Nos celulares, o bloqueio de sites vale no Chrome; apps como o do YouTube continuam abrindo: bloqueie-os em \"Bloquear programas\".",
            SiteBlockText.PhoneNote(Both));
    }

    [Fact]
    public void Done_CountsMachinesPhonesOrDevices()
    {
        Assert.Equal("Feito.", SiteBlockText.Done(new[] { Phones[0] }, failures: 0));
        Assert.Equal("Feito nas 3 máquinas.", SiteBlockText.Done(Pcs, failures: 0));
        Assert.Equal("Feito nos 3 celulares.", SiteBlockText.Done(Phones, failures: 0));
        Assert.Equal("Feito nos 3 aparelhos.", SiteBlockText.Done(Both, failures: 0));
        Assert.Equal("Feito em 2 de 3.", SiteBlockText.Done(Phones, failures: 1));
    }

    [Fact]
    public void APhoneThatRefusesTheConnection_SaysThePhone()
    {
        var refused = new SocketException((int)SocketError.ConnectionRefused);

        Assert.Equal("O Midview não está rodando neste celular", SiteBlockText.Failure(Phones[0], refused));
        Assert.Equal("O Midview não está rodando nessa máquina", SiteBlockText.Failure(Pcs[0], refused));
        Assert.Equal("Offline", SiteBlockText.Failure(Phones[0], new OperationCanceledException()));
    }

    [Fact]
    public void AMachineWithoutTheAgentsCertificate_IsToldToPairAgain_InItsOwnWords()
    {
        Assert.Equal(
            "a lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\"",
            SiteBlockText.NoThumbprint(Pcs[0]));
        Assert.Equal(
            "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"",
            SiteBlockText.NoThumbprint(Phones[0]));
    }
}
