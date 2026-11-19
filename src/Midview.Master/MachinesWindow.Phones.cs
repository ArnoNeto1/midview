using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Midview.Master;

// What "Configurar celular" and "Configurar tablet" do to the room's devices,
// through the classroom (MainWindow): Act opens the action's window on the
// devices, Ringing says whether all of them still ring, CanWipe whether
// "Apagar e preparar de novo" works on a device now.
public sealed record DeviceRoomCallbacks(
    Action<DeviceRoomAction, IReadOnlyList<KnownMachine>> Act,
    Func<IReadOnlyList<KnownMachine>, bool> Ringing,
    Func<KnownMachine, bool>? CanWipe = null);

// The phones and tablets in "Máquinas da sala": the line under the search
// list and the "Configurar celular" and "Configurar tablet" pages
// (DeviceSetupPage), whose upper buttons open the "Preparar celulares"
// assistant and whose cards act on all the room's devices of that kind
// (DeviceSupport.RoomTargets), or on the ones picked from a tile's menu
// ("Configurar tablet...": ShowDevices) until the page is left.
public partial class MachinesWindow
{
    // The names of the devices each page was opened for from a tile's menu.
    private readonly Dictionary<AndroidDeviceKind, IReadOnlyList<string>> _focus = new();

    // "Configurar tablet..." of a tile's menu: that page, acting only on
    // these devices (all of the same kind).
    public void ShowDevices(AndroidDeviceKind kind, IReadOnlyList<KnownMachine> devices)
    {
        _focus[kind] = devices.Select(device => device.Name).ToList();
        ShowPage(kind == AndroidDeviceKind.Tablet ? MachinesPage.TabletSetup : MachinesPage.PhoneSetup);
        ShowRoomDevices();
        PageOf(kind).ScrollToTop();
    }

    private DeviceSetupPage PageOf(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? TabletSetupPage : PhoneSetupPage;

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
            page.RoomSection.Act += action =>
            {
                // The selection may have changed since the page was drawn.
                if (RoomTargets(kind) is { Count: > 0 } targets)
                {
                    _roomDevices.Act(action, targets);
                }
                else
                {
                    ShowRoomDevices();
                }
            };
            page.ShowAll += () =>
            {
                _focus.Remove(kind);
                ShowRoomDevices();
            };
            // Left (another page, or the window closed): back to the room next time.
            page.IsVisibleChanged += (_, _) =>
            {
                if (!page.IsVisible)
                {
                    _focus.Remove(kind);
                }
                ShowRoomDevices();
            };
        }
        Activated += (_, _) => ShowRoomDevices();
        ShowRoomDevices();
    }

    // The picked devices still in the room, or every one of that kind.
    private IReadOnlyList<KnownMachine> RoomTargets(AndroidDeviceKind kind)
    {
        var room = DeviceSupport.RoomTargets(kind, _roster());
        return _focus.TryGetValue(kind, out var names)
            ? room.Where(device => names.Contains(device.Name, StringComparer.OrdinalIgnoreCase)).ToList()
            : room;
    }

    // The count of each kind and "Tocar..." or "Parar o toque". The classroom
    // calls it again when its list changes and when a ring starts or stops.
    public void ShowRoomDevices()
    {
        var room = _roster();
        foreach (var (page, kind) in new[] { (PhoneSetupPage, AndroidDeviceKind.Phone), (TabletSetupPage, AndroidDeviceKind.Tablet) })
        {
            var targets = RoomTargets(kind);
            var focus = _focus.TryGetValue(kind, out var names)
                ? new DeviceFocus(
                    DeviceRoomText.FocusSection(kind, names),
                    targets is [var one] && (_roomDevices.CanWipe?.Invoke(one) ?? false))
                : null;
            page.ShowFocus(focus is null ? null : names);
            page.RoomSection.Show(kind, targets.Count, _roomDevices.Ringing(targets), focus);
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
