package io.github.arnoneto1.midview.agent.apps

import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import android.net.Uri
import android.provider.MediaStore
import android.provider.Settings
import android.telecom.TelecomManager
import android.view.inputmethod.InputMethodManager
import android.webkit.WebView
import io.github.arnoneto1.midview.agent.optimize.BloatCatalog
import io.github.arnoneto1.midview.agent.optimize.BloatList
import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.protocol.PhoneAppList
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.sites.BrowserCatalog
import io.github.arnoneto1.midview.agent.sites.ChromePolicy
import java.io.ByteArrayOutputStream
import java.text.Collator
import java.util.Locale

// The phone's apps as the PackageManager sees them: what the student can open
// (with its label and icon, for the teacher's list), what is never suspended
// and what is suspended now; its browsers (OtherBrowsers); and, for
// "Otimizar", which listed apps it has and what is never hidden (Optimizer).
class InstalledApps(context: Context) : AppCatalog, BrowserCatalog, BloatCatalog {
    private val app = context.applicationContext
    private val pm = app.packageManager

    override fun launchable(): Set<String> =
        pm.queryIntentActivities(launcherIntent(), 0).map { it.activityInfo.packageName }.toSet()

    // ProtectedApps plus what this phone resolves (ProtectedApps.resolve):
    // of the apps for the home screen, dialing, the settings, picking a file
    // (the system's file picker, which other apps open), installing and
    // verifying packages (Android refuses to suspend the verifier) and the
    // keyboards, those that came with the phone; and the ones in use: the
    // default home screen, dialer and keyboard, and the WebView.
    override fun protected(): Set<String> {
        val candidates = mutableListOf<ProtectedApps.Candidate>()
        fun all(intent: Intent) = pm.queryIntentActivities(intent, PackageManager.MATCH_DEFAULT_ONLY)
            .mapTo(candidates) { candidate(it.activityInfo.applicationInfo) }
        val home = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
        all(home)
        all(Intent(Intent.ACTION_DIAL))
        all(Intent(Intent.ACTION_DIAL, Uri.parse("tel:190")))
        all(Intent(Settings.ACTION_SETTINGS))
        all(Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"))
        @Suppress("DEPRECATION")
        all(Intent(Intent.ACTION_INSTALL_PACKAGE).setDataAndType(Uri.parse("content://x/y.apk"), APK_TYPE))
        pm.queryBroadcastReceivers(Intent(Intent.ACTION_PACKAGE_NEEDS_VERIFICATION).setType(APK_TYPE), 0)
            .mapTo(candidates) { candidate(it.activityInfo.applicationInfo) }
        runCatching { app.getSystemService(InputMethodManager::class.java)?.inputMethodList }.getOrNull()
            ?.mapTo(candidates) { candidate(it.serviceInfo.applicationInfo) }

        val inUse = listOf(
            runCatching { pm.resolveActivity(home, PackageManager.MATCH_DEFAULT_ONLY)?.activityInfo?.packageName }.getOrNull(),
            runCatching { app.getSystemService(TelecomManager::class.java)?.defaultDialerPackage }.getOrNull(),
            ProtectedApps.inputMethodPackage(
                runCatching { Settings.Secure.getString(app.contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD) }.getOrNull(),
            ),
            runCatching { WebView.getCurrentWebViewPackage()?.packageName }.getOrNull(),
            app.packageName,
        )
        return ProtectedApps.resolve(candidates, inUse)
    }

    // What opens a web address of any site: a site of its own would not
    // match example.com.
    override fun browsers(): Set<String> =
        listOf("http://www.example.com/", "https://www.example.com/").flatMap { address ->
            val view = Intent(Intent.ACTION_VIEW, Uri.parse(address)).addCategory(Intent.CATEGORY_BROWSABLE)
            pm.queryIntentActivities(view, PackageManager.MATCH_ALL).map { it.activityInfo.packageName }
        }.toSet()

    override fun isChromeInstalled(): Boolean = isInstalled(ChromePolicy.PACKAGE)

    override fun isInstalled(pkg: String): Boolean =
        try {
            pm.getApplicationInfo(pkg, 0)
            true
        } catch (_: PackageManager.NameNotFoundException) {
            false
        }

    // Hidden apps too (Android lists them only with MATCH_UNINSTALLED_PACKAGES),
    // not one removed for this user (which comes without FLAG_INSTALLED).
    override fun isPresent(pkg: String): Boolean =
        try {
            (pm.getApplicationInfo(pkg, PackageManager.MATCH_UNINSTALLED_PACKAGES).flags and ApplicationInfo.FLAG_INSTALLED) != 0
        } catch (_: PackageManager.NameNotFoundException) {
            false
        }

    override fun neverHide(): Set<String> = BloatList.neverHide(protected(), cameras())

    // The phone's own cameras: the system apps that take a picture for
    // another app or open as the camera.
    private fun cameras(): Set<String> =
        listOf(Intent(MediaStore.ACTION_IMAGE_CAPTURE), Intent(MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA))
            .flatMap { pm.queryIntentActivities(it, PackageManager.MATCH_DEFAULT_ONLY) }
            .map { candidate(it.activityInfo.applicationInfo) }
            .filter { it.system }
            .map { it.pkg }
            .toSet()

    private fun candidate(info: ApplicationInfo) = ProtectedApps.Candidate(
        info.packageName,
        system = (info.flags and (ApplicationInfo.FLAG_SYSTEM or ApplicationInfo.FLAG_UPDATED_SYSTEM_APP)) != 0,
    )

    override fun suspended(): Set<String> =
        pm.getInstalledApplications(0)
            .filter { (it.flags and ApplicationInfo.FLAG_SUSPENDED) != 0 }
            .map { it.packageName }
            .toSet()

    // The apps the teacher may block: those with an icon on the home screen,
    // without the protected ones, one entry per app; sorted and cut to what is
    // sent before any icon is drawn.
    fun list(): List<PhoneApp> {
        val protected = protected()
        val seen = HashSet<String>()
        val entries = pm.queryIntentActivities(launcherIntent(), 0)
            .filter { info -> info.activityInfo.packageName.let { it !in protected && seen.add(it) } }
            .map { info ->
                val pkg = info.activityInfo.packageName
                val label = PhoneAppsAnswer.label(info.loadLabel(pm)?.toString() ?: pkg)
                info to label.ifEmpty { pkg.take(PhoneAppList.MAX_LABEL_LENGTH) }
            }
        return PhoneAppsAnswer.pick(entries) { it.second }.map { (info, label) ->
            val icon = runCatching { png(info.loadIcon(pm)) }.getOrDefault(ByteArray(0))
            PhoneApp(info.activityInfo.packageName, label, icon)
        }
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

    // Sorted by label as a Brazilian reader expects, at most 300.
    fun <T> pick(items: List<T>, label: (T) -> String): List<T> {
        val collator = Collator.getInstance(Locale.forLanguageTag("pt-BR"))
        return items.sortedWith { a, b -> collator.compare(label(a), label(b)) }.take(PhoneAppList.MAX_APPS)
    }

    fun encode(apps: List<PhoneApp>): ByteArray {
        val seen = HashSet<String>()
        val kept = pick(apps.filter { PhoneAppPolicy.isValidPackageName(it.pkg) && seen.add(it.pkg) }) { it.label }
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
