using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Midview.Master;

// The texts of "Configurar celular" and "Configurar tablet": the same page,
// with "celular" or "tablet" in it.
internal static class DeviceSetupText
{
    public static string Word(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "tablet" : "celular";

    public static string Words(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "tablets" : "celulares";

    private static string Capital(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "Tablet" : "Celular";

    public static string Heading(AndroidDeviceKind kind) => "Configurar " + Word(kind);

    public static string Icon(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "" : "";

    public static string Subtitle(AndroidDeviceKind kind) =>
        $"Prepare {Words(kind)} novos e cuide dos {Words(kind)} da sala.";

    public static string Intro(AndroidDeviceKind kind) =>
        $"Prepare cada {Word(kind)} da escola pelo cabo USB, um de cada vez. Leva cerca de 1 minuto por {Word(kind)}.";

    public static IReadOnlyList<string> BeforeStarting(AndroidDeviceKind kind) => new[]
    {
        $"1. {Capital(kind)} sem senha de tela e sem conta Google. Não precisa restaurar de fábrica: " +
        "restaure só se ele tem uma conta que ninguém sabe a senha, ou se já teve senha de tela e a escola vai usar o PIN do professor.",
        "2. Nas Opções do desenvolvedor: Depuração USB e Instalar via USB ligadas.",
        "3. Ligue o cabo neste computador.",
    };

    // The count is PhoneCable's own list, so the page never says a number of
    // apps the cable does not remove.
    public static string Optimized(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet
        ? $"Instala o Midview e deixa o tablet mais leve: tira até {PhoneCable.RemovedApps.Count + PhoneCable.TabletRemovedApps.Count} apps " +
          "que a turma não usa (testes de fábrica, propaganda e coleta de dados, App Google, telefone, estilos do sistema), " +
          "desliga as animações, segura os apps pesados em segundo plano e arruma a tela inicial."
        : $"Instala o Midview e deixa o {Word(kind)} mais leve: tira {PhoneCable.RemovedApps.Count} apps que a turma não usa " +
          "(testes de fábrica, propaganda e coleta de dados, App Google), desliga as animações, segura os apps pesados " +
          "em segundo plano e arruma a tela inicial.";

    // Measured on the school's Redmi 10C and Multilaser M10 on 2026-11-13.
    public static string Gain(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet
        ? "Ganho no tablet Multilaser M10: a tela inicial trava quatro vezes menos (de 20% para 5% dos quadros)."
        : "Ganho no Redmi 10C: cerca de 460 MB a mais de memória livre, menos travadas e menos bateria.";

    public static string OnlyMidview(AndroidDeviceKind kind) =>
        $"Instala o Midview e não mexe no resto do {Word(kind)}. " +
        $"Use se a turma precisa de algum app da lista ou se quer o {Word(kind)} como veio de fábrica.";

    public static string Kept(AndroidDeviceKind kind) =>
        (kind == AndroidDeviceKind.Tablet
            ? "Câmera, Fotos, Files, calculadora, relógio, gravador e Chrome ficam. "
            : "Câmera, galeria, calculadora, arquivos, gravador de tela, WPS e Chrome ficam. ") +
        $"Os apps tirados voltam se o {Word(kind)} for restaurado de fábrica.";

    // The school's Mi account line: where it is saved and changed (the
    // assistant), and that it can be taken off this PC here.
    public const string MiAccountNone =
        "Conta Mi da escola (para os aparelhos Xiaomi): nenhuma guardada. O assistente pede no passo 4 e guarda só neste computador.";

    public static string MiAccountSaved(string login) =>
        $"Conta Mi da escola: {login}, guardada só neste computador. Para trocar, use \"trocar\" no passo 4 do assistente. ";

    // Why the buttons are off; null when they work (the assistant is out).
    public static string? Gate(AndroidDeviceKind kind, PhoneSetupOffer setup) => setup switch
    {
        PhoneSetupOffer.Soon =>
            "O app do celular ainda não foi publicado: os botões funcionam a partir da próxima versão do Midview.",
        PhoneSetupOffer.Cable =>
            $"O assistente ainda não saiu: por enquanto, o {Word(kind)} é preparado pelo cabo USB seguindo os passos do README.",
        _ => null,
    };
}

// One "Configurar" page of "Máquinas da sala". Its two buttons open the
// "Preparar celulares" assistant (Prepare: the kind and whether to lighten
// the device); they work only with the assistant out (PhoneSetupOffer.Wizard),
// otherwise the page still explains the two ways and says why they are off.
// Below, the room's devices of that kind (RoomSection: optimize, PIN, Wi-Fi,
// wallpaper, ring, take the Midview off), whatever the gate.
public partial class DeviceSetupPage : UserControl
{
    // Below this width the two ways stand one under the other.
    private const double SideBySideWidth = 520;

    private AndroidDeviceKind _kind;

    public DeviceSetupPage()
    {
        InitializeComponent();
    }

    // The kind and "Midview otimizado" (true) or "Só o Midview" (false).
    public event Action<AndroidDeviceKind, bool>? Prepare;

    // "Como preparar pelo cabo USB...", while only the README's steps are out.
    public event Action? CableSteps;

    public void Show(AndroidDeviceKind kind, PhoneSetupOffer setup)
    {
        _kind = kind;
        Header.Icon = DeviceSetupText.Icon(kind);
        Header.Heading = DeviceSetupText.Heading(kind);
        Header.Content = DeviceSetupText.Subtitle(kind);
        NewSectionHeading.Text = DeviceRoomText.NewSection(kind);
        IntroLine.Text = DeviceSetupText.Intro(kind);
        var before = DeviceSetupText.BeforeStarting(kind);
        ResetLine.Text = before[0];
        SwitchesLine.Text = before[1];
        CableLine.Text = before[2];
        OptimizedText.Text = DeviceSetupText.Optimized(kind);
        GainText.Text = DeviceSetupText.Gain(kind);
        OnlyMidviewText.Text = DeviceSetupText.OnlyMidview(kind);
        KeptText.Text = DeviceSetupText.Kept(kind);

        var works = setup == PhoneSetupOffer.Wizard;
        OptimizedButton.IsEnabled = works;
        OnlyMidviewButton.IsEnabled = works;
        GateText.Inlines.Clear();
        if (DeviceSetupText.Gate(kind, setup) is { } gate)
        {
            GateText.Inlines.Add(new Run(gate));
            if (setup == PhoneSetupOffer.Cable)
            {
                GateText.Inlines.Add(new Run(" "));
                var link = new Hyperlink(new Run("Como preparar pelo cabo USB..."))
                {
                    ToolTip = "Abre no navegador os passos do README do Midview: o aparelho é preparado pelo cabo USB, ligado a este computador.",
                };
                link.Click += (_, _) => CableSteps?.Invoke();
                GateText.Inlines.Add(link);
            }
        }
        GateText.Visibility = works ? Visibility.Collapsed : Visibility.Visible;
        ShowMiAccount();
    }

    // Read again each time the page shows: the assistant may have saved it meanwhile.
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ShowMiAccount();
        }
    }

    private void ShowMiAccount()
    {
        MiAccountLine.Inlines.Clear();
        if (MiAccountFile.Load() is not { } account)
        {
            MiAccountLine.Inlines.Add(new Run(DeviceSetupText.MiAccountNone));
            return;
        }
        MiAccountLine.Inlines.Add(new Run(DeviceSetupText.MiAccountSaved(account.Login)));
        var remove = new Hyperlink(new Run("Tirar deste computador"));
        remove.Click += (_, _) => RemoveMiAccount();
        MiAccountLine.Inlines.Add(remove);
    }

    private void RemoveMiAccount()
    {
        var answer = MidviewMessage.Show(
            Window.GetWindow(this),
            "Tirar a conta Mi da escola deste computador? Os aparelhos já preparados não mudam. " +
            "Sem ela, o Midview não sai sozinho da conta Mi: em cada aparelho, você sai à mão.",
            Header.Heading, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            MiAccountFile.Clear();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Left as it was: the line still shows it.
        }
        ShowMiAccount();
    }

    private void OnOptimizedClicked(object sender, RoutedEventArgs e) => Prepare?.Invoke(_kind, true);

    private void OnOnlyMidviewClicked(object sender, RoutedEventArgs e) => Prepare?.Invoke(_kind, false);

    // Side by side when there is room for both texts; one under the other
    // at the window's narrowest.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var sideBySide = e.NewSize.Width >= SideBySideWidth;
        ChoicesGap.Width = new GridLength(sideBySide ? 12 : 0);
        SecondColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumnSpan(OptimizedCard, sideBySide ? 1 : 3);
        Grid.SetRow(OnlyMidviewCard, sideBySide ? 0 : 1);
        Grid.SetColumn(OnlyMidviewCard, sideBySide ? 2 : 0);
        Grid.SetColumnSpan(OnlyMidviewCard, sideBySide ? 1 : 3);
        OnlyMidviewCard.Margin = new Thickness(0, sideBySide ? 0 : 12, 0, 0);
    }
}
