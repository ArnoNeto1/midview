using System.Drawing;
using System.Windows.Forms;
using Midview.Protocol;

namespace Midview.Session;

// The student's side of a private chat: one window per conversation, on its
// own STA thread (Session is a console app with no UI thread of its own).
public sealed class WinFormsChatWindows : IChatWindows
{
    public IChatWindow Open(Func<string, bool> sendToTeacher)
    {
        ChatForm? form = null;
        // Not disposed: HandleCreated fires again if Windows ever recreates
        // the handle, and Set on a disposed event would crash that thread.
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            form = new ChatForm(sendToTeacher);
            form.HandleCreated += (_, _) => ready.Set();
            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "Chat with the teacher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // The form can only be called into (BeginInvoke) once its handle exists.
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || form is null)
        {
            throw new InvalidOperationException("The chat window didn't open");
        }
        return form;
    }

    private sealed class ChatForm : Form, IChatWindow
    {
        private readonly Func<string, bool> _sendToTeacher;
        private readonly TextBox _transcript;
        private readonly TextBox _input;
        private readonly Button _send;
        private bool _conversationEnded;

        public ChatForm(Func<string, bool> sendToTeacher)
        {
            _sendToTeacher = sendToTeacher;
            Text = "Conversa com o professor";
            SessionIcon.ApplyTo(this);
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 380);
            var font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 11f);

            _transcript = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Dock = DockStyle.Fill,
                Font = font,
                TabStop = false,
            };
            _input = new TextBox { Dock = DockStyle.Fill, MaxLength = TextMessage.MaxLength, Font = font };
            _send = new Button { Text = "Enviar", Dock = DockStyle.Right, Width = 90 };
            var inputRow = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 4, 0, 0) };

            inputRow.Controls.Add(_send);
            inputRow.Controls.Add(_input);
            _input.BringToFront(); // fills whatever the Send button leaves
            Controls.Add(inputRow);
            Controls.Add(_transcript);
            _transcript.BringToFront();

            AcceptButton = _send; // Enter sends
            _send.Click += (_, _) => SendInput();
            Shown += (_, _) => _input.Focus();

            // While the conversation is on, closing only hides the window;
            // the teacher's next line brings it back.
            FormClosing += (_, e) =>
            {
                if (!_conversationEnded && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        public void ShowTeacherMessage(string text) => RunOnUiThread(() =>
        {
            Append($"Professor: {text}");
            if (!Visible)
            {
                Show();
            }
            Activate();
        });

        public void ShowConversationEnded() => RunOnUiThread(() =>
        {
            _conversationEnded = true;
            if (!Visible)
            {
                Close(); // hidden and over: nothing left to show
                return;
            }
            Append("— O professor encerrou a conversa —");
            _input.Enabled = false;
            _send.Enabled = false;
        });

        private void SendInput()
        {
            var text = _input.Text.Trim();
            if (text.Length == 0 || _conversationEnded)
            {
                return;
            }

            _input.Clear();
            Append($"Você: {text}");
            // The network write happens off the UI thread.
            Task.Run(() => _sendToTeacher(text)).ContinueWith(sent =>
            {
                if (sent.IsFaulted || !sent.Result)
                {
                    RunOnUiThread(() => Append("(não enviada: a conversa caiu)"));
                }
            }, TaskScheduler.Default);
        }

        private void Append(string line)
        {
            if (_transcript.TextLength > 0)
            {
                _transcript.AppendText(Environment.NewLine);
            }
            _transcript.AppendText(line);
        }

        // Calls arrive from the connection's thread; the window may already
        // be gone (closed after the conversation ended), which is fine.
        private void RunOnUiThread(Action action)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
            }
        }
    }
}
