# Midview — Giving a Colleague the Class by Invite Code (Design)

Date: 2026-10-14
Status: Implemented 2026-10-14 (not yet validated live)

## Context

Today a second teacher gets a class the long way
(`2026-10-05-installer-and-pairing-design.md`). The first teacher clicks
"Permitir novo professor", which opens a 10-minute window on every machine.
The second teacher then pairs the machines one by one, checking a code on
each student's screen, and the window on a machine closes after one teacher
pairs through it.

The teacher who already has the class should be able to hand it over in one
step instead. The colleague asks, the teacher reads out a code, the colleague
types it and gets every machine, and nothing shows on the students' screens.

What there is today, and what that means:

- **The Master never listens on the network.** It only makes outbound
  TLS connections, the UDP discovery probe and Wake-on-LAN packets. No
  teacher PC has an inbound firewall rule; the setup adds none for
  "Professor".
- **Only the Agent writes trust**, in `%ProgramData%\Midview\trusted-masters.json`,
  and only at the end of a pairing (`PairingService` calls
  `TrustedMastersFile.Add`). No message lets a trusted teacher add another,
  and none removes one.
- **The Session follows that file** (`TrustStore.FromFile` reloads it when
  it changes), so a teacher the Agent adds is accepted by both on their next
  connection.
- **The classroom list** (`%LOCALAPPDATA%\Midview\known-machines.json`) already
  holds everything the colleague needs for each machine: `Name`,
  `HostAddress`, `SessionThumbprint`, `AgentThumbprint`, `MacAddress`. Only
  the teacher who paired has the Session thumbprint (it came in `PairDone`).
- **An old Agent answers a message type it doesn't know** with a one-byte
  failure (`CommandAck [0]`), which the Master can show as "Midview antigo".

## Goals

- A teacher who has a class (A) gives it to a colleague (B) with one code.
  B gets every machine A has, in A's classroom order, without pairing any of
  them.
- Nothing appears on the students' screens.
- The code is short enough to read out or send by message, is good for 10
  minutes and one use, and works only while A's program is open.
- A computer in the middle of the connection, or someone who guesses at the
  code, gets nothing.

## How it works for the teachers

**Teacher A** opens "Máquinas da sala" and, in the "Outro professor"
section, clicks a new button, **"Dar acesso a um colega"**, next to
"Permitir novo professor (10 minutos)". The window "Dar acesso a um colega"
says:

> Passe este código ao colega:
> **K7P Q2M 94R**
> Vale por 10 minutos, uma vez. Deixe esta janela aberta até ele entrar.
> Vai liberar as 25 máquinas da sua sala.

It has a "Cancelar" button, and a countdown ("Expira às 14:32"). Closing the
window cancels the invite. Only one invite is open at a time: the button
brings the open window back.

**Teacher B** clicks **"Entrar na sala de um colega"**, which is in
"Máquinas da sala" ("Procurar e parear" section) and in the empty-room panel
("Nenhuma máquina na sala ainda."), next to "Procurar máquinas na rede". The
window "Entrar na sala de um colega" has one field, "Código do convite", and
an "Entrar" button. Then:

1. B's program finds A's on the network by itself: "Procurando o professor
   na rede..."
2. "Conectando a PROF-01..." then "PROF-01 está liberando as máquinas..."
3. A's program sends B's identity to every machine in A's list, and then
   sends B the list of the machines that accepted.
4. B's tiles appear, and the window says "Pronto: 23 de 25 máquinas na sua
   sala. Não liberadas: ALUNO-07 (não respondeu), ALUNO-12 (Midview antigo:
   atualize-a)."
5. A's window says "PROF-02 recebeu acesso a 23 de 25 máquinas." and the
   code stops working.

**The code is the permission.** A doesn't confirm when B arrives; A agreed
by reading out the code.

A machine that was off or didn't answer is left out. A can open a new
invite later: the machines B already has are neither duplicated nor
disturbed. "Permitir novo professor", with a code on each machine, stays as
it is, for when A isn't around.

## The code

Nine characters from the Crockford Base32 alphabet
(`0123456789ABCDEFGHJKMNPQRSTVWXYZ`: no I, L, O or U), shown in three groups
of three, "K7P Q2M 94R". Typed input is cleaned before use: case is ignored,
spaces and dashes are dropped, O is read as 0, and I and L as 1. Anything
else that isn't nine alphabet characters gets "O código tem 9 letras e
números, como K7P Q2M 94R." before anything goes on the network.

The 45 bits are two parts:

- **The secret** `s`: the first 4 characters (20 bits), random
  (`RandomNumberGenerator`).
- **The check** `v`: the last 5 characters (25 bits) = the first 25 bits of
  SHA-256("Midview convite v1" ‖ A's certificate thumbprint ‖ `s`), where the
  thumbprint is hex, uppercase, ASCII, and `s` is its 4 characters, ASCII.

So the code ties `s` to A's certificate. B can check, from the code alone,
that the program it reached is A's, before it sends anything.

## The exchange

Port **28902**, TCP and UDP, on A's PC, open only while A's invite is.

1. **A opens the invite.** A's Master makes the code and starts listening on
   UDP 28902 and TCP 28902 (`TcpListener` on all addresses). It stops when
   the invite ends: used, cancelled, wrong code, expired (10 minutes), or
   its window closed.
2. **B looks for A.** B's Master broadcasts
   `{"midview":1,"invite":"<guid>"}` to UDP 28902 on the same targets as
   Wake-on-LAN and discovery (`WakeOnLanSender.BroadcastTargets`), and
   collects replies for 2 seconds. A Master with an open invite replies to
   the sender with `{"midview":1,"invite":"<guid>","name":"PROF-01","port":28902}`.
   B connects to the reply's source address on that port. (A false reply
   can only point B at a certificate the code doesn't match.) Packets over 2 KB, without
   `"midview":1`, with another guid, or not valid JSON are ignored. A probe
   gets one reply, about its size, so it can't amplify traffic. Nothing
   derived from the code is ever broadcast.
3. **B checks who answered.** For each reply (at most 10, in the order they
   came), B connects to the port it named with its own certificate
   (`SecureChannel.AuthenticateAsClientAcceptingAnyServer`; A uses
   `AuthenticateAsServerAcceptingAnyClient`). With the thumbprint of the
   certificate it got, B computes `v` from the `s` that was typed. If it
   doesn't match the typed `v`, B closes the connection without sending
   anything and tries the next reply. If none matches: "Nenhum professor na
   rede tem esse código aberto. Confira o código com o colega e se o
   Midview Professor dele está aberto, com o código na tela."
4. **B sends the secret.** `InviteJoin`: `s` and B's computer name (shown to
   A, cleaned like the pairing's teacher name, `Pairing.CleanTeacherName`).
5. **A checks it.** A compares `s` with its own in constant time
   (`CryptographicOperations.FixedTimeEquals`), and only then looks at who
   B is:
   - Wrong: A answers `InviteRefused` "Código errado. O convite foi
     cancelado; peça outro ao colega.", ends the invite, and its window
     says "Alguém na rede tentou entrar com um código errado. O convite foi
     cancelado. Gere outro se ainda precisar." An honest typo never gets
     here (step 3 catches it on B's side), so a wrong secret means someone
     guessing.
   - B's certificate is A's own (the same thumbprint, as when
     `master-identity.pfx` was copied): `InviteRefused` "Este computador já
     usa a mesma identidade de professor que PROF-01; ele já tem acesso às
     mesmas máquinas." The invite stays open.
   - Right: A's window shows "PROF-02 entrou. Liberando as máquinas..." and
     A's Master sends `TrustTeacher` (below) to every machine in its
     classroom list, in parallel, the way "Permitir novo professor" sends
     `AllowNewTeacher`: `CommandClient.SendCommandForResult`, pinned to each
     machine's Agent thumbprint. A machine without one (typed in by hand
     in "Ajustes avançados") is left out with "sem a identificação do
     Agent: pareie-a de novo".
6. **A answers** `InviteDone`: the machines whose Agent accepted, each with
   `Name`, `HostAddress`, `SessionThumbprint`, `AgentThumbprint` and
   `MacAddress` as in A's list, in A's order; and the ones left out, each
   with its name and the reason, in Portuguese. Then the invite ends.

B waits up to 90 seconds for the answer after sending `InviteJoin` (25
machines answer in parallel, each within `CommandClient`'s timeouts). A
gives each connection 30 seconds to send `InviteJoin` and handles one
connection at a time; a second one meanwhile is closed unanswered. Before
the secret is checked, A reads at most one message of at most 4 KB.

**B takes the list.** B checks each entry like `KnownMachinesFile.Load`
does (name, address and Session thumbprint required, MAC normalized), places
each one with `Roster.Place` (replace by Agent thumbprint, else by name,
else in classroom order; a MAC B already knows is kept), and saves the
classroom list once. The tiles appear as for a paired machine.

## On the student machine

**`TrustTeacher`**, a trusted-only Agent command:

- Payload: the new teacher's thumbprint (40 hex characters) and computer
  name. Anything else is refused with "identificação de professor
  inválida" (lower case, like the Agent's other details: the Master shows
  it as "ALUNO-07 (identificação de professor inválida)").
- It is handled after the existing trust gate in `AgentServer`, so only a
  teacher the machine already trusts can send it; from anyone else the
  connection is closed unanswered and logged, as today.
- It adds the thumbprint with `TrustedMastersFile.Add` (atomic, keeps the
  others, never duplicates). The Session takes the new teacher on its next
  connection through the file, with no restart.
- It logs "Teacher {sender thumbprint} gave access to teacher {thumbprint}
  ({name})" and answers `CommandResult` (true, "liberada").
- If saving fails, it answers `CommandResult` (false, the reason).
- An Agent from before this change answers `CommandAck [0]`; A's Master
  reports that machine as "Midview antigo: atualize-a".

This gives a trusted teacher no power it lacks today: it can already open
the new-teacher window and pair anyone, or send a network update.

## Messages

New `MessageType` values, each with a comment saying who sends it, the
reply, and what an older program does with it:

| Value | Name             | From → to | Payload |
|-------|------------------|-----------|---------|
| 61    | `TrustTeacher`   | Master → Agent | thumbprint, computer name |
| 62    | `InviteJoin`     | B → A | `s`, B's computer name |
| 63    | `InviteDone`     | A → B | machines given, machines left out with reasons |
| 64    | `InviteRefused`  | A → B | the reason, in Portuguese |

Payloads are JSON, like `PairingResult`. The code, the check, the UDP probe
and reply, and the payload codecs live in `Midview.Protocol`
(`TeacherInvite.cs`); `InviteDone`'s machine entries are a Protocol record
that the Master converts to `KnownMachine`.

## Why it is safe

- **B reveals the secret only to A's real program.** A computer in the
  middle has to present its own certificate; the check computed with it
  doesn't match, and B sends nothing. It can't present A's certificate
  without A's private key, which TLS proves.
- **Guessing at A:** someone who connects to A directly has to send the
  secret: 1 chance in about a million (2^20), and the first wrong guess ends
  the invite.
- **Pretending to be A to B:** a fake reply has to come with a certificate
  whose check matches a code it has never seen: 1 chance in about 33
  million (2^25) for each of the at most 10 B tries.
- **Listening in** gets nothing: the UDP probe and reply carry no part of
  the code, and everything else is inside TLS.
- **B's identity** is the certificate B used in the TLS session where the
  secret arrived. Since only A's real program ever sees the secret, A can't
  be made to give the class to anyone else.
- **The list B gets** comes over that same session, from the certificate
  the code points to, so B pins the real Agent and Session thumbprints.

## The teacher's PC on the network

- The Master stays non-elevated, so it can't add a firewall rule itself.
  **The setup does, for "Professor"**: at the end of the install (Inno
  `ssPostInstall`, the "Professor" branch, which today does nothing) it
  deletes then adds two inbound rules, `MidviewProfessorConvite` (TCP 28902)
  and `MidviewProfessorConviteBusca` (UDP 28902), each limited to
  `{app}\Midview.Master.exe` and `remoteip=LocalSubnet`. Uninstalling
  "Professor" deletes both.
- The port is open only while an invite window is, at most 10 minutes. A
  listener that can't start (port in use) says so in A's window: "Não foi
  possível abrir o convite neste computador: <motivo>."
- Only A needs the rule; B only connects out, and Windows lets in the unicast
  reply to B's broadcast, as with discovery.

## Out of scope

- **Removing a teacher** from the machines. It still means uninstalling with
  "Apagar também os professores pareados" (the file has no remove, and
  `TrustStore`'s change check relies on the file growing).
- **Choosing which machines to give.** B gets the whole list.
- **Machines added to A's list later**, or off at the time: A opens a new
  invite.
- **Different networks.** Finding A works on the same local network only,
  like "Procurar na rede" and "Ligar".

## Limitations accepted

- Whoever has the code in those 10 minutes gets the class, like a password.
  A should pass it only to the colleague.
- A's program has to stay open, with the invite window, until B enters.
- Both teachers need this version: B for the window, A for the listener and,
  through the setup, the firewall rule. The students need it for
  `TrustTeacher`, through the network update.

## Testing

- **Protocol:** reading the code (case, spaces, dashes, O→0, I/L→1, wrong
  length or characters refused); a new code is 9 alphabet characters and its
  check matches; the check against fixed reference values; changing one
  character of the thumbprint or of `s` changes the check; the UDP probe and
  reply round-trip, and oversized, wrong-magic, wrong-guid or bad-JSON
  packets are ignored; `InviteJoin`/`InviteDone`/`InviteRefused` payloads
  round-trip and invalid machine entries are rejected.
- **Agent, over real TLS on loopback:** `TrustTeacher` from a trusted
  teacher makes the new thumbprint trusted, and the new teacher's next
  command is accepted; from an untrusted teacher it gets no reply and
  nothing changes (added to the `AgentPairingTests` theory); a malformed
  thumbprint is refused; adding twice keeps one entry.
- **Master, `InviteHost` and `InviteJoin` over loopback** (port 0, the
  machine fan-out replaced by a fake): the right code gives B the list in
  A's order, with the left-out machines and reasons; a wrong secret is
  refused and ends the invite; a code whose check doesn't match A's
  certificate makes B close without sending `InviteJoin`; an expired or
  cancelled invite refuses; a second connection during the first is closed;
  the same thumbprint on both sides is refused and the invite stays open;
  the probe gets a reply only while the invite is open.
- **Master, the list:** entries from `InviteDone` placed by Agent thumbprint,
  then by name, else in classroom order, saved once.
- **Installer:** CI compiles the `.iss` with the new rules.
- **Live only:** two teacher PCs on the lab network with the firewall rules,
  a class of 25, one machine off and one on an old version; a wrong code; the
  window closed in the middle.

## Order of work

Each step leaves the program working and is tested before the next:

1. Protocol (`TeacherInvite`, message types) and `TrustTeacher` in the
   Agent.
2. `InviteHost` and `InviteJoin` in the Master, without windows.
3. The two windows, the buttons, and taking the list into the classroom.
4. The setup's firewall rules, and CI.
