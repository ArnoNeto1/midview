package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport

// What the commands port asks of "Otimizar" (Optimizer).
interface PhoneOptimizer {
    // "Otimizar" (OptimizePhone): runs now, answered with the report.
    fun run(): PhoneOptimizeReport

    // A trusted teacher sent a command: the first optimization, or the DNS
    // tried again, in the background when due.
    fun teacherSeen()
}

// What the optimizer needs to know about the phone's apps (InstalledApps).
interface BloatCatalog {
    // On the phone for this user, hidden or not (an app removed for the
    // user is not).
    fun isPresent(pkg: String): Boolean

    // Never hidden on this phone (BloatList.neverHide).
    fun neverHide(): Set<String>

    // A system app or an update of one, which a Device Owner cannot
    // uninstall (only hide); true when it cannot be told.
    fun cameWithPhone(pkg: String): Boolean
}

// The Device Owner's silent uninstall (SilentUninstaller), for UnusedApps,
// the cable's lists and the apps someone installed (InstalledAppsSweep).
fun interface AppRemover {
    // Waits at most waitMillis for Android: null once uninstalled, NO_ANSWER
    // when Android said nothing in time, otherwise why not.
    fun uninstall(pkg: String, waitMillis: Long): String?

    companion object {
        const val NO_ANSWER = "o Android não respondeu sobre a desinstalação"
    }
}
