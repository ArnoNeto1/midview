using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Midview.Master;

// Google's adb on the steps that use the cable (the Chrome step and the cable step). The first look at the cable checks it
// again (AdbTool.Find); when this PC doesn't have it, the step says it comes
// from Google, on Google's terms, and the teacher's click downloads it. The
// cable is not looked at until it is here.
public partial class PreparePhonesWindow
{
    private enum ToolState
    {
        // Not looked for yet, in this window.
        Unknown,
        Missing,
        Downloading,
        Failed,
        Ready,
    }

    private readonly CancellationTokenSource _toolCancel = new();
    private ToolState _tool;

    // The adb.exe in use (FindToolAsync), so another program's adb is told apart from it.
    private string? _adbPath;
    private int _toolPercent;
    private string? _toolProblem;

    // The cable step waits for the teacher's click (or the download).
    private bool ToolNeeded => _tool is ToolState.Missing or ToolState.Downloading or ToolState.Failed;

    // The adb.exe to use, checked again; null when it has to be downloaded.
    private async Task<bool> FindToolAsync()
    {
        var path = await Task.Run(_host.Tool.Find);
        if (path is null)
        {
            _tool = ToolState.Missing;
            return false;
        }
        UseTool(path);
        return true;
    }

    // Found or just downloaded: the cable tool, and its path for telling
    // another program's adb apart ("outro programa usando o cabo").
    private void UseTool(string path)
    {
        if (_adb is null)
        {
            _adb = _host.Adb(path);
            _adbPath = path;
        }
        _tool = ToolState.Ready;
    }

    // Called by ShowStep, after the footer is drawn, and when the download moves.
    private void ShowTool()
    {
        var needed = UsesCable && ToolNeeded;
        ToolCard.Visibility = Shown(needed);
        CableWaitCard.Visibility = Shown(OnCableStep && !ToolNeeded);
        ChromeCard.Visibility = Shown(OnChromeStep && !ToolNeeded);
        if (!UsesCable)
        {
            return;
        }
        // "Liguei o cabo e toquei em Permitir" has nothing to look with yet.
        NextButton.IsEnabled = !ToolNeeded;
        if (!needed)
        {
            return;
        }

        var failed = _tool == ToolState.Failed;
        ToolText.Style = failed ? (Style)FindResource("WarningText") : null;
        ToolText.Text = _tool switch
        {
            ToolState.Downloading => $"Baixando a ferramenta do Google para falar com o celular... {_toolPercent}%",
            ToolState.Failed => "Não deu para baixar a ferramenta do Google: " + _toolProblem,
            _ => "Para falar com o celular pelo cabo, o Midview precisa de uma ferramenta do Google (cerca de 8 MB). " +
                 "Ela é baixada uma vez e fica neste computador.",
        };
        ToolButton.Visibility = Shown(_tool != ToolState.Downloading);
        ToolButton.Content = failed ? "Tentar de novo" : "Baixar a ferramenta do Google";
    }

    // "Baixar a ferramenta do Google" or "Tentar de novo": the teacher's go-ahead.
    private async void OnToolClicked(object sender, RoutedEventArgs e)
    {
        if (_tool == ToolState.Downloading || _closed)
        {
            return;
        }
        _tool = ToolState.Downloading;
        _toolPercent = 0;
        _toolProblem = null;
        ShowTool();

        var token = _toolCancel.Token;
        var percent = new Progress<int>(now =>
        {
            if (_tool == ToolState.Downloading && !_closed)
            {
                _toolPercent = now;
                ShowTool();
            }
        });
        string? path = null;
        string? problem = null;
        try
        {
            path = await Task.Run(() => _host.Tool.DownloadAsync(percent, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (InvalidDataException ex)
        {
            problem = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "o Midview não conseguiu guardá-la neste computador. Tente de novo.";
        }
        if (_closed)
        {
            return;
        }
        if (path is null)
        {
            _tool = ToolState.Failed;
            _toolProblem = problem;
            ShowTool();
            return;
        }
        UseTool(path);
        ShowTool();
        UpdateCableWatch();
    }

    private void OnToolTermsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AdbTool.TermsUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MidviewMessage.Show(this, $"Não foi possível abrir o navegador. A página do Google é {AdbTool.TermsUrl}",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
