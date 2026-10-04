using Microsoft.Win32;

namespace Midview.Agent;

// With User Account Control off, every administrator's programs run
// elevated, and elevated programs are never touched by program blocking.
public static class UacCheck
{
    public const string OffWarning =
        "o controle de contas (UAC) está desligado nesta máquina: programas de quem é administrador não podem ser fechados";

    public static string? Warning()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("EnableLUA") is int enabled && enabled == 0 ? OffWarning : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
