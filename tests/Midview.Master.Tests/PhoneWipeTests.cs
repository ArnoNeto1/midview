using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneWipeTests
{
    private static KnownMachine Phone(string? thumbprint) =>
        new("Celular 07", "192.0.2.9", "ANY", thumbprint, Kind: DeviceKind.Android);

    [Fact]
    public void The_confirmation_names_the_phone_and_what_is_lost() =>
        Assert.Equal("O Celular 07 vai ser apagado e voltar de fábrica. Os alunos perdem o que estiver nele. Para prepará-lo de novo, vai precisar outra vez do chip e da conta Mi.",
                     PhoneWipe.ConfirmText("Celular 07"));

    [Fact]
    public void The_wipe_command_has_its_own_message_number() =>
        Assert.Equal(100, (int)MessageType.WipePhone);

    [Theory]
    [InlineData("Celular 07", "07")]
    [InlineData("Celular 12", "12")]
    [InlineData("Celular 105", "105")]
    [InlineData("celular 03", "03")]
    [InlineData("LAB-01", null)]
    [InlineData("Celular", null)]
    [InlineData("Celular 7a", null)]
    [InlineData("Celular 1234", null)]
    [InlineData("", null)]
    public void The_number_is_read_from_the_phone_name(string name, string? number) =>
        Assert.Equal(number, PhoneWipe.NumberOf(name));

    [Fact]
    public void A_name_made_from_a_number_gives_it_back() =>
        Assert.Equal("07", PhoneWipe.NumberOf(PhoneNumber.NameOf("07")));

    [Fact]
    public void Without_the_phones_identity_nothing_is_sent()
    {
        var sent = false;
        var result = PhoneWipe.SendWith(Phone(null), _ => { sent = true; return (true, ""); });

        Assert.Equal((false, "sem a identificação do celular"), result);
        Assert.False(sent);
    }

    [Fact]
    public void The_command_goes_to_the_phones_thumbprint_and_success_is_success()
    {
        string? target = null;
        var result = PhoneWipe.SendWith(Phone("AB12"), thumbprint => { target = thumbprint; return (true, ""); });

        Assert.Equal((true, ""), result);
        Assert.Equal("AB12", target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ainda não funciona em celular.")]
    public void An_old_agent_gets_a_plain_text_without_an_update_hint(string answer) =>
        Assert.Equal((false, PhoneWipe.OldAgent),
                     PhoneWipe.SendWith(Phone("AB12"), _ => (false, answer)));

    [Fact]
    public void A_reason_from_the_phone_is_passed_on() =>
        Assert.Equal((false, "o celular está sem bateria"),
                     PhoneWipe.SendWith(Phone("AB12"), _ => (false, "o celular está sem bateria")));

    [Fact]
    public void An_exception_is_described_for_the_teacher()
    {
        var ex = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
        var result = PhoneWipe.SendWith(Phone("AB12"), _ => throw ex);

        Assert.False(result.Ok);
        Assert.Equal(FailureText.DescribeAgent(ex), result.Detail);
    }

    [Fact]
    public void An_online_phone_with_a_number_can_be_wiped_from_the_menu()
    {
        Assert.True(PhoneWipe.CanWipe(Phone("AB12"), online: true));
        Assert.False(PhoneWipe.CanWipe(Phone("AB12"), online: false));
        Assert.False(PhoneWipe.CanWipe(new KnownMachine("LAB-01", "192.0.2.1", "ANY", "AB12"), online: true));
        Assert.False(PhoneWipe.CanWipe(new KnownMachine("Pixel de Ana", "192.0.2.9", "ANY", "AB12", Kind: DeviceKind.Android), online: true));
    }
}
