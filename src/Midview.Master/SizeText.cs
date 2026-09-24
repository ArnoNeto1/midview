namespace Midview.Master;

internal static class SizeText
{
    public static string Describe(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB",
    };
}
