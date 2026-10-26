using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeConfirmation: which mode ClassFolderRole acts on, from the
// tracker's readings. A change counts only once two readings in a row say
// it, the same mode and, for Remote, the same home PC (one reading can be
// wrong: class-home.json unreadable for a moment shows a renamed home as
// another PC). The first Local is taken at once, and so is a Local naming
// this PC as the home (the Agents' own word); Remote never from one reading.
public sealed class ClassHomeConfirmationTests
{
    private static readonly ClassHome Prof2 = new(new string('A', 40), "PROF-2", "10.0.0.2", ClassFolderRemote.Port, 2_000);
    private static readonly ClassHome Prof3 = new(new string('B', 40), "PROF-3", "10.0.0.3", ClassFolderRemote.Port, 3_000);
    private static readonly ClassHome ThisPc = new(new string('C', 40), "PROF-1", "10.0.0.1", ClassFolderRemote.Port, 1_000);

    private static ClassHomeReading Local(ClassHome? home = null, int asked = 3) => new(home, ClassFolderMode.Local, asked);

    private static ClassHomeReading Remote(ClassHome home, int asked = 3) => new(home, ClassFolderMode.Remote, asked);

    [Fact]
    public void TheFirstLocal_IsTakenAtOnce()
    {
        var confirmation = new ClassHomeConfirmation();

        Assert.Null(confirmation.Acted);
        Assert.Equal(ClassFolderMode.Local, confirmation.Take(Local(asked: 0)));
        Assert.False(confirmation.Confirming);
    }

    [Fact]
    public void TheFirstRemote_WaitsForASecondReading()
    {
        var confirmation = new ClassHomeConfirmation();

        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.True(confirmation.Confirming);
        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof2 with { Address = "10.0.0.9" })));
        Assert.False(confirmation.Confirming);
    }

    [Fact]
    public void OneReadingAgainstTheModeActedOn_ChangesNothing()
    {
        var confirmation = new ClassHomeConfirmation();
        confirmation.Acting(ClassFolderMode.Local);

        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.True(confirmation.Confirming);
        Assert.Null(confirmation.Take(Local()));
        Assert.False(confirmation.Confirming);
        // The run starts over: one more Remote is again only one.
        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.Equal(ClassFolderMode.Local, confirmation.Acted);
    }

    // Two readings naming two different PCs as the home are not one change said twice.
    [Fact]
    public void TwoReadingsNamingOtherHomes_DoNotConfirmEachOther()
    {
        var confirmation = new ClassHomeConfirmation();
        confirmation.Acting(ClassFolderMode.Local);

        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.Null(confirmation.Take(Remote(Prof3)));
        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof3)));
    }

    // Back to Local with no home named (no Agent said anything) is a change
    // like any other: two readings.
    [Fact]
    public void BackToLocal_WithNoHome_NeedsTwoReadings()
    {
        var confirmation = new ClassHomeConfirmation();
        confirmation.Acting(ClassFolderMode.Remote);

        Assert.Null(confirmation.Take(Remote(Prof3))); // another remote home: still Remote
        Assert.False(confirmation.Confirming);
        Assert.Null(confirmation.Take(Local()));
        Assert.True(confirmation.Confirming);
        Assert.Equal(ClassFolderMode.Local, confirmation.Take(Local()));
    }

    // The Agents naming this PC as the home is never a reading gone wrong (this
    // PC took the folders over): Local at once, its window its own at once.
    [Fact]
    public void BackToLocal_NamingThisPc_IsTakenAtOnce()
    {
        var confirmation = new ClassHomeConfirmation();
        confirmation.Acting(ClassFolderMode.Remote);

        Assert.Equal(ClassFolderMode.Local, confirmation.Take(Local(ThisPc)));
        Assert.False(confirmation.Confirming);
    }

    // A Remote this PC knows is true (it handed the folders over itself) is taken at once.
    [Fact]
    public void ARemoteThisPcKnowsOf_IsTakenAtOnce()
    {
        var confirmation = new ClassHomeConfirmation();
        confirmation.Acting(ClassFolderMode.Local);

        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof2), sure: true));
        Assert.False(confirmation.Confirming);
    }

    // A change the role couldn't make (a lesson of this PC's still on) is asked
    // for again at every reading that says it, without asking for readings sooner;
    // acting Local meanwhile (nothing was decided yet) doesn't start the run over.
    [Fact]
    public void AChangeNotMade_IsWantedAgainAtEveryReading()
    {
        var confirmation = new ClassHomeConfirmation();
        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof2)));

        confirmation.Acting(ClassFolderMode.Local);
        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof2)));
        Assert.False(confirmation.Confirming);
        Assert.Equal(ClassFolderMode.Remote, confirmation.Take(Remote(Prof2)));

        confirmation.Acting(ClassFolderMode.Remote);
        Assert.Null(confirmation.Take(Remote(Prof2)));
        Assert.Equal(ClassFolderMode.Remote, confirmation.Acted);
        // A new run starts from nothing: one Local is one.
        Assert.Null(confirmation.Take(Local()));
        Assert.True(confirmation.Confirming);
    }
}
