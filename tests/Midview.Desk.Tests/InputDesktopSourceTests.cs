using Midview.Desk;
using Xunit;

// The name-keyed stable-handle wrapper over the OpenInputDesktop/desktop-name/
// CloseDesktop trio, tested with scripted delegates so no real desktop is
// needed. The real Win32 is wired in Program.cs and verified live (Task 12).
public class InputDesktopSourceTests
{
    [Fact]
    public void Returns_a_stable_handle_while_the_desktop_name_is_unchanged()
    {
        var h1 = new IntPtr(0x11);
        var h2 = new IntPtr(0x22);
        var h3 = new IntPtr(0x33);
        // OpenInputDesktop hands back a brand-new handle every call, all for the
        // same desktop ("Default").
        var fake = new FakeDesktops(new[] { (h1, "Default"), (h2, "Default"), (h3, "Default") });
        var source = new InputDesktopSource(fake.Open, fake.Name, fake.Close);

        // The first poll adopts h1 as the stable handle for "Default"; every
        // later poll opens a fresh duplicate but keeps returning h1.
        Assert.Equal(h1, source.Current());
        Assert.Equal(h1, source.Current());
        Assert.Equal(h1, source.Current());

        // Each duplicate it opened is closed at once; the stable handle is not.
        Assert.Equal(new[] { h2, h3 }, fake.Closed);
    }

    [Fact]
    public void Switches_handle_when_the_desktop_name_changes()
    {
        var h1 = new IntPtr(0x11);
        var h2 = new IntPtr(0x22);
        var h3 = new IntPtr(0x33);
        // Default -> Winlogon (UAC appears) -> Default (it is gone).
        var fake = new FakeDesktops(new[] { (h1, "Default"), (h2, "Winlogon"), (h3, "Default") });
        var source = new InputDesktopSource(fake.Open, fake.Name, fake.Close);

        // Each name change yields a new stable handle. ReleaseSuperseded is
        // driven here exactly as the DeskServer worker's attach seam drives it:
        // after the thread has attached to the new desktop.
        Assert.Equal(h1, source.Current());
        source.ReleaseSuperseded();
        Assert.Empty(fake.Closed); // first desktop: nothing left behind yet

        Assert.Equal(h2, source.Current());
        source.ReleaseSuperseded(); // thread left Default, so h1 is safe to close
        Assert.Equal(new[] { h1 }, fake.Closed);

        Assert.Equal(h3, source.Current());
        source.ReleaseSuperseded(); // thread left Winlogon, so h2 is safe to close
        Assert.Equal(new[] { h1, h2 }, fake.Closed);

        // The handle currently in use is never closed.
        Assert.DoesNotContain(h3, fake.Closed);
    }

    [Fact]
    public void A_failed_open_returns_zero()
    {
        var namesAsked = new List<IntPtr>();
        var closed = new List<IntPtr>();
        var source = new InputDesktopSource(
            () => IntPtr.Zero,
            handle => { namesAsked.Add(handle); return "Default"; },
            closed.Add);

        // No input desktop to open right now: report none, and do not touch the
        // name or close any handle -- DeskServer skips a zero handle and never
        // calls SetThreadDesktop(IntPtr.Zero).
        Assert.Equal(IntPtr.Zero, source.Current());
        Assert.Empty(namesAsked);
        Assert.Empty(closed);
    }

    // Scripts the three Win32 calls: Open dequeues the next handle (or zero when
    // the script runs dry), Name maps a handle to its desktop name, Close records
    // what was closed so a test can assert nothing leaks.
    private sealed class FakeDesktops
    {
        private readonly Queue<IntPtr> _toOpen;
        private readonly Dictionary<IntPtr, string> _names;

        public FakeDesktops(IEnumerable<(IntPtr handle, string name)> sequence)
        {
            var list = sequence.ToList();
            _toOpen = new Queue<IntPtr>(list.Select(entry => entry.handle));
            _names = list.ToDictionary(entry => entry.handle, entry => entry.name);
        }

        public List<IntPtr> Closed { get; } = new();

        public IntPtr Open() => _toOpen.Count > 0 ? _toOpen.Dequeue() : IntPtr.Zero;

        public string? Name(IntPtr handle) => _names.TryGetValue(handle, out var name) ? name : null;

        public void Close(IntPtr handle) => Closed.Add(handle);
    }
}
