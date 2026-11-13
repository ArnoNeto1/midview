using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneUpdateTextTests
{
    // GitHub's digest is lower case; the phone reports its APK's hash in upper case.
    private const string ApkSha = "40bcdb81aa00000000000000000000000000000000000000000000000000ffee";

    private static UpdateStatusReport Status(string? fingerprint, string? build = "0.0.52", bool ready = true) =>
        new(fingerprint, build, ready, null);

    [Fact]
    public void Same_apk_is_up_to_date_whatever_the_case() =>
        Assert.Equal(PhoneUpdateStep.UpToDate, PhoneUpdateText.Plan(Status(ApkSha.ToUpperInvariant(), "0.0.53"), ApkSha));

    [Fact]
    public void Another_apk_is_pushed() =>
        Assert.Equal(PhoneUpdateStep.Push, PhoneUpdateText.Plan(Status(new string('A', 64)), ApkSha));

    // The phone is still hashing its APK: it decides when offered (same hash: "já está nesta versão").
    [Fact]
    public void A_phone_not_ready_is_pushed() =>
        Assert.Equal(PhoneUpdateStep.Push, PhoneUpdateText.Plan(Status(null, null, ready: false), ApkSha));

    [Fact]
    public void No_report_is_an_older_agent() =>
        Assert.Equal(PhoneUpdateStep.OldAgent, PhoneUpdateText.Plan(null, ApkSha));

    [Fact]
    public void Lines()
    {
        Assert.Equal("já está na versão 0.0.53", PhoneUpdateText.UpToDate("0.0.53"));
        Assert.Equal("atualizando…", PhoneUpdateText.Updating);
        Assert.Equal("atualizado para 0.0.53", PhoneUpdateText.Updated("0.0.53"));
        Assert.Equal("não atualizou: assinado com outra chave", PhoneUpdateText.NotUpdated("assinado com outra chave"));
        Assert.Equal("enviando o app… 40%", PhoneUpdateText.Sending(40));
    }

    [Fact]
    public void Installed_version_before_updating()
    {
        Assert.Equal("versão 0.0.52", PhoneUpdateText.Installed(new StatusResult(Status("AB", "0.0.52"), false)));
        Assert.Equal("calculando a versão instalada…", PhoneUpdateText.Installed(new StatusResult(Status(null, null, ready: false), false)));
        Assert.Equal(PhoneUpdateText.OldAgent, PhoneUpdateText.Installed(new StatusResult(null, true)));
    }

    [Fact]
    public void Older_agent_line() =>
        Assert.Equal("o Midview deste celular é de uma versão anterior e não se atualiza pela rede: instale-o de novo pelo cabo",
                     PhoneUpdateText.OldAgent);

    // An agent from before updating answers the offer with this CommandAck.
    [Theory]
    [InlineData(PushOutcome.OldAgent, "", true)]
    [InlineData(PushOutcome.Refused, "Ainda não funciona em celular.", true)]
    [InlineData(PushOutcome.Refused, "este pacote não é o aplicativo do celular", false)]
    [InlineData(PushOutcome.AlreadyInstalled, "já está nesta versão", false)]
    public void Older_agent_refusing_the_offer(PushOutcome outcome, string detail, bool old) =>
        Assert.Equal(old, PhoneUpdateText.IsOldAgent(new PushResult(outcome, detail)));

    [Theory]
    [InlineData(true, "App do celular, versão 0.0.53: já está guardado neste PC.")]
    [InlineData(false, "App do celular, versão 0.0.53: ainda não está neste PC. Ao atualizar, ele é baixado do GitHub.")]
    public void Apk_line(bool kept, string expected) => Assert.Equal(expected, PhoneUpdateText.ApkLine("0.0.53", kept));

    [Fact]
    public void Downloading_line() =>
        Assert.Equal("Baixando o app do celular do GitHub… 45%", PhoneUpdateText.Downloading(45));

    [Theory]
    [InlineData(1, "Atualizar o Midview do celular...")]
    [InlineData(2, "Atualizar o Midview dos celulares...")]
    public void Menu_item(int phones, string expected) => Assert.Equal(expected, PhoneUpdateText.MenuItem(phones));

    [Theory]
    [InlineData(1, "Atualizar o celular")]
    [InlineData(2, "Atualizar os celulares")]
    public void Heading(int phones, string expected) => Assert.Equal(expected, PhoneUpdateText.Heading(phones));

    [Theory]
    [InlineData(1, 1, "Terminado: o celular está na versão nova.")]
    [InlineData(0, 1, "Terminado: o celular não ficou na versão nova (veja abaixo).")]
    [InlineData(3, 3, "Terminado: os 3 celulares estão na versão nova.")]
    [InlineData(1, 3, "Terminado: 1 de 3 celulares na versão nova (veja abaixo).")]
    [InlineData(2, 3, "Terminado: 2 de 3 celulares na versão nova (veja abaixo).")]
    public void Summary(int current, int total, string expected) => Assert.Equal(expected, PhoneUpdateText.Summary(current, total));

    private static KnownMachine Android(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    // A window gets a tablet's words only when every device is a tablet.
    [Fact]
    public void Kind_of_the_devices()
    {
        Assert.Equal(AndroidDeviceKind.Tablet, PhoneUpdateText.KindOf(new[] { Android("Tablet 01"), Android("Tablet 02") }));
        Assert.Equal(AndroidDeviceKind.Phone, PhoneUpdateText.KindOf(new[] { Android("Tablet 01"), Android("Celular 01") }));
        Assert.Equal(AndroidDeviceKind.Phone, PhoneUpdateText.KindOf(new[] { Android("Celular 01") }));
        Assert.Equal(AndroidDeviceKind.Phone, PhoneUpdateText.KindOf(Array.Empty<KnownMachine>()));
    }

    // The window's lines, word for word as before for the phones.
    [Fact]
    public void Phone_window_lines()
    {
        Assert.Equal(PhoneUpdateText.OldAgent, PhoneUpdateText.OldAgentOf(AndroidDeviceKind.Phone));
        Assert.Equal(
            "Manda pela rede o Midview desta versão para os celulares: um celular primeiro e, quando ele confirmar que a versão nova funciona, os outros. "
            + "O aluno não precisa tocar em nada. O Midview do celular some por alguns segundos e volta sozinho, e os bloqueios continuam valendo. "
            + "O Android mostra ao aluno o aviso \"Atualizado pelo seu administrador\".",
            PhoneUpdateText.Intro());
        Assert.Equal("falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"", PhoneUpdateText.NoIdentity());
        Assert.Equal("Conferindo o app do celular...", PhoneUpdateText.CheckingApk());
        Assert.Equal("O app do celular não pôde ser obtido: sem rede.", PhoneUpdateText.ApkFailed("sem rede"));
        Assert.Equal("Atualizando o primeiro celular...", PhoneUpdateText.UpdatingFirst());
        Assert.Equal("Atualizando os outros celulares...", PhoneUpdateText.UpdatingOthers());
        Assert.Equal("não começou: o primeiro celular não confirmou a versão nova", PhoneUpdateText.NotStarted());
        Assert.Equal("O primeiro celular não confirmou a versão nova; os outros não foram atualizados.", PhoneUpdateText.FirstFailed());
        Assert.Equal("Nenhum celular na lista da sala para atualizar.", PhoneUpdateText.NoneToUpdate());
        Assert.Equal("Nenhum celular na sala ainda.", PhoneUpdateText.NoneInRoom(AndroidDeviceKind.Phone));
    }

    [Fact]
    public void Tablet_window_lines()
    {
        const AndroidDeviceKind tablet = AndroidDeviceKind.Tablet;
        Assert.Equal("o Midview deste tablet é de uma versão anterior e não se atualiza pela rede: instale-o de novo pelo cabo",
                     PhoneUpdateText.OldAgentOf(tablet));
        Assert.Equal(PhoneUpdateText.OldAgentOf(tablet), PhoneUpdateText.Installed(new StatusResult(null, true), tablet));
        Assert.Equal(
            "Manda pela rede o Midview desta versão para os tablets: um tablet primeiro e, quando ele confirmar que a versão nova funciona, os outros. "
            + "O aluno não precisa tocar em nada. O Midview do tablet some por alguns segundos e volta sozinho, e os bloqueios continuam valendo. "
            + "O Android mostra ao aluno o aviso \"Atualizado pelo seu administrador\".",
            PhoneUpdateText.Intro(tablet));
        Assert.Equal("falta a identidade do tablet: pareie-o de novo em \"Máquinas da sala\"", PhoneUpdateText.NoIdentity(tablet));
        Assert.Equal("Conferindo o app do tablet...", PhoneUpdateText.CheckingApk(tablet));
        Assert.Equal("O app do tablet não pôde ser obtido: sem rede.", PhoneUpdateText.ApkFailed("sem rede", tablet));
        Assert.Equal("Atualizando o primeiro tablet...", PhoneUpdateText.UpdatingFirst(tablet));
        Assert.Equal("Atualizando os outros tablets...", PhoneUpdateText.UpdatingOthers(tablet));
        Assert.Equal("não começou: o primeiro tablet não confirmou a versão nova", PhoneUpdateText.NotStarted(tablet));
        Assert.Equal("O primeiro tablet não confirmou a versão nova; os outros não foram atualizados.", PhoneUpdateText.FirstFailed(tablet));
        Assert.Equal("Nenhum tablet na lista da sala para atualizar.", PhoneUpdateText.NoneToUpdate(tablet));
        Assert.Equal("Nenhum tablet na sala ainda: prepare um em \"Configurar tablet\".", PhoneUpdateText.NoneInRoom(tablet));
        Assert.Equal("Baixando o app do tablet do GitHub… 45%", PhoneUpdateText.Downloading(45, tablet));
    }

    [Theory]
    [InlineData(true, "App do tablet, versão 0.0.53: já está guardado neste PC.")]
    [InlineData(false, "App do tablet, versão 0.0.53: ainda não está neste PC. Ao atualizar, ele é baixado do GitHub.")]
    public void Tablet_apk_line(bool kept, string expected) =>
        Assert.Equal(expected, PhoneUpdateText.ApkLine("0.0.53", kept, AndroidDeviceKind.Tablet));

    [Theory]
    [InlineData(1, "Atualizar o tablet", "Atualizar o Midview do tablet...")]
    [InlineData(2, "Atualizar os tablets", "Atualizar o Midview dos tablets...")]
    public void Tablet_heading_and_menu_item(int tablets, string heading, string menuItem)
    {
        Assert.Equal(heading, PhoneUpdateText.Heading(tablets, AndroidDeviceKind.Tablet));
        Assert.Equal(menuItem, PhoneUpdateText.MenuItem(tablets, AndroidDeviceKind.Tablet));
    }

    [Theory]
    [InlineData(1, 1, "Terminado: o tablet está na versão nova.")]
    [InlineData(0, 1, "Terminado: o tablet não ficou na versão nova (veja abaixo).")]
    [InlineData(3, 3, "Terminado: os 3 tablets estão na versão nova.")]
    [InlineData(2, 3, "Terminado: 2 de 3 tablets na versão nova (veja abaixo).")]
    public void Tablet_summary(int current, int total, string expected) =>
        Assert.Equal(expected, PhoneUpdateText.Summary(current, total, AndroidDeviceKind.Tablet));
}
