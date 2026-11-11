namespace Midview.Protocol;

// What kind of device answered a probe or paired. A teacher that predates
// Android never sees "kind" on the wire and an Agent on Windows never sends
// it, so anything missing or unknown reads as Windows.
// The wire "kind" must stay a lower-case JSON string ("windows"/"android"):
// 0.0.51 Masters already read it and drop a reply whose "kind" isn't a string.
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
