using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;

namespace Midview.Master;

// The school's Mi account: its page ("Conta Mi da escola", where it is
// saved, changed or taken off this PC) and its card on the step that signs
// in with it, where the password shows only on "Mostrar". The cable signs
// out of it by itself (PhoneCable) while it is saved here.
public partial class PreparePhonesWindow
{
    // Never the password's length: the same dots for any.
    private const string Dots = "••••••••";

    // The account for this window: the saved one, or the one typed here even
    // when it could not be saved.
    private MiAccount? _mi = MiAccountFile.Load();

    private bool _miPasswordShown;

    // The Midview signs out of the Mi account over the cable, unless the cable
    // already could not (then the step is done by hand).
    private bool MiAuto => _mi is not null && !_progress.SignOutByHand;

    // The step shown, in the words that fit how the Mi account ends.
    private GuideStep? ShownStep => _progress.Step is { } step ? PhoneSetupGuide.StepFor(step, MiAuto) : null;

    private string? MiPasswordForCable => MiAuto ? _mi?.Password : null;

    private void ShowMiAccountPage()
    {
        MiIntroText.Text = PhoneSetupGuide.MiAccountIntro;
        MiCreateText.Text = PhoneSetupGuide.MiAccountCreate;
        MiLoginInput.Text = _mi?.Login ?? "";
        MiPasswordInput.Password = _mi?.Password ?? "";
        ShowMiInput(shown: false);
        MiErrorText.Text = "";
        MiRemoveLine.Visibility = Shown(_mi is not null);
        Footer(back: true, next: "Guardar e continuar", other: _mi is null ? "Pular por agora" : null);
        Dispatcher.BeginInvoke(() => MiLoginInput.Focus(), DispatcherPriority.Input);
    }

    private string MiInputPassword =>
        MiPasswordShownInput.Visibility == Visibility.Visible ? MiPasswordShownInput.Text : MiPasswordInput.Password;

    // "Mostrar" beside the password being typed.
    private void OnMiShowInputClicked(object sender, RoutedEventArgs e) =>
        ShowMiInput(MiPasswordShownInput.Visibility != Visibility.Visible);

    private void ShowMiInput(bool shown)
    {
        if (shown)
        {
            MiPasswordShownInput.Text = MiPasswordInput.Password;
        }
        else
        {
            MiPasswordInput.Password = MiPasswordShownInput.Visibility == Visibility.Visible ? MiPasswordShownInput.Text : MiPasswordInput.Password;
            MiPasswordShownInput.Text = "";
        }
        MiPasswordShownInput.Visibility = Shown(shown);
        MiPasswordInput.Visibility = Shown(!shown);
        MiShowInputButton.Content = shown ? "Esconder" : "Mostrar";
    }

    private bool SaveMiAccount()
    {
        var login = MiLoginInput.Text.Trim();
        var password = MiInputPassword;
        if (login.Length == 0)
        {
            MiErrorText.Text = "Digite o e-mail ou o telefone da conta Mi da escola.";
            return false;
        }
        if (password.Length == 0)
        {
            MiErrorText.Text = "Digite a senha da conta Mi da escola.";
            return false;
        }
        MiErrorText.Text = "";
        _mi = new MiAccount(login, password);
        _progress.AsksMiAccount = false;
        ShowMiInput(shown: false);
        MiPasswordInput.Password = "";
        try
        {
            MiAccountFile.Save(_mi);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Like the Wi-Fi: this window keeps it, and the next one asks again.
        }
        return true;
    }

    private void OnMiRemoveClicked(object sender, RoutedEventArgs e)
    {
        var answer = MidviewMessage.Show(
            this,
            "Tirar a conta Mi da escola deste computador? Os celulares já preparados não mudam. " +
            "Sem ela, o Midview não sai sozinho da conta Mi: em cada celular, você sai à mão.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            MiAccountFile.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MiErrorText.Text = "Não deu para tirar a conta deste computador. Tente de novo.";
            return;
        }
        _mi = null;
        ShowPage();
    }

    // The card on the step that signs in with the account.
    private void ShowMiCard(GuideStep step)
    {
        var onStep = step.Id == "chip-conta-mi";
        MiCard.Visibility = Shown(onStep);
        if (!onStep)
        {
            return;
        }
        _miPasswordShown = false;
        MiCopyText.Text = "";
        MiCopyText.Visibility = Visibility.Collapsed;
        MiSavedPanel.Visibility = Shown(_mi is not null);
        MiMissingPanel.Visibility = Shown(_mi is null);
        MiChangeLink.Visibility = Shown(_mi is not null);
        MiLoginText.Text = _mi?.Login ?? "";
        MiPasswordText.Text = Dots;
        MiShowButton.Content = "Mostrar";
        MiMissingText.Text = "A escola ainda não guardou a conta Mi dela no Midview. " + PhoneSetupGuide.MiAccountCreate;
    }

    private void OnMiChangeClicked(object sender, RoutedEventArgs e)
    {
        _progress.ChangeMiAccount();
        ShowPage();
    }

    private void OnMiShowClicked(object sender, RoutedEventArgs e)
    {
        if (_mi is null)
        {
            return;
        }
        _miPasswordShown = !_miPasswordShown;
        MiPasswordText.Text = _miPasswordShown ? _mi.Password : Dots;
        MiShowButton.Content = _miPasswordShown ? "Esconder" : "Mostrar";
    }

    private void OnMiCopyLoginClicked(object sender, RoutedEventArgs e)
    {
        if (_mi is not null)
        {
            MiCopied(SecretClipboard.CopyText(_mi.Login) ? "E-mail copiado." : null);
        }
    }

    private void OnMiCopyPasswordClicked(object sender, RoutedEventArgs e)
    {
        if (_mi is not null)
        {
            MiCopied(SecretClipboard.Copy(_mi.Password)
                ? "Senha copiada. Ela sai da área de transferência em 1 minuto."
                : null);
        }
    }

    private void MiCopied(string? text)
    {
        MiCopyText.Text = text ?? "A área de transferência está ocupada por outro programa. Tente de novo.";
        MiCopyText.Visibility = Visibility.Visible;
    }
}
