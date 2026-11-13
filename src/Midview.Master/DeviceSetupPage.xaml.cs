using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Midview.Master;

// The texts of "Configurar celular" and "Configurar tablet": the same page,
// with "celular" or "tablet" in it.
internal static class DeviceSetupText
{
    public static string Word(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "tablet" : "celular";

    private static string Capital(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "Tablet" : "Celular";

    public static string Heading(AndroidDeviceKind kind) => "Configurar " + Word(kind);

    public static string Icon(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "" : "";

    public static string Intro(AndroidDeviceKind kind) =>
        $"Prepare cada {Word(kind)} da escola pelo cabo USB, um de cada vez. Leva cerca de 1 minuto por {Word(kind)}.";

    public static IReadOnlyList<string> BeforeStarting(AndroidDeviceKind kind) => new[]
    {
        $"1. {Capital(kind)} restaurado de fábrica, sem conta Google.",
        "2. Nas Opções do desenvolvedor: Depuração USB e Instalar via USB ligadas.",
        "3. Ligue o cabo neste computador.",
    };

    // The count is PhoneCable's own list, so the page never says a number of
    // apps the cable does not remove.
    public static string Optimized(AndroidDeviceKind kind) =>
        $"Instala o Midview e deixa o {Word(kind)} mais leve: tira {PhoneCable.RemovedApps.Count} apps que a turma não usa " +
        "(testes de fábrica, propaganda e coleta de dados, App Google), desliga as animações e segura os apps pesados em segundo plano.";

    // Measured on the school's Redmi 10C on 2026-11-13.
    public const string Gain = "Ganho no Redmi 10C: cerca de 460 MB a mais de memória livre, menos travadas e menos bateria.";

    public static string OnlyMidview(AndroidDeviceKind kind) =>
        $"Instala o Midview e não mexe no resto do {Word(kind)}. " +
        $"Use se a turma precisa de algum app da lista ou se quer o {Word(kind)} como veio de fábrica.";

    public static string Kept(AndroidDeviceKind kind) =>
        "Câmera, galeria, calculadora, arquivos, gravador de tela, WPS e Chrome ficam. " +
        $"Os apps tirados voltam se o {Word(kind)} for restaurado de fábrica.";

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
        Header.Content = DeviceSetupText.Intro(kind);
        var before = DeviceSetupText.BeforeStarting(kind);
        ResetLine.Text = before[0];
        SwitchesLine.Text = before[1];
        CableLine.Text = before[2];
        OptimizedText.Text = DeviceSetupText.Optimized(kind);
        GainText.Text = DeviceSetupText.Gain;
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
