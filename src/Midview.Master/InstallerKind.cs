using System.IO;
using System.Text;

namespace Midview.Master;

public enum InstallerKind
{
    Msi,
    BatchScript,
    InnoSetup,
    Nsis,
    WixBurn,
    Unknown,
}

// Which engine built an installer, read from the file on the teacher's PC, so
// the "Instalar programa" window can offer the switches that make it install
// without asking anything: the Agent runs it in session 0, where nobody can
// answer a question. Each engine leaves a plain-text mark near the start of
// its program: Inno Setup's and NSIS's manifests name them, NSIS's data
// starts with "NullsoftInst", and a WiX bundle has a ".wixburn" section.
public static class InstallerDetection
{
    // The marks are in the program part, before any packed payload.
    private const int ScanLimit = 32 * 1024 * 1024;

    private static readonly (InstallerKind Kind, byte[] Mark)[] Marks =
    {
        (InstallerKind.InnoSetup, Encoding.ASCII.GetBytes("JR.Inno.Setup")),
        (InstallerKind.InnoSetup, Encoding.ASCII.GetBytes("Inno Setup Setup Data")),
        (InstallerKind.Nsis, Encoding.ASCII.GetBytes("Nullsoft.NSIS.exehead")),
        (InstallerKind.Nsis, Encoding.ASCII.GetBytes("NullsoftInst")),
        (InstallerKind.WixBurn, Encoding.ASCII.GetBytes(".wixburn")),
    };

    public static InstallerKind Detect(string path)
    {
        if (Midview.Protocol.ProgramInstall.IsMsi(path))
        {
            return InstallerKind.Msi;
        }

        if (Midview.Protocol.ProgramInstall.IsBatchScript(path))
        {
            return InstallerKind.BatchScript;
        }

        using var file = File.OpenRead(path);
        var longest = Marks.Max(mark => mark.Mark.Length);
        var buffer = new byte[1 << 20];
        var kept = 0;
        long scanned = 0;
        while (scanned < ScanLimit)
        {
            var read = file.Read(buffer, kept, buffer.Length - kept);
            if (read == 0)
            {
                break;
            }
            scanned += read;
            var window = buffer.AsSpan(0, kept + read);
            foreach (var (kind, mark) in Marks)
            {
                if (window.IndexOf(mark) >= 0)
                {
                    return kind;
                }
            }

            // The end of this read again at the start of the next, so a mark
            // split between two reads is still found.
            kept = Math.Min(longest - 1, window.Length);
            window[^kept..].CopyTo(buffer);
        }
        return InstallerKind.Unknown;
    }

    // The switches each engine installs silently with, for every user and
    // without restarting. msiexec's own (/qn /norestart) the Agent adds
    // itself: what's typed for an MSI is extra options. For an engine this
    // doesn't know, NSIS's and Inno Setup's together, which each ignores from
    // the other (as the uninstaller guesses, ProgramUninstaller).
    public static string DefaultArguments(InstallerKind kind) => kind switch
    {
        InstallerKind.Msi => "ALLUSERS=1",
        InstallerKind.BatchScript => string.Empty,
        InstallerKind.InnoSetup => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /ALLUSERS",
        InstallerKind.Nsis => "/S",
        InstallerKind.WixBurn => "/quiet /norestart",
        _ => "/S /VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
    };

    public static string Describe(InstallerKind kind) => kind switch
    {
        InstallerKind.Msi => "Pacote do Windows Installer (MSI): instala sem perguntas.",
        InstallerKind.BatchScript => "Script (.bat ou .cmd): executa como administrador, sem janela.",
        InstallerKind.InnoSetup => "Instalador Inno Setup: as opções abaixo instalam sem perguntas.",
        InstallerKind.Nsis => "Instalador NSIS: as opções abaixo instalam sem perguntas.",
        InstallerKind.WixBurn => "Instalador WiX: as opções abaixo instalam sem perguntas.",
        _ => "Tipo de instalador não reconhecido: as opções abaixo são um palpite. "
             + "Confira no site do programa como instalar \"em modo silencioso\" e teste numa máquina primeiro.",
    };
}
