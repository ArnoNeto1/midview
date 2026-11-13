package io.github.arnoneto1.midview.agent.wallpaper

import io.github.arnoneto1.midview.agent.protocol.PhoneWallpaper
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

class TeacherWallpaperTest {
    private val jpeg = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 0xE0.toByte(), 1, 2, 3)

    private class FakeTarget(
        var size: Pair<Int, Int>? = 1080 to 2400,
        var decodes: Boolean = true,
        var accepts: Boolean = true,
        var fails: Exception? = null,
    ) : WallpaperTarget {
        val set = mutableListOf<Int>()
        val cleared = mutableListOf<Int>()

        override fun size(image: ByteArray) = size
        override fun decodes(image: ByteArray) = decodes
        override fun set(image: ByteArray, screens: Int): Boolean {
            fails?.let { throw it }
            if (accepts) set += screens
            return accepts
        }
        override fun clear(screens: Int) {
            fails?.let { throw it }
            cleared += screens
        }
    }

    private fun wallpaper(target: WallpaperTarget, owner: Boolean = true) = TeacherWallpaper(target, { owner })

    @Test fun thePictureGoesOnTheScreensAsked() {
        val target = FakeTarget()
        val wallpaper = wallpaper(target)
        assertEquals(true to "papel de parede posto na tela inicial e na de bloqueio", wallpaper.handle(PhoneWallpaper.encode(3, jpeg)))
        assertEquals(true to "papel de parede posto na tela inicial", wallpaper.handle(PhoneWallpaper.encode(1, jpeg)))
        assertEquals(true to "papel de parede posto na tela de bloqueio", wallpaper.handle(PhoneWallpaper.encode(2, jpeg)))
        assertEquals(listOf(3, 1, 2), target.set)
    }

    @Test fun noPicturePutsThePhonesOwnBack() {
        val target = FakeTarget()
        assertEquals(
            true to "papel de parede do celular de volta na tela inicial e na de bloqueio",
            wallpaper(target).handle(PhoneWallpaper.encode(3, ByteArray(0))),
        )
        assertEquals(listOf(3), target.cleared)
        assertTrue(target.set.isEmpty())
    }

    @Test fun anInvalidRequestTouchesNothing() {
        val target = FakeTarget()
        assertEquals(false to TeacherWallpaper.INVALID_REQUEST, wallpaper(target).handle(byteArrayOf(9, 1, 2)))
        assertEquals(false to TeacherWallpaper.INVALID_REQUEST, wallpaper(target).handle(byteArrayOf(1, 0x47)))
        assertTrue(target.set.isEmpty() && target.cleared.isEmpty())
    }

    @Test fun onlyTheDeviceOwnerSetsIt() {
        val target = FakeTarget()
        assertEquals(false to TeacherWallpaper.NOT_PREPARED, wallpaper(target, owner = false).handle(PhoneWallpaper.encode(3, jpeg)))
        assertEquals(false to TeacherWallpaper.NOT_PREPARED, wallpaper(target, owner = false).handle(PhoneWallpaper.encode(3, ByteArray(0))))
        assertTrue(target.set.isEmpty() && target.cleared.isEmpty())
    }

    @Test fun anUnreadableOrHugePictureIsRefusedBeforeAndroidSeesIt() {
        val unreadable = FakeTarget(size = null)
        assertEquals(false to TeacherWallpaper.UNREADABLE, wallpaper(unreadable).handle(PhoneWallpaper.encode(3, jpeg)))
        val broken = FakeTarget(decodes = false)
        assertEquals(false to TeacherWallpaper.UNREADABLE, wallpaper(broken).handle(PhoneWallpaper.encode(3, jpeg)))
        val huge = FakeTarget(size = 4097 to 2000)
        assertEquals(false to TeacherWallpaper.TOO_BIG, wallpaper(huge).handle(PhoneWallpaper.encode(3, jpeg)))
        assertTrue(unreadable.set.isEmpty() && broken.set.isEmpty() && huge.set.isEmpty())
    }

    @Test fun androidsRefusalIsSaid() {
        assertEquals(false to TeacherWallpaper.REFUSED, wallpaper(FakeTarget(accepts = false)).handle(PhoneWallpaper.encode(3, jpeg)))
        assertEquals(false to TeacherWallpaper.REFUSED, wallpaper(FakeTarget(fails = IOException("x"))).handle(PhoneWallpaper.encode(3, jpeg)))
        assertEquals(false to TeacherWallpaper.REFUSED, wallpaper(FakeTarget(fails = SecurityException("x"))).handle(PhoneWallpaper.encode(3, jpeg)))
        assertEquals(
            false to TeacherWallpaper.NOT_RESTORED,
            wallpaper(FakeTarget(fails = IOException("x"))).handle(PhoneWallpaper.encode(3, ByteArray(0))),
        )
    }

    // Xiaomi's "Temas" goes around no_set_wallpaper: hidden while the
    // teacher's wallpaper is on, shown again with the phone's own.
    @Test fun thePhonesOwnPickerIsHiddenWhileTheTeachersWallpaperIsOn() {
        val shown = mutableListOf<Boolean>()
        val wallpaper = TeacherWallpaper(FakeTarget(), { true }, showPickers = { shown += it })
        wallpaper.handle(PhoneWallpaper.encode(3, jpeg))
        wallpaper.handle(PhoneWallpaper.encode(3, ByteArray(0)))
        assertEquals(listOf(false, true), shown)
        assertEquals(listOf("com.android.thememanager"), TeacherWallpaper.PICKERS)
    }

    @Test fun aRefusedOrInvalidWallpaperLeavesThePickerAlone() {
        val shown = mutableListOf<Boolean>()
        TeacherWallpaper(FakeTarget(accepts = false), { true }, showPickers = { shown += it }).handle(PhoneWallpaper.encode(3, jpeg))
        TeacherWallpaper(FakeTarget(size = null), { true }, showPickers = { shown += it }).handle(PhoneWallpaper.encode(3, jpeg))
        TeacherWallpaper(FakeTarget(), { false }, showPickers = { shown += it }).handle(PhoneWallpaper.encode(3, ByteArray(0)))
        assertTrue(shown.isEmpty())
    }

    @Test fun aPickerAndroidWillNotHideDoesNotFailTheWallpaper() {
        val wallpaper = TeacherWallpaper(FakeTarget(), { true }, showPickers = { throw SecurityException("no") })
        assertEquals(true to "papel de parede posto na tela inicial e na de bloqueio", wallpaper.handle(PhoneWallpaper.encode(3, jpeg)))
    }

    @Test fun upTo4096PixelsASideFit() {
        assertTrue(TeacherWallpaper.fits(4096, 4096))
        assertTrue(TeacherWallpaper.fits(1, 1))
        assertFalse(TeacherWallpaper.fits(4097, 100))
        assertFalse(TeacherWallpaper.fits(100, 4097))
        assertFalse(TeacherWallpaper.fits(0, 100))
    }

    @Test fun theCheckDecodesAtMost1024PixelsASide() {
        assertEquals(1, TeacherWallpaper.checkSampleSize(1024, 800))
        assertEquals(2, TeacherWallpaper.checkSampleSize(1440, 1000))
        assertEquals(4, TeacherWallpaper.checkSampleSize(1440, 3200))
        assertEquals(4, TeacherWallpaper.checkSampleSize(4096, 4096))
    }
}
