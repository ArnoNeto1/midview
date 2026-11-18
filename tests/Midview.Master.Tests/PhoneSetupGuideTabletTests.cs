using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Midview.Master;
using Xunit;

public class PhoneSetupGuideTabletTests
{
    private static IEnumerable<string> Texts(GuideStep step) =>
        new[] { step.Title, step.NextText, step.Timing ?? "" }
            .Concat(step.Taps)
            .Concat(step.Help.SelectMany(Texts));

    private static IEnumerable<string> Texts(GuideHelp help) =>
        new[] { help.Problem, help.Answer }.Concat((help.Links ?? Array.Empty<GuideHelp>()).SelectMany(Texts));

    [Fact]
    public void A_tablet_has_the_phones_steps_in_the_same_order()
    {
        Assert.Equal(
            PhoneSetupGuide.Steps.Select(step => step.Id),
            PhoneSetupGuide.StepsFor(AndroidDeviceKind.Tablet).Select(step => step.Id));
        Assert.Same(PhoneSetupGuide.Steps, PhoneSetupGuide.StepsFor(AndroidDeviceKind.Phone));
    }

    // Nothing about a phone, a Xiaomi or its chip shows to a teacher preparing a tablet,
    // and no print of the Redmi's MIUI (only the tablet's own).
    [Fact]
    public void A_tablets_steps_never_speak_of_a_phone_a_xiaomi_or_a_chip()
    {
        var phoneWords = new Regex(@"\b(celular|celulares|MIUI|Xiaomi|chip|Conta Mi|conta Mi)\b", RegexOptions.IgnoreCase);
        foreach (var step in PhoneSetupGuide.StepsFor(AndroidDeviceKind.Tablet))
        {
            Assert.True(step.Image is null || step.Image.StartsWith("Assets/PrepararTablet/", StringComparison.Ordinal), step.Id);
            foreach (var text in Texts(step))
            {
                Assert.False(phoneWords.IsMatch(text), $"{step.Id}: {text}");
            }
        }
        foreach (var text in PhoneSetupGuide.GatherFor(AndroidDeviceKind.Tablet).Items
                     .Concat(Texts(PhoneSetupGuide.LockedWithPinFor(AndroidDeviceKind.Tablet))))
        {
            Assert.False(phoneWords.IsMatch(text), text);
        }
    }

    [Fact]
    public void A_tablet_turns_on_usb_debugging_and_leaves_no_screen_lock()
    {
        var steps = PhoneSetupGuide.StepsFor(AndroidDeviceKind.Tablet).ToDictionary(step => step.Id);
        Assert.Equal("Ligue a Depuração USB", steps["chaves"].Title);
        Assert.Equal("Confira que o tablet está sem senha de tela", steps["chip-conta-mi"].Title);
        Assert.Contains(steps["desenvolvedor"].Taps, tap => tap.Contains("Número da versão", StringComparison.Ordinal));
    }

    // The state's tablets come with the Secretaria de Educação's account,
    // whose management locks the developer options and keeps the account on
    // the tablet: only a factory reset from Settings frees them.
    [Fact]
    public void A_tablet_of_the_secretaria_is_restored_first()
    {
        var first = DeviceSetupText.BeforeStarting(AndroidDeviceKind.Tablet)[0];
        Assert.StartsWith("1. Tablet sem senha de tela e sem conta Google. Tablet com a conta da Secretaria de Educação (SED) precisa ser restaurado de fábrica antes", first);
        Assert.DoesNotContain("Não precisa restaurar", first, StringComparison.Ordinal);
        Assert.DoesNotContain("SED", DeviceSetupText.BeforeStarting(AndroidDeviceKind.Phone)[0], StringComparison.Ordinal);

        var developer = PhoneSetupGuide.StepsFor(AndroidDeviceKind.Tablet).Single(step => step.Id == "desenvolvedor");
        var locked = Assert.Single(developer.Help, help => help.Problem.Contains("bloqueado", StringComparison.Ordinal));
        Assert.Contains("Secretaria de Educação (SED)", locked.Answer, StringComparison.Ordinal);
        Assert.Contains("Limpar todos os dados", locked.Answer, StringComparison.Ordinal);
        Assert.Contains("pule o Wi-Fi e a conta Google", locked.Answer, StringComparison.Ordinal);
    }

    // "Instalar via USB" is MIUI's own switch: a tablet's page doesn't ask for it.
    [Fact]
    public void The_tablet_page_asks_only_for_usb_debugging()
    {
        Assert.DoesNotContain("Instalar via USB", DeviceSetupText.BeforeStarting(AndroidDeviceKind.Tablet)[1], StringComparison.Ordinal);
        Assert.Contains("Instalar via USB", DeviceSetupText.BeforeStarting(AndroidDeviceKind.Phone)[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ligue o celular no computador", "Ligue o tablet no computador")]
    [InlineData("Quantos celulares a escola tem?", "Quantos tablets a escola tem?")]
    [InlineData("Ver \"Celular travado com PIN\"", "Ver \"Tablet travado com PIN\"")]
    [InlineData("Sem a palavra", "Sem a palavra")]
    public void A_phones_text_reads_in_a_tablets_words(string phone, string tablet)
    {
        Assert.Equal(tablet, PhoneSetupGuide.ForKind(phone, AndroidDeviceKind.Tablet));
        Assert.Equal(phone, PhoneSetupGuide.ForKind(phone, AndroidDeviceKind.Phone));
    }

    // The window keeps its texts in the words of the device it prepares: the
    // ones there at first, the ones set later, and back when it turns to a phone.
    [Fact]
    public void The_window_follows_the_devices_words()
    {
        InSta(() =>
        {
            var kind = AndroidDeviceKind.Tablet;
            var status = new TextBlock { Text = "O celular está no cabo." };
            var link = new TextBlock();
            link.Inlines.Add(new Run("Ver "));
            link.Inlines.Add(new Hyperlink(new Run("Celular travado com PIN")));
            var button = new Button { Content = "Preparar outro celular" };
            var input = new TextBox { Text = "rede do celular" };
            // The teacher's own data (a Wi-Fi name, a login) stays as typed.
            var ssid = new TextBlock { Text = "Celulares-Lab", Tag = PhoneSetupGuide.AsIs };
            var line = new TextBlock();
            line.Inlines.Add(new Run("Wi-Fi do celular: "));
            line.Inlines.Add(new Run("Tablet2024") { Tag = PhoneSetupGuide.AsIs });
            var window = new Window
            {
                Width = 300, Height = 200, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                Left = -10000, Top = -10000,
                Content = new StackPanel { Children = { status, link, button, input, ssid, line } },
            };
            PhoneSetupGuide.FollowDeviceWords(window, () => kind);
            window.Show();
            window.UpdateLayout(); Settle(window);

            Assert.Equal("O tablet está no cabo.", status.Text);
            Assert.Equal("Ver Tablet travado com PIN", string.Concat(link.Inlines.SelectMany(RunsOf).Select(run => run.Text)));
            Assert.Equal("Preparar outro tablet", button.Content);
            Assert.Equal("rede do celular", input.Text);
            Assert.Equal("Celulares-Lab", ssid.Text);
            Assert.Equal("Wi-Fi do tablet: Tablet2024", string.Concat(line.Inlines.SelectMany(RunsOf).Select(run => run.Text)));

            status.Text = "Há mais de um celular no cabo.";
            window.UpdateLayout(); Settle(window);
            Assert.Equal("Há mais de um tablet no cabo.", status.Text);

            kind = AndroidDeviceKind.Phone;
            window.InvalidateMeasure();
            window.UpdateLayout(); Settle(window);
            Assert.Equal("Há mais de um celular no cabo.", status.Text);
            Assert.Equal("Preparar outro celular", button.Content);
            window.Close();
        });
    }

    // Runs what the window queued after its layout (the follower's walk).
    private static void Settle(Window window) =>
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static IEnumerable<Run> RunsOf(Inline inline) => inline switch
    {
        Run run => new[] { run },
        Span span => span.Inlines.SelectMany(RunsOf),
        _ => Array.Empty<Run>(),
    };

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
