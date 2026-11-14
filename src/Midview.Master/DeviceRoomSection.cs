using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Midview.Master;

// What the cards under "Configurar celular" ("Configurar tablet") open: the
// windows that were in the "Telas" menu.
public enum DeviceRoomAction
{
    Optimize,
    Pin,
    Network,
    Wallpaper,
    Find,
    Remove,
}

// The texts of the room's section of "Configurar celular" and "Configurar tablet".
internal static class DeviceRoomText
{
    public static string NewSection(AndroidDeviceKind kind) =>
        kind == AndroidDeviceKind.Tablet ? "PREPARAR UM TABLET NOVO" : "PREPARAR UM CELULAR NOVO";

    // The count is every device of that kind in the room; the cards act on
    // the selected ones of that kind when there are some (DeviceSupport.RoomTargets).
    public static string RoomSection(AndroidDeviceKind kind, int count) =>
        $"{DeviceSetupText.Words(kind).ToUpperInvariant()} DA SALA ({count})" + (count > 0 ? " · OU SÓ OS SELECIONADOS NA TELA" : string.Empty);

    public static string NoneInRoom(AndroidDeviceKind kind) => $"Nenhum {DeviceSetupText.Word(kind)} na sala ainda.";

    public static string FindLine(AndroidDeviceKind kind) => $"Toca alto para achar um {DeviceSetupText.Word(kind)} perdido.";

    public const string FindButton = "Tocar...";

    // Each card: its title, its line and its button.
    public static (string Title, string Line, string Button) Card(DeviceRoomAction action, AndroidDeviceKind kind) => action switch
    {
        DeviceRoomAction.Optimize => ("Otimizar", "Tira apps que a turma não usa e liga o DNS sem anúncios.", "Otimizar..."),
        DeviceRoomAction.Pin => ("Senha (PIN)", "PIN de 4 números que o aluno não troca.", "Definir PIN..."),
        DeviceRoomAction.Network => ("Wi-Fi e modo avião", "Travados. Libere por um tempo para trocar de rede.", "Liberar..."),
        DeviceRoomAction.Wallpaper => ("Papel de parede", "Põe a imagem da escola na tela inicial e de bloqueio.", "Escolher imagem..."),
        DeviceRoomAction.Find => ("Tocar para encontrar", FindLine(kind), FindButton),
        _ => ("Tirar o Midview", "Desfaz tudo e desinstala, ou restaura de fábrica.", "Tirar o Midview..."),
    };

    public static string ToolTip(DeviceRoomAction action, AndroidDeviceKind kind)
    {
        var words = DeviceSetupText.Words(kind);
        var line = action switch
        {
            DeviceRoomAction.Optimize => $"Esconde os apps de propaganda dos {words} e liga o DNS sem anúncios.",
            DeviceRoomAction.Pin => $"Põe ou tira o PIN de 4 números da tela de bloqueio dos {words}; o aluno não consegue trocá-lo.",
            DeviceRoomAction.Network => $"Libera por alguns minutos o Wi-Fi e o modo avião, que ficam travados nos {words}, ou trava de novo.",
            DeviceRoomAction.Wallpaper => $"Põe uma imagem como papel de parede da tela inicial e da de bloqueio dos {words}; o aluno não consegue trocá-la.",
            DeviceRoomAction.Find => PhoneFindText.StartToolTip,
            _ => $"Desfaz tudo o que o Midview fez nos {words} e o desinstala (fotos e arquivos ficam), ou os restaura de fábrica (apaga tudo). Pede confirmação.",
        };
        return line + $" Com alguns {words} selecionados na tela, só neles.";
    }
}

// The lower part of a "Configurar" page: the room's devices of that kind,
// with one card per action in two columns (one at the window's narrowest).
// Built in code so it holds without the app's resources (its styles are
// resource references).
public sealed class DeviceRoomSection : StackPanel
{
    // Below this width the cards stand in one column.
    private const double TwoColumnsWidth = 520;

    private readonly TextBlock _heading = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _none = new() { FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
    private readonly UniformGrid _cards = new() { Columns = 2, Margin = new Thickness(-5) };
    private readonly Dictionary<DeviceRoomAction, (TextBlock Line, Button Button)> _parts = new();

    public DeviceRoomSection()
    {
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        _heading.Margin = new Thickness(0, 22, 0, 10);
        _none.SetResourceReference(StyleProperty, "Hint");
        Children.Add(_heading);
        Children.Add(_none);
        Children.Add(_cards);
        foreach (var action in Enum.GetValues<DeviceRoomAction>())
        {
            _cards.Children.Add(Card(action));
        }
        SizeChanged += (_, e) => _cards.Columns = e.NewSize.Width >= TwoColumnsWidth ? 2 : 1;
    }

    // The action asked, on the devices DeviceSupport.RoomTargets gives.
    public event Action<DeviceRoomAction>? Act;

    public AndroidDeviceKind Kind { get; private set; }

    // count: the devices of that kind in the room; ringing: whether all the
    // ones the cards reach still ring ("Parar o toque", as the tile menu says).
    public void Show(AndroidDeviceKind kind, int count, bool ringing)
    {
        Kind = kind;
        _heading.Text = DeviceRoomText.RoomSection(kind, count);
        _none.Text = DeviceRoomText.NoneInRoom(kind);
        _none.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (action, (line, button)) in _parts)
        {
            var (_, text, label) = DeviceRoomText.Card(action, kind);
            line.Text = text;
            button.Content = action == DeviceRoomAction.Find && ringing ? PhoneFindText.StopMenuItem : label;
            button.ToolTip = action == DeviceRoomAction.Find && ringing ? PhoneFindText.StopToolTip : DeviceRoomText.ToolTip(action, kind);
            button.IsEnabled = count > 0;
        }
    }

    // The button of an action (the tests click it).
    public Button ButtonOf(DeviceRoomAction action) => _parts[action].Button;

    private Border Card(DeviceRoomAction action)
    {
        var (title, line, label) = DeviceRoomText.Card(action, Kind);
        var titleBlock = new TextBlock { Text = title };
        titleBlock.SetResourceReference(StyleProperty, "CardTitle");
        var lineBlock = new TextBlock { Text = line, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) };
        lineBlock.SetResourceReference(StyleProperty, "Hint");
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 10, 0, 0),
        };
        if (action == DeviceRoomAction.Remove)
        {
            button.SetResourceReference(StyleProperty, "DangerButton");
        }
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += (_, _) => Act?.Invoke(action);
        _parts[action] = (lineBlock, button);
        DockPanel.SetDock(button, Dock.Bottom);
        var text = new StackPanel();
        text.Children.Add(titleBlock);
        text.Children.Add(lineBlock);
        var dock = new DockPanel();
        dock.Children.Add(button);
        dock.Children.Add(text);
        var card = new Border { Padding = new Thickness(14), Margin = new Thickness(5), Child = dock };
        card.SetResourceReference(StyleProperty, "Card");
        return card;
    }
}
