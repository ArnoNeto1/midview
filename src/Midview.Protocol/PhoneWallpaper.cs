namespace Midview.Protocol;

// Which screens of a phone get the teacher's wallpaper: Android's
// WallpaperManager.FLAG_SYSTEM (the home screen) and FLAG_LOCK (the lock
// screen), the same bits.
[Flags]
public enum PhoneWallpaperScreens : byte
{
    Home = 1,
    Lock = 2,
    Both = Home | Lock,
}

// "Papel de parede dos celulares e tablets": the payload of
// MessageType.SetPhoneWallpaper is one byte, the screens
// (PhoneWallpaperScreens: 1, 2 or 3), then the picture, a JPEG or PNG of at
// most MaxImageBytes; with no picture the phone puts its own wallpaper back
// on those screens. Same rules as PhoneWallpaper.kt.
public static class PhoneWallpaper
{
    // Far above a phone-sized JPEG (the Master sends at most 1440 x 3200 at
    // quality 90, about 1 to 2 MB), and one message.
    public const int MaxImageBytes = 6 * 1024 * 1024;

    // The agent refuses a picture bigger than this on either side.
    public const int MaxSide = 4096;

    public static bool IsValid(PhoneWallpaperScreens screens) =>
        screens is PhoneWallpaperScreens.Home or PhoneWallpaperScreens.Lock or PhoneWallpaperScreens.Both;

    // A JPEG (FF D8 FF) or a PNG (its 8-byte signature), by the first bytes.
    public static bool IsImage(ReadOnlySpan<byte> image) =>
        image.StartsWith(JpegStart) || image.StartsWith(PngStart);

    private static ReadOnlySpan<byte> JpegStart => new byte[] { 0xFF, 0xD8, 0xFF };

    private static ReadOnlySpan<byte> PngStart => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // image: the JPEG or PNG, or empty to put the phone's own wallpaper back.
    public static byte[] Encode(PhoneWallpaperScreens screens, byte[] image)
    {
        if (!IsValid(screens))
        {
            throw new ArgumentOutOfRangeException(nameof(screens), screens, "tela inicial, de bloqueio ou as duas");
        }
        if (image.Length > MaxImageBytes)
        {
            throw new ArgumentException("imagem grande demais", nameof(image));
        }
        if (image.Length > 0 && !IsImage(image))
        {
            throw new ArgumentException("só JPEG ou PNG", nameof(image));
        }
        var payload = new byte[1 + image.Length];
        payload[0] = (byte)screens;
        image.CopyTo(payload, 1);
        return payload;
    }

    // Null for anything but valid screens followed by nothing or a JPEG or
    // PNG of at most MaxImageBytes.
    public static (PhoneWallpaperScreens Screens, byte[] Image)? Decode(byte[] payload)
    {
        if (payload.Length < 1 || payload.Length - 1 > MaxImageBytes)
        {
            return null;
        }
        var screens = (PhoneWallpaperScreens)payload[0];
        if (!IsValid(screens))
        {
            return null;
        }
        var image = payload[1..];
        return image.Length == 0 || IsImage(image) ? (screens, image) : null;
    }
}
