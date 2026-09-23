using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// Session has no UI thread of its own (it's a console app), so each message
// gets a short-lived STA thread running its own message loop until the
// student closes the window. Several messages can be open at once.
public sealed class WinFormsMessageDisplay : IMessageDisplay
{
    public void Show(string text)
    {
        var thread = new Thread(() => Application.Run(new TeacherMessageForm(text)))
        {
            IsBackground = true,
            Name = "Teacher message",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private sealed class TeacherMessageForm : Form
    {
        public TeacherMessageForm(string text)
        {
            Text = "Mensagem do professor";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 300);

            // A read-only text box rather than a label, so a long message
            // scrolls instead of being cut off.
            var body = new TextBox
            {
                Text = text,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 14f),
                Dock = DockStyle.Fill,
                TabStop = false,
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 44 };

            Controls.Add(ok);
            Controls.Add(body);
            body.BringToFront(); // docked last, so it fills whatever the OK button leaves
            AcceptButton = ok;
            Shown += (_, _) => ok.Focus();
        }
    }
}
