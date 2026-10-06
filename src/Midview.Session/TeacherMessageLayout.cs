using System.Drawing;

namespace Midview.Session;

// How the teacher's message is laid out on the student's screen
// (WinFormsMessageDisplay): its texts, and the biggest bold size at which
// the message fits the window, so a short "ATENÇÃO TURMA!" is big and a
// paragraph still fits.
public static class TeacherMessageLayout
{
    public const string Header = "Mensagem do professor";
    public const string OkButton = "OK, ENTENDI";

    public const float MaxFontSize = 22f;
    public const float MinFontSize = 12f;

    // The biggest size, in whole points from MaxFontSize down to
    // MinFontSize, at which the text (measure: its size at a font size,
    // wrapped to the box's width) fits the box; null if it doesn't fit even
    // at MinFontSize, and then it scrolls.
    public static float? FitFontSize(Size box, Func<float, Size> measure)
    {
        for (var size = MaxFontSize; size >= MinFontSize; size--)
        {
            var needed = measure(size);
            if (needed.Width <= box.Width && needed.Height <= box.Height)
            {
                return size;
            }
        }
        return null;
    }
}
