using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

// Who may pair with this machine, and the Agent's side of the exchange (see
// Pairing). A machine with no teacher takes the first confirmed pairing; a
// teacher it already trusts may pair again (a lost classroom list); anyone
// else only while a trusted teacher's "Permitir novo professor" lasts, which
// ends with the first new teacher. One pairing at a time.
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

    // AllowNewTeacher, from a trusted teacher (AgentServer checks that).
    public (bool Succeeded, string Detail) AllowNewTeacher()
    {
        lock (_gate)
        {
            var until = _now() + Pairing.NewTeacherWindow;
            _newTeacherUntil = until;
            AgentLog.Warn($"New-teacher pairing allowed until {until:HH:mm}.");
            return (true, $"liberada até {until:HH:mm}");
        }
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
        bool usesWindow;
        lock (_gate)
        {
            var refusal = Refusal(masterThumbprint, out usesWindow);
            if (refusal is not null)
            {
                AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) refused: {refusal}");
                send(Pairing.Refused(refusal));
                return;
            }
            _busy = true;
        }

        try
        {
            Exchange(masterThumbprint, agentThumbprint, teacherName, receive, send, macAddress, usesWindow);
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    private string? Refusal(string masterThumbprint, out bool usesWindow)
    {
        usesWindow = false;
        if (_busy)
        {
            return "Outro pareamento está em andamento nesta máquina.";
        }
        if (_trust.Count > 0 && !_trust.IsTrusted(masterThumbprint))
        {
            if (!WindowOpen())
            {
                return "Esta máquina já tem professor. Peça a um professor dela para clicar em \"Permitir novo professor\".";
            }
            usesWindow = true;
        }
        if (!_display.SomeoneLoggedOn)
        {
            return "Ninguém está usando esta máquina agora. Entre com um usuário nela e tente de novo.";
        }
        return null;
    }

    private bool WindowOpen() => _newTeacherUntil is { } until && _now() < until;

    private void Exchange(
        string masterThumbprint, string agentThumbprint, string teacherName,
        Func<Message> receive, Action<Message> send, string? macAddress, bool usesWindow)
    {
        var agentNonce = Pairing.NewNonce();
        send(new Message(MessageType.PairCommit, Pairing.Commit(agentNonce)));

        var nonce = receive();
        if (nonce.Type != MessageType.PairNonce || nonce.Payload.Length != Pairing.NonceLength)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint}: expected the teacher's nonce, got {nonce.Type}.");
            return;
        }

        // On the screen before the reveal: once the teacher has the code,
        // the student must be able to see it too.
        var code = Pairing.Code(masterThumbprint, agentThumbprint, agentNonce, nonce.Payload);
        using var shown = _display.Show(code, teacherName);
        if (shown is null)
        {
            send(Pairing.Refused("Não foi possível mostrar o código na tela do aluno. Tente de novo."));
            return;
        }

        var revealedAt = _now();
        send(new Message(MessageType.PairReveal, agentNonce));

        var answer = receive();
        if (answer.Type != MessageType.PairConfirm)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) cancelled ({answer.Type}).");
            return;
        }
        if (_now() - revealedAt > Pairing.ConfirmTimeout)
        {
            send(Pairing.Refused("O tempo para conferir o código acabou. Tente de novo."));
            return;
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
            return;
        }

        if (usesWindow)
        {
            lock (_gate)
            {
                _newTeacherUntil = null;
            }
        }
        AgentLog.Warn($"Paired with teacher {masterThumbprint} ({teacherName}).");
        send(new Message(MessageType.PairDone, new PairingResult(_machineName, _sessionThumbprint(), macAddress).Encode()));
    }
}
