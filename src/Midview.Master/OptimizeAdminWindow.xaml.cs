using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// The admin password of the phones and tablets, typed twice and saved as its
// salted hash alone (PhoneDefaultsFile), the same one "Preparar celulares"
// keeps. Asked when "Otimizar" opens and none is saved yet, which then puts
// it on each device (PhoneOptimizeAdmin): "Otimizar sem senha" (or closing)
// optimizes without one, and it is asked again next time. Or set from
// "Senha de administrador" in "Máquinas da sala" (change: true), which then
// puts it on every device of the room. The same rules and filtering as in
// "Preparar celulares" (PhoneDefaultsText, PhoneNetworkText). The boxes are
// emptied once saved.
public partial class OptimizeAdminWindow : MidviewWindow
{
    private bool _syncing;

    // The folder the password is saved in (tests); null: the teacher's data folder.
    private readonly string? _folder;

    private readonly bool _change;

    public OptimizeAdminWindow(bool change = false, string? folder = null)
    {
        InitializeComponent();
        _change = change;
        _folder = folder;
        LineTextBlock.Text = change ? PhoneDefaultsText.ChangeLine : PhoneDefaultsText.AskLine;
        RepeatLabel.Text = PhoneDefaultsText.AdminRepeat;
        SkipButton.Content = change ? PhoneDefaultsText.ChangeCancel : PhoneDefaultsText.AskSkip;
        SaveButton.Content = change ? PhoneDefaultsText.ChangeSave : PhoneDefaultsText.AskSave;
        ShowHint();
    }

    // True once a password was saved.
    public bool Saved { get; private set; }

    private bool Shown => ShowCheckBox.IsChecked == true;

    private string Password => Shown ? PasswordText.Text : PasswordBox.Password;

    private string Repeat => Shown ? RepeatText.Text : RepeatBox.Password;

    private void ShowHint() =>
        HintTextBlock.Text = Password.Length > 0 ? PhoneDefaultsText.AdminTyping(Password, false, false) : PhoneDefaultsText.AskHint;

    // Letters and digits only.
    private void OnTyping(object sender, TextCompositionEventArgs e) =>
        e.Handled = PhoneNetworkText.Clean(e.Text).Length != e.Text.Length;

    private void OnKeyDown(object sender, KeyEventArgs e) => e.Handled |= e.Key == Key.Space;

    // Each pair of boxes (hidden and shown) keeps the same cleaned letters and digits.
    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        try
        {
            Keep(PasswordBox, PasswordText, ReferenceEquals(sender, PasswordText));
            Keep(RepeatBox, RepeatText, ReferenceEquals(sender, RepeatText));
        }
        finally
        {
            _syncing = false;
        }
        ErrorTextBlock.Text = "";
        ShowHint();
    }

    private static void Keep(PasswordBox box, TextBox text, bool fromText)
    {
        var clean = PhoneNetworkText.Clean(fromText ? text.Text : box.Password);
        if (box.Password != clean)
        {
            box.Password = clean;
        }
        if (text.Text != clean)
        {
            text.Text = clean;
            text.CaretIndex = clean.Length;
        }
    }

    private void OnShowClicked(object sender, RoutedEventArgs e)
    {
        var show = Shown;
        PasswordText.Visibility = RepeatText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = RepeatBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (PhoneDefaultsText.AskProblem(Password, Repeat, _change) is { } problem)
        {
            ErrorTextBlock.Text = problem;
            return;
        }
        try
        {
            PhoneDefaultsFile.SaveAdminPassword(Password, _folder);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                       or System.Security.Cryptography.CryptographicException)
        {
            ErrorTextBlock.Text = $"A senha não pôde ser guardada: {ErrorText.Describe(ex)}";
            return;
        }
        Saved = true;
        Clear();
        DialogResult = true;
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        Clear();
        DialogResult = false;
    }

    private void Clear()
    {
        _syncing = true;
        try
        {
            PasswordBox.Password = RepeatBox.Password = "";
            PasswordText.Text = RepeatText.Text = "";
        }
        finally
        {
            _syncing = false;
        }
    }
}
