namespace Midview.Session;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
