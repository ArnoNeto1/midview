using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Midview.Master;

// "Telas" (group Configurar): a menu with which screens the grid shows
// (DeviceFilter: Todos, Computadores, Celulares; Tablets later) and
// "Otimizar celulares e tablets..." and "Senha dos celulares e tablets..."
// (every Android device, or the selected ones). Celulares is
// greyed in a room with no phone (unless the "Preparar celulares" assistant
// is out: its button is on the empty Celulares), and the optimizing is left
// out of a room with no Android device, and so is the PIN. While the grid leaves machines out,
// the button carries a filter badge so the teacher sees why.
public partial class MainWindow
{
    // Segoe Fluent Icons "Filter".
    internal const string FilterBadge = "";

    private void OnScreensClicked(object sender, RoutedEventArgs e)
    {
        var room = _tiles.Select(tile => tile.Machine).ToList();
        var menu = ScreensMenu(
            _devices, DeviceSupport.HasPhones(room), DeviceSupport.HasAndroid(room),
            ShowDevices, () => OpenOptimizeWindow(ToolbarReach(ToolbarAction.Optimize)),
            () => OpenPhonePinWindow(ToolbarReach(ToolbarAction.PhonePin)), PhoneProvisioning.Setup);
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
        DeviceFilter.Computers => "Mostrando só os computadores. Escolha aqui as telas que aparecem; também otimiza celulares e tablets.",
        DeviceFilter.Phones => "Mostrando só os celulares. Escolha aqui as telas que aparecem; também otimiza celulares e tablets.",
        _ => "Escolhe as telas que aparecem: todas, só os computadores ou só os celulares. Também otimiza celulares e tablets.",
    };

    // Built at each click, so it follows the room as it is then. phoneSetup
    // decides what the greyed Celulares says about getting the first phone.
    internal static ContextMenu ScreensMenu(
        DeviceFilter shown, bool hasPhones, bool hasAndroid, Action<DeviceFilter> show, Action optimize, Action phonePin,
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

        Choice("Todos", DeviceFilter.All, "Mostra computadores e celulares.");
        Choice("Computadores", DeviceFilter.Computers, "Mostra só os computadores.");
        var phones = Choice("Celulares", DeviceFilter.Phones,
            "Mostra só os celulares. Os celulares ficam de fora das ações que ainda não funcionam neles.");
        if (!hasPhones)
        {
            // With the "Preparar celulares" assistant out, the empty Celulares
            // is where its big button is: it stays open to the teacher.
            phones.IsEnabled = phoneSetup == PhoneSetupOffer.Wizard;
            phones.InputGestureText = "nenhum na sala";
            phones.ToolTip = PhoneProvisioning.NoPhoneYet(phoneSetup);
            ToolTipService.SetShowOnDisabled(phones, true);
        }
        menu.Items.Add(new MenuItem
        {
            Header = "Tablets",
            IsCheckable = true,
            IsEnabled = false,
            InputGestureText = "em breve",
        });

        if (hasAndroid)
        {
            menu.Items.Add(new Separator());
            var optimizeItem = new MenuItem
            {
                Header = "Otimizar celulares e tablets...",
                ToolTip = "Esconde os apps de propaganda dos celulares e tablets e liga o DNS sem anúncios. Com alguns selecionados, só neles.",
            };
            optimizeItem.Click += (_, _) => optimize();
            menu.Items.Add(optimizeItem);
            var pinItem = new MenuItem
            {
                Header = "Senha dos celulares e tablets...",
                ToolTip = "Põe ou tira o PIN de 4 números da tela de bloqueio dos celulares e tablets; o aluno não consegue trocá-lo. Com alguns selecionados, só neles.",
            };
            pinItem.Click += (_, _) => phonePin();
            menu.Items.Add(pinItem);
        }
        return menu;
    }
}
