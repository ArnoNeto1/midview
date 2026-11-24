using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// "Celulares e tablets" in "Senha de administrador": the one admin password
// of the school's phones and tablets (PhoneDefaultsFile, its salted hash
// alone), the same one "Preparar celulares" keeps and "Otimizar" sends.
// Saved here, it goes at once to every phone and tablet of the room that
// answers (PhoneNetworkClient.SetAdminPassword), so all of them have the
// same; the others get it at their next "Otimizar". "Remover" only stops
// sending it: the devices keep the one they have.
public partial class MachinesWindow
{
    private bool _sendingDeviceAdmin;

    private void RefreshDeviceAdminStatus()
    {
        var saved = PhoneDefaultsFile.Load().AdminPassword is not null;
        DeviceAdminTitle.Text = PhoneDefaultsText.CardTitle;
        DeviceAdminLine.Text = PhoneDefaultsText.CardLine;
        DeviceAdminButton.Content = saved ? "Trocar a senha..." : "Definir a senha...";
        DeviceAdminButton.IsEnabled = !_sendingDeviceAdmin;
        RemoveDeviceAdminButton.IsEnabled = saved && !_sendingDeviceAdmin;
        DeviceAdminStatus.Text = PhoneDefaultsText.CardStatus(saved);
    }

    private async void OnSetDeviceAdminClicked(object sender, RoutedEventArgs e)
    {
        if (_sendingDeviceAdmin || new OptimizeAdminWindow(change: true) { Owner = this }.ShowDialog() != true)
        {
            return;
        }
        RefreshDeviceAdminStatus();
        if (PhoneDefaultsFile.Load().AdminPassword is not { } payload)
        {
            return;
        }
        var devices = _roster().Where(machine => machine.IsPhone && machine.AgentThumbprint is { Length: > 0 }).ToList();
        _sendingDeviceAdmin = true;
        RefreshDeviceAdminStatus();
        DeviceAdminSent.Visibility = Visibility.Visible;
        DeviceAdminSent.Text = PhoneDefaultsText.Sending;
        try
        {
            var certificate = _masterCertificate;
            var put = await Task.WhenAll(devices.Select(device => Task.Run(() => PutDeviceAdmin(device, payload, certificate))));
            if (!_closed)
            {
                DeviceAdminSent.Text = PhoneDefaultsText.Sent(put.Count(done => done), devices.Count);
            }
        }
        finally
        {
            _sendingDeviceAdmin = false;
            if (!_closed)
            {
                RefreshDeviceAdminStatus();
            }
        }
    }

    // True when the device took the password; false when it is off, out of
    // the network or its Midview too old.
    private static bool PutDeviceAdmin(
        KnownMachine device, byte[] payload, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        try
        {
            return PhoneNetworkClient.SetAdminPassword(device, payload, certificate) is { Succeeded: true };
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException
                                       or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    private void OnRemoveDeviceAdminClicked(object sender, RoutedEventArgs e)
    {
        if (MidviewMessage.Show(this, PhoneDefaultsText.RemoveQuestion, PhoneDefaultsText.AdminTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            PhoneDefaultsFile.SaveAdminPassword(null);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                       or System.Security.Cryptography.CryptographicException)
        {
            MidviewMessage.Show(this, $"Não foi possível remover a senha: {Midview.Protocol.ErrorText.Describe(ex)}",
                PhoneDefaultsText.AdminTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DeviceAdminSent.Visibility = Visibility.Collapsed;
        RefreshDeviceAdminStatus();
    }
}
