using System.Windows;
using System.Windows.Input;

namespace Midview.Master;

// "Senha de administrador dos celulares", the third card of "Papel de parede
// e PIN da escola": the phones' admin password, typed twice, saved with the
// school's defaults as its salted hash alone (PhoneDefaultsFile) and put by
// itself on each device prepared next (PreparedDefaults), and on each one
// "Otimizar" reaches. The same rules and filtering as in "Wi-Fi e modo
// avião" (PhoneNetworkText). Left blank, a saved one stays; none saved, none
// is put. The boxes are emptied once saved: nothing here keeps the password.
public partial class PreparePhonesWindow
{
    private bool _adminSaved;
    private bool _adminRemove;
    private bool _adminSyncing;

    private bool AdminShown => DefaultsShowAdmin.IsChecked == true;

    private string DefaultsAdmin => AdminShown ? DefaultsAdminText.Text : DefaultsAdminBox.Password;

    private string DefaultsAdminRepeat => AdminShown ? DefaultsAdminRepeatText.Text : DefaultsAdminRepeatBox.Password;

    // From ShowDefaultsPage: empty boxes, and whether one is saved.
    private void ShowDefaultsAdmin(PhoneDefaultsFile.Defaults saved)
    {
        DefaultsAdminTitle.Text = PhoneDefaultsText.AdminTitle;
        DefaultsAdminLine.Text = PhoneDefaultsText.AdminLine;
        DefaultsAdminRepeatLabel.Text = PhoneDefaultsText.AdminRepeat;
        DefaultsAdminRemoveRun.Text = PhoneDefaultsText.AdminRemoveLink;
        _adminSaved = saved.AdminPassword is not null;
        _adminRemove = false;
        DefaultsShowAdmin.IsChecked = false;
        ClearDefaultsAdmin();
        ShowAdminBoxes();
    }

    private void ClearDefaultsAdmin()
    {
        _adminSyncing = true;
        try
        {
            DefaultsAdminBox.Password = DefaultsAdminRepeatBox.Password = "";
            DefaultsAdminText.Text = DefaultsAdminRepeatText.Text = "";
        }
        finally
        {
            _adminSyncing = false;
        }
        ShowAdminHint();
    }

    private void ShowAdminBoxes()
    {
        var show = AdminShown;
        DefaultsAdminText.Visibility = DefaultsAdminRepeatText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DefaultsAdminBox.Visibility = DefaultsAdminRepeatBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowAdminHint()
    {
        DefaultsAdminHint.Text = PhoneDefaultsText.AdminTyping(DefaultsAdmin, _adminSaved, _adminRemove);
        // The password is required: no "Não pôr senha" here (Senha de administrador still removes it).
        DefaultsAdminRemoveLink.Visibility = Visibility.Collapsed;
    }

    // Letters and digits only, as in "Wi-Fi e modo avião".
    private void OnDefaultsAdminTyping(object sender, TextCompositionEventArgs e) =>
        e.Handled = PhoneNetworkText.Clean(e.Text).Length != e.Text.Length;

    // A space never reaches PreviewTextInput; a paste of anything else is
    // cleaned when it lands (OnDefaultsAdminChanged).
    private void OnDefaultsAdminKeyDown(object sender, KeyEventArgs e) => e.Handled |= e.Key == Key.Space;

    // Each pair of boxes (hidden and shown) keeps the same cleaned letters and digits.
    private void OnDefaultsAdminChanged(object sender, RoutedEventArgs e)
    {
        if (_adminSyncing)
        {
            return;
        }
        _adminSyncing = true;
        try
        {
            Keep(DefaultsAdminBox, DefaultsAdminText, ReferenceEquals(sender, DefaultsAdminText));
            Keep(DefaultsAdminRepeatBox, DefaultsAdminRepeatText, ReferenceEquals(sender, DefaultsAdminRepeatText));
        }
        finally
        {
            _adminSyncing = false;
        }
        if (DefaultsAdmin.Length > 0)
        {
            _adminRemove = false;
        }
        DefaultsErrorText.Text = "";
        ShowAdminHint();
    }

    private static void Keep(System.Windows.Controls.PasswordBox box, System.Windows.Controls.TextBox text, bool fromText)
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

    private void OnDefaultsShowAdminClicked(object sender, RoutedEventArgs e) => ShowAdminBoxes();

    private void OnDefaultsAdminRemoveClicked(object sender, RoutedEventArgs e)
    {
        _adminRemove = true;
        ShowAdminHint();
    }

    // Null when fine to save; else what the page says (SaveDefaults).
    private string? DefaultsAdminProblem() => PhoneDefaultsText.AdminProblem(DefaultsAdmin, DefaultsAdminRepeat, _adminSaved && !_adminRemove);

    // "Pular" goes on only with a password saved: it is the one default the
    // devices cannot do without.
    private bool CanSkipDefaults()
    {
        if (_adminSaved && !_adminRemove)
        {
            return true;
        }
        DefaultsErrorText.Text = PhoneDefaultsText.AdminRequired;
        return false;
    }

    // After the picture and the PIN: a typed password replaces the saved
    // one, "Não pôr senha" takes it off, blank keeps what there is.
    private void SaveDefaultsAdmin()
    {
        var typed = DefaultsAdmin;
        if (typed.Length > 0)
        {
            PhoneDefaultsFile.SaveAdminPassword(typed);
        }
        else if (_adminRemove)
        {
            PhoneDefaultsFile.SaveAdminPassword(null);
        }
        _adminSaved = typed.Length > 0 || (_adminSaved && !_adminRemove);
        _adminRemove = false;
        ClearDefaultsAdmin();
    }
}
