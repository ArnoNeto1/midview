using Midview.Session;
using Xunit;

// A fake Desktop (positions in a dictionary) and a temp layout file, so the
// developer's real icons and LocalAppData are never touched.
public sealed class DesktopIconGuardTests : IDisposable
{
    private const string Chrome = @"C:\Users\aluno\Desktop\Google Chrome.lnk";
    private const string Word = @"C:\Users\aluno\Desktop\Word.lnk";
    private const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const string ClassFolder = @"C:\Users\aluno\Desktop\Pasta da turma - 7A";

    private readonly string _layoutPath = Path.Combine(Path.GetTempPath(), $"midview-layout-{Guid.NewGuid():N}", "desktop-layout.json");
    private readonly FakeDesktop _desktop = new();
    private bool _locked;

    public DesktopIconGuardTests()
    {
        _desktop.Icons[ThisPc] = new IconPoint(10, 10);
        _desktop.Icons[Chrome] = new IconPoint(10, 110);
        _desktop.Icons[Word] = new IconPoint(10, 210);
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_layoutPath)!;
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private DesktopIconGuard Guard() => new(_desktop, () => _locked, _layoutPath, _ => { });

    // A guard that has seen the lock turned on while the student was using
    // the PC: the layout on screen then is the one kept.
    private DesktopIconGuard LockedNow()
    {
        var guard = Guard();
        guard.Pass();
        _locked = true;
        guard.Pass();
        return guard;
    }

    [Fact]
    public void Locked_AnIconDraggedAway_GoesBack()
    {
        var guard = LockedNow();
        _desktop.Icons[Chrome] = new IconPoint(800, 400);

        guard.Pass();

        Assert.Equal(new IconPoint(10, 110), _desktop.Icons[Chrome]);
        Assert.Equal(new IconPoint(10, 210), _desktop.Icons[Word]);
    }

    // "Classificar por" or a drag of several: every moved icon goes back.
    [Fact]
    public void Locked_SortingTheIcons_PutsThemAllBack()
    {
        var guard = LockedNow();
        _desktop.Icons[ThisPc] = new IconPoint(10, 210);
        _desktop.Icons[Word] = new IconPoint(10, 10);

        guard.Pass();

        Assert.Equal(new IconPoint(10, 10), _desktop.Icons[ThisPc]);
        Assert.Equal(new IconPoint(10, 210), _desktop.Icons[Word]);
    }

    [Fact]
    public void Locked_AutoArrangeIsTurnedOff()
    {
        _desktop.AutoArrange = true;

        LockedNow();

        Assert.False(_desktop.AutoArrange);
    }

    [Fact]
    public void Unlocked_IconsMoveFreely()
    {
        var guard = Guard();
        guard.Pass();
        _desktop.Icons[Chrome] = new IconPoint(800, 400);

        guard.Pass();

        Assert.Equal(new IconPoint(800, 400), _desktop.Icons[Chrome]);
        Assert.Equal(0, _desktop.Placed);
    }

    [Fact]
    public void TurnedOff_IconsMoveFreely_AndTheLayoutIsForgotten()
    {
        var guard = LockedNow();
        Assert.True(File.Exists(_layoutPath));

        _locked = false;
        guard.Pass();
        _desktop.Icons[Chrome] = new IconPoint(800, 400);
        guard.Pass();

        Assert.Equal(new IconPoint(800, 400), _desktop.Icons[Chrome]);
        Assert.False(File.Exists(_layoutPath));
    }

    // A lesson starts: the Agent puts the class folder on the Desktop. It
    // stays where Windows put it, and is then kept there too.
    [Fact]
    public void Locked_AnIconThatAppears_StaysWhereWindowsPutIt_AndIsKeptThere()
    {
        var guard = LockedNow();
        _desktop.Icons[ClassFolder] = new IconPoint(110, 10);

        guard.Pass();
        Assert.Equal(new IconPoint(110, 10), _desktop.Icons[ClassFolder]);

        _desktop.Icons[ClassFolder] = new IconPoint(900, 500);
        guard.Pass();
        Assert.Equal(new IconPoint(110, 10), _desktop.Icons[ClassFolder]);
    }

    // The lesson ends and the class folder goes away: nothing to put back.
    [Fact]
    public void Locked_AnIconThatGoesAway_IsLeftAlone()
    {
        _desktop.Icons[ClassFolder] = new IconPoint(110, 10);
        var guard = LockedNow();
        _desktop.Icons.Remove(ClassFolder);

        guard.Pass();

        Assert.False(_desktop.Icons.ContainsKey(ClassFolder));
        Assert.Equal(0, _desktop.Placed);
    }

    // The next logon: the layout kept under the lock comes back, even if
    // Explorer put an icon elsewhere.
    [Fact]
    public void NextLogon_TheKeptLayoutComesBack()
    {
        LockedNow();
        _desktop.Icons[Chrome] = new IconPoint(500, 500);

        var afterLogon = Guard();
        afterLogon.Pass();

        Assert.Equal(new IconPoint(10, 110), _desktop.Icons[Chrome]);
    }

    // A logon with the lock on and nothing kept yet: while Explorer is still
    // placing icons, none is moved; the layout is taken once it holds still.
    [Fact]
    public void LogonWithNothingKept_TakesTheLayoutOnceExplorerIsDone()
    {
        _locked = true;
        var guard = Guard();
        guard.Pass();
        _desktop.Icons[Chrome] = new IconPoint(10, 310); // Explorer still placing
        guard.Pass();
        Assert.Equal(0, _desktop.Placed);

        for (var i = 0; i < DesktopIconGuard.SettlePasses; i++)
        {
            guard.Pass();
        }
        _desktop.Icons[Chrome] = new IconPoint(800, 400);
        guard.Pass();

        Assert.Equal(new IconPoint(10, 310), _desktop.Icons[Chrome]);
    }

    [Fact]
    public void ExplorerNotThere_NothingHappens_AndTheLayoutIsKept()
    {
        var guard = LockedNow();
        _desktop.Running = false;
        guard.Pass();

        _desktop.Running = true;
        _desktop.Icons[Chrome] = new IconPoint(800, 400);
        guard.Pass();

        Assert.Equal(new IconPoint(10, 110), _desktop.Icons[Chrome]);
    }

    [Fact]
    public void ADesktopThatFails_NeverThrows_AndLogsOnce()
    {
        var log = new List<string>();
        _locked = true;
        var guard = new DesktopIconGuard(new BrokenDesktop(), () => _locked, _layoutPath, log.Add);

        guard.Pass();
        guard.Pass();

        Assert.Single(log);
    }

    [Fact]
    public void LabDesktopLock_ReadsLockDesktopFromTheAgentsFile()
    {
        var folder = Path.GetDirectoryName(_layoutPath)!;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "lab-config.json");

        Assert.False(LabDesktopLock.IsOn(path)); // missing

        File.WriteAllText(path, """{"SavedAt":5,"LockTaskbar":true,"LockDesktop":true}""");
        Assert.True(LabDesktopLock.IsOn(path));

        File.WriteAllText(path, """{"SavedAt":6,"LockTaskbar":true,"LockDesktop":false}""");
        Assert.False(LabDesktopLock.IsOn(path));

        File.WriteAllText(path, """{"SavedAt":7,"LockTaskbar":true}"""); // an Agent from before it
        Assert.False(LabDesktopLock.IsOn(path));

        File.WriteAllText(path, "{ quebrado");
        Assert.False(LabDesktopLock.IsOn(path));
    }

    private sealed class FakeDesktop : IDesktopIcons
    {
        public Dictionary<string, IconPoint> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AutoArrange { get; set; }
        public bool Running { get; set; } = true;
        public int Placed { get; private set; }

        public IReadOnlyDictionary<string, IconPoint>? Read() =>
            Running ? new Dictionary<string, IconPoint>(Icons, StringComparer.OrdinalIgnoreCase) : null;

        public bool TurnOffAutoArrange()
        {
            var was = AutoArrange;
            AutoArrange = false;
            return was;
        }

        public void Place(IReadOnlyDictionary<string, IconPoint> positions)
        {
            foreach (var (name, where) in positions)
            {
                if (Icons.ContainsKey(name))
                {
                    Icons[name] = where;
                    Placed++;
                }
            }
        }
    }

    private sealed class BrokenDesktop : IDesktopIcons
    {
        public IReadOnlyDictionary<string, IconPoint>? Read() => throw new InvalidOperationException("Explorer falhou");
        public bool TurnOffAutoArrange() => throw new InvalidOperationException("Explorer falhou");
        public void Place(IReadOnlyDictionary<string, IconPoint> positions) => throw new InvalidOperationException("Explorer falhou");
    }
}
