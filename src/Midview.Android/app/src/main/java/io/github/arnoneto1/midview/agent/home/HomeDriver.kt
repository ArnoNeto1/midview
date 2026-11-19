package io.github.arnoneto1.midview.agent.home

import io.github.arnoneto1.midview.agent.protocol.PhoneHomeStates

// What Launcher3Home needs of the device to arrange its home screen, as the
// Master's PhoneLauncher3Home has it over the cable ("adb shell"): the screen
// read, Android's Home and Back buttons, taps, swipes and long-press drags
// (AccessibilityHomeDriver: the Midview accessibility service), the
// launcher's settings opened, and a clock. A fake in the tests.
interface HomeDriver {
    // The screen's window in front (the launcher's, its settings'), or null
    // when it cannot be read now.
    fun screen(): HomeNode?

    fun home()

    fun back()

    fun tap(x: Int, y: Int)

    fun swipe(x1: Int, y1: Int, x2: Int, y2: Int, millis: Long)

    // A long press at (x1, y1), then the finger moved to (x2, y2) and
    // lifted there: what "input draganddrop" does over the cable.
    fun drag(x1: Int, y1: Int, x2: Int, y2: Int)

    // The launcher's settings (APPLICATION_PREFERENCES on the launcher);
    // false when Android did not open them.
    fun openLauncherSettings(): Boolean

    // Whether the student can open this app now (installed, not hidden by
    // "Otimizar"): the dock wants only those.
    fun isLaunchable(pkg: String): Boolean

    fun pause(millis: Long)

    // Since the arrangement began.
    fun elapsedMillis(): Long
}

// What is checked before the home is touched, and the driver that touches
// it (AndroidHomeDevice).
interface HomeDevice {
    // The default home's package (null: none chosen, or unknown).
    fun defaultHome(): String?

    // The Midview accessibility service is connected.
    fun accessibilityRunning(): Boolean

    // The screen is on, not locked, and not under the teacher's lock screen.
    fun screenReady(): Boolean

    // The driver for one arrangement, or null when the service went away;
    // close() gives back what it changed to read the launcher.
    fun open(): HomeSession?
}

interface HomeSession : AutoCloseable {
    val driver: HomeDriver

    override fun close()
}

// The home screen arranged over the network ("Otimizar"), or why not
// (PhoneHomeStates): only a home that is Android's own launcher, with the
// accessibility service connected and the screen on and unlocked (an
// arrangement drives the screen as the teacher would by hand, so the screen
// must be there to drive); anything Launcher3Home did not expect leaves it
// for the teacher's hands.
object HomeArrangement {
    fun arrange(device: HomeDevice, log: (String, Throwable?) -> Unit = { _, _ -> }): String {
        val home = device.defaultHome()
        if (home != Launcher3Home.LAUNCHER) {
            log("Home screen not arranged: the home is $home, not Android's own launcher.", null)
            return PhoneHomeStates.OTHER_LAUNCHER
        }
        if (!device.accessibilityRunning()) return PhoneHomeStates.NO_ACCESSIBILITY
        if (!device.screenReady()) return PhoneHomeStates.SCREEN_OFF
        val session = device.open() ?: return PhoneHomeStates.NO_ACCESSIBILITY
        return session.use {
            if (Launcher3Home(it.driver, log).run()) PhoneHomeStates.ARRANGED else PhoneHomeStates.BY_HAND
        }
    }
}
