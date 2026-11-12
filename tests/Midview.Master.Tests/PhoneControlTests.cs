using System.Windows.Input;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneControlTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", null);

    private static KnownMachine Phone(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    // Letters, digits, punctuation (ABNT2's own keys among them), the
    // keypad's and dead keys make characters: they go as TextInput.
    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Z)]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad7)]
    [InlineData(Key.Space)]
    [InlineData(Key.OemPeriod)]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.Oem1)]
    [InlineData(Key.AbntC1)]
    [InlineData(Key.OemBackslash)]
    [InlineData(Key.Add)]
    [InlineData(Key.Decimal)]
    [InlineData(Key.DeadCharProcessed)]
    public void Keys_that_type_are_left_to_the_typed_text(Key key)
    {
        Assert.True(PhoneControl.Types(key, ModifierKeys.None));
        Assert.True(PhoneControl.Types(key, ModifierKeys.Shift));
        // AltGr (Ctrl and Alt) types too: @ on many layouts.
        Assert.True(PhoneControl.Types(key, ModifierKeys.Control | ModifierKeys.Alt));
    }

    [Theory]
    [InlineData(Key.Back)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    [InlineData(Key.Delete)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Home)]
    [InlineData(Key.LWin)]
    [InlineData(Key.Tab)]
    [InlineData(Key.F5)]
    [InlineData(Key.LeftShift)]
    public void Keys_that_edit_or_move_go_as_keys(Key key) =>
        Assert.False(PhoneControl.Types(key, ModifierKeys.None));

    [Fact]
    public void Ctrl_or_alt_with_a_letter_types_nothing()
    {
        Assert.False(PhoneControl.Types(Key.V, ModifierKeys.Control));
        Assert.False(PhoneControl.Types(Key.F, ModifierKeys.Alt));
    }

    [Fact]
    public void Typed_text_goes_one_utf16_unit_at_a_time_without_control_characters()
    {
        var events = PhoneControl.TextEvents("aç\b\r\u007F😀");

        Assert.Equal(new ushort[] { 'a', 'ç', 0xD83D, 0xDE00 }, events.Select(e => e.VirtualKeyCode));
        Assert.All(events, e => Assert.Equal(InputEventKind.Text, e.Kind));
    }

    // The viewer's buttons: the keys the phone's agent reads as Back, Home and Recents.
    [Fact]
    public void Android_buttons_use_the_keys_the_phone_knows()
    {
        Assert.Equal(0xA6, PhoneControl.BackKey);
        Assert.Equal(0xAC, PhoneControl.HomeKey);
        Assert.Equal(0x5D, PhoneControl.RecentsKey);
    }

    [Fact]
    public void The_tile_menu_says_control_without_the_lock_for_phones_only()
    {
        Assert.Equal("Controlar", PhoneControl.MenuItem(new[] { Phone("Celular 1") }));
        Assert.Equal(PhoneControl.PcMenuItem, PhoneControl.MenuItem(new[] { Pc("PC 01") }));
        Assert.Equal(PhoneControl.PcMenuItem, PhoneControl.MenuItem(new[] { Pc("PC 01"), Phone("Celular 1") }));
        Assert.Equal(PhoneControl.PcMenuItem, PhoneControl.MenuItem(Array.Empty<KnownMachine>()));
    }

    [Fact]
    public void A_refusal_names_the_phone_and_the_reason()
    {
        Assert.Equal(
            "Celular 99 não deixou controlar: a acessibilidade do Midview está desligada neste celular: ligue pelo cabo.",
            PhoneControl.Refused("Celular 99", "a acessibilidade do Midview está desligada neste celular: ligue pelo cabo"));
        Assert.Equal("Celular 99 não deixou controlar.", PhoneControl.Refused("Celular 99", ""));
    }

    [Fact]
    public void An_old_phone_is_told_to_update() =>
        Assert.Contains("Atualize os celulares", PhoneControl.OldAgent("Celular 99"));
}
