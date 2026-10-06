using System.Drawing;
using System.Windows.Forms;
using Midview.Protocol;

namespace Midview.Session;

// The pairing code on the student's screen while a teacher pairs with this
// machine. The Agent starts it (Midview.Session.exe --pairing-code ...)
// and ends it when the pairing ends; it also closes itself after
// Pairing.ConfirmTimeout. Nothing to click: the teacher compares the code
// with the one on their own screen.
public static class PairingCodeWindow
{
    private static readonly Color Background = Color.FromArgb(64, 64, 64);
    private static readonly Color Ink = Color.FromArgb(235, 235, 235);
    private static readonly Color MutedInk = Color.FromArgb(190, 190, 190);

    public static (string Title, string Detail, string Code, string Footer) Texts(int code, string teacherName) => (
        "Pareamento com o professor",
        $"O computador {teacherName} está conectando esta máquina ao Midview.",
        Pairing.Format(code),
        "O professor confere se o mesmo código aparece na tela dele.");

    public static void Run(int code, string teacherName)
    {
        var (title, detail, codeText, footer) = Texts(code, teacherName);
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Background,
            Size = new Size(620, 330),
            Padding = new Padding(28),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Background };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(NewLabel(title, new Font("Segoe UI Semibold", 20f), Ink), 0, 0);
        layout.Controls.Add(NewLabel(detail, new Font("Segoe UI", 12f), MutedInk), 0, 1);
        layout.Controls.Add(NewLabel(codeText, new Font("Segoe UI", 54f, FontStyle.Bold), Ink), 0, 2);
        layout.Controls.Add(NewLabel(footer, new Font("Segoe UI", 11f), MutedInk), 0, 3);
        form.Controls.Add(layout);

        using var timer = new System.Windows.Forms.Timer { Interval = (int)Pairing.ConfirmTimeout.TotalMilliseconds };
        timer.Tick += (_, _) => form.Close();
        timer.Start();
        Application.Run(form);
    }

    private static Label NewLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Height = font.Height + 12,
    };
}
