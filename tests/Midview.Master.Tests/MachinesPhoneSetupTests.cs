using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using Midview.Master;
using Xunit;

// The line under "Procurar e parear" about phones (MachinesWindow.ShowPhoneSetup).
public class MachinesPhoneSetupTests
{
    [Fact]
    public void Em_breve_while_the_phone_app_is_not_published()
    {
        InSta(() =>
        {
            var span = XamlSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Soon, () => { });

            Assert.Equal("  Celulares: em breve.", Text(span));
            Assert.Empty(span.Inlines.OfType<Hyperlink>());
        });
    }

    [Fact]
    public void Cable_points_to_the_readme_steps_while_the_assistant_is_not_out()
    {
        InSta(() =>
        {
            var span = XamlSpan();
            var opened = 0;

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Cable, () => opened++);

            Assert.Equal("  Celular novo? Como preparar pelo cabo USB...", Text(span));
            var link = Assert.Single(span.Inlines.OfType<Hyperlink>());
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Assert.Equal(1, opened);
        });
    }

    // The XAML's link opens the "Configurar celular" page.
    [Fact]
    public void Wizard_keeps_the_preparar_celular_link()
    {
        InSta(() =>
        {
            var span = XamlSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Wizard, () => { });

            Assert.Equal("  Celular novo? Preparar celular...", Text(span));
        });
    }

    [Fact]
    public void This_build_shows_the_preparar_celular_link_only_with_the_assistant_out()
    {
        InSta(() =>
        {
            var span = XamlSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneProvisioning.Setup, () => { });

            Assert.Equal(PhoneProvisioning.WizardReady, Text(span).Contains("Preparar celular..."));
        });
    }

    // As in MachinesWindow.xaml.
    private static Span XamlSpan()
    {
        var span = new Span();
        span.Inlines.Add(new Run("  Celular novo?"));
        span.Inlines.Add(new Run(" "));
        span.Inlines.Add(new Hyperlink(new Run("Preparar celular...")));
        return span;
    }

    private static string Text(Span span) => string.Concat(span.Inlines.Select(inline => inline switch
    {
        Run run => run.Text,
        Span inner => Text(inner),
        _ => string.Empty,
    }));

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

    // The side menu: the two "Configurar" pages right after "Procurar e parear".
    [Fact]
    public void Pages_are_in_the_side_menu_order()
    {
        Assert.Equal(
            new[]
            {
                MachinesPage.Search, MachinesPage.PhoneSetup, MachinesPage.TabletSetup, MachinesPage.Updates,
                MachinesPage.OtherTeacher, MachinesPage.AdminPassword, MachinesPage.Advanced,
            },
            Enum.GetValues<MachinesPage>());
        Assert.Equal(1, (int)MachinesPage.PhoneSetup);
    }

    // The count of apps said on the page is the cable's own list.
    [Fact]
    public void Optimized_text_counts_the_removed_apps()
    {
        Assert.Contains($"tira {PhoneCable.RemovedApps.Count} apps", DeviceSetupText.Optimized(AndroidDeviceKind.Phone));
        Assert.StartsWith("Instala o Midview e deixa o tablet mais leve", DeviceSetupText.Optimized(AndroidDeviceKind.Tablet));
        Assert.Contains($"tira até {PhoneCable.RemovedApps.Count + PhoneCable.TabletRemovedApps.Count} apps", DeviceSetupText.Optimized(AndroidDeviceKind.Tablet));
        Assert.Contains("Redmi 10C", DeviceSetupText.Gain(AndroidDeviceKind.Phone));
        Assert.Contains("Multilaser M10", DeviceSetupText.Gain(AndroidDeviceKind.Tablet));
    }

    [Theory]
    [InlineData(AndroidDeviceKind.Phone, "Configurar celular", "1. Celular restaurado de fábrica, sem conta Google.")]
    [InlineData(AndroidDeviceKind.Tablet, "Configurar tablet", "1. Tablet restaurado de fábrica, sem conta Google.")]
    public void Each_page_names_its_kind(AndroidDeviceKind kind, string heading, string firstLine)
    {
        Assert.Equal(heading, DeviceSetupText.Heading(kind));
        Assert.Equal(firstLine, DeviceSetupText.BeforeStarting(kind)[0]);
        Assert.Contains(kind == AndroidDeviceKind.Tablet ? "o tablet for restaurado" : "o celular for restaurado", DeviceSetupText.Kept(kind));
    }

    // The buttons work only with the assistant out; otherwise the page says why.
    [Fact]
    public void Gate_explains_the_buttons_off_until_the_assistant_is_out()
    {
        Assert.Equal("O app do celular ainda não foi publicado: os botões funcionam a partir da próxima versão do Midview.",
            DeviceSetupText.Gate(AndroidDeviceKind.Tablet, PhoneSetupOffer.Soon));
        Assert.Contains("README", DeviceSetupText.Gate(AndroidDeviceKind.Phone, PhoneSetupOffer.Cable));
        Assert.Null(DeviceSetupText.Gate(AndroidDeviceKind.Phone, PhoneSetupOffer.Wizard));
    }
}
