using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Midview.Desk;
using Xunit;

// Counting the process's GDI objects is process-wide, so these real-GDI
// captures must not run beside another test churning bitmaps (DeskServerTests
// streams through a fake capture): a collection with parallelization off keeps
// this class off on its own.
[CollectionDefinition("Desk real GDI", DisableParallelization = true)]
public sealed class DeskRealGdiCollection
{
}

[Collection("Desk real GDI")]
public class DeskScreenCaptureTests
{
    [DeskFact]
    public void Captures_a_bitmap_of_the_primary_screen_size()
    {
        var capture = new DeskScreenCapture();

        using var frame = capture.CaptureFrame();

        Assert.NotNull(frame);
        Assert.Equal(PrimaryScreen.Width, frame.Width);
        Assert.Equal(PrimaryScreen.Height, frame.Height);
    }

    // FramePipeline diffs frames as 32-bit ARGB (TileDiff reads them that
    // way without converting), the format GdiScreenCapture hands it.
    [DeskFact]
    public void Captures_in_the_32_bit_format_the_frame_diff_reads()
    {
        var capture = new DeskScreenCapture();

        using var frame = capture.CaptureFrame();

        Assert.Equal(PixelFormat.Format32bppArgb, frame.PixelFormat);
    }

    // The resolution changed since the target was made: the pipeline then
    // takes a whole new frame, and the target must be as it was.
    [DeskFact]
    public void TryCaptureInto_returns_false_for_a_wrong_sized_target()
    {
        var capture = new DeskScreenCapture();
        var untouched = Color.FromArgb(255, 12, 34, 56);

        // One side off is as wrong as both.
        var wrongSizes = new[]
        {
            (PrimaryScreen.Width + 1, PrimaryScreen.Height),
            (PrimaryScreen.Width, PrimaryScreen.Height + 1),
            (10, 10),
        };

        foreach (var (width, height) in wrongSizes)
        {
            using var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.Clear(untouched);
            }

            Assert.False(capture.TryCaptureInto(target));

            Assert.Equal(untouched.ToArgb(), target.GetPixel(0, 0).ToArgb());
            Assert.Equal(untouched.ToArgb(), target.GetPixel(width - 1, height - 1).ToArgb());
        }
    }

    // What the pipeline does with every frame after the second, so that a
    // new full-screen bitmap isn't made each time.
    [DeskFact]
    public void TryCaptureInto_draws_the_screen_into_a_target_of_its_size()
    {
        var capture = new DeskScreenCapture();
        var notTheScreen = Color.FromArgb(255, 12, 34, 56);
        using var target = new Bitmap(PrimaryScreen.Width, PrimaryScreen.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(target))
        {
            graphics.Clear(notTheScreen);
        }

        Assert.True(capture.TryCaptureInto(target));

        // If the call drew nothing, every point sampled still has the colour
        // the target started with; no screen is all of that colour.
        var changed = false;
        for (var y = 0; y < target.Height && !changed; y += target.Height / 8 + 1)
        {
            for (var x = 0; x < target.Width && !changed; x += target.Width / 8 + 1)
            {
                changed = target.GetPixel(x, y).ToArgb() != notTheScreen.ToArgb();
            }
        }
        Assert.True(changed, "the target still is what it was before the capture");
    }

    // The helper captures many times a second for as long as it runs, as
    // SYSTEM: a GDI object left behind by every frame would use up the
    // process's 10,000 within minutes, and capture would stop for good.
    [DeskFact]
    public void Capturing_leaves_no_GDI_object_behind()
    {
        var capture = new DeskScreenCapture();
        using var reused = capture.CaptureFrame();
        capture.TryCaptureInto(reused); // what GDI+ keeps for good is made by the first of each
        var before = GdiObjects.Count();

        for (var i = 0; i < 50; i++)
        {
            using var frame = capture.CaptureFrame();
            capture.TryCaptureInto(reused);
        }

        // Room for what the rest of the process does meanwhile; one object
        // lost per frame would be over a hundred.
        var after = GdiObjects.Count();
        Assert.True(after - before <= 5, $"GDI objects went from {before} to {after} over 100 captures");
    }
}

// These tests capture the screen of the machine they run on, so they need
// one. Where there is none (a service, a session nobody is logged on to)
// Windows says the screen is 0 x 0 and there is nothing to capture: the test
// is then reported as skipped, saying why, instead of failing on a bitmap
// that can't be that size. The GitHub Actions runner has a screen, so there
// they run. The secure desktop itself isn't tested here; that is tried live.
public sealed class DeskFactAttribute : FactAttribute
{
    public DeskFactAttribute() =>
        Skip = PrimaryScreen.Width > 0 && PrimaryScreen.Height > 0
            ? null
            : "precisa de uma tela: esta sessão não tem área de trabalho interativa (a tela mede 0 x 0)";
}

// The screen's size as Windows says it, asked here and not of the class under
// test, so that the tests don't check it against itself.
internal static class PrimaryScreen
{
    private const int SmCxScreen = 0; // SM_CXSCREEN
    private const int SmCyScreen = 1; // SM_CYSCREEN

    public static int Width => GetSystemMetrics(SmCxScreen);

    public static int Height => GetSystemMetrics(SmCyScreen);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

internal static class GdiObjects
{
    private const uint GrGdiObjects = 0; // GR_GDIOBJECTS

    // The GDI objects (bitmaps, device contexts, brushes...) this process has
    // open at the moment.
    public static int Count()
    {
        using var process = Process.GetCurrentProcess();
        return (int)GetGuiResources(process.Handle, GrGdiObjects);
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
