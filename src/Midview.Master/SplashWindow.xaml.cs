using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Midview.Master;

// The teacher program's opening: the logo assembles itself -- the monitor
// grows in, the eye lights up, the network spreads to the corners, the
// signal goes out, the word rises in (the timings of
// assets\gerar-animacao.py) -- then Finished, for the classroom to open. A
// click skips it.
public partial class SplashWindow : Window
{
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(3.1);

    // A little past the farthest corner of the network from the eye
    // (139 px), so the soft edge clears it too.
    private const double NetworkRadius = 160;

    private readonly DispatcherTimer _timer = new() { Interval = Length };
    private bool _finished;

    public SplashWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Finish();
        Loaded += (_, _) =>
        {
            Play();
            _timer.Start();
        };
    }

    public event EventHandler? Finished;

    private void Play()
    {
        Grow(Monitor, MonitorScale, from: 0.6, begin: 0.0, seconds: 0.55);
        Grow(Eye, EyeScale, from: 0.3, begin: 0.4, seconds: 0.35);
        Animate(NetworkReveal, RadialGradientBrush.RadiusXProperty, 0, NetworkRadius, begin: 0.6, seconds: 1.0);
        Animate(NetworkReveal, RadialGradientBrush.RadiusYProperty, 0, NetworkRadius, begin: 0.6, seconds: 1.0);
        Animate(InnerWaves, OpacityProperty, 0, 1, begin: 1.35, seconds: 0.3);
        Animate(OuterWaves, OpacityProperty, 0, 1, begin: 1.6, seconds: 0.3);
        Animate(Word, OpacityProperty, 0, 1, begin: 2.0, seconds: 0.7);
        Animate(WordShift, TranslateTransform.YProperty, 18, 0, begin: 2.0, seconds: 0.7);
    }

    private static void Grow(UIElement piece, ScaleTransform scale, double from, double begin, double seconds)
    {
        Animate(piece, OpacityProperty, 0, 1, begin, seconds);
        Animate(scale, ScaleTransform.ScaleXProperty, from, 1, begin, seconds);
        Animate(scale, ScaleTransform.ScaleYProperty, from, 1, begin, seconds);
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to, double begin, double seconds) =>
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(begin),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

    private void OnMouseDown(object sender, MouseButtonEventArgs e) => Finish();

    // Once: the timer and a click can both get here.
    private void Finish()
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        _timer.Stop();
        Finished?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
