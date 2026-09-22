namespace Midview.Master;

internal static class ConnectionErrors
{
    // Socket and TLS failures often carry the useful part (e.g. "connection
    // refused") only in the inner exception.
    public static string Describe(Exception ex) =>
        ex.InnerException is not null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
}
