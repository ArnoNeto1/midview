# Midview — Setup Program and Pairing (Design)

Date: 2026-10-05
Status: Implemented 2026-10-05; used in the lab 2026-10-07 (installed, paired and reported
working by the teacher) and run end to end on a real Windows machine in CI

## Context

Today a lab is set up with a folder on a USB stick (`2026-09-24-lab-installer-design.md`):
`Instalar-Aluno.cmd` on each machine, the teacher's thumbprint carried in the
package's `trusted-masters.json`, each machine's thumbprints carried back in
`maquinas\<computer>.json`, and an "Importar máquinas instaladas" step in the
Master. It works, but it is a folder of scripts, the teacher has to be
authorized in the package before the first install, and nothing can be
handed to another school as one download.

This replaces it with one setup program with a
"Professor" and an "Aluno" option, and trust set up afterwards, from the
teacher's program, by pairing over the network.

## Goals

- One file, `MidviewSetup.exe` (its version in its file properties), installs either kind of
  computer, shows in "Aplicativos instalados", and uninstalls cleanly.
- A freshly installed student machine trusts nobody until a teacher pairs
  with it. Pairing needs no USB stick, no files and no thumbprints typed by
  hand.
- A teacher who pairs can tell when another computer is sitting in the middle
  of the connection.
- Machines installed from the USB stick keep working and keep trusting whom
  they trusted.

## The setup program

Built with **Inno Setup 6** (free, a single `setup.exe`, the standard wizard
and uninstaller).

- **License.** After the welcome page, the wizard shows the repository's
  `LICENSE` and asks to accept it (added 2026-10-20, 0.0.22); a silent install
  clicks through. `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` go in each
  program's folder, so the network update carries them too.
- **Choice of kind.** A page after the license page offers two options:
  - **Professor** — "o computador de onde você controla a sala". Installs the
    Master to `C:\Program Files\Midview Professor`, with a Desktop and Start
    menu shortcut for all users. It also installs a copy of the student
    program in `C:\Program Files\Midview Professor\aluno`, which is what the
    network update sends (below).
  - **Aluno** — "uma máquina do laboratório". Installs the Agent and Session to
    `C:\Program Files\Midview`, the folder used today, and sets up the
    `MidviewAgent` service.
- **One kind per computer.** The kind installed is recorded in the setup's
  uninstall registry entry. Running the setup again offers that same kind
  (an update); choosing the other kind is refused with "Este computador já
  tem o Midview <tipo>. Desinstale-o antes de instalar como <outro tipo>."
  The uninstall entry is named "Midview Professor" or "Midview Aluno".
- **Administrator rights** are required for both kinds (a service, and
  Program Files).
- **Silent install** for many machines: `/VERYSILENT /SUPPRESSMSGBOXES
  /TIPO=aluno` (or `/TIPO=professor`). Without `/TIPO`, a silent install
  fails, so it can't silently install the wrong kind. (Inno's own `/TYPE` is
  not used; the parameter is `/TIPO`.)
- **Aluno, step by step.** The service-handling logic in the scripts is
  already tested on Windows PowerShell 5.1, so it is reused. The scripts are
  installed in `C:\Program Files\Midview\instalador`:
  1. *Before copying files* (Inno's `PrepareToInstall`): `Parar-Aluno.ps1`
     (new, made of the existing functions) waits for a network update in
     progress to finish (`update.lock`, up to 2 minutes), then stops the
     service and any Session. If it fails, the setup stops with the
     script's message and nothing has been copied.
  2. Inno copies the files.
  3. *After copying*: `Configurar-Aluno.ps1` (today's `Instalar-Aluno.ps1`
     without the copy, the teacher list and the machine file) runs
     `Midview.Agent.exe --prepare-install`, points the service at this copy
     (creating it if needed, even if a network update had moved it to
     `Midview.update`), sets restart-on-failure, removes what network
     updates left behind and starts the service. `trusted-masters.json` is
     never written by the setup, so an update or a reinstall keeps the
     teachers the machine already trusts.
  4. The last page says: "Agora abra o Midview Professor no computador do
     professor e clique em 'Máquinas da sala' > 'Procurar na rede'."
- **Professor, step by step.** The Master is refused while it runs: the
  Master holds a named mutex, `Midview.Master`, and the setup's `AppMutex`
  asks to close it. The setup also removes what `Instalar-Professor.cmd`
  installed: the `%LOCALAPPDATA%\Programs\Midview Professor` folder and its
  shortcuts. The teacher's identity and classroom list
  (`%LOCALAPPDATA%\Midview`) are not touched, so the teacher keeps their
  machines.
- **Uninstall.**
  - *Aluno*: before files are removed, `Desinstalar-Aluno.ps1` runs. It is
    today's script without deleting the program folder, since the
    uninstaller does that: stop, `--uninstall-cleanup`, delete the service,
    remove `Midview.update` and the recovery task. The uninstaller then
    deletes the whole `C:\Program Files\Midview`, including files left by
    older USB-stick installs.
  - *Aluno*: the uninstaller asks "Apagar também os professores pareados e a
    identidade desta máquina?" (default *No*; a silent uninstall keeps them).
    *Yes* runs the script with `-RemoverDados`, so the machine starts from
    zero if installed again.
  - *Professor*: removes the program and shortcuts, never the teacher's
    `%LOCALAPPDATA%\Midview`.
- **Building it.** `installer\Gerar-Instalador.ps1` replaces
  `Gerar-Pacote.ps1`:
  - it publishes as today: Agent then Session into one folder, with
    `midview-build.json`, and the Master into another;
  - it runs Inno's compiler (`ISCC.exe`) on `installer\Midview.iss`, passing
    the version and the folders;
  - the output is `dist\MidviewSetup.exe`. The version Windows shows (the
    wizard, Installed apps, the properties of the setup and of the programs)
    is Midview's, set in `Directory.Build.props` (0.0.1 from 2026-10-07,
    0.0.2 to 0.0.30 from 2026-10-08);
    until 2026-10-07 the setup was `Midview-Instalador-<yyyy.MM.dd-HHmm>.exe`
    and its version that date and time;
  - `midview-build.json`'s label stays as today (date and commit): network
    updates compare that, not the version.
- **CI** (`pacote.yml`): builds the setup on `windows-latest` (installing
  Inno Setup with Chocolatey if the image lacks it) and checks the scripts
  under 5.1. It keeps the setup as a download (7 days) on a manual run, and
  attaches it to a GitHub Release when a `v*` tag is pushed. The repository
  is private, so those downloads are for its members; the `.exe` itself can
  be shared any way.

## Pairing

### Who a machine accepts

The Agent accepts a pairing request when one of these holds:

1. it trusts no teacher yet. The first confirmed pairing wins;
2. the requesting teacher is already trusted. This repairs a lost classroom
   list;
3. a trusted teacher opened the **new-teacher window**: "Permitir novo
   professor", 10 minutes. It is kept in memory only, so a restart of the
   service closes it, and it closes after one new teacher pairs.

Otherwise it refuses with "Esta máquina já tem professor. Peça a um professor
dela para clicar em 'Permitir novo professor'." The Agent runs one pairing at a
time. A second request meanwhile is refused with "Outro pareamento está em
andamento nesta máquina."

### The exchange

Over the Agent's existing TLS port (28900). Both sides present their usual
certificates. For this exchange only, each side accepts a certificate it
doesn't know yet, and the code binds the two certificates together:

1. Master → Agent `PairRequest`: the teacher's computer name, shown to the
   student, for information only.
2. The Agent checks the rules above and whether someone is logged on (the
   code has to appear somewhere). It picks a 32-byte random `Na` and answers
   `PairCommit` with SHA-256(`Na`), or `PairRefused` with the reason, in
   Portuguese.
3. Master → Agent `PairNonce`: a 32-byte random `Nm`.
4. Agent → Master `PairReveal`: `Na`. Now both compute the code, and the
   Agent shows it on the student's screen. The Master checks that `Na`
   matches the commitment, then shows the code to the teacher.
5. The teacher compares, then Master → Agent `PairConfirm` or `PairCancel`.
   The teacher has 5 minutes; after that the Agent cancels.
6. On confirm, the Agent adds the teacher's thumbprint to
   `trusted-masters.json`. The write is atomic, keeps the others and never
   duplicates. The Agent logs the pairing and answers `PairDone`, which
   carries the Session thumbprint, the computer name and the MAC address of
   the card the connection came in on (for Wake-on-LAN). If saving fails,
   the answer is `PairRefused` with the reason.

**The code** is SHA-256("Midview pairing v1" ‖ master thumbprint ‖ agent
thumbprint ‖ `Na` ‖ `Nm`): the first 4 bytes, big-endian, mod 1,000,000,
shown as two groups of three digits ("482 913"). Thumbprints are hex,
uppercase, ASCII.

**Why it works.** A computer in the middle has two TLS sessions, one with
each end, so the two ends see different certificate pairs and so different
codes. The commitment stops it from choosing certificates or nonces until a
pair of codes match: the Agent's nonce is fixed before the Master's is
known, and vice versa on the other session. A match by chance is 1 in a
million.

**What it protects, and what it doesn't.** The code protects the
*teacher*: if it doesn't match, the teacher clicks "Não confere", nothing is
trusted, and the Master says "O código não conferiu. Pode haver outro
computador no meio da conexão. Tente de novo e, se repetir, avise o
responsável pela rede." The student's side confirms nothing. A machine with
no teacher yet accepts whoever confirms first, which is the limitation
accepted below.

### The code on the student's screen

The Agent, as SYSTEM, can't show windows itself. It starts
`Midview.Session.exe --pairing-code <code> --from <teacher's computer name>`
in the active console session, using the existing `UserSessionProcessLauncher`
with arguments added. When the pairing ends (done, cancelled, timed out, or
the connection drops), it ends that process. The process also exits by
itself after 5 minutes.

The window: topmost, centered, no close button, in the screen lock's grey.
Its text:

> **Pareamento com o professor**
> O computador **PROF-01** está conectando esta máquina ao Midview.
> **482 913**
> O professor confere se o mesmo código aparece na tela dele.

With nobody logged on, the Agent refuses the pairing with "Ninguém está
usando esta máquina agora. Entre com um usuário nela e tente de novo."

### Trust takes effect without a restart

- `TrustStore` gets a file-backed form: it re-reads `trusted-masters.json`
  when the file's write time or size changes, checked when a certificate is
  validated. The Agent and the Session both use it.
- **The Session starts with no teacher.** Today it throws, and the
  supervisor relaunches it in a loop. Now it starts, refuses every
  connection until a teacher is trusted, and takes the new teacher on that
  teacher's next connection.
- **The Agent accepts any client certificate at the TLS handshake** and
  decides after it. A trusted teacher gets every command, as today. An
  unknown one gets `PairRequest` only. Any other message from an unknown
  teacher closes the connection unanswered and is logged. This gate is what
  keeps strangers out now, so it gets its own tests.

### Allowing a new teacher

`AllowNewTeacher`, a trusted-only command, opens the window on that machine
and answers with a `CommandResult` "liberada até 14:32". The Master sends it to
the whole classroom list and reports how many machines answered.

## Finding machines on the network

- **The Agent listens on UDP 28901.** The firewall rule
  `MidviewAgentDiscovery` (UDP, inbound) is added on start like the two TCP
  ones, is listed in `InboundRuleNames`, and so is removed on uninstall. The
  internet block already allows the local network outbound, so replies get
  through.
- **The Master broadcasts a probe** to the same targets as Wake-on-LAN
  (`WakeOnLanSender.BroadcastTargets`: 255.255.255.255 and each local
  network's broadcast). The probe is
  `{"midview":1,"probe":"<guid>","master":"<thumbprint>"}`. The Master
  collects replies for 2 seconds; Windows lets unicast replies to a
  broadcast in for 3 seconds by default.
- **The reply** goes back to the sender:
  `{"midview":1,"probe":"<guid>","name":"ALUNO-01","agent":"<thumbprint>","teachers":"none|you|others","newTeacherOpen":true|false}`.
  The address is the reply's source address.
- Packets over 2 KB, without `"midview":1`, or not valid JSON are ignored.
  A probe gets one reply, about the probe's size, so it can't amplify
  traffic.
- **A reply is a hint, never trust.** It tells the teacher what is there.
  Identity comes only from pairing, or from the pinned thumbprints already
  in the roster. For the same reason, a machine already in the roster
  (matched by agent thumbprint) whose address changed gets the new address.
  Connecting there still checks the pinned thumbprint, so a false reply
  can only make it unreachable.
- Machines still on a USB-stick build don't answer until they are updated
  over the network. They are already in the roster.

## The teacher's program

- **"Máquinas da sala"** replaces "Instalar nos alunos" in the toolbar's
  Configurar group (`MachinesWindow` replaces `LabInstallWindow`). It has
  three parts:
  1. **Procurar e parear.** "Procurar na rede" lists what answered: name,
     address and status, which is "Sem professor", "Pareada com você", "Com
     outro professor", "Com outro professor — aceitando novo professor" or
     "Já está na sala". Each row has a checkbox, checked by default for "Sem
     professor". "Parear as selecionadas" starts every selected pairing at
     once. Each row then shows its code and two buttons, "Confere" and "Não
     confere", or the refusal. A confirmed machine goes into the classroom
     list. It replaces an entry with the same agent thumbprint, or else the
     same name; otherwise it is added in classroom order (Aluno-2 before
     Aluno-10), as the import does today. Closing the window cancels
     pairings still waiting.
  2. **Atualizar as máquinas.** Shows "Versão do programa do aluno neste PC:
     <build>" and opens the existing `UpdateWindow` on the bundled `aluno`
     folder, found next to the Master's own executable. Without that
     folder, as in a development build, the button is disabled with "Esta
     cópia não traz o programa do aluno. Instale pelo MidviewSetup."
     To update a class: run the new setup on the teacher's PC, then click
     this.
  3. **Permitir novo professor.** Sends `AllowNewTeacher` to the class, after
     confirming "Por 10 minutos, as máquinas da sala vão aceitar parear com
     outro professor. Use quando um colega for parear o computador dele."
- **An empty classroom list** shows "Nenhuma máquina na sala ainda." with a
  "Procurar máquinas na rede" button, which opens "Máquinas da sala" and
  searches at once.
- "Ajustes avançados" stays as it is, for typing a machine in by hand.

## What goes away

- `installer\pacote\` (the three `.cmd` files and `LEIA-ME.txt`),
  `Instalar-Professor.ps1`, and `Instalar-Aluno.ps1`, which becomes
  `Configurar-Aluno.ps1`.
- In the Master: `LabInstallWindow`, `InstallPackage`, "Autorizar este
  professor no pacote" and "Importar máquinas instaladas".
- The machine files and the package's `trusted-masters.json`. `UpdatePackage`
  takes the student program's folder directly, instead of a package folder.

## Limitations accepted

- **Between installing a machine and pairing it, anyone could pair it
  first**, for example a student with the teacher's program on a notebook.
  They would need the Midview Professor program, and the code appears on that
  machine's own screen. The risk closes once the teacher pairs, so pair
  right after installing.
- **No digital signature.** Windows shows "O Windows protegeu o
  computador" when the setup is opened. The way through is "Mais
  informações" > "Executar assim mesmo".
- **Finding machines works on the same local network only**, like "Ligar".
  Machines elsewhere can still be typed in through "Ajustes avançados".
- **The code needs someone logged on** to the student machine.
- **Removing a teacher from a machine** means uninstalling it and answering
  *Yes* to "Apagar também os professores pareados".

## Testing

- **Protocol:** the code against fixed vectors; any change to a
  thumbprint or a nonce changes the code; formatting ("000 042"); the
  commitment check. Discovery messages: round-trip, and wrong magic,
  oversize or bad JSON ignored. The file-backed `TrustStore` reloads after
  the file changes and reads it only then. Adding a teacher keeps the
  others, never duplicates, and writes atomically.
- **Agent** (`PairingService`, fake clock and fake code display): with no
  teacher, the first confirmed pairing is accepted. With a teacher, an
  unknown master is refused; it is accepted inside the 10-minute window,
  and the window then closes. It also closes when it expires. An already
  trusted master can re-pair. One pairing at a time. Cancel, timeout and a
  dropped connection close the code display and trust nothing. Nobody
  logged on means a refusal.
- **Agent over real TLS on loopback:** a full pairing makes the master
  trusted for commands. An unknown master sending `Power`, `SetAppBlock`,
  `UpdateOffer` or a site-block command is cut off, and nothing happens. A
  wrong `Na` is caught by the Master. The discovery responder answers a
  probe on loopback with the right status for "none", "you" and "others".
- **Session:** it starts with no teacher and refuses connections; it
  accepts the teacher once the file names them, without a restart.
- **Master:** discovery replies merged and de-duplicated; statuses against
  the roster; a confirmed pairing replaces by agent thumbprint, then by
  name, else inserts in classroom order; addresses refreshed by agent
  thumbprint; `UpdatePackage` from the bundled folder.
- **Installer:** CI compiles the `.iss`, and the scripts parse and their
  shared functions run under 5.1 (`Testar-Scripts.ps1`, adapted).
- **Live only:** both kinds of install, refusing the other kind, silent
  install, update over an existing USB-stick install, uninstall with and
  without removing data, the code window on a real student session,
  pairing a class, discovery across the lab's switch, and a network update
  from the bundled program.

## Order of work

Each step leaves the program working and is tested before the next:

1. File-backed trust, and the Session starting with no teacher.
2. The pairing exchange: Agent gate, `PairingService`, code window,
   `AllowNewTeacher`.
3. Discovery: Agent responder and firewall rule; Master client.
4. Master: "Máquinas da sala", the empty-list button, update from the
   bundled program. Remove the package flow.
5. The setup program: `Midview.iss`, the scripts, `Gerar-Instalador.ps1`,
   CI.
