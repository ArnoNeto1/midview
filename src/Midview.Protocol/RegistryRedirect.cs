using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Midview.Protocol;

// The kernel's own name for an open registry key, which a registry symbolic
// link cannot fake: opening through a link gives a handle named for its target.
internal static class RegistryRedirect
{
    private const int KeyNameInformation = 3;
    private const uint StatusBufferOverflow = 0x80000005;
    private const uint StatusBufferTooSmall = 0xC0000023;

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryKey(SafeRegistryHandle key, int infoClass, byte[] buffer, int length, out int resultLength);

    // Null when the name cannot be had, which callers treat as redirected.
    public static string? RealName(RegistryKey key)
    {
        var buffer = new byte[1024];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var status = NtQueryKey(key.Handle, KeyNameInformation, buffer, buffer.Length, out var needed);
            if (status == 0)
            {
                // KEY_NAME_INFORMATION: a ULONG length in bytes, then the UTF-16 name.
                var length = BitConverter.ToInt32(buffer, 0);
                return length < 0 || length > buffer.Length - 4 ? null : Encoding.Unicode.GetString(buffer, 4, length);
            }
            if (status is not (StatusBufferOverflow or StatusBufferTooSmall))
                return null;
            buffer = new byte[Math.Max(needed, buffer.Length * 2)];
        }
        return null;
    }
}
