# Midview — Lab Installer (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24; replaced 2026-10-05 by 2026-10-05-installer-and-pairing-design.md (the setup program and pairing)

## Context

Until now a lab machine was set up by hand: publish the Agent and Session,
`sc create` the service, write the teacher's thumbprint into
`%ProgramData%\Midview\trusted-masters.json`, read the machine's two
thumbprints out of `agent.log`, and type them into the Master's "Adicionar /
testar máquina" window. Fine for one test machine, error-prone for 25. This
replaces it with a package on a USB stick and one double-click per machine.

## Design

- **The package** (`installer\Gerar-Pacote.ps1`, run on a PC with the .NET
  8 SDK) is a folder, `dist\Midview-Instalacao`:
  - `Instalar-Aluno.cmd`, `Desinstalar-Aluno.cmd`, `Instalar-Professor.cmd`,
    `LEIA-ME.txt` (the step by step, in Portuguese);
  - `programa\aluno` (Agent and Session, side by side), `programa\professor`
    (Master), both self-contained win-x64, so nothing needs .NET installed;
  - `programa\scripts\*.ps1`, which do the actual work;
  - `trusted-masters.json` and `maquinas\`, written later (below). Rebuilding
    into an existing package replaces only the programs and scripts.
- **Trust travels on the stick, not over the network.** In the Master, "Instalar
  nos alunos..." (`LabInstallWindow`) has two actions:
  - "Autorizar este professor no pacote" adds this Master's thumbprint to
    the package's `trusted-masters.json`, keeping other teachers already
    there (`InstallPackage.AuthorizeMaster`).
  - "Importar máquinas instaladas" reads the `maquinas\<computer>.json`
    file each install left behind and puts those machines in the classroom
    list: same name is replaced in place, new ones are added at the end in
    classroom order (Aluno-2 before Aluno-10), and bad files are listed and
    skipped (`InstallPackage.LoadInstalledMachines`, `MainWindow.ImportMachines`).

  Nothing is ever accepted on first contact over the LAN: the thumbprints
  come from the machine itself.
- **`Instalar-Aluno.cmd`** asks for administrator rights (`fltmc` check, then
  `Start-Process -Verb RunAs`, with a guard so a refused elevation can't
  loop) and runs `Instalar-Aluno.ps1`:
  1. stops the service and any running Session (an update);
  2. mirrors `programa\aluno` into `C:\Program Files\Midview` (the target
     must be named `Midview*`, since mirroring deletes whatever else is in
     it) and clears the "downloaded from the internet" mark;
  3. runs `Midview.Agent.exe --prepare-install`, which does what the
     service's first start does (lock down `%ProgramData%\Midview`, create
     the Agent's and Session's identities) and prints both thumbprints as
     JSON, so nothing waits on the service or parses `agent.log`;
  4. writes the package's teacher list as the machine's
     `trusted-masters.json`, into the already-protected directory. It
     replaces what was there, so removing a teacher from the package and
     reinstalling revokes them;
  5. creates the `MidviewAgent` service (or points the existing one at the
     new path), automatic start, restart on failure, and starts it;
  6. writes `maquinas\<computer>.json` to the package: computer name, IPv4
     addresses (adapters with a gateway first; never loopback or
     169.254.x.x), both thumbprints. If the stick is read-only it prints the
     same data to type in by hand. Running it again updates the
     installation; the identities, and so the file, stay the same.
- **Connecting by IP or by name:** the import uses the first address by
  default, since it works whatever the network profile or name resolution;
  a checkbox imports by computer name instead, for labs whose router
  reassigns addresses.
- **`Desinstalar-Aluno.cmd`** stops the service (which already re-enables
  keyboard and mouse), then runs `Midview.Agent.exe --uninstall-cleanup`
  *before* deleting anything (`UninstallCleanup`): re-enable input devices,
  remove Midview's hosts-file block, restore the firewall's default policy
  and remove the site-block rules, remove the two inbound rules, and save an
  empty block state. Each step runs even if an earlier one fails, and any
  failure is reported. Then it deletes the service and the program.
  `%ProgramData%\Midview` stays (a reinstall keeps the same identity)
  unless run with `-RemoverDados`.
- **`Instalar-Professor.cmd`** copies the Master to
  `%LOCALAPPDATA%\Programs\Midview Professor` and adds Desktop and Start
  menu shortcuts. It needs no administrator rights and refuses while the
  Master is running.
- **Windows PowerShell 5.1** is what lab machines have, so the scripts avoid
  7-only syntax. The builder writes them as UTF-8 with a BOM (5.1 reads a
  BOM-less `.ps1` as ANSI and garbles the Portuguese) and every text file with
  CRLF (cmd.exe misreads labels in LF-only batch files). The JSON files are
  written by hand from validated values: 5.1's `ConvertTo-Json` changes
  the shape of one-item lists. As a second guard, the Master also reads a
  single address written as a plain string.
- **Publish order:** Agent first, Session second, into the same folder, so
  the few runtime files both ship (`WindowsBase`, `System.Drawing`, the
  EventLog assemblies) end up as the Windows Desktop runtime's full,
  same-or-newer versions the Session needs.

## Out of scope

An MSI or other signed setup program, pushing the install over the network
(it still takes one visit per machine), automatic updates, and
installing the Master for all users of the teacher's PC.

## Testing

- `UninstallCleanupTests` (Agent): everything a lesson can leave behind is
  undone (input, hosts block, firewall, inbound rules, saved state); a
  failing step doesn't stop the rest and is reported; the
  `--prepare-install` JSON carries both thumbprints.
- `InstallPackageTests` (Master, Windows-only; also run on Linux in a scratch
  project): authorizing writes the list, keeps other teachers and never
  duplicates; an unreadable list is reported and left alone; import by
  address or by name, a single address as a string, classroom order, bad
  files reported one by one while the rest import, a missing Agent
  thumbprint still imports.
- Checked on Linux with PowerShell 7 (5.1 isn't available there): every
  script parses; the shared functions (`Comum.ps1`) run and their output
  (the teacher list, the machine files, including a name that needs JSON
  escaping) is read correctly by the C# side, and the list the Master writes
  is read correctly by the scripts; a dry run of the builder produced the
  full package, kept an existing `trusted-masters.json` and `maquinas\`, and
  wrote the expected encodings and line endings.
- The steps that touch Windows itself are live-tested only: elevation,
  service, firewall, robocopy, shortcuts, `--prepare-install` and
  `--uninstall-cleanup` for real.
