using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Atualizar professor" (TeacherUpdate): this PC's version against the newest
// one on GitHub, looked up as the window opens, and the button that downloads
// it, closes the Midview and leaves the install to TeacherUpdate's script.
public partial class MachinesWindow
{
    private readonly CancellationTokenSource _teacherUpdateCancel = new();

    // The newer version found on GitHub, null while there is none to install.
    private TeacherUpdate.Release? _teacherRelease;
    private bool _teacherUpdating;

    private async void CheckTeacherVersion()
    {
        var current = TeacherUpdate.CurrentVersion;
        _teacherRelease = null;
        TeacherUpdateButton.IsEnabled = false;
        if (TeacherUpdate.InstalledProgram(out var problem) is null)
        {
            TeacherVersionTextBlock.Text = $"Versão deste PC: {current}. {problem}";
            return;
        }

        TeacherVersionTextBlock.Text = $"Versão deste PC: {current}. Procurando a mais nova no GitHub...";
        try
        {
            using var client = TeacherUpdate.CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_teacherUpdateCancel.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var release = await TeacherUpdate.FetchLatestAsync(client, timeout.Token);
            if (_closed || _teacherUpdating)
            {
                return; // an update started meanwhile (StartTeacherUpdate) owns the text
            }
            switch (TeacherUpdate.Compare(current, release.Version))
            {
                case TeacherUpdate.Standing.Newer:
                    _teacherRelease = release;
                    TeacherVersionTextBlock.Text = $"Versão deste PC: {current}. Já saiu a {release.Version} no GitHub.";
                    TeacherUpdateButton.Content = $"Atualizar para a {release.Version}...";
                    break;
                case TeacherUpdate.Standing.Same:
                    TeacherVersionTextBlock.Text = $"Versão deste PC: {current}, a mais nova publicada.";
                    TeacherUpdateButton.Content = "Procurar de novo";
                    break;
                default:
                    TeacherVersionTextBlock.Text = $"Versão deste PC: {current}, mais nova que a publicada ({release.Version}).";
                    TeacherUpdateButton.Content = "Procurar de novo";
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or OperationCanceledException)
        {
            if (_closed || _teacherUpdating)
            {
                return;
            }
            var why = ex is OperationCanceledException ? "o GitHub demorou demais para responder" : ErrorText.Describe(ex);
            TeacherVersionTextBlock.Text = $"Versão deste PC: {current}. Não deu para ver a mais nova no GitHub: {why}.";
            TeacherUpdateButton.Content = "Tentar de novo";
        }
        TeacherUpdateButton.IsEnabled = true;
    }

    private async void OnTeacherUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_teacherUpdating)
        {
            return;
        }
        if (_teacherRelease is not { } release)
        {
            CheckTeacherVersion();
            return;
        }
        if (TeacherUpdate.InstalledProgram(out var problem) is not { } program)
        {
            TeacherVersionTextBlock.Text = problem;
            return;
        }
        if (MidviewMessage.Show(this,
                $"Atualizar o Midview deste PC da versão {TeacherUpdate.CurrentVersion} para a {release.Version}?\n\n" +
                "Ele baixa o instalador do GitHub e depois fecha para instalar, o que leva 1 ou 2 minutos; no fim, abre de novo sozinho. " +
                "Ao fechar, as telas bloqueadas dos alunos são liberadas.\n\n" +
                "O Windows vai pedir permissão de administrador.",
                "Atualizar professor", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }
        await RunTeacherUpdateAsync(release, program);
    }

    // "Atualizar agora" in the notice the Midview shows as it opens: the
    // teacher said yes there already, so the download starts at once, here,
    // where its progress shows.
    public async void StartTeacherUpdate(TeacherUpdate.Release release)
    {
        ShowPage(MachinesPage.Updates);
        if (_teacherUpdating || _closed)
        {
            return;
        }
        if (TeacherUpdate.InstalledProgram(out var problem) is not { } program)
        {
            TeacherVersionTextBlock.Text = problem;
            return;
        }
        _teacherRelease = release;
        await RunTeacherUpdateAsync(release, program);
    }

    private async Task RunTeacherUpdateAsync(TeacherUpdate.Release release, string program)
    {
        _teacherUpdating = true;
        TeacherUpdateButton.IsEnabled = false;
        TeacherVersionTextBlock.Text = $"Baixando a versão {release.Version}...";
        string setup;
        try
        {
            using var client = TeacherUpdate.CreateClient();
            setup = await TeacherUpdate.DownloadAsync(
                client, release, TeacherUpdate.Folder,
                new Progress<int>(percent => TeacherVersionTextBlock.Text = $"Baixando a versão {release.Version}... {percent}%"),
                _teacherUpdateCancel.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
                                       or UnauthorizedAccessException or OperationCanceledException)
        {
            _teacherUpdating = false;
            if (_closed)
            {
                return; // the window was closed: no update, the Midview stays open
            }
            TeacherVersionTextBlock.Text = $"O download da versão {release.Version} falhou: {ErrorText.Describe(ex)}.";
            TeacherUpdateButton.Content = "Tentar de novo";
            TeacherUpdateButton.IsEnabled = true;
            return;
        }
        if (_closed)
        {
            _teacherUpdating = false;
            return;
        }

        string script;
        try
        {
            script = TeacherUpdate.WriteApplyScript(TeacherUpdate.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _teacherUpdating = false;
            TeacherVersionTextBlock.Text = $"A atualização não pôde começar: {ErrorText.Describe(ex)}.";
            TeacherUpdateButton.Content = "Tentar de novo";
            TeacherUpdateButton.IsEnabled = true;
            return;
        }

        // The setup can't replace a Midview still open. Closing it may be
        // refused (a class folder still being handed over): then nothing else
        // happens, and the setup that was downloaded waits for another try.
        TeacherVersionTextBlock.Text = "Fechando o Midview para instalar...";
        // With the default shutdown mode the app starts ending as the last window
        // closes, and the error message below would have no window life left to
        // show in: the explicit Shutdown() in the finally ends it instead, on
        // every path from here, unless the close is refused (then the mode
        // goes back to how it was).
        var shutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var shutDown = true;
        try
        {
            var main = Application.Current.MainWindow;
            main?.Close();
            if (main is { IsVisible: true })
            {
                shutDown = false;
                Application.Current.ShutdownMode = shutdownMode;
                _teacherUpdating = false;
                TeacherVersionTextBlock.Text = "A atualização ficou para depois: o Midview não fechou.";
                TeacherUpdateButton.Content = $"Atualizar para a {release.Version}...";
                TeacherUpdateButton.IsEnabled = true;
                return;
            }

            try
            {
                Process.Start(TeacherUpdate.ApplyStartInfo(
                    script, setup, release.Sha256, Environment.ProcessId, program,
                    TeacherUpdate.LogPath(TeacherUpdate.Folder, release.Version), release.Version))?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The windows are gone already: said on its own, then the Midview ends.
                MidviewMessage.Show(
                    $"A atualização não pôde começar: {ErrorText.Describe(ex)}. Abra o Midview de novo e tente outra vez.",
                    "Atualizar professor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            if (shutDown)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
