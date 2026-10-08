using System.Drawing;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class RemoteScreenTests
{
    [Fact]
    public void Nothing_Received_DrawsNothing()
    {
        var screen = new RemoteScreen();

        var drawn = 0;
        var frameId = screen.TakeChanges(_ => drawn++);

        Assert.Equal(0, drawn);
        Assert.Equal(0, frameId);
    }

    [Fact]
    public void AFrame_IsDrawnAtItsSize_InPlace_AndAcknowledged()
    {
        var screen = new RemoteScreen();
        screen.AddFrame(new ScreenFrame(5, 256, 128, new[]
        {
            new ScreenRegion(0, 0, 256, 128, Jpeg(256, 128, Color.Black)),
            new ScreenRegion(64, 64, 64, 64, Jpeg(64, 64, Color.White)),
        }));

        ScreenPicture? picture = null;
        var frameId = screen.TakeChanges(drawn => picture = drawn with { Pixels = drawn.Pixels.ToArray() });

        Assert.Equal(5, frameId);
        Assert.NotNull(picture);
        Assert.Equal((256, 128, 1024, true), (picture.Width, picture.Height, picture.Stride, picture.Resized));
        Assert.Contains(new PixelRect(0, 0, 256, 128), picture.Changed);
        Assert.True(Brightness(picture, 10, 10) < 30);
        Assert.True(Brightness(picture, 96, 96) > 225);
    }

    [Fact]
    public void ALaterFrame_OnlyItsRegionsChange_AndOnlyTheNewestIsAcknowledged()
    {
        var screen = new RemoteScreen();
        screen.AddFrame(new ScreenFrame(1, 256, 128, new[] { new ScreenRegion(0, 0, 256, 128, Jpeg(256, 128, Color.Black)) }));
        screen.TakeChanges(_ => { });

        screen.AddFrame(new ScreenFrame(2, 256, 128, new[] { new ScreenRegion(0, 64, 64, 64, Jpeg(64, 64, Color.White)) }));
        screen.AddFrame(new ScreenFrame(3, 256, 128, new[] { new ScreenRegion(192, 0, 64, 64, Jpeg(64, 64, Color.White)) }));
        ScreenPicture? picture = null;
        var frameId = screen.TakeChanges(drawn => picture = drawn with { Pixels = drawn.Pixels.ToArray() });

        Assert.Equal(3, frameId);
        Assert.False(picture!.Resized);
        Assert.Equal(new[] { new PixelRect(0, 64, 64, 64), new PixelRect(192, 0, 64, 64) }, picture.Changed);
        Assert.True(Brightness(picture, 10, 100) > 225);
        Assert.True(Brightness(picture, 200, 10) > 225);
        Assert.True(Brightness(picture, 100, 100) < 30);
        Assert.Equal(0, screen.TakeChanges(_ => Assert.Fail("nothing changed since")));
    }

    [Fact]
    public void ManySmallChanges_AreCopiedAsOneRectangleAroundThem()
    {
        var screen = new RemoteScreen();
        var white = Jpeg(64, 64, Color.White);
        var regions = Enumerable.Range(0, 40)
            .Select(i => new ScreenRegion(i % 10 * 64, i / 10 * 64, 64, 64, white))
            .ToArray();
        screen.AddFrame(new ScreenFrame(1, 640, 256, regions[..1]));
        screen.TakeChanges(_ => { });

        screen.AddFrame(new ScreenFrame(2, 640, 256, regions));
        ScreenPicture? picture = null;
        screen.TakeChanges(drawn => picture = drawn);

        Assert.True(picture!.Changed.Count <= 32);
        Assert.All(regions, region => Assert.Contains(picture.Changed, part =>
            part.X <= region.X && part.Y <= region.Y
            && part.X + part.Width >= region.X + region.Width && part.Y + part.Height >= region.Y + region.Height));
    }

    [Fact]
    public void ANewResolution_StartsANewPicture()
    {
        var screen = new RemoteScreen();
        screen.AddFrame(new ScreenFrame(1, 256, 128, new[] { new ScreenRegion(0, 0, 256, 128, Jpeg(256, 128, Color.Black)) }));
        screen.TakeChanges(_ => { });

        screen.AddFrame(new ScreenFrame(2, 128, 64, new[] { new ScreenRegion(0, 0, 128, 64, Jpeg(128, 64, Color.White)) }));
        ScreenPicture? picture = null;
        screen.TakeChanges(drawn => picture = drawn);

        Assert.Equal((128, 64, true), (picture!.Width, picture.Height, picture.Resized));
        Assert.Contains(new PixelRect(0, 0, 128, 64), picture.Changed);
    }

    [Fact]
    public void Tiles_FromAnOlderSession_GrowThePictureToFit_AndAreNeverAcknowledged()
    {
        var screen = new RemoteScreen();
        screen.AddTile(0, 0, Jpeg(64, 64, Color.Black));
        screen.AddTile(0, 1, Jpeg(36, 64, Color.White)); // the screen's right edge
        screen.AddTile(1, 0, Jpeg(64, 20, Color.White)); // and its bottom

        ScreenPicture? picture = null;
        var frameId = screen.TakeChanges(drawn => picture = drawn with { Pixels = drawn.Pixels.ToArray() });

        Assert.Equal(0, frameId);
        Assert.Equal((100, 84, true), (picture!.Width, picture.Height, picture.Resized));
        Assert.True(Brightness(picture, 10, 10) < 30);
        Assert.True(Brightness(picture, 80, 10) > 225);
        Assert.True(Brightness(picture, 10, 70) > 225);
    }

    [Fact]
    public void ARegionThatIsNotAnImage_IsSkipped_AndTheRestStillDrawn()
    {
        var screen = new RemoteScreen();
        screen.AddFrame(new ScreenFrame(1, 128, 64, new[]
        {
            new ScreenRegion(0, 0, 64, 64, new byte[] { 1, 2, 3 }),
            new ScreenRegion(64, 0, 64, 64, Jpeg(64, 64, Color.White)),
        }));
        screen.AddTile(0, 0, new byte[] { 4, 5, 6 });

        ScreenPicture? picture = null;
        var frameId = screen.TakeChanges(drawn => picture = drawn with { Pixels = drawn.Pixels.ToArray() });

        Assert.Equal(1, frameId);
        Assert.True(Brightness(picture!, 100, 10) > 225);
    }

    // A JPEG bigger than the region it came for is not what a Session sends:
    // it's skipped rather than decoded.
    [Fact]
    public void AJpegBiggerThanItsRegion_IsSkipped()
    {
        var screen = new RemoteScreen();
        screen.AddFrame(new ScreenFrame(1, 128, 64, new[]
        {
            new ScreenRegion(0, 0, 128, 64, Jpeg(128, 64, Color.Black)),
        }));
        screen.TakeChanges(_ => { });

        screen.AddFrame(new ScreenFrame(2, 128, 64, new[] { new ScreenRegion(0, 0, 64, 64, Jpeg(128, 64, Color.White)) }));
        screen.AddTile(0, 1, Jpeg(128, 64, Color.White));

        ScreenPicture? picture = null;
        Assert.Equal(2, screen.TakeChanges(drawn => picture = drawn));
        Assert.Null(picture);
    }

    // A header claiming a huge image (bigger than GDI+ can even size) is
    // turned away, and the update's other regions are still drawn.
    [Fact]
    public void AJpegClaimingAHugeSize_IsSkipped_AndTheRestStillDrawn()
    {
        var screen = new RemoteScreen();
        var huge = ClaimingSize(Jpeg(16, 16, Color.White), 30000, 30000);

        screen.AddFrame(new ScreenFrame(1, 128, 64, new[]
        {
            new ScreenRegion(0, 0, 64, 64, huge),
            new ScreenRegion(64, 0, 64, 64, Jpeg(64, 64, Color.White)),
        }));
        screen.AddTile(0, 0, huge);

        ScreenPicture? picture = null;
        Assert.Equal(1, screen.TakeChanges(drawn => picture = drawn with { Pixels = drawn.Pixels.ToArray() }));
        Assert.True(Brightness(picture!, 100, 10) > 225);
        Assert.True(Brightness(picture!, 10, 10) < 30);
    }

    [Fact]
    public void ATileFarBeyondAnyScreen_IsSkipped()
    {
        var screen = new RemoteScreen();
        screen.AddTile(int.MaxValue / 64, 0, Jpeg(64, 64, Color.White));
        screen.AddTile(0, 300, Jpeg(64, 64, Color.White));
        screen.AddTile(200, 200, Jpeg(64, 64, Color.White)); // 12800x12800: too many pixels in all

        Assert.Equal(0, screen.TakeChanges(_ => Assert.Fail("nothing to draw")));
    }

    private static byte[] Jpeg(int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
        }
        return TileCodec.EncodeTile(bitmap, new Rectangle(0, 0, width, height));
    }

    // The same JPEG with its frame header (SOF0) saying it is width x height.
    private static byte[] ClaimingSize(byte[] jpeg, int width, int height)
    {
        var copy = jpeg.ToArray();
        for (var i = 0; i + 8 < copy.Length; i++)
        {
            if (copy[i] == 0xFF && copy[i + 1] == 0xC0)
            {
                copy[i + 5] = (byte)(height >> 8);
                copy[i + 6] = (byte)height;
                copy[i + 7] = (byte)(width >> 8);
                copy[i + 8] = (byte)width;
                return copy;
            }
        }
        throw new InvalidOperationException("no SOF0 marker");
    }

    // The green channel of the pixel at (x, y).
    private static int Brightness(ScreenPicture picture, int x, int y) => picture.Pixels[y * picture.Stride + x * 4 + 1];
}
