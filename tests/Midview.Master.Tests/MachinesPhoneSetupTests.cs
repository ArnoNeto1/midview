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
            var span = QrSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Soon, () => { });

            Assert.Equal("  Celulares: em breve.", Text(span));
            Assert.Empty(span.Inlines.OfType<Hyperlink>());
        });
    }

    [Fact]
    public void Cable_points_to_the_readme_steps_instead_of_the_qr()
    {
        InSta(() =>
        {
            var span = QrSpan();
            var opened = 0;

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Cable, () => opened++);

            Assert.Equal("  Celular novo? Como preparar pelo cabo USB...", Text(span));
            var link = Assert.Single(span.Inlines.OfType<Hyperlink>());
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Assert.Equal(1, opened);
        });
    }

    [Fact]
    public void Qr_keeps_the_preparar_celular_link()
    {
        InSta(() =>
        {
            var span = QrSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneSetupOffer.Qr, () => { });

            Assert.Equal("  Celular novo? Preparar celular...", Text(span));
        });
    }

    [Fact]
    public void This_build_never_shows_the_qr_link()
    {
        InSta(() =>
        {
            var span = QrSpan();

            MachinesWindow.ShowPhoneSetup(span, PhoneProvisioning.Setup, () => { });

            Assert.DoesNotContain("Preparar celular...", Text(span));
        });
    }

    // As in MachinesWindow.xaml.
    private static Span QrSpan()
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
}
