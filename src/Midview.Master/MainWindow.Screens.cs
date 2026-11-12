using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Midview.Master;

// "Telas" (group Configurar): a menu with which screens the grid shows
// (DeviceFilter: Todos, Computadores, Celulares; Tablets later) and
// "Otimizar celulares e tablets..." (every Android device). Celulares is
// greyed in a room with no phone, and the optimizing is left out of a room
// with no Android device. While the grid leaves machines
// out, the button carries a filter badge so the teacher sees why.
public partial class MainWindow
{
    // Segoe Fluent Icons "Filter".
    internal const string FilterBadge = "";

    private void OnScreensClicked(object sender, RoutedEventArgs e)
    {
        var room = _tiles.Select(tile => tile.Machine).ToList();
        var menu = ScreensMenu(
            _devices, DeviceSupport.HasPhones(room), DeviceSupport.HasAndroid(room),
            ShowDevices, () => OpenOptimizeWindow(ToolbarReach(ToolbarAction.Optimize)));
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

    // Built at each click, so it follows the room as it is then.
    internal static ContextMenu ScreensMenu(
        DeviceFilter shown, bool hasPhones, bool hasAndroid, Action<DeviceFilter> show, Action optimize)
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
            phones.IsEnabled = false;
            phones.InputGestureText = "nenhum na sala";
            phones.ToolTip = "Nenhum celular nesta sala ainda. Para colocar o primeiro, abra \"Máquinas da sala\" e use \"Preparar celular\".";
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
        }
        return menu;
    }
}
