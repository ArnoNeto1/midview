using Midview.Master;
using Midview.Protocol;
using Xunit;

public class DeviceSupportTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", null);

    private static KnownMachine Phone(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    [Fact]
    public void Phone_only_selection_disables_pc_actions() =>
        Assert.False(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, ToolbarAction.Wake));

    [Fact]
    public void A_computer_in_the_selection_keeps_the_action_enabled() =>
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01"), Pc("PC 01") }, ToolbarAction.Wake));

    [Fact]
    public void Nothing_selected_enables_nothing() =>
        Assert.False(DeviceSupport.IsEnabled(Array.Empty<KnownMachine>(), ToolbarAction.View));

    [Theory]
    [InlineData(ToolbarAction.View)]
    [InlineData(ToolbarAction.Control)]
    [InlineData(ToolbarAction.ScreenLock)]
    [InlineData(ToolbarAction.Power)]
    [InlineData(ToolbarAction.BlockSites)]
    [InlineData(ToolbarAction.Update)]
    public void Computers_support_every_action_but_the_phone_group(ToolbarAction action) =>
        Assert.True(DeviceSupport.Supports(DeviceKind.Windows, action));

    [Fact]
    public void Phone_supports_view_control_screen_lock_blocking_closing_messages_chat_files_power_schedule_programs_class_folder_wiping_and_its_own_actions_only()
    {
        foreach (var action in Enum.GetValues<ToolbarAction>())
        {
            var expected = action is ToolbarAction.View or ToolbarAction.Control or ToolbarAction.ScreenLock
                or ToolbarAction.BlockPrograms or ToolbarAction.BlockSites or ToolbarAction.CloseApps
                or ToolbarAction.Message or ToolbarAction.Chat or ToolbarAction.WipePhone
                or ToolbarAction.SendFile or ToolbarAction.CollectFiles or ToolbarAction.Power or ToolbarAction.Schedule
                or ToolbarAction.OpenWebsite or ToolbarAction.OpenProgram or ToolbarAction.Install or ToolbarAction.Uninstall
                or ToolbarAction.LabConfig or ToolbarAction.ClassFolder
                or ToolbarAction.Optimize or ToolbarAction.UpdatePhones or ToolbarAction.PhonePin;
            Assert.Equal(expected, DeviceSupport.Supports(DeviceKind.Android, action));
        }
    }

    // "Otimizar" and "Atualizar celulares" are made for phones: a PC has nothing to do with them.
    [Theory]
    [InlineData(ToolbarAction.Optimize)]
    [InlineData(ToolbarAction.UpdatePhones)]
    [InlineData(ToolbarAction.PhonePin)]
    public void Phone_actions_are_for_phones_only(ToolbarAction action)
    {
        Assert.False(DeviceSupport.Supports(DeviceKind.Windows, action));
        Assert.False(DeviceSupport.IsEnabled(new[] { Pc("PC 01") }, action));
        Assert.True(DeviceSupport.IsEnabled(new[] { Pc("PC 01"), Phone("Cel 01") }, action));
    }

    // "Celulares" and "Tablets" (Telas menu) are each for a room with a device
    // of their own kind: a tablet is named "Tablet 07", any other Android device is a phone.
    [Fact]
    public void A_room_has_phones_or_tablets_only_with_one_of_that_kind_in_it()
    {
        Assert.False(DeviceSupport.HasAndroidOf(Array.Empty<KnownMachine>(), AndroidDeviceKind.Phone));
        Assert.False(DeviceSupport.HasAndroidOf(new[] { Pc("PC 01"), Pc("PC 02") }, AndroidDeviceKind.Phone));
        Assert.True(DeviceSupport.HasAndroidOf(new[] { Pc("PC 01"), Phone("Cel 01") }, AndroidDeviceKind.Phone));
        Assert.False(DeviceSupport.HasAndroidOf(new[] { Pc("PC 01"), Phone("Cel 01") }, AndroidDeviceKind.Tablet));
        Assert.True(DeviceSupport.HasAndroidOf(new[] { Phone("Tablet 01") }, AndroidDeviceKind.Tablet));
        Assert.False(DeviceSupport.HasAndroidOf(new[] { Phone("Tablet 01") }, AndroidDeviceKind.Phone));
    }

    [Theory]
    [InlineData(DeviceFilter.Phones, AndroidDeviceKind.Phone)]
    [InlineData(DeviceFilter.Tablets, AndroidDeviceKind.Tablet)]
    [InlineData(DeviceFilter.All, null)]
    [InlineData(DeviceFilter.Computers, null)]
    public void Each_android_filter_names_its_kind(DeviceFilter shown, AndroidDeviceKind? kind) =>
        Assert.Equal(kind, DeviceSupport.AndroidKindOf(shown));

    // Phones and tablets run the same Android agent: optimizing is for every Android device.
    [Fact]
    public void Optimizing_is_for_every_android_device()
    {
        Assert.True(DeviceSupport.IsAndroid(Phone("Tablet 01")));
        Assert.False(DeviceSupport.IsAndroid(Pc("PC 01")));
        Assert.False(DeviceSupport.HasAndroid(new[] { Pc("PC 01") }));
        Assert.True(DeviceSupport.HasAndroid(new[] { Pc("PC 01"), Phone("Tablet 01") }));
    }

    // "Atualizar os celulares" and "Atualizar os tablets" each reach their own kind; never a PC.
    [Fact]
    public void Android_devices_of_a_kind()
    {
        Assert.True(DeviceSupport.IsAndroidOf(Phone("Tablet 01"), AndroidDeviceKind.Tablet));
        Assert.False(DeviceSupport.IsAndroidOf(Phone("Tablet 01"), AndroidDeviceKind.Phone));
        Assert.True(DeviceSupport.IsAndroidOf(Phone("Celular 01"), AndroidDeviceKind.Phone));
        Assert.False(DeviceSupport.IsAndroidOf(Phone("Celular 01"), AndroidDeviceKind.Tablet));
        Assert.False(DeviceSupport.IsAndroidOf(Pc("Tablet 01"), AndroidDeviceKind.Tablet));
        Assert.False(DeviceSupport.IsAndroidOf(Pc("PC 01"), AndroidDeviceKind.Phone));
    }

    // The sites window sends its commands to every machine it reaches; so do
    // "Mensagem", "Bate-papo", "Enviar arquivo" and "Recolher arquivos".
    [Theory]
    [InlineData(ToolbarAction.BlockPrograms)]
    [InlineData(ToolbarAction.BlockSites)]
    [InlineData(ToolbarAction.Message)]
    [InlineData(ToolbarAction.Chat)]
    [InlineData(ToolbarAction.SendFile)]
    [InlineData(ToolbarAction.CollectFiles)]
    public void Blocking_messages_and_chat_leave_no_phone_out(ToolbarAction action)
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01") };

        Assert.Null(DeviceSupport.LeftOutNotice(machines, action));
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, action));
        Assert.Equal(new[] { "PC 01", "Cel 01" }, DeviceSupport.Only(machines, action).Select(machine => machine.Name));
    }

    [Fact]
    public void Phone_only_selection_keeps_view_control_and_lock_on()
    {
        var selected = new[] { Phone("Cel 01") };

        Assert.True(DeviceSupport.IsEnabled(selected, ToolbarAction.View));
        Assert.True(DeviceSupport.IsEnabled(selected, ToolbarAction.ScreenLock));
        Assert.True(DeviceSupport.IsEnabled(selected, ToolbarAction.Control));
    }

    [Fact]
    public void Lock_of_a_mixed_class_leaves_no_phone_out() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01") }, ToolbarAction.ScreenLock));

    [Fact]
    public void Control_of_a_phone_leaves_nothing_out() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Phone("Cel 01") }, ToolbarAction.Control));

    // The Ctrl+Alt+Del escapes are a Windows Agent thing (port 28900): a phone has no Agent for it.
    [Fact]
    public void Phone_lock_does_not_start_switch_user_block()
    {
        Assert.True(SwitchUserBlockKeeper.AppliesTo(new KnownMachine("PC 01", "192.0.2.1", "ANY", "AGENT")));
        Assert.False(SwitchUserBlockKeeper.AppliesTo(new KnownMachine("PC 02", "192.0.2.2", "ANY", null)));
        Assert.False(SwitchUserBlockKeeper.AppliesTo(
            new KnownMachine("Cel 01", "192.0.2.9", "ANY", "AGENT", Kind: DeviceKind.Android)));
    }

    [Fact]
    public void Mixed_selection_warns_about_phones_left_out() =>
        Assert.Equal("2 celulares ficaram de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01"), Phone("Cel 02") }, ToolbarAction.Wake));

    [Fact]
    public void One_phone_left_out_uses_singular() =>
        Assert.Equal("1 celular ficou de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01") }, ToolbarAction.Wake));

    [Fact]
    public void Nobody_left_out_is_no_notice() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Pc("PC 02") }, ToolbarAction.Wake));

    // What the update window gets as its targets.
    [Fact]
    public void Only_leaves_phones_out_of_the_update()
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01"), Pc("PC 02") };

        Assert.Equal(new[] { "PC 01", "PC 02" }, DeviceSupport.Only(machines, ToolbarAction.Update).Select(machine => machine.Name));
    }

    // What PublishRoster feeds the class folder: the whole room, phones too, in its order.
    [Fact]
    public void The_class_folder_takes_the_phones_too()
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01"), Pc("PC 02") };

        Assert.Equal(new[] { "PC 01", "Cel 01", "PC 02" }, DeviceSupport.Only(machines, ToolbarAction.ClassFolder).Select(machine => machine.Name));
        Assert.True(DeviceSupport.Supports(DeviceKind.Android, ToolbarAction.ClassFolder));
    }

    [Fact]
    public void Action_names_in_the_xaml_parse_to_actions()
    {
        var item = new System.Windows.DependencyObject();
        Assert.Null(DeviceActions.Of(item));
        DeviceActions.SetAction(item, "SendFile");
        Assert.Equal(ToolbarAction.SendFile, DeviceActions.Of(item));
    }

    [Fact]
    public void Wiping_a_phone_is_for_phones_only()
    {
        Assert.True(DeviceSupport.Supports(DeviceKind.Android, ToolbarAction.WipePhone));
        Assert.False(DeviceSupport.Supports(DeviceKind.Windows, ToolbarAction.WipePhone));
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01"), Pc("PC 01") }, ToolbarAction.WipePhone));
        Assert.False(DeviceSupport.IsEnabled(new[] { Pc("PC 01") }, ToolbarAction.WipePhone));
    }
}
