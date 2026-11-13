using System;

namespace Midview.Master;

// The home screen of a Xiaomi phone in "Midview otimizado", arranged over
// the cable (PhoneHomeScreen) after the setup is handed over and before the
// Mi sign-out. A failure never stops the preparation: the ready page then
// says how to finish it by hand.
public sealed partial class PhoneCable
{
    // How the home screen arrangement gets its clock: a fake one in the tests.
    internal Func<Func<string[], AdbOutput>, PhoneHomeScreen> MakeHomeScreen { get; init; } = PhoneHomeScreen.WithRealClock;

    // True once arranged and locked.
    private bool ArrangeHome() => MakeHomeScreen(command => Shell(Short, command)).Run();
}
