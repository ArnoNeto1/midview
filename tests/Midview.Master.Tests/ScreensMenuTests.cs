using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Midview.Master;
using Xunit;

// "Telas" (group Configurar): which screens the grid shows, and "Otimizar celulares e tablets...".
public class ScreensMenuTests
{
    [Fact]
    public void In_a_room_with_phones_it_offers_every_filter_tablets_later_and_optimizing()
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.Phones, hasPhones: true, hasAndroid: true, _ => { }, () => { });

            Assert.Equal(new[] { "Todos", "Computadores", "Celulares", "Tablets", "-", "Otimizar celulares e tablets..." }, Headers(menu));
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(new[] { false, false, true, false }, items.Take(4).Select(item => item.IsChecked));
            Assert.All(items.Take(3), item => Assert.True(item.IsEnabled));
            Assert.False(items[3].IsEnabled);
            Assert.Equal("em breve", items[3].InputGestureText);
            Assert.True(items[4].IsEnabled);
        });
    }

    [Fact]
    public void Choosing_a_filter_shows_it_and_optimizing_opens_the_window()
    {
        InSta(() =>
        {
            var shown = new List<DeviceFilter>();
            var optimized = 0;
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: true, hasAndroid: true, shown.Add, () => optimized++);
            var items = menu.Items.OfType<MenuItem>().ToList();

            Assert.True(items[0].IsChecked);
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(new[] { DeviceFilter.Computers, DeviceFilter.Phones, DeviceFilter.All }, shown);
            Assert.Equal(1, optimized);
        });
    }

    // A room of PCs alone: "Celulares" greyed (it would show nothing) and nothing to optimize.
    [Fact]
    public void In_a_room_of_pcs_phones_are_greyed_and_there_is_nothing_to_optimize()
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasAndroid: false, _ => { }, () => { });

            Assert.Equal(new[] { "Todos", "Computadores", "Celulares", "Tablets" }, Headers(menu));
            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.False(phones.IsEnabled);
            Assert.Equal("nenhum na sala", phones.InputGestureText);
        });
    }

    // A PC-only teacher while phones are "em breve": no pointer to a phone
    // preparation that doesn't exist yet; once the app is out, the cable
    // steps; with the assistant out, "Preparar celulares".
    [Theory]
    [InlineData(PhoneSetupOffer.Soon, "Nenhum celular nesta sala ainda.")]
    [InlineData(PhoneSetupOffer.Cable, "Nenhum celular nesta sala ainda. O celular é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"Procurar e parear\".")]
    [InlineData(PhoneSetupOffer.Wizard, "Nenhum celular nesta sala ainda. Para colocar o primeiro, escolha \"Celulares\" e use \"Preparar celulares\".")]
    public void Greyed_phones_hint_follows_the_phone_setup(PhoneSetupOffer setup, string toolTip)
    {
        InSta(() =>
        {
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasAndroid: false, _ => { }, () => { }, setup);

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
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasAndroid: false, shown.Add, () => { }, setup);

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
            var menu = MainWindow.ScreensMenu(DeviceFilter.All, hasPhones: false, hasAndroid: false, _ => { }, () => { }, PhoneProvisioning.Setup);

            var phones = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Celulares"));
            Assert.Equal(PhoneProvisioning.WizardReady, ((string)phones.ToolTip).Contains("Preparar celular"));
            Assert.Equal(PhoneProvisioning.WizardReady, phones.IsEnabled);
        });
    }

    [Theory]
    [InlineData(DeviceFilter.All, "Escolhe as telas")]
    [InlineData(DeviceFilter.Computers, "Mostrando só os computadores.")]
    [InlineData(DeviceFilter.Phones, "Mostrando só os celulares.")]
    public void The_tool_tip_says_what_the_grid_shows(DeviceFilter shown, string start) =>
        Assert.StartsWith(start, MainWindow.ScreensToolTip(shown));

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
