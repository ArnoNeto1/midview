using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Máquinas da sala": find the machines with Midview on this network and
// pair with them (each shows a code the teacher compares), push the student
// program installed with this one to the class, give a colleague the class
// with a code or take a colleague's (GiveAccessWindow, JoinClassWindow), let
// another teacher pair for 10 minutes, and open the advanced settings
// (AddMachineWindow).
public partial class MachinesWindow : Window
{
    private const string MismatchText =
        "O código não conferiu. Pode haver outro computador no meio da conexão. Tente de novo e, se repetir, avise o responsável pela rede.";

    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _roster;
    private readonly Action<KnownMachine> _addPaired;
    private readonly Func<IReadOnlyList<FoundMachine>, int> _refreshAddresses;
    private readonly Action _openAdvanced;
    private readonly Action _openGiveAccess;
    private readonly Action _openJoinClass;
    private UpdateWindow? _updateWindow;
    private readonly ObservableCollection<FoundRow> _rows = new();
    private readonly AdminPasswordStore _adminPasswords = new(MasterData.Folder);
    private bool _closed;

    // addPaired and refreshAddresses change the classroom list (MainWindow);
    // both may throw when the list can't be written to disk.
    public MachinesWindow(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster,
        Action<KnownMachine> addPaired, Func<IReadOnlyList<FoundMachine>, int> refreshAddresses, Action openAdvanced,
        Action openGiveAccess, Action openJoinClass)
    {
        InitializeComponent();
        _openAdvanced = openAdvanced;
        _openGiveAccess = openGiveAccess;
        _openJoinClass = openJoinClass;
        _masterCertificate = masterCertificate;
        _roster = roster;
        _addPaired = addPaired;
        _refreshAddresses = refreshAddresses;
        FoundItemsControl.ItemsSource = _rows;
        ShowStudentProgramVersion();
        RefreshAdminPasswordStatus();
    }

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
            MessageBox.Show(this, $"Não foi possível guardar a senha: {ErrorText.Describe(ex)}",
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
        if (MessageBox.Show(this, "Remover a senha de administrador guardada neste computador?",
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
            MessageBox.Show(this, $"Não foi possível remover a senha: {ErrorText.Describe(ex)}",
                "Senha de administrador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAdminPasswordStatus();
    }

    private void OnGiveAccessClicked(object sender, RoutedEventArgs e) => _openGiveAccess();

    private void OnJoinClassClicked(object sender, RoutedEventArgs e) => _openJoinClass();

    private async void OnSearchClicked(object sender, RoutedEventArgs e) => await SearchAsync();

    public async Task SearchAsync()
    {
        SearchButton.IsEnabled = false;
        SearchStatusTextBlock.Text = "Procurando...";
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
                summary = FoundMachineText.SearchSummary(found.Count, _refreshAddresses(found));
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
            var row = new FoundRow(machine, FoundMachineText.Status(machine, roster), FoundMachineText.CheckedByDefault(machine, roster));
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }
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
        await Task.WhenAll(chosen.Select(row => StartPairingAsync(row, teacherName)));
    }

    private async Task StartPairingAsync(FoundRow row, string teacherName)
    {
        row.IsBusy = true;
        row.IsChecked = false;
        row.OutcomeIsError = false;
        row.Outcome = "Conectando...";
        UpdatePairButton();
        try
        {
            var session = await Task.Run(() => PairingSession.Start(row.Address, _masterCertificate, teacherName));
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
        _updateWindow = new UpdateWindow(UpdatePackage.BundledStudentFolder, _roster, _masterCertificate) { Owner = this };
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        OwnedWindow.ReturnToOwnerWhenClosed(_updateWindow);
        _updateWindow.Show();
    }

    private async void OnAllowNewTeacherClicked(object sender, RoutedEventArgs e)
    {
        AllowNewTeacherButton.IsEnabled = false;
        AllowNewTeacherResultTextBlock.Text = "Avisando as máquinas...";
        var machines = _roster();
        var results = await Task.WhenAll(machines.Select(machine => Task.Run(() => AllowNewTeacher(machine))));
        AllowNewTeacherResultTextBlock.Text = FoundMachineText.AllowNewTeacherSummary(results);
        AllowNewTeacherButton.IsEnabled = true;
    }

    private (string Name, bool Opened, string Detail) AllowNewTeacher(KnownMachine machine)
    {
        if (machine.AgentThumbprint is null)
        {
            return (machine.Name, false, "sem a identificação do Agent");
        }

        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint, _masterCertificate,
                MessageType.AllowNewTeacher, Array.Empty<byte>());
            return (machine.Name, succeeded, succeeded || detail.Length > 0 ? detail : "Midview antigo: atualize-a");
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        foreach (var row in _rows.Where(row => row.Session is not null))
        {
            row.Session!.Cancel();
            row.Session.Dispose();
            row.Session = null;
        }
    }
}
