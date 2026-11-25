using System.Windows.Threading;

namespace Midview.Master;

// The school's admin password on the phones and tablets it didn't reach when
// the teacher saved it (DeviceAdminPendingFile): each gets it as soon as its
// tile is online, and leaves the list once it took it (or answered that it
// can't). One that is online but doesn't answer is tried again a minute
// later. With no password saved any more ("Remover"), nothing is sent.
public partial class MainWindow
{
    private static readonly TimeSpan DeviceAdminCheck = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeviceAdminRetry = TimeSpan.FromMinutes(1);

    private DispatcherTimer? _deviceAdminTimer;
    private readonly HashSet<string> _deviceAdminSending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _deviceAdminTried = new(StringComparer.OrdinalIgnoreCase);

    // Started by App once the classroom is open, like StartAddressSearch: a
    // window made elsewhere (a test, a preview) sends nothing by itself.
    public void StartDeviceAdminWatch()
    {
        if (_deviceAdminTimer is not null)
        {
            return;
        }
        _deviceAdminTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = DeviceAdminCheck };
        _deviceAdminTimer.Tick += (_, _) => PutDeviceAdminOnArrivals();
        _deviceAdminTimer.Start();
        Closed += (_, _) => _deviceAdminTimer.Stop();
    }

    private void PutDeviceAdminOnArrivals()
    {
        var waiting = DeviceAdminPendingFile.Load();
        if (waiting.Count == 0 || PhoneDefaultsFile.Load().AdminPassword is not { } payload)
        {
            return;
        }
        var now = DateTime.UtcNow;
        foreach (var agent in waiting)
        {
            if (_deviceAdminSending.Contains(agent) || !IsPhoneOnline(agent)
                || (_deviceAdminTried.TryGetValue(agent, out var tried) && now - tried < DeviceAdminRetry))
            {
                continue;
            }
            var device = _tiles.First(tile => tile.Machine.IsPhone
                && string.Equals(tile.Machine.AgentThumbprint, agent, StringComparison.OrdinalIgnoreCase)).Machine;
            var certificate = _masterCertificate;
            _deviceAdminSending.Add(agent);
            _deviceAdminTried[agent] = now;
            _ = Task.Run(() => DeviceAdminArrival.Put(() => PhoneNetworkClient.SetAdminPassword(device, payload, certificate)))
                .ContinueWith(done =>
                {
                    _deviceAdminSending.Remove(agent);
                    var outcome = done.Status == TaskStatus.RanToCompletion ? done.Result : DeviceAdminArrival.Outcome.NoAnswer;
                    if (DeviceAdminArrival.KeepsWaiting(outcome))
                    {
                        return;
                    }
                    try
                    {
                        DeviceAdminPendingFile.Remove(agent);
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                    {
                        // Tried again at the next check; it takes the same password again harmlessly.
                    }
                    if (outcome == DeviceAdminArrival.Outcome.Put)
                    {
                        StatusTextBlock.Text = $"Senha de administrador posta no {device.Name}, que estava desligado quando a senha foi salva.";
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
