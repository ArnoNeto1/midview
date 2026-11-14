<p align="center">
  <img src="assets/midview-logo.jpg" alt="Midview" width="140">
</p>

# Midview

[Português](README.md) · **English**

[![Latest release](https://img.shields.io/github/v/release/ArnoNeto1/midview?label=version&color=0e7490)](https://github.com/ArnoNeto1/midview/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ArnoNeto1/midview/total?label=downloads&color=6b7684)](https://github.com/ArnoNeto1/midview/releases)
[![Download the installer](https://img.shields.io/badge/⬇%20Download-Installer%20(.exe)-0e7490?style=for-the-badge)](https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewSetup.exe)

> The button above always downloads the newest version. The same installer
> works for the teacher's computer and for the students' machines.
> Website for teachers: https://arnoneto1.github.io/midview/en/

Midview manages a school's computer lab, and the school's Android phones and tablets too. From their own computer, the teacher sees and controls the students' machines, talks to the class, sends and collects files, blocks programs and websites, and turns the lab on or off. Everything runs over the school's local network, with no outside server. The teacher's computer uses the internet only to fetch Midview updates from GitHub, and optimized phones use a public ad-free DNS (see Privacy).

> **Language:** Midview's interface, installer and videos are in Brazilian Portuguese. In this page, each English name is followed by the label you will see in the app, in Portuguese.

## See it in action

[![Every Midview feature in under 2 minutes, narrated: the classroom with every screen live, in the dark look](docs/resumo-capa.jpg)](https://arnoneto1.github.io/midview/midview.mp4)

[Watch the narrated video](https://arnoneto1.github.io/midview/midview.mp4) (MP4, 1 min 36 s, narrated in Portuguese): every toolbar feature, in the dark look of version 0.0.41. Power (shut down, restart, turn on and schedule), Screen (view, control and lock screens), Class (message, chat, send and collect files, class folder), Programs and websites (open a website or program, install, uninstall, close and block programs, block websites) and Settings (lab panel and classroom machines). The machines and screens in it are made up.

### Full tutorial

A step-by-step, narrated walkthrough of every feature, in the dark look of version 0.0.41: installation, pairing, the classroom, power, screen, class, programs and websites, settings, the right-click menu and a classroom with two teachers.

[![Midview tutorial: the "Midview - Sala de aula" window in the dark look, with every screen live](docs/tutorial-capa.jpg)](https://arnoneto1.github.io/midview/tutorial.mp4)

[Watch the tutorial](https://arnoneto1.github.io/midview/tutorial.mp4) (MP4, 8 min, narrated in Portuguese).

## What the teacher can do

The "Midview - Sala de aula" (classroom) window shows every machine's screen in a grid. The toolbar buttons act on the selected machines, if any (the bottom bar says so, for example "Só na seleção (2)." (only on the selection)); with nothing selected, they act on every machine the grid shows (with the **Telas** button on **Todos**, the whole class). Right-clicking a machine acts on it, or on every selected one when it is part of the selection; once the menu closes, or the screen is opened with a double click, the selection goes back to what it was.

| Group | Features |
| --- | --- |
| **Power** (Energia) | **Shut down** (Desligar) and **Restart** (Reiniciar), with a 1- or 5-minute warning and, if you want, installing the pending Windows updates; **Turn on** (Ligar) over the network (Wake-on-LAN); **Schedule** (Agendar) the days and times to turn on and shut down. |
| **Screen** (Tela) | **View the screen** (Ver a tela) of a student (or double-click their tile), in a window shaped like their screen, with no black bands: the button bar sits over the picture and hides by itself (it comes back when the mouse touches the top edge), and **Full screen** (Tela Cheia) fills the whole monitor; **Control** (Controlar) the machine (the student's keyboard and mouse are locked and they see the notice "Este computador está sendo controlado pelo professor", "This computer is being controlled by the teacher", which all but disappears when the mouse comes near it, so it doesn't hide what's underneath). Control keeps working when the UAC screen (the administrator password prompt) appears: with the lab's password saved, **Fill in the administrator password** (Preencher a senha de administrador) types it into the UAC prompt in one click. **Lock screens** (Bloquear telas) with a padlock; while the screens are locked, if a student presses Ctrl+Alt+Del, the Windows security screen closes by itself, and while it shows it has no Task Manager, Sign out, Switch user, Lock, Change a password or shut down, restart and sleep button. |
| **Class** (Turma) | **Message** (Mensagem) on everyone's screen; **Chat** (Bate-papo) with the class (replies reach only the teacher); **Send file** (Enviar arquivo) to the "Arquivos do professor" (teacher's files) folder; **Collect files** (Recolher arquivos) from the "Entregar ao professor" (hand in to the teacher) folder; **Class folder** (Pasta da turma), a folder on the students' desktop where whatever each one downloads or saves shows up on every computer (with classes, lessons and up to two teacher PCs). During the lesson, downloads from any program go to the class folder, and Word, Excel, PowerPoint (2016 or newer), Notepad and Paint open "Save as" in it. |
| **Programs and websites** (Programas e sites) | **Open website** (Abrir site) (one, or up to 10 at once, separated by commas) and **Open program** (Abrir programa) on the machines; **Install program** (Instalar programa) on the whole class, as administrator, with no UAC screen and no password to type; **Uninstall program** (Desinstalar programa); **Close programs** (Fechar programas), with a warning and time for the student to save; **Block programs** (Bloquear programas) (a blocklist, or only the allowed ones); **Block websites** (Bloquear sites) (the whole internet or only some websites, still in effect after a restart). |
| **Settings** (Configurar) | **Lab panel** (Painel do laboratório) (Chrome and Windows policies, such as an ad blocker, blocking downloads, guest mode and locking the student's desktop); **Classroom machines** (Máquinas da sala), with the pages Find and pair (Procurar e parear: search the network and pair by checking a code within 5 minutes), Updates (Atualizações: update this computer and the machines), Configurar celular and Configurar tablet (prepare and look after the Android devices, see Phones and tablets), Another teacher (Outro professor: give a colleague access or join theirs), Administrator password (Senha de administrador: save the lab's password) and Advanced (Avançado). |

The right-click menu also has **Talk** (Conversar) to a single student, **Uninstall program** (Desinstalar programa), **Edit the machine** (Editar a máquina) and **Remove from the classroom list** (Remover da lista da sala). In the bottom bar, **Show all** (Visualizar todos) fits every screen, **Online only** (Só online) hides the machines that are off, and the slider changes the size of the screens (Ctrl + mouse wheel works too). **Arrange room** (Organizar sala) lets you drag the screens until they match how the machines sit in the room, and **Align** (Alinhar) straightens the grid.

## How it works

The same installer has two types:

- **Teacher** (Professor): installs the "Midview - Sala de aula" program, which runs without administrator rights.
- **Student** (Aluno): installs the **Midview Agent** service, which runs as system on each lab machine, and the program that shows messages, the screen lock and notices to the student.

The teacher finds the machines on the network and pairs each one by checking, within 5 minutes, a 6-digit code shown on the student's screen. After that, the machine only accepts paired teachers. Commands, screens and files travel encrypted with TLS and Midview's own certificates; only machine discovery and Wake-on-LAN use unencrypted UDP packets. Midview has no telemetry. The teacher's computer only connects to the internet to reach GitHub, to check for a new version and download it; the students' machines receive updates over the local network, and optimized phones use a public ad-free DNS (see Privacy).

Ports used on the local network:

| Port | Use |
| --- | --- |
| TCP 28900 | Commands to the student service |
| TCP 5901 | Screen and remote control |
| TCP 5902 | Screen and remote control on the secure desktop (UAC) |
| UDP 28901 | Finding machines on the network |
| TCP and UDP 28902 | Another teacher's access (invitation) |
| TCP 28903 | Class folder between two teacher PCs |
| UDP 9 | Turning machines on over the network |

## Requirements

- 64-bit Windows 10 or 11, on the teacher's computer and on the machines.
- Administrator rights to install.
- Teacher and machines on the same local network. For **Turn on**, Wake-on-LAN must be enabled in the machines' BIOS and network card.
- Internet only on the teacher's computer, and only to receive Midview updates.
- No need to install .NET: it ships inside Midview.

## Installation

1. Click **⬇ Download Installer (.exe)** at the top. You don't need to install .NET or anything else.
2. On the teacher's computer, open the installer and choose **Professor** (teacher). If the "Windows protected your PC" (O Windows protegeu o computador) screen appears, click **More info** (Mais informações) and then **Run anyway** (Executar assim mesmo). It creates the "Midview - Sala de aula" shortcut on the desktop.
3. On each lab machine, open the installer and choose **Aluno** (student).
4. In Midview, click **Máquinas da sala** > **Procurar e parear** > **Procurar na rede** (search the network), select the machines and click **Parear as selecionadas** (pair the selected ones). Check the 6-digit code shown on each student's screen (you have 5 minutes to do it).

Pair the machines right after installing: until the first pairing, any teacher on the network can pair them.

The installer shows the license and asks you to accept it. To install many machines at once, use the silent installation, which skips the installer's screens:

```powershell
MidviewSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /TIPO=aluno
```

Use `/TIPO=professor` on the teacher's computer. Without `/TIPO`, the silent installation installs nothing. Exit code 10 means the files were copied but the service could not be configured.

## Updating

- **Teacher's computer**: when a new version comes out on GitHub, Midview tells you as it opens. **Atualizar agora** (update now) downloads the installer, checks that it arrived intact, closes Midview, installs and opens it again on its own; **Agora não** (not now) leaves it for the next time Midview opens. You can do the same at any time in **Máquinas da sala** > **Atualizações** > **Atualizar professor** (update teacher). Windows asks for administrator permission once, and while Midview closes to install (1 or 2 minutes), the students' locked screens are released. You can also run the new version's installer over the old one.
- **Lab machines**: after the teacher's computer is updated, Midview asks whether to update the students. Or use **Máquinas da sala** > **Atualizações** > **Atualizar as máquinas pela rede...** (update the machines over the network). One machine is updated first and the others only after it confirms; if something fails, the machine goes back to the previous version on its own. Each machine leaves the grid for 1 to 3 minutes.

## More than one teacher

In **Máquinas da sala** > **Outro professor**:

- **Dar acesso a um colega...** (give a colleague access) shows a 9-character code, valid for 10 minutes. The colleague types the code in **Entrar na sala de um colega...** (join a colleague's classroom) and sees the same machines.
- **Permitir novo professor (10 minutos)** (allow a new teacher) shows an 8-character code. For 10 minutes, the machines accept the pairing of the teacher who types that code in **Procurar e parear** (search and pair), once on each machine.
- **Esquecer os outros professores...** (forget the other teachers) makes the room's machines accept only you.

Limits:

- A compromised machine in the room can reveal the **Permitir novo professor** code while it is valid (10 minutes).
- Any trusted teacher can forget all the others, including whoever set up the room.

## Phones and tablets

An Android phone or tablet (Android 10 or newer) shows up in the grid next to the computers and is prepared once, over a USB cable (see below). The **Telas** (screens) button picks what the grid shows: **Todos** (all), **Computadores** (computers), **Celulares** (phones) or **Tablets**. With phones or tablets in the selection, or in the grid, the toolbar buttons work on them too:

- **View the screen** (Ver a tela) and **Control** (Controlar): a click is a tap, dragging swipes, the mouse wheel scrolls, the keyboard types into the text field, and the window gets **Voltar**, **Início** and **Recentes**, Android's Back, Home and Recents buttons. The student sees the notice "Este celular está sendo controlado pelo professor" (this phone is being controlled by the teacher), which almost fades away when the mouse comes near. **Travar o toque do aluno** (lock the student's touch), on as soon as control opens, makes the device's screen and buttons ignore the student while the teacher controls it (that's why the preparation leaves navigation on the 3 buttons: screen gestures would get past the lock). The teacher sees the screen without the student having to tap "Iniciar agora" (start now), on Android 10 too.
- **Lock screens** (Bloquear telas) with the padlock.
- **Message** (Mensagem), **Chat** (Bate-papo), **Send file** (Enviar arquivo, to Download/Midview), **Collect files** (Recolher arquivos) and the **Class folder** (Pasta da turma, see below).
- **Open website** (Abrir site) in Chrome, **Open program** (Abrir programa), **Install program** (Instalar programa, an .apk file) and **Uninstall program** (Desinstalar programa, only apps Midview installed).
- **Close programs** (Fechar programas), with a warning and time for the student, as on the PCs.
- **Shut down** (Desligar) and **Restart** (Reiniciar) turn the screen off (the device stays on and connected), and **Schedule** (Agendar) turns the screens off on the chosen days and times.
- **Block programs** (Bloquear programas) shows the device's apps with their icons. **Bloquear só estes** (block only these) greys out the ticked apps so they don't open; **Liberar só estes** (allow only these) lets only the ticked ones open (Midview, the home screen, the phone app and Settings keep working).
- **Block websites** (Bloquear sites) is the same window as for the PCs. On the device, blocked websites don't open in Chrome, and the other browsers are hidden. **Bloquear a internet** (block the internet) cuts the internet for every app, and Midview keeps seeing the device.

The app and website blocks stay in effect if the device restarts.

Midview also always locks: the date, the time and the time zone (the device keeps setting them by itself from the network); other apps' windows over the screen and over the teacher's padlock (MIUI's floating windows, chat bubbles); **Wi-Fi** and **airplane mode** (the student can't change networks or turn the network off); and the wallpaper, when the school sets one.

The **Class folder** (Pasta da turma) covers the whole room, phones and tablets too. On the device it's Download/Midview/Pasta da turma – <class>, in the Files app, and what the student saves or moves into it shows up on every computer and device. Chrome still downloads to Download (Android has no way to send downloads to another folder): to share a download with the class, the student moves it into that folder. **Recolher arquivos** and **Apagar as pastas dos alunos** don't touch it. When the lesson ends, the files leave the folder and are kept in a hidden area, Download/Midview/.midview-turmas, until the teacher's Midview collects them in the next lesson. Anything nobody collects is removed after 30 days, or sooner if the area grows past 2 GB.

To receive files (**Enviar arquivo**, **Pasta da turma**, **Instalar programa** and the app update), the device must keep at least 100 MB free after receiving; an app or an update needs twice its size plus those 100 MB. Without that space the teacher reads "sem espaço no celular" (no space on the phone).

On Android 14 or newer, with the screen off, Midview may take a little longer to answer the teacher: from Android 14 on, Wi-Fi saves power with the screen off, and Midview can't prevent it.

### Configurar celular and Configurar tablet

In **Máquinas da sala** (classroom machines), the **Configurar celular** (set up phones) and **Configurar tablet** (set up tablets) pages prepare new devices and look after the ones already in the room. Each action works on the devices selected in the grid or, with no selection, on every phone (or tablet) in the room:

- **Otimizar** (optimize) hides the advertising apps that come from the factory and turns on an ad-free DNS, which removes most ads from websites and apps, when the network allows it. If the ad-free DNS doesn't work on the network, the device goes back to the usual DNS by itself and keeps its internet. With the internet blocked, it turns on by itself when unblocked.
- **Senha (PIN)** puts a 4-digit PIN on the lock screen that the student can't change or remove. The PIN isn't stored on the computer.
- **Wi-Fi e modo avião** (Wi-Fi and airplane mode) frees both for a few minutes, to change networks, or locks them again; when locked, the device goes back to the school's network. You can also set an **administrator password** (senha de administrador, 6 to 12 letters or digits): with it, a teacher frees Wi-Fi and airplane mode on the device itself, from Midview's notification, without the computer.
- **Papel de parede** (wallpaper) puts an image (JPG or PNG) on the home screen, the lock screen or both, and the student can't change it. The [Midview page for teachers](https://arnoneto1.github.io/midview/en/) has the phone and tablet rules images, ready to use.
- **Tocar para encontrar** (ring to find) rings the alarm at full volume for up to 1 minute, to find a lost device; on the device, the **Parar** (stop) button silences it.
- **Tirar o Midview** (remove Midview) has two choices, confirmed by typing a word: **Desfazer e tirar o Midview** (undo and remove Midview) keeps the photos and files, removes the school's locks and settings and uninstalls Midview; **Restaurar de fábrica** (factory reset) erases everything. With the administrator password, Midview can also be removed on the device itself, from Midview's notification ("Tirar o Midview deste celular").

In **Máquinas da sala** > **Atualizações** (updates), **Atualizar os celulares** and **Atualizar os tablets** send the new app version over the network, the same version as the teacher's Midview, downloaded from GitHub. One device is updated first, and the others only after it confirms the new version. The student doesn't need to tap anything and the blocks stay in effect; Android shows the student "Atualizado pelo seu administrador" (updated by your administrator).

The **Painel do laboratório** (lab panel) also works on the devices, where it makes sense: **Bloqueador de anúncios** (ad blocker) turns the ad-free DNS on or off, **Travar downloads** (lock downloads) makes Chrome download nothing (needs Chrome 131 or newer; what the teacher sends with **Enviar arquivo** still arrives), **Bloquear sites de distração** (block distracting sites) works through Chrome, and **RESET geral** (general reset) frees downloads and turns the ad-free DNS back on. The other options are for the computers only, and the window says so under each one when there are devices in the list.

### To prepare a phone or tablet

In **Máquinas da sala** > **Configurar celular** (or **Configurar tablet**), choose:

- **Midview otimizado** (optimized, recommended): installs Midview and makes the device lighter. It removes the apps a class doesn't use (factory tests, Xiaomi and Google advertising and telemetry, the Google app, Messages and, on a tablet, the phone app and the system's style packs), turns animations off, keeps heavy apps from running unseen and arranges the home screen: no icons on the pages, just the wallpaper, and at the bottom Chrome, Camera, Gallery (or Photos), Files and Calculator. On the Redmi 10C about 460 MB more memory was left free; on the Multilaser M10 tablet, the home screen stuttered four times less.
- **Só o Midview** (Midview only): installs Midview and leaves the rest of the device alone.

The assistant does everything over the USB cable, one device at a time, in about 1 minute each, and shows each step with pictures:

1. Leave the device without a screen lock. A factory reset isn't needed: reset only a device with an account nobody knows the password of, or one that once had a screen lock when the school will use Midview's **Senha (PIN)** (a device that once had a screen lock doesn't take that PIN).
2. Turn on **Developer options** and, inside them, **USB debugging**. On a Xiaomi, also turn on **USB debugging (Security settings)**, which only turns on with a Mi account signed in. With the **school's Mi account** saved in the assistant (only on this computer, protected by Windows), Midview signs out of it by itself at the end; without it, sign out by hand.
3. If the device's Chrome is old, the assistant opens the Play Store to update Chrome and the WebView (with a Google account on the device). Afterwards, sign out of the Google accounts: with any account on it, Android doesn't accept Midview as the device's owner.
4. Plug in the cable, tap **Allow** on the debugging prompt and type the device's number: 7 becomes "Celular 07" (or "Tablet 07"). Midview installs the app, connects to the lab's Wi-Fi, becomes the device's owner and adds it to the room, already paired.

The first time, Midview downloads Google's `adb` tool, which talks to the device over the cable, and checks it before use.

If the student closes all recent apps ("Limpar tudo" on a Xiaomi), Midview on the device comes back by itself within seconds (more slowly with the screen off).

## Uninstalling

In **Settings** > **Apps**, uninstall "Midview Professor" or "Midview Aluno". On a student machine, the uninstaller releases whatever was blocked, unlocks the desktop, gives back the download and save folders if a lesson is in progress, and asks whether to delete the paired teachers too.

The other **Lab panel** policies stay applied after uninstalling. To remove them, use **RESET geral** (general reset) in the panel before uninstalling.

## Privacy

Midview sends nothing to the developer. Between the teacher and the machines go the screen images, the text copied during remote control, the messages, the chat, the files sent and collected, the machines' names and MAC addresses, and the names of the programs opened or installed.

For updates, the teacher's computer queries GitHub (`api.github.com`), stating the installed Midview version, and downloads the installer and the phone app (`MidviewAgent.apk`) from there. No classroom data goes with it. The students' computers don't access the internet through Midview.

On optimized phones, Android looks up the addresses of websites and apps with a public ad-free DNS service, over an encrypted connection, instead of the school network's DNS. That service learns which addresses the phone looks up, but not the content of the pages. To turn this DNS on and check it, the phone app tests the internet through Android's connectivity-check address and through the service itself. Without optimizing, or if the network blocks this DNS, the phone uses its usual DNS.

A student's screen can be viewed with no notice on their machine; only remote control shows a notice. So the school must tell students and their parents or guardians that Midview is in use, as clause 6 of the [license](LICENSE) requires.

The school's Mi account, if saved in the preparation assistant, stays only on the teacher's computer, protected by Windows (user DPAPI); it's typed into the device over the USB cable only to sign out of the Mi account at the end of the preparation.

The lab's administrator password, if saved in **Máquinas da sala**, stays only on the teacher's computer, protected by Windows (user DPAPI), and is never sent to the students' machines. **Preencher a senha de administrador** types it into the controlled machine's UAC prompt only when the teacher clicks.

Some **Lab panel** options configure Google Chrome, which can then reach Google services on its own, for example to download the ad blocker.

## For developers

| Folder | Contents |
| --- | --- |
| `src/Midview.Master` | Teacher program (WPF) |
| `src/Midview.Agent` | Windows service on the students' machines |
| `src/Midview.Session` | Program that runs in the student's session: screen, control, messages and lock |
| `src/Midview.Desk` | Helper that shows and controls the secure desktop (UAC), fills in the administrator password and closes the Ctrl+Alt+Del screen while the screens are locked |
| `src/Midview.Android` | The phones' and tablets' app (Kotlin), the device's owner (Device Owner) |
| `src/Midview.Protocol` | Shared library: messages, TLS, pairing, discovery and updates |
| `tests/` | xUnit tests and the end-to-end test (`Midview.E2E`) |
| `installer/` | Installer (Inno Setup), machine scripts and their tests |
| `docs/superpowers/` | Specifications and plans for each feature (in Portuguese) |
| `site/` | Website for teachers, in Portuguese and in English (`site/en/`), published to GitHub Pages by the **Site** workflow |
| `promo/` | Promo videos and the tutorial, made with Remotion (the GIF and MP4 files in `docs/` come from it) |

Requirements: Windows, the .NET 8 SDK and, for the installer, Inno Setup 6.

```powershell
dotnet build Midview.sln --configuration Release
dotnet test Midview.sln --configuration Release --no-build
```

Some service tests need administrator rights and are skipped without them.

To build the installer into `dist\MidviewSetup.exe` and check it:

```powershell
powershell -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1
powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote dist
```

The version lives in `Directory.Build.props`. On GitHub, four workflows check every change: **Testes (Windows)**, **Instalador**, **Atualização pela rede (Windows de verdade)** and **Bloqueio de programas (Windows de verdade)**. A `vX.Y.Z` tag makes the **Instalador** workflow publish `MidviewSetup.exe` to the GitHub release, which is where the teacher's Midview updates from.

## License

Copyright © 2026 MidView. All rights reserved.

Midview is proprietary software, free to use for schools, under the terms of the [License](LICENSE) (in Portuguese). The third-party components included and their licenses are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Google's `adb`, which the teacher's Midview uses to prepare phones over the USB cable, is not in the setup: the first time the teacher prepares a phone, and only when asked, Midview downloads it from Google itself, checks the file and keeps it on that computer; it is under Google's terms.
