using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// "Máquinas da sala": find the machines with Midview on this network and
// pair with them (each shows a code the teacher compares), push the student
// program installed with this one to the class, give a colleague the class
// with a code or take a colleague's (GiveAccessWindow, JoinClassWindow), let
// another teacher with a code pair for 10 minutes or forget the other
// teachers (MachinesWindow.OtherTeacher), care for the room's phones and
// tablets (MachinesWindow.Phones), and open the advanced settings
// (AddMachineWindow).
public partial class MachinesWindow : MidviewWindow
{
    private const string MismatchText =
        "O código não conferiu. Pode haver outro computador no meio da conexão. Tente de novo e, se repetir, avise o responsável pela rede.";

    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _roster;
    private readonly Action<KnownMachine> _addPaired;
    private readonly Func<IReadOnlyList<FoundMachine>, Task<int>> _refreshAddresses;
    private readonly Action _openAdvanced;
    private readonly Action _openGiveAccess;
    private readonly Action _openJoinClass;
    private readonly Action<AndroidDeviceKind> _openPhoneUpdate;
    private readonly Action<AndroidDeviceKind, bool> _openPreparePhones;
    private readonly DeviceRoomCallbacks _roomDevices;
    private UpdateWindow? _updateWindow;
    private readonly ObservableCollection<FoundRow> _rows = new();
    private readonly AdminPasswordStore _adminPasswords = new(MasterData.Folder);
    private bool _closed;

    // addPaired and refreshAddresses change the classroom list (MainWindow);
    // both may throw when the list can't be written to disk.
    public MachinesWindow(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster,
        Action<KnownMachine> addPaired, Func<IReadOnlyList<FoundMachine>, Task<int>> refreshAddresses, Action openAdvanced,
        Action openGiveAccess, Action openJoinClass, Action<AndroidDeviceKind> openPhoneUpdate,
        Action<AndroidDeviceKind, bool> openPreparePhones, DeviceRoomCallbacks roomDevices)
    {
        InitializeComponent();
        _roster = roster;
        _roomDevices = roomDevices;
        ShowPhoneSetup(PreparePhoneSpan, PhoneProvisioning.Setup, OpenCableSteps);
        ShowDeviceSetupPages(PhoneProvisioning.Setup);
        _openAdvanced = openAdvanced;
        _openGiveAccess = openGiveAccess;
        _openJoinClass = openJoinClass;
        _openPhoneUpdate = openPhoneUpdate;
        _openPreparePhones = openPreparePhones;
        _masterCertificate = masterCertificate;
        _addPaired = addPaired;
        _refreshAddresses = refreshAddresses;
        FoundItemsControl.ItemsSource = _rows;
        ShowStudentProgramVersion();
        ShowPhoneUpdateCard();
        CheckTeacherVersion();
        RefreshAdminPasswordStatus();
        ShowPage(MachinesPage.Search);
    }

    // The side menu's entries and their panels, in MachinesPage's order.
    private RadioButton[] Navs => new[]
    {
        SearchNav, PhoneSetupNav, TabletSetupNav, UpdatesNav, OtherTeacherNav, AdminPasswordNav, AdvancedNav,
    };

    private Grid[] Panels => new[]
    {
        SearchPanel, PhoneSetupPanel, TabletSetupPanel, UpdatesPanel, OtherTeacherPanel, AdminPasswordPanel, AdvancedPanel,
    };

    // Marks the entry in the side menu and shows that page's panel.
    public void ShowPage(MachinesPage page)
    {
        var panels = Panels;
        for (var i = 0; i < panels.Length; i++)
        {
            panels[i].Visibility = i == (int)page ? Visibility.Visible : Visibility.Collapsed;
        }
        Navs[(int)page].IsChecked = true;
        if (page == MachinesPage.Updates)
        {
            ShowPhoneUpdateCard(); // the phone app may have been downloaded since
        }
    }

    private void OnNavChecked(object sender, RoutedEventArgs e) => ShowPage((MachinesPage)Array.IndexOf(Navs, sender));

    private void OnAdvancedClicked(object sender, RoutedEventArgs e) => _openAdvanced();

    // The lab admin password lives only on this teacher PC (DPAPI CurrentUser);
    // it is never sent to a student machine. With one saved, "Preencher a senha
    // de administrador" in a control session types it into the student's UAC.
    private void RefreshAdminPasswordStatus()
    {
        var has = _adminPasswords.HasPassword;
        AdminPasswordButton.Content = has ? "Trocar a senha..." : "Definir a senha...";
        RemoveAdminPasswordButton.IsEnabled = has;
        AdminPasswordStatusTextBlock.Text = has
            ? "Uma senha está salva neste computador."
            : "Nenhuma senha salva ainda.";
    }

    private void OnSetAdminPasswordClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SetAdminPasswordWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.PasswordUtf8 is not { } bytes)
        {
            return;
        }
        try
        {
            _adminPasswords.Save(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MidviewMessage.Show(this, $"Não foi possível guardar a senha: {ErrorText.Describe(ex)}",
                "Senha de administrador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Array.Clear(bytes);
        }
        RefreshAdminPasswordStatus();
    }

    private void OnRemoveAdminPasswordClicked(object sender, RoutedEventArgs e)
    {
        if (MidviewMessage.Show(this, "Remover a senha de administrador guardada neste computador?",
                "Senha de administrador", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            _adminPasswords.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MidviewMessage.Show(this, $"Não foi possível remover a senha: {ErrorText.Describe(ex)}",
                "Senha de administrador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAdminPasswordStatus();
    }

    private void OnGiveAccessClicked(object sender, RoutedEventArgs e) => _openGiveAccess();

    private void OnJoinClassClicked(object sender, RoutedEventArgs e) => _openJoinClass();

    private async void OnSearchClicked(object sender, RoutedEventArgs e) => await SearchAsync();

    public async Task SearchAsync()
    {
        ShowSearching();
        try
        {
            var thumbprint = _masterCertificate.Thumbprint;
            var found = await Task.Run(() => DiscoveryClient.Search(
                thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime));
            if (_closed)
            {
                return;
            }

            ShowFound(found);
            var summary = FoundMachineText.SearchSummary(found.Count, 0);
            try
            {
                summary = FoundMachineText.SearchSummary(found.Count, await _refreshAddresses(found));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                summary += $" Os endereços novos não puderam ser gravados na lista da sala: {ErrorText.Describe(ex)}";
            }
            SearchStatusTextBlock.Text = summary;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            SearchStatusTextBlock.Text = $"A procura falhou: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
            UpdatePairButton();
        }
    }

    // A search shows on its own page, whichever page was open ("Procurar
    // máquinas na rede" with the window already open on another one).
    private void ShowSearching()
    {
        ShowPage(MachinesPage.Search);
        SearchButton.IsEnabled = false;
        SearchStatusTextBlock.Text = "Procurando...";
    }

    // Rows still being paired stay; the rest are replaced by what answered.
    private void ShowFound(IReadOnlyList<FoundMachine> found)
    {
        foreach (var row in _rows.Where(row => !row.IsBusy).ToList())
        {
            _rows.Remove(row);
        }

        var roster = _roster();
        foreach (var machine in found)
        {
            if (_rows.Any(row => string.Equals(row.Machine.AgentThumbprint, machine.AgentThumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var row = new FoundRow(machine, FoundMachineText.Status(machine, roster), FoundMachineText.CheckedByDefault(machine, roster))
            {
                NeedsCode = FoundMachineText.NeedsNewTeacherCode(machine, roster),
            };
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }
        ShowNewTeacherCodeField();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FoundRow.IsChecked))
        {
            UpdatePairButton();
        }
    }

    private void UpdatePairButton() => PairButton.IsEnabled = _rows.Any(row => row.IsChecked && !row.IsBusy);

    private async void OnPairClicked(object sender, RoutedEventArgs e)
    {
        var chosen = _rows.Where(row => row.IsChecked && !row.IsBusy).ToList();
        var teacherName = Environment.MachineName;
        var code = ChosenNewTeacherCode(chosen);
        await Task.WhenAll(chosen
            .Where(row => !row.NeedsCode || code is not null)
            .Select(row => StartPairingAsync(row, teacherName, row.NeedsCode ? code : null)));
    }

    private async Task StartPairingAsync(FoundRow row, string teacherName, string? newTeacherCode)
    {
        row.IsBusy = true;
        row.IsChecked = false;
        row.OutcomeIsError = false;
        row.Outcome = "Conectando...";
        UpdatePairButton();
        try
        {
            var session = await Task.Run(() => PairingSession.Start(
                row.Address, _masterCertificate, teacherName, newTeacherCode: newTeacherCode));
            if (_closed)
            {
                session.Cancel();
                session.Dispose();
                return;
            }
            row.Session = session;
            row.CodeText = session.CodeText;
            row.AwaitingConfirmation = true;
            row.Outcome = string.Empty;
        }
        catch (PairingRefusedException ex)
        {
            Fail(row, ex.Message);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            Fail(row, $"Não foi possível conectar: {FailureText.DescribeAgent(ex)}");
        }
    }

    private async void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FoundRow { Session: { } session } row)
        {
            return;
        }

        row.AwaitingConfirmation = false;
        row.Outcome = "Confirmando...";
        try
        {
            var machine = await Task.Run(session.Confirm);
            row.Status = FoundMachineText.InRoster;
            try
            {
                _addPaired(machine);
                row.OutcomeIsError = false;
                row.Outcome = "Pareada. Já está na sala.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                row.OutcomeIsError = true;
                row.Outcome = $"Pareada e colocada na grade, mas a lista da sala não pôde ser gravada: {ErrorText.Describe(ex)}";
            }
        }
        catch (PairingRefusedException ex)
        {
            row.OutcomeIsError = true;
            row.Outcome = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            row.OutcomeIsError = true;
            row.Outcome = "A máquina encerrou o pareamento (o tempo para conferir pode ter acabado). Tente de novo.";
        }
        finally
        {
            EndSession(row);
        }
    }

    private void OnMismatchClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FoundRow { Session: { } session } row)
        {
            return;
        }

        session.Cancel();
        EndSession(row);
        row.OutcomeIsError = true;
        row.Outcome = MismatchText;
    }

    private void EndSession(FoundRow row)
    {
        row.Session?.Dispose();
        row.Session = null;
        row.AwaitingConfirmation = false;
        row.CodeText = string.Empty;
        row.IsBusy = false;
        UpdatePairButton();
    }

    private void Fail(FoundRow row, string text)
    {
        row.IsBusy = false;
        row.OutcomeIsError = true;
        row.Outcome = text;
        UpdatePairButton();
    }

    private void ShowStudentProgramVersion()
    {
        try
        {
            var manifest = BuildManifest.Load(UpdatePackage.BundledStudentFolder);
            StudentVersionTextBlock.Text = $"Versão do programa do aluno neste PC: {manifest.Build}";
            UpdateButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or IOException)
        {
            StudentVersionTextBlock.Text = "Esta cópia não traz o programa do aluno. Instale pelo MidviewSetup.";
            UpdateButton.IsEnabled = false;
        }
    }

    // After "Atualizar professor" put a new version on this PC and the
    // teacher said yes to updating the students too: the same window as the
    // button, when this copy carries the students' program.
    public void OpenStudentUpdate()
    {
        ShowPage(MachinesPage.Updates);
        if (UpdateButton.IsEnabled)
        {
            OnUpdateClicked(this, new RoutedEventArgs());
        }
    }

    // "Atualizar os celulares" and "Atualizar os tablets", always there so the
    // teacher knows where to update: with the version of the app this PC
    // sends (the same for both) and whether it is already downloaded, or
    // saying the room has none yet. The classroom calls it again when its
    // list changes.
    public void ShowPhoneUpdateCard()
    {
        var room = _roster();
        var version = TeacherUpdate.CurrentVersion;
        var kept = PhoneAgentApk.IsKept(PhoneAgentApk.DefaultFolder, version);
        foreach (var (line, button, kind) in new[]
        {
            (PhoneVersionTextBlock, PhoneUpdateButton, AndroidDeviceKind.Phone),
            (TabletVersionTextBlock, TabletUpdateButton, AndroidDeviceKind.Tablet),
        })
        {
            var any = room.Any(machine => DeviceSupport.IsAndroidOf(machine, kind));
            line.Text = any ? PhoneUpdateText.ApkLine(version.ToString(), kept, kind) : PhoneUpdateText.NoneInRoom(kind);
            button.IsEnabled = any;
        }
    }

    // The classroom's own window (one at a time, as from a phone's menu).
    private void OnPhoneUpdateClicked(object sender, RoutedEventArgs e) => _openPhoneUpdate(AndroidDeviceKind.Phone);

    private void OnTabletUpdateClicked(object sender, RoutedEventArgs e) => _openPhoneUpdate(AndroidDeviceKind.Tablet);

    // One at a time: asking again brings it back.
    private void OnUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_updateWindow is not null)
        {
            if (_updateWindow.WindowState == WindowState.Minimized)
            {
                _updateWindow.WindowState = WindowState.Normal;
            }
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(
            UpdatePackage.BundledStudentFolder, () => DeviceSupport.Only(_roster(), ToolbarAction.Update), _masterCertificate) { Owner = this };
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        OwnedWindow.ReturnToOwnerWhenClosed(_updateWindow);
        _updateWindow.Show();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _teacherUpdateCancel.Cancel();
        foreach (var row in _rows.Where(row => row.Session is not null))
        {
            row.Session!.Cancel();
            row.Session.Dispose();
            row.Session = null;
        }
    }
}
