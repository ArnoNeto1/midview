namespace Midview.Protocol;

// What kind of device answered a probe or paired. A teacher that predates
// Android never sees "kind" on the wire and an Agent on Windows never sends
// it, so anything missing or unknown reads as Windows.
public enum DeviceKind
{
    Windows = 0,
    Android = 1,
}

public static class DeviceKindText
{
    public const string WindowsWire = "windows";
    public const string AndroidWire = "android";

    public static string ToWire(DeviceKind kind) => kind == DeviceKind.Android ? AndroidWire : WindowsWire;

    public static DeviceKind FromWire(string? text) => text == AndroidWire ? DeviceKind.Android : DeviceKind.Windows;
}
