using System.Runtime.InteropServices;

namespace Midview.Agent;

// Runs in the Agent (SYSTEM), not in Session: changing a device's state
// needs administrator rights, so from Session -- running as a standard
// student account -- every call here used to fail silently and the input
// lock simply never locked anything.
//
// DICS_DISABLE is persistent: a disabled device stays disabled across
// reboots, including at the logon screen. That's why the Agent re-enables
// everything when it starts at boot (see InputLockWorker).
public sealed class WindowsInputDeviceController : IInputDeviceController
{
    // GUID_DEVCLASS_KEYBOARD and GUID_DEVCLASS_MOUSE
    private static readonly Guid KeyboardClassGuid = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid MouseClassGuid = new("4D36E96F-E325-11CE-BFC1-08002BE10318");

    // Non-short-circuiting "&": the mouse must still be handled when the keyboard fails.
    public bool DisableInputDevices() =>
        SetClassDevicesState(KeyboardClassGuid, enable: false) & SetClassDevicesState(MouseClassGuid, enable: false);

    public bool EnableInputDevices() =>
        SetClassDevicesState(KeyboardClassGuid, enable: true) & SetClassDevicesState(MouseClassGuid, enable: true);

    private static bool SetClassDevicesState(Guid classGuid, bool enable)
    {
        const uint DIGCF_PRESENT = 0x02;
        var deviceInfoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            return false;
        }

        try
        {
            var allSucceeded = true;
            var deviceInfoData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData); index++)
            {
                var parameters = new SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                    {
                        cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                        InstallFunction = DIF_PROPERTYCHANGE,
                    },
                    StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };

                // Keeps going after a failure: one device that refuses to
                // change state mustn't stop the others from being handled.
                var changed = SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref parameters,
                        Marshal.SizeOf<SP_PROPCHANGE_PARAMS>())
                    && SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
                allSucceeded &= changed;
            }
            return allSucceeded;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private const uint DIF_PROPERTYCHANGE = 0x12;
    private const uint DICS_ENABLE = 1;
    private const uint DICS_DISABLE = 2;
    private const uint DICS_FLAG_GLOBAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
