using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

// Who may pair with this machine, and the Agent's side of the exchange (see
// Pairing). A machine with no teacher takes the first confirmed pairing; a
// teacher it already trusts may pair again (a lost classroom list); anyone
// else only while a trusted teacher's "Permitir novo professor" lasts, and
// only with that window's code (NewTeacherInvite): the first valid code ends
// the window, and so do NewTeacherInvite.MaxTries pairings that brought a
// code to the screen without one. One pairing at a time. It also takes
// TrustTeacher: a trusted teacher gives a colleague access ("Dar acesso a um
// colega"), and the colleague is added to the same list; and
// ForgetOtherTeachers, which leaves the sender the only teacher.
public sealed class PairingService
{
    private readonly object _gate = new();
    // trusted-masters.json is read, changed and written back: two teachers
    // added at once (a pairing and a TrustTeacher) must not lose one.
    private readonly object _writeGate = new();
    private readonly TrustStore _trust;
    private readonly string _trustedMastersPath;
    private readonly IPairingCodeDisplay _display;
    private readonly Func<DateTime> _now;
    private readonly string _machineName;
    private readonly Func<string> _sessionThumbprint;
    private DateTime? _newTeacherUntil;
    private string? _inviteCode;
    private int _inviteTries;
    // Which window a pairing was let in by: one opened since is not its to end.
    private int _windowGeneration;
    private bool _busy;

    // now is local time: it's what "liberada até 14:32" shows.
    public PairingService(
        TrustStore trust, string trustedMastersPath, IPairingCodeDisplay display, Func<DateTime> now,
        string machineName, Func<string> sessionThumbprint)
    {
        _trust = trust;
        _trustedMastersPath = trustedMastersPath;
        _display = display;
        _now = now;
        _machineName = machineName;
        _sessionThumbprint = sessionThumbprint;
    }

    public bool NewTeacherOpen
    {
        get
        {
            lock (_gate)
            {
                return WindowOpen();
            }
        }
    }

    // For discovery: whom this machine trusts, as the asking teacher sees it.
    public string TeachersSeenBy(string masterThumbprint) =>
        _trust.Count == 0 ? TeacherStatus.None
        : _trust.IsTrusted(masterThumbprint) ? TeacherStatus.You
        : TeacherStatus.Others;

    public const string OldTeacherProgram =
        "o Midview deste professor é antigo: atualize-o para liberar outro professor com código";

    public const string NeedsCode =
        "Esta máquina pede o código de \"Permitir novo professor\". Digite o código que apareceu na tela do colega e tente de novo.";

    public const string WrongCode =
        "O código do colega não confere. Confira o código de \"Permitir novo professor\" e tente de novo.";

    // AllowNewTeacher, from a trusted teacher (AgentServer checks that): its
    // payload is the code the new teacher will type. A Master from before
    // the code sends none, and opens nothing: anyone on the network could
    // walk in.
    public (bool Succeeded, string Detail) AllowNewTeacher(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return (false, OldTeacherProgram);
        }
        if (NewTeacherInvite.Decode(payload) is not { } code)
        {
            return (false, "pedido inválido");
        }

        lock (_gate)
        {
            var until = _now() + Pairing.NewTeacherWindow;
            _newTeacherUntil = until;
            _inviteCode = code;
            _inviteTries = 0;
            _windowGeneration++;
            AgentLog.Warn($"New-teacher pairing allowed until {until:HH:mm}, with a code.");
            return (true, NewTeacherInvite.WithCode($"liberada até {until:HH:mm}"));
        }
    }

    // ForgetOtherTeachers, from a trusted teacher (AgentServer checks that):
    // the sender becomes the only teacher, and an open window closes.
    public (bool Succeeded, string Detail) ForgetOtherTeachers(string senderThumbprint)
    {
        lock (_gate)
        {
            CloseWindow();
        }

        int dropped;
        try
        {
            lock (_writeGate)
            {
                dropped = TrustedMastersFile.KeepOnly(_trustedMastersPath, senderThumbprint);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, $"não foi possível gravar a lista de professores: {ErrorText.Describe(ex)}");
        }

        AgentLog.Warn($"Teacher {senderThumbprint} made this machine forget {dropped} other teacher(s).");
        return (true, OtherTeachers.ForgottenText(dropped));
    }

    // TrustTeacher, from a trusted teacher (AgentServer checks that): trust
    // the colleague that teacher let in with "Dar acesso a um colega".
    // Nothing shows on the student's screen: the colleague was checked by
    // the teacher's own program, with the code.
    public (bool Succeeded, string Detail) TrustTeacher(string senderThumbprint, byte[] payload)
    {
        if (TeacherGrant.Decode(payload) is not { } grant)
        {
            return (false, "identificação de professor inválida");
        }

        try
        {
            lock (_writeGate)
            {
                TrustedMastersFile.Add(_trustedMastersPath, grant.Thumbprint);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, $"não foi possível gravar o professor nesta máquina: {ErrorText.Describe(ex)}");
        }

        AgentLog.Warn($"Teacher {senderThumbprint} gave access to teacher {grant.Thumbprint} ({grant.Name}).");
        return (true, "liberada");
    }

    // Everything after a PairRequest, over that connection (receive and send
    // are its own). macAddress is the card the connection came in on. A
    // dropped connection surfaces as the receive's exception, after the
    // code has been taken down.
    public void Handle(
        string masterThumbprint, string agentThumbprint, byte[] requestPayload,
        Func<Message> receive, Action<Message> send, string? macAddress)
    {
        var teacherName = Pairing.CleanTeacherName(Encoding.UTF8.GetString(requestPayload));
        Window? window;
        lock (_gate)
        {
            var refusal = Refusal(masterThumbprint, out window);
            if (refusal is not null)
            {
                AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) refused: {refusal}");
                send(Pairing.Refused(refusal));
                return;
            }
            _busy = true;
        }

        var paired = false;
        try
        {
            paired = Exchange(masterThumbprint, agentThumbprint, teacherName, receive, send, macAddress, window);
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
                if (window is not null && window.Generation == _windowGeneration
                    && (paired || (window.CodeShown && ++_inviteTries >= NewTeacherInvite.MaxTries)))
                {
                    AgentLog.Warn(paired ? "The new teacher's code was used." : "Too many tries at the new teacher's code.");
                    CloseWindow();
                }
            }
        }
    }

    // The window a pairing came in by: its code, and whether the pairing
    // brought a code to the student's screen (a try).
    private sealed class Window(string code, int generation)
    {
        public string Code { get; } = code;

        public int Generation { get; } = generation;

        public bool CodeShown { get; set; }
    }

    private void CloseWindow()
    {
        _newTeacherUntil = null;
        _inviteCode = null;
        _inviteTries = 0;
        _windowGeneration++;
    }

    private string? Refusal(string masterThumbprint, out Window? window)
    {
        window = null;
        if (_busy)
        {
            return "Outro pareamento está em andamento nesta máquina.";
        }
        if (_trust.Count > 0 && !_trust.IsTrusted(masterThumbprint))
        {
            if (!WindowOpen() || _inviteCode is null)
            {
                return "Esta máquina já tem professor. Peça a um professor dela para clicar em \"Permitir novo professor\".";
            }
            window = new Window(_inviteCode, _windowGeneration);
        }
        if (!_display.SomeoneLoggedOn)
        {
            return "Ninguém está usando esta máquina agora. Entre com um usuário nela e tente de novo.";
        }
        return null;
    }

    private bool WindowOpen() => _newTeacherUntil is { } until && _now() < until;

    // Whether the teacher is now trusted.
    private bool Exchange(
        string masterThumbprint, string agentThumbprint, string teacherName,
        Func<Message> receive, Action<Message> send, string? macAddress, Window? window)
    {
        var agentNonce = Pairing.NewNonce();
        send(new Message(MessageType.PairCommit, Pairing.Commit(agentNonce)));

        var nonce = receive();
        if (nonce.Type != MessageType.PairNonce || nonce.Payload.Length != Pairing.NonceLength)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint}: expected the teacher's nonce, got {nonce.Type}.");
            return false;
        }

        // On the screen before the reveal: once the teacher has the code,
        // the student must be able to see it too.
        var code = Pairing.Code(masterThumbprint, agentThumbprint, agentNonce, nonce.Payload);
        using var shown = _display.Show(code, teacherName);
        if (shown is null)
        {
            send(Pairing.Refused("Não foi possível mostrar o código na tela do aluno. Tente de novo."));
            return false;
        }
        if (window is not null)
        {
            window.CodeShown = true;
        }

        var revealedAt = _now();
        send(new Message(MessageType.PairReveal, agentNonce));

        var answer = receive();
        if (answer.Type != MessageType.PairConfirm)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) cancelled ({answer.Type}).");
            return false;
        }
        if (_now() - revealedAt > Pairing.ConfirmTimeout)
        {
            send(Pairing.Refused("O tempo para conferir o código acabou. Tente de novo."));
            return false;
        }
        // A new teacher proves the window's code; anyone else's payload means
        // nothing (a first teacher, or one this machine trusts already).
        if (window is not null)
        {
            if (answer.Payload.Length == 0)
            {
                AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) refused: no new teacher's code.");
                send(Pairing.Refused(NeedsCode));
                return false;
            }
            if (!NewTeacherInvite.Verifies(window.Code, answer.Payload, masterThumbprint, agentThumbprint, agentNonce, nonce.Payload))
            {
                AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) refused: wrong new teacher's code.");
                send(Pairing.Refused(WrongCode));
                return false;
            }
        }

        try
        {
            lock (_writeGate)
            {
                TrustedMastersFile.Add(_trustedMastersPath, masterThumbprint);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            send(Pairing.Refused($"Não foi possível gravar o professor nesta máquina: {ErrorText.Describe(ex)}"));
            return false;
        }

        AgentLog.Warn($"Paired with teacher {masterThumbprint} ({teacherName}).");
        send(new Message(MessageType.PairDone, new PairingResult(_machineName, _sessionThumbprint(), macAddress).Encode()));
        return true;
    }
}
