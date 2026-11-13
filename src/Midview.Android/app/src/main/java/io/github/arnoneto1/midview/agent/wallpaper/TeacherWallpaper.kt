package io.github.arnoneto1.midview.agent.wallpaper

import io.github.arnoneto1.midview.agent.protocol.PhoneWallpaper
import java.io.IOException

// What the wallpaper needs from Android (WallpaperManager and BitmapFactory),
// behind an interface so the decisions are tested on the JVM. screens is
// PhoneWallpaper's: WallpaperManager.FLAG_SYSTEM and FLAG_LOCK.
interface WallpaperTarget {
    // The picture's width and height from its header, or null when Android
    // cannot read it.
    fun size(image: ByteArray): Pair<Int, Int>?

    // Whether the whole picture decodes (a header alone can be fine on a
    // cut or broken file).
    fun decodes(image: ByteArray): Boolean

    // Puts the picture on the screens; false when Android refused it.
    fun set(image: ByteArray, screens: Int): Boolean

    // The phone's own wallpaper back on the screens.
    fun clear(screens: Int)
}

// "Papel de parede dos celulares e tablets": the teacher's picture on the
// home screen, the lock screen or both, as the Device Owner, so the student
// never goes through the "Temas" app (on Xiaomi it wants its terms
// accepted). No picture puts the phone's own wallpaper back. The picture is
// checked before Android sees it: a JPEG or PNG (PhoneWallpaper.decode), at
// most PhoneWallpaper.MAX_SIDE pixels on each side, that decodes whole.
//
// The student cannot change it: Lockdown's no_set_wallpaper stops every app
// that goes through Android's WallpaperManager (the Device Owner still can).
// Xiaomi's own picker ("Temas", reached from a long press on the home screen,
// Settings and the gallery) does not: it hands the picture to MIUI's
// wallpaper service directly (checked on the Redmi 10C), so it is hidden
// (showPickers false, PICKERS) while the teacher's wallpaper is on, and
// shown again when the phone's own comes back.
class TeacherWallpaper(
    private val target: WallpaperTarget,
    private val isDeviceOwner: () -> Boolean,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    // Hides (false) or shows again (true) the phone makers' own pickers.
    private val showPickers: (Boolean) -> Unit = {},
) {
    // The teacher's SET_PHONE_WALLPAPER payload in, the CommandAck's result out.
    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val request = PhoneWallpaper.decode(payload) ?: return false to INVALID_REQUEST
        if (!isDeviceOwner()) return false to NOT_PREPARED
        if (request.image.isEmpty()) return restore(request.screens)
        checkPicture(request.image)?.let { return false to it }
        return try {
            if (target.set(request.image, request.screens)) {
                log("Wallpaper set by the teacher (screens ${request.screens}).", null)
                pickers(show = false)
                true to setText(request.screens)
            } else {
                false to REFUSED
            }
        } catch (e: IOException) {
            log("Android did not set the wallpaper", e)
            false to REFUSED
        } catch (e: RuntimeException) {
            log("Android did not set the wallpaper", e)
            false to REFUSED
        }
    }

    private fun restore(screens: Int): Pair<Boolean, String> =
        try {
            target.clear(screens)
            log("The phone's own wallpaper is back (screens $screens).", null)
            pickers(show = true)
            true to restoredText(screens)
        } catch (e: IOException) {
            log("Android did not put the phone's wallpaper back", e)
            false to NOT_RESTORED
        } catch (e: RuntimeException) {
            log("Android did not put the phone's wallpaper back", e)
            false to NOT_RESTORED
        }

    // The wallpaper is on (or off) already: a picker Android would not hide
    // only goes in the log.
    private fun pickers(show: Boolean) {
        try {
            showPickers(show)
        } catch (e: RuntimeException) {
            log("Could not ${if (show) "show" else "hide"} the phone's wallpaper picker", e)
        }
    }

    // Why the picture is refused, or null when it may go on the screen.
    private fun checkPicture(image: ByteArray): String? {
        val (width, height) = target.size(image) ?: return UNREADABLE
        if (!fits(width, height)) return TOO_BIG
        return if (target.decodes(image)) null else UNREADABLE
    }

    companion object {
        // The phone makers' wallpaper pickers that skip no_set_wallpaper:
        // Xiaomi's "Temas" (MIUI and HyperOS).
        val PICKERS = listOf("com.android.thememanager")

        fun fits(width: Int, height: Int): Boolean =
            width in 1..PhoneWallpaper.MAX_SIDE && height in 1..PhoneWallpaper.MAX_SIDE

        // The sample size a check of the whole picture decodes at, so it never
        // holds a 4096 x 4096 bitmap: the smallest power of two that brings
        // the longer side to at most CHECK_SIDE.
        fun checkSampleSize(width: Int, height: Int): Int {
            var sample = 1
            while (maxOf(width, height) / sample > CHECK_SIDE) sample *= 2
            return sample
        }

        const val CHECK_SIDE = 1024

        const val INVALID_REQUEST = "pedido inválido"
        const val NOT_PREPARED = "Este celular não foi preparado pelo Midview."
        const val UNREADABLE = "a imagem não pôde ser lida"
        const val TOO_BIG = "imagem grande demais (até 4096 pixels de cada lado)"
        const val REFUSED = "o Android não trocou o papel de parede"
        const val NOT_RESTORED = "o Android não voltou o papel de parede do celular"

        fun setText(screens: Int): String = "papel de parede posto " + where(screens)

        fun restoredText(screens: Int): String = "papel de parede do celular de volta " + where(screens)

        private fun where(screens: Int): String = when (screens) {
            PhoneWallpaper.HOME -> "na tela inicial"
            PhoneWallpaper.LOCK -> "na tela de bloqueio"
            else -> "na tela inicial e na de bloqueio"
        }
    }
}
