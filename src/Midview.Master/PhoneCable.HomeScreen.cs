using System;

namespace Midview.Master;

// The home screen in "Midview otimizado", arranged over the cable after the
// setup is handed over and before the Mi sign-out: a Xiaomi's
// (PhoneHomeScreen), or that of a device whose home is Android's own
// launcher, Launcher3 (PhoneLauncher3Home: the school's Multilaser M10).
// Other launchers are left as they came. A failure never stops the
// preparation: the ready page then says how to finish it by hand.
public sealed partial class PhoneCable
{
    // How the home screen arrangement gets its clock: a fake one in the tests.
    internal Func<Func<string[], AdbOutput>, PhoneHomeScreen> MakeHomeScreen { get; init; } = PhoneHomeScreen.WithRealClock;

    internal Func<Func<string[], AdbOutput>, PhoneLauncher3Home> MakeLauncher3Home { get; init; } = PhoneLauncher3Home.WithRealClock;

    // True once arranged and locked.
    private bool ArrangeHome() => MakeHomeScreen(command => Shell(Short, command)).Run();

    // True once arranged, with its two settings off.
    private bool ArrangeLauncher3Home() => MakeLauncher3Home(command => Shell(Short, command)).Run();

    // Whether the device's home (the default one) is Launcher3.
    private bool HomeIsLauncher3() => PhoneLauncher3Home.IsHome(Shell(
        Short, "cmd", "package", "resolve-activity", "--brief",
        "-a", "android.intent.action.MAIN", "-c", "android.intent.category.HOME").Text);

    // The home screen of a Xiaomi or of a Launcher3 device, in "Midview
    // otimizado"; null when it is not the cable's to arrange. False: the
    // teacher arranges it by hand.
    private bool? ArrangedHome(bool optimize, Action<CableStage> stage)
    {
        if (!optimize || (!Miui && !HomeIsLauncher3()))
        {
            return null;
        }
        stage(CableStage.HomeScreen);
        return Miui ? ArrangeHome() : ArrangeLauncher3Home();
    }
}
