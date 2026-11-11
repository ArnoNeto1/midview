using Midview.Master;
using Midview.Protocol;
using Xunit;

public class DeviceSupportTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", null);

    private static KnownMachine Phone(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    [Fact]
    public void Phone_only_selection_disables_pc_actions() =>
        Assert.False(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, ToolbarAction.Power));

    [Fact]
    public void A_computer_in_the_selection_keeps_the_action_enabled() =>
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01"), Pc("PC 01") }, ToolbarAction.Power));

    [Fact]
    public void Nothing_selected_enables_nothing() =>
        Assert.False(DeviceSupport.IsEnabled(Array.Empty<KnownMachine>(), ToolbarAction.View));

    [Theory]
    [InlineData(ToolbarAction.View)]
    [InlineData(ToolbarAction.Power)]
    [InlineData(ToolbarAction.BlockSites)]
    [InlineData(ToolbarAction.Update)]
    public void Computers_support_every_action_and_phones_none_yet(ToolbarAction action)
    {
        Assert.True(DeviceSupport.Supports(DeviceKind.Windows, action));
        Assert.False(DeviceSupport.Supports(DeviceKind.Android, action));
    }

    [Fact]
    public void Mixed_selection_warns_about_phones_left_out() =>
        Assert.Equal("2 celulares ficaram de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01"), Phone("Cel 02") }, ToolbarAction.Power));

    [Fact]
    public void One_phone_left_out_uses_singular() =>
        Assert.Equal("1 celular ficou de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01") }, ToolbarAction.Power));

    [Fact]
    public void Nobody_left_out_is_no_notice() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Pc("PC 02") }, ToolbarAction.Power));

    // What PublishRoster feeds the class folder, and the update window its targets.
    [Theory]
    [InlineData(ToolbarAction.ClassFolder)]
    [InlineData(ToolbarAction.Update)]
    public void Only_leaves_phones_out_of_the_class_folder_and_the_update(ToolbarAction action)
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01"), Pc("PC 02") };

        Assert.Equal(new[] { "PC 01", "PC 02" }, DeviceSupport.Only(machines, action).Select(machine => machine.Name));
    }

    [Fact]
    public void Action_names_in_the_xaml_parse_to_actions()
    {
        var item = new System.Windows.DependencyObject();
        Assert.Null(DeviceActions.Of(item));
        DeviceActions.SetAction(item, "SendFile");
        Assert.Equal(ToolbarAction.SendFile, DeviceActions.Of(item));
    }
}
