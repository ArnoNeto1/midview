namespace TreezeSuite.Session;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
