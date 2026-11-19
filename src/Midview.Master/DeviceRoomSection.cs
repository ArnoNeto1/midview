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
    Accounts,
    Wallpaper,
    ClearChrome,
    Find,
    Debugging,
    // Only for the devices picked from a tile's menu ("Configurar tablet..."),
    // not for the whole room: DeviceRoomSection shows them with a focus.
    Update,
    Wipe,
    Remove,
}

// The devices a "Configurar" page acts on when opened from a tile's menu
// ("Configurar tablet..."): its heading ("TABLET 04") and whether "Apagar e
// preparar de novo" works on them (one school device that answers now).
public sealed record DeviceFocus(string Heading, bool CanWipe);

// The texts of the room's section of "Configurar celular" and "Configurar tablet".
internal static class DeviceRoomText
{
    public static string NewSection(AndroidDeviceKind kind) =>
        kind == AndroidDeviceKind.Tablet ? "PREPARAR UM TABLET NOVO" : "PREPARAR UM CELULAR NOVO";

    // The count is every device of that kind in the room, which the cards
    // act on (DeviceSupport.RoomTargets).
    public static string RoomSection(AndroidDeviceKind kind, int count) =>
        $"{DeviceSetupText.Words(kind).ToUpperInvariant()} DA SALA ({count})";

    public static string NoneInRoom(AndroidDeviceKind kind) => $"Nenhum {DeviceSetupText.Word(kind)} na sala ainda.";

    // The heading of the cards when they act on the devices picked from a
    // tile's menu: "TABLET 04", or "3 TABLETS: TABLET 04, TABLET 12, TABLET 16".
    public static string FocusSection(AndroidDeviceKind kind, IReadOnlyList<string> names) =>
        (names.Count == 1 ? names[0] : $"{names.Count} {DeviceSetupText.Words(kind)}: {string.Join(", ", names)}").ToUpperInvariant();

    // Over the page: whom it acts on, and the way back to the whole room.
    public static string FocusLine(IReadOnlyList<string> names) =>
        names.Count == 1 ? $"Só no {names[0]}." : $"Só nestes {names.Count}: {string.Join(", ", names)}.";

    public static string AllOfTheRoom(AndroidDeviceKind kind) => $"Ver todos os {DeviceSetupText.Words(kind)} da sala";

    // The menu item: "Configurar tablet...", or "Configurar 3 tablets...".
    public static string MenuItem(AndroidDeviceKind kind, int count) =>
        count > 1 ? $"Configurar {count} {DeviceSetupText.Words(kind)}..." : $"Configurar {DeviceSetupText.Word(kind)}...";

    public const string WipeOneOnly =
        "Só para um aparelho da escola de cada vez, ligado agora e com o assistente Preparar pronto.";

    public static string FindLine(AndroidDeviceKind kind) => $"Toca alto para achar um {DeviceSetupText.Word(kind)} perdido.";

    public const string FindButton = "Tocar...";

    public static string DebuggingLine(AndroidDeviceKind kind) =>
        $"Travada: o aluno não liga a depuração. Libere por um tempo para conferir o {DeviceSetupText.Word(kind)} pelo cabo; ela liga sozinha e desliga no fim.";

    // Each card: its title, its line and its button.
    public static (string Title, string Line, string Button) Card(DeviceRoomAction action, AndroidDeviceKind kind) => action switch
    {
        DeviceRoomAction.Optimize => ("Otimizar", "Tira apps que a turma não usa, arruma a tela inicial e liga o DNS sem anúncios.", "Otimizar..."),
        DeviceRoomAction.Pin => ("Senha (PIN)", "PIN de 4 números que o aluno não troca.", "Definir PIN..."),
        DeviceRoomAction.Network => ("Wi-Fi e modo avião", "Travados. Libere por um tempo para trocar de rede.", "Liberar..."),
        DeviceRoomAction.Accounts => (
            "Contas do aparelho",
            "Travadas: o aluno não põe nem tira contas. Libere por um tempo para pôr a conta Google da escola.",
            "Liberar..."),
        DeviceRoomAction.ClearChrome => (
            "Sair das contas do Chrome",
            "Apaga os logins de sites, os cookies, o histórico e as abas do Chrome. O próximo aluno abre o Chrome limpo.",
            "Sair das contas..."),
        DeviceRoomAction.Wallpaper => ("Papel de parede", "Põe a imagem da escola na tela inicial e de bloqueio.", "Escolher imagem..."),
        DeviceRoomAction.Find => ("Tocar para encontrar", FindLine(kind), FindButton),
        DeviceRoomAction.Debugging => ("Depuração USB", DebuggingLine(kind), "Liberar..."),
        DeviceRoomAction.Update => ("Atualizar o Midview", $"Põe no {DeviceSetupText.Word(kind)} a versão nova do Midview, se houver.", "Atualizar..."),
        DeviceRoomAction.Wipe => (
            "Apagar e preparar de novo",
            $"Restaura o {DeviceSetupText.Word(kind)} de fábrica (apaga tudo) e abre o Preparar para pôr o Midview de novo pelo cabo.",
            "Apagar..."),
        _ => ("Tirar o Midview", "Desfaz tudo e desinstala, ou restaura de fábrica.", "Tirar o Midview..."),
    };

    // focused: the cards act on the devices picked from a tile's menu, so
    // the way to reach one alone is not said again.
    public static string ToolTip(DeviceRoomAction action, AndroidDeviceKind kind, bool focused = false)
    {
        var words = DeviceSetupText.Words(kind);
        var line = action switch
        {
            DeviceRoomAction.Optimize =>
                $"Tira dos {words} os apps que a turma não usa (os mesmos da preparação pelo cabo), arruma a tela inicial do Android " +
                "(com a tela acesa) e liga o DNS sem anúncios.",
            DeviceRoomAction.Pin => $"Põe ou tira o PIN de 4 números da tela de bloqueio dos {words}; o aluno não consegue trocá-lo.",
            DeviceRoomAction.Network => $"Libera por alguns minutos o Wi-Fi e o modo avião, que ficam travados nos {words}, ou trava de novo.",
            DeviceRoomAction.Accounts =>
                $"Libera por alguns minutos as contas dos {words}, que ficam travadas, para pôr a conta Google da escola em Configurações > Contas; ou trava de novo.",
            DeviceRoomAction.ClearChrome =>
                $"No fim da aula: tira os alunos das contas dos sites no Chrome dos {words}, apagando logins, cookies, histórico e abas. Pede confirmação.",
            DeviceRoomAction.Wallpaper => $"Põe uma imagem como papel de parede da tela inicial e da de bloqueio dos {words}; o aluno não consegue trocá-la.",
            DeviceRoomAction.Find => PhoneFindText.StartToolTip,
            DeviceRoomAction.Debugging =>
                $"Libera por alguns minutos a depuração USB dos {words}, que fica travada, e a liga, para conferi-los pelo cabo; no fim ela desliga e trava de novo. Ou trava agora.",
            DeviceRoomAction.Update => $"Manda a versão nova do Midview, baixada do GitHub, para os {words}.",
            DeviceRoomAction.Wipe =>
                $"Apaga tudo e restaura de fábrica, depois abre o Preparar no mesmo número. Pede confirmação. {WipeOneOnly}",
            _ => $"Desfaz tudo o que o Midview fez nos {words} e o desinstala (fotos e arquivos ficam), ou os restaura de fábrica (apaga tudo). Pede confirmação.",
        };
        return focused ? line : line + $" Para um {DeviceSetupText.Word(kind)} só, use o botão direito na tela dele.";
    }
}

// The lower part of a "Configurar" page: the room's devices of that kind,
// with one card per action in two columns (one at the window's narrowest),
// two by two: Otimizar and PIN, the Wi-Fi's and the accounts' locks, the
// wallpaper and Chrome's logins, ringing and USB debugging (both to reach a
// device in hand), and taking the Midview off alone at the end. Opened from
// a tile's menu (DeviceFocus), it also has "Atualizar o Midview" and
// "Apagar e preparar de novo", which are for the devices picked there.
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
    private readonly Dictionary<DeviceRoomAction, Border> _cardsOf = new();

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

    // count: the devices of that kind in the room, which the cards reach
    // (DeviceSupport.RoomTargets); ringing: whether all of them still ring
    // ("Parar o toque", as the tile menu says).
    // focus: the devices picked from a tile's menu, or null for the room.
    public void Show(AndroidDeviceKind kind, int count, bool ringing, DeviceFocus? focus = null)
    {
        Kind = kind;
        _heading.Text = focus?.Heading ?? DeviceRoomText.RoomSection(kind, count);
        _none.Text = DeviceRoomText.NoneInRoom(kind);
        _none.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (action, (line, button)) in _parts)
        {
            var (_, text, label) = DeviceRoomText.Card(action, kind);
            line.Text = text;
            button.Content = action == DeviceRoomAction.Find && ringing ? PhoneFindText.StopMenuItem : label;
            button.ToolTip = action == DeviceRoomAction.Find && ringing
                ? PhoneFindText.StopToolTip
                : DeviceRoomText.ToolTip(action, kind, focused: focus is not null);
            button.IsEnabled = count > 0 && (action != DeviceRoomAction.Wipe || focus is { CanWipe: true });
            _cardsOf[action].Visibility = focus is null && action is DeviceRoomAction.Update or DeviceRoomAction.Wipe
                ? Visibility.Collapsed
                : Visibility.Visible;
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
        if (action is DeviceRoomAction.Remove or DeviceRoomAction.Wipe)
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
        _cardsOf[action] = card;
        return card;
    }
}
