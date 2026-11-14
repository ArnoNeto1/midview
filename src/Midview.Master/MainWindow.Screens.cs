using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Midview.Master;

// "Telas" (group Configurar): a menu with which screens the grid shows
// (DeviceFilter: Todos, Computadores, Celulares, Tablets). Celulares is
// greyed in a room with no phone, and Tablets in one with no tablet (unless
// the "Preparar celulares" assistant is out: its button, "Preparar
// celulares" or "Preparar tablets", is on the empty filter). What is done to
// the phones and tablets (optimize, PIN, Wi-Fi, wallpaper, ring, take the
// Midview off) is in "Máquinas da sala" > "Configurar celular" and
// "Configurar tablet" (DeviceRoomSection).
// While the grid leaves machines out, the button carries a filter badge so
// the teacher sees why, and the toolbar acts only on what it shows.
public partial class MainWindow
{
    // Segoe Fluent Icons "Filter".
    internal const string FilterBadge = "";

    private void OnScreensClicked(object sender, RoutedEventArgs e)
    {
        var room = _tiles.Select(tile => tile.Machine).ToList();
        var menu = ScreensMenu(
            _devices, DeviceSupport.HasAndroidOf(room, AndroidDeviceKind.Phone),
            DeviceSupport.HasAndroidOf(room, AndroidDeviceKind.Tablet), ShowDevices, PhoneProvisioning.Setup);
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowScreensBadge()
    {
        ScreensButton.Badge = _devices == DeviceFilter.All ? string.Empty : FilterBadge;
        ScreensButton.ToolTip = ScreensToolTip(_devices);
    }

    internal static string ScreensToolTip(DeviceFilter shown) => shown switch
    {
        DeviceFilter.Computers => "Mostrando só os computadores: os botões valem só para eles. Escolha aqui as telas que aparecem.",
        DeviceFilter.Phones => "Mostrando só os celulares: os botões valem só para eles. Escolha aqui as telas que aparecem.",
        DeviceFilter.Tablets => "Mostrando só os tablets: os botões valem só para eles. Escolha aqui as telas que aparecem.",
        _ => "Escolhe as telas que aparecem: todas, só os computadores, só os celulares ou só os tablets.",
    };

    // Built at each click, so it follows the room as it is then. phoneSetup
    // decides what the greyed Celulares (Tablets) says about getting the
    // first phone (tablet).
    internal static ContextMenu ScreensMenu(
        DeviceFilter shown, bool hasPhones, bool hasTablets, Action<DeviceFilter> show,
        PhoneSetupOffer phoneSetup = PhoneSetupOffer.Soon)
    {
        var menu = new ContextMenu();
        MenuItem Choice(string header, DeviceFilter devices, string toolTip)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = shown == devices, ToolTip = toolTip };
            item.Click += (_, _) => show(devices);
            menu.Items.Add(item);
            return item;
        }

        Choice("Todos", DeviceFilter.All, "Mostra computadores, celulares e tablets; os botões valem para a sala toda.");
        Choice("Computadores", DeviceFilter.Computers, "Mostra só os computadores; os botões valem só para eles.");
        var phones = Choice("Celulares", DeviceFilter.Phones,
            "Mostra só os celulares; os botões valem só para eles, e os que ainda não funcionam em celular ficam cinza.");
        if (!hasPhones)
        {
            NoneInTheRoom(phones, AndroidDeviceKind.Phone);
        }
        var tablets = Choice("Tablets", DeviceFilter.Tablets,
            "Mostra só os tablets; os botões valem só para eles, e os que ainda não funcionam em tablet ficam cinza.");
        if (!hasTablets)
        {
            NoneInTheRoom(tablets, AndroidDeviceKind.Tablet);
        }
        return menu;

        // With the "Preparar celulares" assistant out, the empty Celulares (or
        // Tablets) is where its big button is: it stays open to the teacher.
        void NoneInTheRoom(MenuItem item, AndroidDeviceKind kind)
        {
            item.IsEnabled = phoneSetup == PhoneSetupOffer.Wizard;
            item.InputGestureText = "nenhum na sala";
            item.ToolTip = PhoneProvisioning.NoDeviceYet(kind, phoneSetup);
            ToolTipService.SetShowOnDisabled(item, true);
        }
    }
}
