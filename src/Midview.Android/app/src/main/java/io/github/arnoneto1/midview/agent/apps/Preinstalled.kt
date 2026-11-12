package io.github.arnoneto1.midview.agent.apps

// Whether an app came with the phone, for "Desinstalar programa" (and the
// flag the teacher's list shows): a system app (or an update of one), which
// Android cannot remove, or one the phone's maker put on it to be installed
// at its first start (MIUI's "data-app": the Notes, the Weather, its
// Calculator...). Android lets those go like any other app, but a school
// phone, with no account, has no way to get them back. Their install time
// is the giveaway: Android dates them before the system itself was built
// (MIUI writes 1974, system apps 1970 or 2009), while any app installed
// later, even by the phone's setup, is dated when it really was.
object Preinstalled {
    // buildTime: when the phone's system was built (Build.TIME), in ms.
    fun cameWithPhone(system: Boolean, firstInstallTime: Long, buildTime: Long): Boolean =
        system || firstInstallTime < buildTime
}
