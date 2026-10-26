using Midview.Master;
using Xunit;

// ClassSetAsideSightings: when each PC was first seen with files set aside
// for a lesson the Master doesn't know, in a temp file.
public sealed class ClassSetAsideSightingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-sightings-{Guid.NewGuid():N}");
    private readonly List<string> _log = new();

    private string FilePath => Path.Combine(_folder, "class-lessons-seen.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private ClassSetAsideSightings New() => new(FilePath, _log.Add);

    // The first sighting stays, across a Master opened again; a PC's entry goes
    // once that lesson isn't unknown there any more (known now, or its files
    // gone); another PC's entries stay.
    [Fact]
    public void TheFirstSighting_IsKept_UntilTheLessonIsNoLongerUnknownOnThatPc()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var sightings = New();
        Assert.Equal(new Dictionary<Guid, long> { [a] = 100, [b] = 100 }, sightings.Note("pc-1", new[] { a, b }, 100));
        sightings.Note("pc-2", new[] { a }, 150);

        var reopened = New();
        Assert.Equal(new Dictionary<Guid, long> { [a] = 100, [b] = 100 }, reopened.Note("PC-1", new[] { a, b }, 200));
        reopened.Note("pc-1", new[] { b }, 300);

        Assert.Null(New().FirstSeen("pc-1", a));
        Assert.Equal(100, New().FirstSeen("pc-1", b));
        Assert.Equal(150, New().FirstSeen("pc-2", a));
        Assert.Equal(300, New().Note("pc-1", new[] { a, b }, 300)[a]);
    }

    // At most MaxEntries: the oldest sightings go first (seen again, their clock starts again: later, never sooner).
    [Fact]
    public void AtMostMaxEntriesAreKept_TheNewest()
    {
        var oldest = Guid.NewGuid();
        var sightings = New();
        sightings.Note("pc-0", new[] { oldest }, 1);

        sightings.Note("pc-1", Enumerable.Range(0, ClassSetAsideSightings.MaxEntries).Select(_ => Guid.NewGuid()).ToList(), 2);

        Assert.Null(New().FirstSeen("pc-0", oldest));
    }

    // ...but first those of PCs this Master no longer sees (none of their
    // listings noted since it opened: a PC taken out of the lab, say), however
    // new, so a PC still in the lab keeps its oldest clock running.
    [Fact]
    public void AtTheCap_APcNoLongerSeen_GoesFirst()
    {
        var oldest = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var before = New();
        before.Note("pc-1", new[] { oldest }, 1);
        before.Note("pc-gone", new[] { gone }, 2);

        var reopened = New();
        reopened.Note("pc-1", Enumerable.Range(0, ClassSetAsideSightings.MaxEntries - 1).Select(_ => Guid.NewGuid()).Append(oldest).ToList(), 3);

        Assert.Equal(1, New().FirstSeen("pc-1", oldest));
        Assert.Null(New().FirstSeen("pc-gone", gone));
    }

    // A first sighting is never earlier than the newest one on file: a clock
    // that was behind for a while doesn't make files go before their 60 days.
    [Fact]
    public void AFirstSighting_IsNeverEarlierThanTheNewestOnFile()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var sightings = New();
        sightings.Note("pc-1", new[] { a }, 1_000);

        Assert.Equal(1_000, sightings.Note("pc-2", new[] { b }, 400)[b]);
        Assert.Equal(1_000, New().FirstSeen("pc-2", b));
        Assert.Equal(2_000, New().Note("pc-2", new[] { b, Guid.NewGuid() }, 2_000).Values.Max());
    }

    // A file that isn't one is taken as empty, and said so.
    [Fact]
    public void AnUnreadableFile_IsTakenAsEmpty()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");
        var lesson = Guid.NewGuid();

        Assert.Equal(500, New().Note("pc-1", new[] { lesson }, 500)[lesson]);
        Assert.NotEmpty(_log);
        Assert.Equal(500, New().FirstSeen("pc-1", lesson));
    }
}
