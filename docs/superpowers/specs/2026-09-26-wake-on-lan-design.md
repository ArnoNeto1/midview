# Midview — Turn Machines On Over the Network (Wake-on-LAN) (Design)

Date: 2026-09-26
Status: Implemented 2026-09-26 (not yet validated live)

## Context

The teacher can already shut down the lab from `MidviewMaster`
(`2026-09-24-shutdown-design.md`). This is the other half: turning the class,
or one machine, on before a lesson without walking to each one.

## Design

- **Wake-on-LAN.** A switched-off machine whose network card is set up for
  it turns on when a "magic packet" reaches its network: 6 bytes of `0xFF`,
  then the card's MAC address 16 times (`WakeOnLan`, in Protocol). The card
  looks for the pattern in any frame, so it's sent as UDP to port 9, the
  usual one. Nothing answers it: the machine coming online in the grid is
  the only confirmation.
- **Broadcast, from the Master** (`WakeOnLanSender`). A machine that's off
  has no IP address to send to. The packet goes to `255.255.255.255`, which
  Windows sends out of one card only, and to the broadcast address of each
  of the teacher PC's own IPv4 networks (`192.168.0.255` on a /24), so it
  leaves through every card. Every packet is sent 3 times, 100 ms apart.
  Only the teacher PC's own networks are reached: routers don't forward
  broadcasts, so the machines must be on the same network as the teacher.
- **Where the MAC address comes from.** The roster (`KnownMachine`) gains
  an optional `MacAddress` (`AA-BB-CC-DD-EE-FF`). It's filled in two ways:
  - **The installer's machine file.** `Instalar-Aluno` records the MAC
    address of the first card that's up and has a gateway (the same card
    `Get-EnderecosIPv4` puts first) as `"macAddress"`; importing the file
    brings it into the roster. That covers a lab installed and switched off
    before the teacher's Master ever saw it on.
  - **The Agent.** `MessageType.GetWakeAddress` (no payload) asks the Agent
    for the MAC address of the card the connection arrived through
    (`WakeAddressLookup`): by definition one on the teacher's network, with
    no guessing among Wi-Fi, VPN and virtual adapters. It answers
    `WakeAddress` with the 6 bytes, or empty if it can't tell. Each tile's
    `ThumbnailPoller` asks once, right after the first thumbnail (so only
    when the machine is known to be on, never costing an offline machine a
    timeout); a failure is retried after 5 minutes. An answer is only
    written to the roster when it's new or changed. So machines added by
    hand, machine files from older installers, and replaced network cards
    are all covered the first time the teacher sees the machine on. An
    Agent from before this answers `CommandAck`: taken as "doesn't know",
    not asked again.
- **Keeping it.** The add/edit window and a re-import don't carry the MAC
  address; replacing an entry keeps the one known as long as the host is
  the same (`KnownMachine.KeepingMacAddressOf`). A bad MAC address in the
  roster file or a machine file is dropped (learned again), never an error.
- **Master UI:** "Ligar a turma..." (toolbar) and "Ligar..." (a tile's
  right-click menu) open `WakeWindow`: what will happen, what the machine
  needs (power cable and network cable, same network, Wake-on-LAN in the
  BIOS), and a "Ligar" button. One line per machine: "pedido enviado (placa
  AA-BB-...)", or that its network card isn't known yet (turn it on once
  with the Master open, or reinstall with the current package). The
  machines are read when the button is clicked, so an address learned
  after the window opened is used.
- **What software can't do.** Whether a machine wakes depends on its BIOS
  (Wake on LAN on, ErP/deep sleep off) and its card's settings. The
  installer doesn't change either -- changing a card's power settings can
  restart it mid-install, and sleep-wake settings could wake machines on
  any traffic. `LEIA-ME.txt` lists what to check on each machine. Midview's
  own shutdown (`shutdown /s`) is a full shutdown, the state Wake-on-LAN
  from the BIOS is designed for; a machine that wakes after that but not
  after a Start-menu shutdown needs Fast Startup turned off.

## Out of scope

Waking machines on another network (directed broadcasts through a router),
scheduled wake-ups, and changing the BIOS or the network card's settings.

## Testing

- `WakeOnLanTests` (Protocol): the packet layout; formatting; parsing the
  common writings; refusing wrong lengths, non-hex, all-zero, broadcast and
  multicast addresses.
- `WakeAddressTests` (Agent): the card holding the connection's address is
  found, also through an IPv4-mapped IPv6 address; no card or a card
  without a usable address gives nothing; `GetWakeAddress` through a real
  loopback `AgentServer` answers with the card of the connection.
- Master: `WakeOnLanSenderTests` (broadcast targets from local networks;
  every machine's packet, 3 times, reaches a loopback receiver),
  `KnownMachineTests`, the MAC address in `KnownMachinesFileTests` and
  `InstallPackageTests`, and `ThumbnailPollerTests` against a fake Agent
  (learned once; an old Agent is asked once and nothing is learned; no Agent
  thumbprint, never asked).
- `Testar-Scripts.ps1`: the machine file with and without a MAC address,
  `ConvertTo-EnderecoMacNormalizado`, and `Get-EnderecoMac` on the CI
  machine.
- Actually waking a machine is live-tested only.
