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

    [Fact]
    public void With_nothing_selected_every_device_of_that_kind_in_the_room()
    {
        Assert.Equal(new[] { Celular01, Celular02 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Phone, Array.Empty<KnownMachine>(), Room));
        Assert.Equal(new[] { Tablet01 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, Array.Empty<KnownMachine>(), Room));
    }

    [Fact]
    public void With_some_of_that_kind_selected_only_those()
    {
        Assert.Equal(new[] { Celular02 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Phone, new[] { Pc01, Celular02, Tablet01 }, Room));
        Assert.Equal(new[] { Tablet01 }, DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, new[] { Celular02, Tablet01 }, Room));
    }

    // A selection of PCs, or of the other kind, reaches none of this kind:
    // never the whole room behind the teacher's back.
    [Fact]
    public void With_none_of_that_kind_selected_none()
    {
        Assert.Empty(DeviceSupport.RoomTargets(AndroidDeviceKind.Phone, new[] { Pc01, Tablet01 }, Room));
        Assert.Empty(DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, new[] { Celular01 }, Room));
        Assert.Empty(DeviceSupport.RoomTargets(AndroidDeviceKind.Tablet, Array.Empty<KnownMachine>(), new[] { Pc01, Celular01 }));
    }

    [Fact]
    public void A_selection_without_that_kind_says_to_clear_it_and_the_buttons_are_off()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 2, ringing: false, reached: 0);

            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.False(section.ButtonOf(action).IsEnabled));
            Assert.Contains(section.Children.OfType<TextBlock>(), block =>
                block.Text == "Nenhum celular selecionado; tire a seleção para valer para a sala toda." && block.Visibility == Visibility.Visible);
            Assert.Equal("Nenhum tablet selecionado; tire a seleção para valer para a sala toda.",
                DeviceRoomText.NoneSelected(AndroidDeviceKind.Tablet));

            section.Show(AndroidDeviceKind.Phone, 2, ringing: false, reached: 1);
            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.True(section.ButtonOf(action).IsEnabled));
            Assert.DoesNotContain(section.Children.OfType<TextBlock>(), block => block.Text.StartsWith("Nenhum") && block.Visibility == Visibility.Visible);
        });
    }

    [Theory]
    [InlineData(AndroidDeviceKind.Phone, 3, "CELULARES DA SALA (3) · OU SÓ OS SELECIONADOS NA TELA")]
    [InlineData(AndroidDeviceKind.Tablet, 1, "TABLETS DA SALA (1) · OU SÓ OS SELECIONADOS NA TELA")]
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
    public void Six_cards_with_their_buttons_all_on_with_devices_in_the_room()
    {
        InSta(() =>
        {
            var section = new DeviceRoomSection();
            section.Show(AndroidDeviceKind.Phone, 2, ringing: false);

            Assert.Equal(
                new[] { "Otimizar...", "Definir PIN...", "Liberar...", "Escolher imagem...", "Tocar...", "Tirar o Midview..." },
                Enum.GetValues<DeviceRoomAction>().Select(action => (string)section.ButtonOf(action).Content));
            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.True(section.ButtonOf(action).IsEnabled));
            var texts = Texts(section);
            Assert.Contains("CELULARES DA SALA (2) · OU SÓ OS SELECIONADOS NA TELA", texts);
            Assert.Contains("Toca alto para achar um celular perdido.", texts);
            Assert.Contains("Senha (PIN)", texts);
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
            Assert.All(Enum.GetValues<DeviceRoomAction>(), action => Assert.True(section.ButtonOf(action).IsEnabled));
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
}
