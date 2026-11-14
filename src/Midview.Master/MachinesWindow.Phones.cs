using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Midview.Master;

// What "Configurar celular" and "Configurar tablet" do to the room's devices,
// through the classroom (MainWindow): Selected is the grid's selection, Act
// opens the action's window on the devices, Ringing says whether all of
// them still ring.
public sealed record DeviceRoomCallbacks(
    Func<IReadOnlyList<KnownMachine>> Selected,
    Action<DeviceRoomAction, IReadOnlyList<KnownMachine>> Act,
    Func<IReadOnlyList<KnownMachine>, bool> Ringing);

// The phones and tablets in "Máquinas da sala": the line under the search
// list and the "Configurar celular" and "Configurar tablet" pages
// (DeviceSetupPage), whose upper buttons open the "Preparar celulares"
// assistant and whose cards act on the room's devices of that kind (the
// selected ones of that kind, or all: DeviceSupport.RoomTargets).
public partial class MachinesWindow
{
    // What the line under the list says about phones (PhoneSetupOffer): "em
    // breve" while the phone app is not published (no link to a factory reset
    // that would be for nothing); the README's cable steps once it is, while
    // the assistant is not out; the XAML's "Preparar celular..." link (the
    // "Configurar celular" page) only with PhoneProvisioning.WizardReady.
    internal static void ShowPhoneSetup(Span span, PhoneSetupOffer setup, Action openCableSteps)
    {
        if (setup == PhoneSetupOffer.Wizard)
        {
            return;
        }
        span.Inlines.Clear();
        if (setup == PhoneSetupOffer.Soon)
        {
            span.Inlines.Add(new Run("  Celulares: em breve."));
            return;
        }
        span.Inlines.Add(new Run("  Celular novo?"));
        span.Inlines.Add(new Run(" "));
        var link = new Hyperlink(new Run("Como preparar pelo cabo USB..."))
        {
            ToolTip = "Abre no navegador os passos do README do Midview: o celular é preparado pelo cabo USB, ligado a este computador.",
        };
        link.Click += (_, _) => openCableSteps();
        span.Inlines.Add(link);
    }

    // Both pages are there whatever the gate: with the assistant not out,
    // they still explain the two ways, with the buttons off and why.
    private void ShowDeviceSetupPages(PhoneSetupOffer setup)
    {
        foreach (var (page, kind) in new[] { (PhoneSetupPage, AndroidDeviceKind.Phone), (TabletSetupPage, AndroidDeviceKind.Tablet) })
        {
            page.Show(kind, setup);
            page.Prepare += (asked, optimize) => _openPreparePhones(asked, optimize);
            page.CableSteps += OpenCableSteps;
            page.RoomSection.Act += action => _roomDevices.Act(action, RoomTargets(kind));
            page.IsVisibleChanged += (_, _) => ShowRoomDevices();
        }
        Activated += (_, _) => ShowRoomDevices();
        ShowRoomDevices();
    }

    private IReadOnlyList<KnownMachine> RoomTargets(AndroidDeviceKind kind) =>
        DeviceSupport.RoomTargets(kind, _roomDevices.Selected(), _roster());

    // The count of each kind and "Tocar..." or "Parar o toque". The classroom
    // calls it again when its list changes and when a ring starts or stops.
    public void ShowRoomDevices()
    {
        var room = _roster();
        foreach (var (page, kind) in new[] { (PhoneSetupPage, AndroidDeviceKind.Phone), (TabletSetupPage, AndroidDeviceKind.Tablet) })
        {
            page.RoomSection.Show(kind, room.Count(machine => DeviceSupport.IsAndroidOf(machine, kind)), _roomDevices.Ringing(RoomTargets(kind)));
        }
    }

    private void OpenCableSteps()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PhoneProvisioning.CableStepsUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MidviewMessage.Show(this, $"Não foi possível abrir o navegador. Os passos estão em {PhoneProvisioning.CableStepsUrl}",
                "Preparar celular", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // "Preparar celular..." under the search list: the "Configurar celular"
    // page, where the teacher picks how the phone is prepared.
    private void OnPreparePhoneClicked(object sender, RoutedEventArgs e) => ShowPage(MachinesPage.PhoneSetup);
}
