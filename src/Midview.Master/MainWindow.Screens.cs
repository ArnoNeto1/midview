using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Midview.Master;

// "Telas" (group Configurar): a menu with which screens the grid shows
// (DeviceFilter: Todos, Computadores, Celulares, Tablets) and
// "Otimizar celulares e tablets..." and "Senha dos celulares e tablets..."
// (the selected Android devices, or every one the grid shows, by the rule
// of every toolbar button: DeviceSupport.ToolbarReach). Celulares is
// greyed in a room with no phone, and Tablets in one with no tablet (unless
// the "Preparar celulares" assistant is out: its button, "Preparar
// celulares" or "Preparar tablets", is on the empty filter), and the optimizing is left
// out of a room with no Android device, and so is the PIN; both are greyed
// while they would reach none (Computadores, or a selection of PCs alone).
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
            DeviceSupport.HasAndroidOf(room, AndroidDeviceKind.Tablet), DeviceSupport.HasAndroid(room),
            ShowDevices, () => OpenOptimizeWindow(ToolbarReach(ToolbarAction.Optimize)),
            () => OpenPhonePinWindow(ToolbarReach(ToolbarAction.PhonePin)), PhoneProvisioning.Setup,
            reachesAndroid: ToolbarReach(ToolbarAction.Optimize).Count > 0,
            phoneNetwork: () => OpenPhoneNetworkWindow(ToolbarReach(ToolbarAction.PhonePin)));
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
        DeviceFilter.Computers => "Mostrando só os computadores: os botões valem só para eles. Escolha aqui as telas que aparecem; também otimiza celulares e tablets.",
        DeviceFilter.Phones => "Mostrando só os celulares: os botões valem só para eles. Escolha aqui as telas que aparecem; também otimiza celulares e tablets.",
        DeviceFilter.Tablets => "Mostrando só os tablets: os botões valem só para eles. Escolha aqui as telas que aparecem; também otimiza celulares e tablets.",
        _ => "Escolhe as telas que aparecem: todas, só os computadores, só os celulares ou só os tablets. Também otimiza celulares e tablets.",
    };

    // Built at each click, so it follows the room as it is then. phoneSetup
    // decides what the greyed Celulares (Tablets) says about getting the
    // first phone (tablet); reachesAndroid, whether optimizing and the PIN
    // would reach any device.
    internal static ContextMenu ScreensMenu(
        DeviceFilter shown, bool hasPhones, bool hasTablets, bool hasAndroid, Action<DeviceFilter> show, Action optimize, Action phonePin,
        PhoneSetupOffer phoneSetup = PhoneSetupOffer.Soon, bool reachesAndroid = true, Action? phoneNetwork = null)
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
            Unreached(optimizeItem);
            var pinItem = new MenuItem
            {
                Header = "Senha dos celulares e tablets...",
                ToolTip = "Põe ou tira o PIN de 4 números da tela de bloqueio dos celulares e tablets; o aluno não consegue trocá-lo. Com alguns selecionados, só neles.",
            };
            pinItem.Click += (_, _) => phonePin();
            menu.Items.Add(pinItem);
            Unreached(pinItem);
            var networkItem = new MenuItem
            {
                Header = "Liberar Wi-Fi e modo avião...",
                ToolTip = "Libera por alguns minutos o Wi-Fi e o modo avião, que ficam travados nos celulares e tablets, ou trava de novo; também a senha de administrador dos celulares. Com alguns selecionados, só neles.",
            };
            networkItem.Click += (_, _) => phoneNetwork?.Invoke();
            menu.Items.Add(networkItem);
            Unreached(networkItem);
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

        // Greyed while the toolbar reaches no phone or tablet.
        void Unreached(MenuItem item)
        {
            if (reachesAndroid)
            {
                return;
            }
            item.IsEnabled = false;
            item.ToolTip = "Nenhum celular ou tablet selecionado ou na grade: escolha Todos, Celulares ou Tablets, ou selecione um celular ou tablet.";
            ToolTipService.SetShowOnDisabled(item, true);
        }
    }
}
