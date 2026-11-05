using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Midview.Master;

// A short label drawn in capitals with room between the letters, which a
// TextBlock can't do: the names of the classroom toolbar's groups
// ("ENERGIA"). Text keeps its own casing, and screen readers get it as
// written. Spacing is in ems, like CSS letter-spacing, and goes only
// between letters, so a centered label stays centered.
public sealed class SpacedCapsText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(SpacedCapsText),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var letters = Letters();
        if (letters.Count == 0)
        {
            return default;
        }
        var width = letters.Sum(letter => letter.WidthIncludingTrailingWhitespace) + Gap * (letters.Count - 1);
        return new Size(width, letters.Max(letter => letter.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var x = 0.0;
        foreach (var letter in Letters())
        {
            drawingContext.DrawText(letter, new Point(x, 0));
            x += letter.WidthIncludingTrailingWhitespace + Gap;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SpacedCapsTextPeer(this);

    private double Gap => Spacing * FontSize;

    private List<FormattedText> Letters()
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return (Text ?? string.Empty).ToUpper(CultureInfo.CurrentCulture)
            .Select(letter => new FormattedText(
                letter.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground, pixelsPerDip))
            .ToList();
    }

    private sealed class SpacedCapsTextPeer(SpacedCapsText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(SpacedCapsText);
    }
}
