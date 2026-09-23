namespace Midview.Master;

internal static class FailureText
{
    // The inner exception usually carries the actual reason (e.g. which
    // certificate check failed), so it's shown alongside the outer message.
    public static string Describe(Exception ex) =>
        ex.InnerException is not null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
}
