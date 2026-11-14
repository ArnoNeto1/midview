using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Midview.Master;
using Xunit;

// "Telas" (group Configurar): which screens the grid shows, and nothing else
// (the phones' and tablets' actions are in "Configurar celular" and
// "Configurar tablet": DeviceRoomSectionTests).
public class ScreensMenuTests
{
    [Fact]
    public void In_a_room_with_phones_and_tablets_it_offers_only_the_four_filters()
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.Phones, hasPhones: true, hasTablets: true, _ => { });

            Assert.Equal(new[] { "Todos", "Computadores", "Celulares", "Tablets" }, Headers(menu));
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(new[] { false, false, true, false }, items.Select(item => item.IsChecked));
            Assert.All(items, item => Assert.True(item.IsEnabled));
            Assert.All(items, item => Assert.Equal(string.Empty, item.InputGestureText));
        });
    }

    [Fact]
    public void Choosing_a_filter_shows_it()
    {
        InSta(() =>
        {
            var shown = new List<DeviceFilter>();
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: true, hasTablets: true, shown.Add);
            var items = menu.Items.OfType<MenuItem>().ToList();

            Assert.True(items[0].IsChecked);
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(new[] { DeviceFilter.Computers, DeviceFilter.Phones, DeviceFilter.All, DeviceFilter.Tablets }, shown);
        });
    }

    // A room of PCs alone: "Celulares" and "Tablets" greyed (they would show nothing).
    [Fact]
    public void In_a_room_of_pcs_phones_and_tablets_are_greyed()
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasTablets: false, _ => { });

            Assert.Equal(new[] { "Todos", "Computadores", "Celulares", "Tablets" }, Headers(menu));
            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.False(phones.IsEnabled);
            Assert.Equal("nenhum na sala", phones.InputGestureText);
            var tablets = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Tablets"));
            Assert.False(tablets.IsEnabled);
            Assert.Equal("nenhum na sala", tablets.InputGestureText);
        });
    }

    // Phones but no tablet: Celulares opens, Tablets is greyed; and the other way round.
    [Fact]
    public void Each_android_filter_follows_its_own_kind()
    {
        InSta(() =>
        {
            var phonesOnly = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: true, hasTablets: false, _ => { });
            Assert.True(Item(phonesOnly, "Celulares").IsEnabled);
            Assert.False(Item(phonesOnly, "Tablets").IsEnabled);
            Assert.Equal("Nenhum tablet nesta sala ainda.", Item(phonesOnly, "Tablets").ToolTip);

            var tabletsOnly = MainWindow.ScreensMenu(DeviceFilter.Tablets, hasPhones: false, hasTablets: true, _ => { });
            Assert.False(Item(tabletsOnly, "Celulares").IsEnabled);
            Assert.True(Item(tabletsOnly, "Tablets").IsEnabled);
            Assert.True(Item(tabletsOnly, "Tablets").IsChecked);
            Assert.Equal(4, tabletsOnly.Items.Count);
        });
    }

    [Theory]
    [InlineData(PhoneSetupOffer.Soon, "Nenhum tablet nesta sala ainda.")]
    [InlineData(PhoneSetupOffer.Cable, "Nenhum tablet nesta sala ainda. O tablet é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"Configurar tablet\".")]
    [InlineData(PhoneSetupOffer.Wizard, "Nenhum tablet nesta sala ainda. Para colocar o primeiro, escolha \"Tablets\" e use \"Preparar tablets\".")]
    public void Greyed_tablets_hint_follows_the_phone_setup(PhoneSetupOffer setup, string toolTip)
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: true, hasTablets: false, _ => { }, setup);

            var tablets = Item(menu, "Tablets");
            Assert.Equal(toolTip, tablets.ToolTip);
            Assert.Equal(setup == PhoneSetupOffer.Wizard, tablets.IsEnabled);
        });
    }

    // A PC-only teacher while phones are "em breve": no pointer to a phone
    // preparation that doesn't exist yet; once the app is out, the cable
    // steps; with the assistant out, "Preparar celulares".
    [Theory]
    [InlineData(PhoneSetupOffer.Soon, "Nenhum celular nesta sala ainda.")]
    [InlineData(PhoneSetupOffer.Cable, "Nenhum celular nesta sala ainda. O celular é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"Configurar celular\".")]
    [InlineData(PhoneSetupOffer.Wizard, "Nenhum celular nesta sala ainda. Para colocar o primeiro, escolha \"Celulares\" e use \"Preparar celulares\".")]
    public void Greyed_phones_hint_follows_the_phone_setup(PhoneSetupOffer setup, string toolTip)
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasTablets: false, _ => { }, setup);

            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.Equal(toolTip, phones.ToolTip);
        });
    }

    // Without the assistant, Celulares stays greyed in a room with no phone;
    // with it, Celulares opens the empty grid with the big "Preparar celulares" button.
    [Theory]
    [InlineData(PhoneSetupOffer.Soon, false)]
    [InlineData(PhoneSetupOffer.Cable, false)]
    [InlineData(PhoneSetupOffer.Wizard, true)]
    public void With_no_phone_celulares_opens_only_with_the_assistant_out(PhoneSetupOffer setup, bool enabled)
    {
        InSta(() =>
        {
            var shown = new List<DeviceFilter>();
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasTablets: false, shown.Add, setup);

            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.Equal(enabled, phones.IsEnabled);
            Assert.Equal("nenhum na sala", phones.InputGestureText);
        });
    }

    [Fact]
    public void Greyed_phones_hint_in_this_build_offers_the_assistant_only_when_it_is_out()
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasTablets: false, _ => { }, PhoneProvisioning.Setup);

            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.Equal(PhoneProvisioning.WizardReady, ((string)phones.ToolTip).Contains("Preparar celular"));
            Assert.Equal(PhoneProvisioning.WizardReady, phones.IsEnabled);
        });
    }

    [Theory]
    [InlineData(DeviceFilter.All, "Escolhe as telas que aparecem: todas, só os computadores, só os celulares ou só os tablets.")]
    [InlineData(DeviceFilter.Computers, "Mostrando só os computadores: os botões valem só para eles.")]
    [InlineData(DeviceFilter.Phones, "Mostrando só os celulares: os botões valem só para eles.")]
    [InlineData(DeviceFilter.Tablets, "Mostrando só os tablets: os botões valem só para eles.")]
    public void The_tool_tip_says_what_the_grid_shows(DeviceFilter shown, string start)
    {
        Assert.StartsWith(start, MainWindow.ScreensToolTip(shown));
        Assert.DoesNotContain("otimiza", MainWindow.ScreensToolTip(shown));
    }

    private static MenuItem Item(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    private static IEnumerable<string> Headers(ContextMenu menu) =>
        menu.Items.Cast<object>().Select(item => item is MenuItem menuItem ? (string)menuItem.Header : "-").ToList();

    private static void InSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); } catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
