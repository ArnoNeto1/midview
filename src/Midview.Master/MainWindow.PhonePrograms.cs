using System.Windows;

namespace Midview.Master;

// "Abrir programa" and "Desinstalar programa" with phones among the machines
// they reach: a phone's apps are its own list, not a PC's commands or
// programs, so the phones get their own windows (PhoneOpenAppWindow,
// PhoneUninstallWindow). Phones alone go straight to theirs; with PCs, the
// PCs' window opens and points to the phones' one ("Escolher o app dos
// celulares...", "Desinstalar apps dos celulares..."). "Abrir site" and
// "Instalar programa" have one window for both (RemoteLaunch, InstallWindow).
public partial class MainWindow
{
    private static (IReadOnlyList<KnownMachine> Pcs, IReadOnlyList<KnownMachine> Phones) SplitPhones(IReadOnlyList<KnownMachine> targets) =>
        (targets.Where(machine => !machine.IsPhone).ToList(), targets.Where(machine => machine.IsPhone).ToList());

    private bool IsEveryPhoneOfTheRoom(IReadOnlyList<KnownMachine> phones) =>
        phones.Count == _tiles.Count(tile => tile.Machine.IsPhone);

    private void OpenPhoneAppWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            return;
        }
        ShowOnce("abrir-app-celular", phones,
            () => new PhoneOpenAppWindow(phones, IsEveryPhoneOfTheRoom(phones), _masterCertificate) { Owner = this });
    }

    private void OpenPhoneUninstallWindow(IReadOnlyList<KnownMachine> phones)
    {
        if (phones.Count == 0)
        {
            return;
        }
        ShowOnce("desinstalar-celular", phones,
            () => new PhoneUninstallWindow(phones, IsEveryPhoneOfTheRoom(phones), _masterCertificate) { Owner = this });
    }

    // The PCs' program window, pointing to the phones' one when phones were among the machines.
    private Window NewProgramWindow(IReadOnlyList<KnownMachine> pcs, IReadOnlyList<KnownMachine> phones) =>
        new OpenProgramWindow(
            pcs, pcs.Count == _tiles.Count(tile => !tile.Machine.IsPhone), _masterCertificate,
            phones.Count, phones.Count > 0 ? () => OpenPhoneAppWindow(phones) : null)
        {
            Owner = this,
        };

    // "Desinstalar programa": the PCs' list (read from the first of them) and,
    // for phones, theirs.
    private void OpenUninstallWindows(IReadOnlyList<KnownMachine> targets, KnownMachine? listFrom = null)
    {
        var (pcs, phones) = SplitPhones(targets);
        if (pcs.Count == 0)
        {
            OpenPhoneUninstallWindow(phones);
            return;
        }
        var from = listFrom is { IsPhone: false } ? listFrom : pcs[0];
        ShowOnce("desinstalar", new[] { from }, () => new UninstallWindow(
            from, pcs, _masterCertificate, phones.Count, phones.Count > 0 ? () => OpenPhoneUninstallWindow(phones) : null,
            wholeClass: pcs.Count == _tiles.Count(tile => !tile.Machine.IsPhone))
        {
            Owner = this,
        });
    }
}
