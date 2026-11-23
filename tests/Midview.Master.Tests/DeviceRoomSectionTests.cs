using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// The room's section of "Configurar celular" and "Configurar tablet"
// (DeviceRoomSection): what was in the "Telas" menu, now for the devices of
// the page's kind, the selected ones or every one of the room.
public class DeviceRoomSectionTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", null);

    private static KnownMachine Android(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    private static readonly KnownMachine Pc01 = Pc("PC 01");
    private static readonly KnownMachine Celular01 = Android("Celular 01");
    private static readonly KnownMachine Celular02 = Android("Celular 02");
    private static readonly KnownMachine Tablet01 = Android("Tablet 01");
    private static readonly KnownMachine[] Room = { Pc01, Celular01, Celular02, Tablet01 };

    // The room's cards: all but "Atualizar o Midview" and "Apagar e preparar
    // de novo", which are for the devices picked from a tile's menu.
    private static readonly DeviceRoomAction[] RoomCards = Enum.GetValues<DeviceRoomAction>()
        .Where(action => action is not (DeviceRoomAction.Update or DeviceRoomAction.Wipe)).ToArray();

    // Every device of that kind in the room, whatever is selected in the
    // grid (a few are reached from a tile's right-click menu).
    [Fact]
    public void The_cards_reach_every_device_of_that_kind_in_the_room()
    {
        Assert.Equal(new[] { Celular01, Celular02 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Phone, Room));
        Assert.Equal(new[] { Tablet01 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, Room));
        Assert.Empty(DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, new[] { Pc01, Celular01 }));
    }

    [Fact]
    public void A_room_without_that_kind_says_so_and_the_buttons_are_off()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 0, ringing: false);

            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.False(section.ButtonOf(action).IsEnabled));
            Assert.Contains(section.Children.OfType<TextBlock>(), block =>
                block.Text == "Nenhum celular na sala ainda." && block.Visibility == Visibility.Visible);

            section.Show(AndroidDeviceKind.Phone, 2, ringing: false);
            Assert.All(RoomCards, action => Assert.True(section.ButtonOf(action).IsEnabled));
            Assert.DoesNotContain(section.Children.OfType<TextBlock>(), block => block.Text.StartsWith("Nenhum") && block.Visibility == Visibility.Visible);
        });
    }

    [Theory]
    [InlineData(AndroidDeviceKind.Phone, 3, "CELULARES DA SALA (3)")]
    [InlineData(AndroidDeviceKind.Tablet, 1, "TABLETS DA SALA (1)")]
    [InlineData(AndroidDeviceKind.Tablet, 0, "TABLETS DA SALA (0)")]
    public void The_heading_counts_the_devices_of_that_kind(AndroidDeviceKind kind, int count, string heading)
    {
        Assert.Equal(heading, DeviceRoomText.RoomSection(kind, count));
    }

    [Fact]
    public void Each_page_names_its_kind()
    {
        Assert.Equal("PREPARAR UM CELULAR NOVO", DeviceRoomText.NewSection(AndroidDeviceKind.Phone));
        Assert.Equal("PREPARAR UM TABLET NOVO", DeviceRoomText.NewSection(AndroidDeviceKind.Tablet));
        Assert.Equal("Toca alto para achar um tablet perdido.", DeviceRoomText.FindLine(AndroidDeviceKind.Tablet));
        Assert.Equal("Prepare celulares novos e cuide dos celulares da sala.", DeviceSetupText.Subtitle(AndroidDeviceKind.Phone));
    }

    [Fact]
    public void Nine_cards_with_their_buttons_all_on_with_devices_in_the_room()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 2, ringing: false);

            Assert.Equal(
                new[]
                {
                    "Otimizar...", "Definir PIN...", "Liberar...", "Liberar...", "Escolher imagem...", "Sair das contas...", "Tocar...",
                    "Liberar...", "Tirar o Midview...",
                },
                RoomCards.Select(action => (string)section.ButtonOf(action).Content));
            Assert.All(RoomCards, action => Assert.True(section.ButtonOf(action).IsEnabled));
            Assert.All(new[] { DeviceRoomAction.Update, DeviceRoomAction.Wipe },
                action => Assert.Equal(Visibility.Collapsed, CardOf(section.ButtonOf(action)).Visibility));
            var texts = Texts(section);
            Assert.Contains("CELULARES DA SALA (2)", texts);
            Assert.Contains("Toca alto para achar um celular perdido.", texts);
            Assert.Contains("Senha (PIN)", texts);
            Assert.Contains("Contas do aparelho", texts);
            Assert.Contains("Travadas: o aluno não põe nem tira contas. Libere por um tempo para pôr a conta Google da escola.", texts);
            Assert.Contains("Sair das contas do Chrome", texts);
            Assert.Contains("Apaga os logins de sites, os cookies, o histórico e as abas do Chrome. O próximo aluno abre o Chrome limpo.", texts);
            Assert.Contains("Depuração USB", texts);
            Assert.Contains(
                "Travada: o aluno não liga a depuração. Libere por um tempo para conferir o celular pelo cabo; ela liga sozinha e desliga no fim.",
                texts);
            Assert.DoesNotContain(section.Children.OfType<TextBlock>(), block => block.Text == "Nenhum celular na sala ainda." && block.Visibility == Visibility.Visible);
        });
    }

    [Fact]
    public void With_none_of_that_kind_it_says_so_and_the_buttons_are_off()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Tablet, 0, ringing: false);

            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.False(section.ButtonOf(action).IsEnabled));
            Assert.Contains(section.Children.OfType<TextBlock>(), block => block.Text == "Nenhum tablet na sala ainda." && block.Visibility == Visibility.Visible);
            Assert.True(ToolTipService.GetShowOnDisabled(section.ButtonOf(DeviceRoomAction.Optimize)));

            section.Show(AndroidDeviceKind.Tablet, 1, ringing: false);
            Assert.All(RoomCards, action => Assert.True(section.ButtonOf(action).IsEnabled));
        });
    }

    [Fact]
    public void Each_button_asks_its_own_action()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 1, ringing: false);
            var asked = new List<DeviceRoomAction>();
            section.Act += asked.Add;

            foreach (var action in Enum.GetValues<DeviceRoomAction>().Reverse())
            {
                section.ButtonOf(action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Assert.Equal(Enum.GetValues<DeviceRoomAction>().Reverse(), asked);
        });
    }

    // The two cards of the device's accounts and Chrome say the page's kind in their tooltips.
    [Fact]
    public void The_accounts_and_chrome_cards_speak_of_the_pages_kind()
    {
        Assert.Contains("contas dos tablets", DeviceRoomText.ToolTip(DeviceRoomAction.Accounts, AndroidDeviceKind.Tablet));
        Assert.Contains("contas dos celulares", DeviceRoomText.ToolTip(DeviceRoomAction.Accounts, AndroidDeviceKind.Phone));
        Assert.Contains("Chrome dos tablets", DeviceRoomText.ToolTip(DeviceRoomAction.ClearChrome, AndroidDeviceKind.Tablet));
        Assert.EndsWith("Para um celular só, use o botão direito na tela dele.", DeviceRoomText.ToolTip(DeviceRoomAction.ClearChrome, AndroidDeviceKind.Phone));
    }

    // "Depuração USB" says the page's kind on its card and in its tooltip.
    // Before pressing, the teacher reads that the apps someone installed go
    // and the "Instalar programa" ones stay.
    [Theory]
    [InlineData(AndroidDeviceKind.Phone, "celulares")]
    [InlineData(AndroidDeviceKind.Tablet, "tablets")]
    public void The_optimize_card_says_installed_apps_go_and_the_teachers_stay(AndroidDeviceKind kind, string words)
    {
        var line = DeviceRoomText.Card(DeviceRoomAction.Optimize, kind).Line;
        Assert.Contains("os que alguém instalou", line);
        Assert.Contains("\"Instalar programa\" ficam", line);
        var tip = DeviceRoomText.ToolTip(DeviceRoomAction.Optimize, kind);
        Assert.Contains($"Tira dos {words} os apps que a turma não usa", tip);
        Assert.Contains("alguém instalou depois, pela Play Store ou por fora", tip);
        Assert.Contains("ficam os que você instalou pelo \"Instalar programa\" do Midview", tip);
    }

    [Fact]
    public void The_debugging_card_speaks_of_the_pages_kind()
    {
        Assert.Equal(
            "Travada: o aluno não liga a depuração. Libere por um tempo para conferir o tablet pelo cabo; ela liga sozinha e desliga no fim.",
            DeviceRoomText.Card(DeviceRoomAction.Debugging, AndroidDeviceKind.Tablet).Line);
        Assert.Equal(("Depuração USB", "Liberar..."), (
            DeviceRoomText.Card(DeviceRoomAction.Debugging, AndroidDeviceKind.Phone).Title,
            DeviceRoomText.Card(DeviceRoomAction.Debugging, AndroidDeviceKind.Phone).Button));
        Assert.Contains("depuração USB dos tablets", DeviceRoomText.ToolTip(DeviceRoomAction.Debugging, AndroidDeviceKind.Tablet));
        Assert.EndsWith("Para um celular só, use o botão direito na tela dele.", DeviceRoomText.ToolTip(DeviceRoomAction.Debugging, AndroidDeviceKind.Phone));
    }

    // "Parar o toque" while all the devices it reaches still ring, as the tile menu.
    [Fact]
    public void Tocar_says_parar_o_toque_while_ringing()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 1, ringing: true);
            Assert.Equal(PhoneFindText.StopMenuItem, section.ButtonOf(DeviceRoomAction.Find).Content);
            Assert.Equal(PhoneFindText.StopToolTip, section.ButtonOf(DeviceRoomAction.Find).ToolTip);

            section.Show(AndroidDeviceKind.Phone, 1, ringing: false);
            Assert.Equal("Tocar...", section.ButtonOf(DeviceRoomAction.Find).Content);
        });
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock block)
            {
                texts.Add(block.Text);
            }
            texts.AddRange(Texts(child));
        }
        return texts;
    }

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

    // "Configurar tablet..." of a tile's menu: the cards name the device and
    // add "Atualizar o Midview" and "Apagar e preparar de novo".
    [Fact]
    public void Opened_for_one_tablet_it_names_it_and_adds_update_and_wipe()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Tablet, 1, ringing: false,
                new DeviceFocus(DeviceRoomText.FocusSection(AndroidDeviceKind.Tablet, new[] { "Tablet 04" }), CanWipe: true));

            Assert.Contains("TABLET 04", Texts(section));
            Assert.Contains("Atualizar o Midview", Texts(section));
            Assert.Contains("Apagar e preparar de novo", Texts(section));
            Assert.All(Enum.GetValues<DeviceRoomAction>(), action =>
            {
                Assert.True(section.ButtonOf(action).IsEnabled);
                Assert.Equal(Visibility.Visible, CardOf(section.ButtonOf(action)).Visibility);
            });
            Assert.DoesNotContain("botão direito", (string)section.ButtonOf(DeviceRoomAction.Pin).ToolTip);
        });
    }

    // Wiping is for one school device that answers now: off otherwise, and it says why.
    [Fact]
    public void Wiping_is_off_when_it_cannot_work()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Tablet, 3, ringing: false,
                new DeviceFocus(DeviceRoomText.FocusSection(AndroidDeviceKind.Tablet, new[] { "Tablet 04", "Tablet 12", "Tablet 16" }), CanWipe: false));

            Assert.Contains("3 TABLETS: TABLET 04, TABLET 12, TABLET 16", Texts(section));
            Assert.False(section.ButtonOf(DeviceRoomAction.Wipe).IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(section.ButtonOf(DeviceRoomAction.Wipe)));
            Assert.Contains("Só para um aparelho", (string)section.ButtonOf(DeviceRoomAction.Wipe).ToolTip);
            Assert.True(section.ButtonOf(DeviceRoomAction.Update).IsEnabled);
        });
    }

    [Fact]
    public void The_menu_item_and_the_line_say_whom_it_is_for()
    {
        Assert.Equal("Configurar tablet...", DeviceRoomText.MenuItem(AndroidDeviceKind.Tablet, 1));
        Assert.Equal("Configurar 3 celulares...", DeviceRoomText.MenuItem(AndroidDeviceKind.Phone, 3));
        Assert.Equal("Só no Tablet 04.", DeviceRoomText.FocusLine(new[] { "Tablet 04" }));
        Assert.Equal("Só nestes 2: Celular 01, Celular 02.", DeviceRoomText.FocusLine(new[] { "Celular 01", "Celular 02" }));
        Assert.Equal("Ver todos os tablets da sala", DeviceRoomText.AllOfTheRoom(AndroidDeviceKind.Tablet));
    }

    private static Border CardOf(DependencyObject button)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(button) ?? LogicalTreeHelper.GetParent(button);
        while (parent is not Border)
        {
            parent = LogicalTreeHelper.GetParent(parent) ?? System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return (Border)parent;
    }
}
