using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Asks for the lab's admin password. On OK, PasswordUtf8 holds it as bytes
// (never a string kept around); the caller saves it and clears the buffer.
public partial class SetAdminPasswordWindow : Window
{
    public byte[]? PasswordUtf8 { get; private set; }

    public SetAdminPasswordWindow() => InitializeComponent();

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var bytes = ReadPasswordBytes();
        PasswordInput.Clear();
        if (bytes.Length == 0)
        {
            ErrorTextBlock.Text = "Digite a senha.";
            return;
        }
        if (bytes.Length > CredentialFill.MaxBytes)
        {
            Array.Clear(bytes);
            ErrorTextBlock.Text = "A senha é grande demais.";
            return;
        }
        PasswordUtf8 = bytes;
        DialogResult = true;
    }

    // Goes through the SecureString so the password never becomes a managed string.
    private byte[] ReadPasswordBytes()
    {
        var secure = PasswordInput.SecurePassword;
        var length = secure.Length;
        if (length == 0)
        {
            return Array.Empty<byte>();
        }
        var chars = new char[length];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            Marshal.Copy(pointer, chars, 0, length);
            return Encoding.UTF8.GetBytes(chars);
        }
        finally
        {
            Array.Clear(chars);
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}
