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
        Assert.Equal(101, (int)MessageType.WipePhone);

    [Theory]
    [InlineData("Celular 07", "07")]
    [InlineData("Celular 12", "12")]
    [InlineData("Celular 105", "105")]
    [InlineData("celular 03", "03")]
    [InlineData("LAB-01", null)]
    [InlineData("Celular", null)]
    [InlineData("Celular 7a", null)]
    [InlineData("Celular 1234", null)]
    [InlineData("Tablet 07", "07")]
    [InlineData("Tablet 105", "105")]
    [InlineData("Tablet", null)]
    [InlineData("Tablet 549C", null)]
    // Not a tablet for PhoneNumber.KindOf either.
    [InlineData("tablet 03", null)]
    [InlineData("", null)]
    public void The_number_is_read_from_the_phone_name(string name, string? number) =>
        Assert.Equal(number, PhoneWipe.NumberOf(name));

    [Fact]
    public void A_name_made_from_a_number_gives_it_back()
    {
        Assert.Equal("07", PhoneWipe.NumberOf(PhoneNumber.NameOf("07")));
        Assert.Equal((AndroidDeviceKind.Tablet, "07"), PhoneWipe.TargetOf(PhoneNumber.NameOf("07", AndroidDeviceKind.Tablet)));
    }

    // What the assistant prepares again: a tablet as a tablet, a phone as a phone.
    [Fact]
    public void The_target_keeps_the_kind_of_device()
    {
        Assert.Equal((AndroidDeviceKind.Phone, "07"), PhoneWipe.TargetOf("Celular 07"));
        Assert.Equal((AndroidDeviceKind.Phone, "03"), PhoneWipe.TargetOf("celular 03"));
        Assert.Equal((AndroidDeviceKind.Tablet, "12"), PhoneWipe.TargetOf("Tablet 12"));
        Assert.Null(PhoneWipe.TargetOf("LAB-01"));
    }

    [Fact]
    public void A_tablets_texts_say_tablet()
    {
        var tablet = new KnownMachine("Tablet 07", "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);
        Assert.Equal((false, "sem a identificação do tablet"), PhoneWipe.SendWith(tablet, _ => (true, "")));

        Assert.Equal((false, "este tablet ainda não aceita o pedido de apagar"),
                     PhoneWipe.SendWith(tablet with { AgentThumbprint = "AB12" }, _ => (false, "")));
        Assert.Equal(PhoneWipe.OldAgent, PhoneWipe.OldAgentOf(AndroidDeviceKind.Phone));
        Assert.Equal("O Tablet 07 vai ser apagado e voltar de fábrica. Os alunos perdem o que estiver nele. Para prepará-lo de novo, vai precisar outra vez do chip e da conta Mi.",
                     PhoneWipe.ConfirmText("Tablet 07"));
    }

    [Fact]
    public void A_school_tablet_gets_the_menu_item_too()
    {
        var tablet = new KnownMachine("Tablet 07", "192.0.2.9", "ANY", "AB12", Kind: DeviceKind.Android);

        Assert.True(PhoneWipe.ShowsItem(tablet));
        Assert.True(PhoneWipe.CanWipe(tablet, online: true));
        Assert.False(PhoneWipe.CanWipe(tablet, online: false));
        // A PC named like a tablet is not one.
        Assert.False(PhoneWipe.ShowsItem(new KnownMachine("Tablet 07", "192.0.2.1", "ANY", "AB12")));
    }

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

    [Fact]
    public void The_menu_item_is_shown_only_for_a_school_phone()
    {
        Assert.True(PhoneWipe.ShowsItem(Phone("AB12")));
        // Paired earlier by code: no number, so no item to grey with no reason.
        Assert.False(PhoneWipe.ShowsItem(new KnownMachine("Celular 549C", "192.0.2.9", "ANY", "AB12", Kind: DeviceKind.Android)));
        Assert.False(PhoneWipe.ShowsItem(new KnownMachine("Celular 07", "192.0.2.1", "ANY", "AB12")));
    }
}
