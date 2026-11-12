package io.github.arnoneto1.midview.agent.apps

import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import android.net.Uri
import android.provider.Settings
import android.telecom.TelecomManager
import android.view.inputmethod.InputMethodManager
import android.webkit.WebView
import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.protocol.PhoneAppList
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import java.io.ByteArrayOutputStream
import java.text.Collator
import java.util.Locale

// The phone's apps as the PackageManager sees them: what the student can open
// (with its label and icon, for the teacher's list), what is never suspended
// and what is suspended now.
class InstalledApps(context: Context) : AppCatalog {
    private val app = context.applicationContext
    private val pm = app.packageManager

    override fun launchable(): Set<String> =
        pm.queryIntentActivities(launcherIntent(), 0).map { it.activityInfo.packageName }.toSet()

    // ProtectedApps plus what this phone resolves: every home screen, every
    // app that dials, the settings, the package installer and verifier
    // (Android refuses to suspend the verifier: Play Store on most phones),
    // the WebView in use and the keyboards.
    override fun protected(): Set<String> {
        val resolved = mutableListOf<String?>()
        fun all(intent: Intent) = pm.queryIntentActivities(intent, PackageManager.MATCH_DEFAULT_ONLY)
            .mapTo(resolved) { it.activityInfo.packageName }
        all(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME))
        all(Intent(Intent.ACTION_DIAL))
        all(Intent(Intent.ACTION_DIAL, Uri.parse("tel:190")))
        all(Intent(Settings.ACTION_SETTINGS))
        @Suppress("DEPRECATION")
        all(Intent(Intent.ACTION_INSTALL_PACKAGE).setDataAndType(Uri.parse("content://x/y.apk"), APK_TYPE))
        pm.queryBroadcastReceivers(Intent(Intent.ACTION_PACKAGE_NEEDS_VERIFICATION).setType(APK_TYPE), 0)
            .mapTo(resolved) { it.activityInfo.packageName }
        resolved += runCatching { app.getSystemService(TelecomManager::class.java)?.defaultDialerPackage }.getOrNull()
        resolved += runCatching { WebView.getCurrentWebViewPackage()?.packageName }.getOrNull()
        runCatching { app.getSystemService(InputMethodManager::class.java)?.inputMethodList }.getOrNull()
            ?.mapTo(resolved) { it.packageName }
        resolved += app.packageName
        return ProtectedApps.with(resolved)
    }

    override fun suspended(): Set<String> =
        pm.getInstalledApplications(0)
            .filter { (it.flags and ApplicationInfo.FLAG_SUSPENDED) != 0 }
            .map { it.packageName }
            .toSet()

    // The apps the teacher may block: those with an icon on the home screen,
    // without the protected ones, one entry per app.
    fun list(): List<PhoneApp> {
        val protected = protected()
        val seen = HashSet<String>()
        val apps = ArrayList<PhoneApp>()
        for (info in pm.queryIntentActivities(launcherIntent(), 0)) {
            val pkg = info.activityInfo.packageName
            if (pkg in protected || !seen.add(pkg)) continue
            val label = PhoneAppsAnswer.label(info.loadLabel(pm)?.toString() ?: pkg)
            val icon = runCatching { png(info.loadIcon(pm)) }.getOrDefault(ByteArray(0))
            apps += PhoneApp(pkg, label.ifEmpty { pkg.take(PhoneAppList.MAX_LABEL_LENGTH) }, icon)
        }
        return apps
    }

    private fun launcherIntent() = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)

    // The icon drawn at 48x48 (adaptive icons included) as a PNG.
    private fun png(drawable: Drawable): ByteArray {
        val bitmap = Bitmap.createBitmap(ICON_SIZE, ICON_SIZE, Bitmap.Config.ARGB_8888)
        drawable.setBounds(0, 0, ICON_SIZE, ICON_SIZE)
        drawable.draw(Canvas(bitmap))
        return ByteArrayOutputStream().use { out ->
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, out)
            bitmap.recycle()
            out.toByteArray()
        }
    }

    private companion object {
        const val ICON_SIZE = 48
        const val APK_TYPE = "application/vnd.android.package-archive"
    }
}

// The PhoneApps answer, kept well under the frame's limit
// (MessageFramer.MAX): at most 300 apps sorted by label, an icon too big for
// the format left out, and every icon left out if the whole would pass
// MAX_PAYLOAD.
object PhoneAppsAnswer {
    const val MAX_PAYLOAD = 4 * 1024 * 1024

    // Trimmed and cut at 64 chars, never inside a surrogate pair.
    fun label(text: String): String {
        val trimmed = text.trim()
        if (trimmed.length <= PhoneAppList.MAX_LABEL_LENGTH) return trimmed
        var end = PhoneAppList.MAX_LABEL_LENGTH
        if (Character.isHighSurrogate(trimmed[end - 1])) end--
        return trimmed.substring(0, end)
    }

    fun encode(apps: List<PhoneApp>): ByteArray {
        val collator = Collator.getInstance(Locale.forLanguageTag("pt-BR"))
        val seen = HashSet<String>()
        val kept = apps
            .filter { PhoneAppPolicy.isValidPackageName(it.pkg) && seen.add(it.pkg) }
            .sortedWith { a, b -> collator.compare(a.label, b.label) }
            .take(PhoneAppList.MAX_APPS)
            .map {
                val label = label(it.label)
                val icon = if (it.iconPng.size > PhoneAppList.MAX_ICON_BYTES) ByteArray(0) else it.iconPng
                if (label == it.label && icon === it.iconPng) it else PhoneApp(it.pkg, label, icon)
            }
        val payload = PhoneAppList.encode(kept)
        return if (payload.size <= MAX_PAYLOAD) {
            payload
        } else {
            PhoneAppList.encode(kept.map { PhoneApp(it.pkg, it.label, ByteArray(0)) })
        }
    }
}
