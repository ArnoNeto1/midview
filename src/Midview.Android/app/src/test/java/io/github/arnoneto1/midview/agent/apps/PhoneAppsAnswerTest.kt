package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.protocol.PhoneAppList
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneAppsAnswerTest {
    private fun app(i: Int, icon: Int = 3000, label: String = "App $i") =
        PhoneApp("com.example.app$i", label, ByteArray(icon) { 7 })

    @Test fun aShortLabelStaysAsIs() {
        assertEquals("Câmera", PhoneAppsAnswer.label("  Câmera "))
    }

    @Test fun aLongLabelIsCutAt64Chars() {
        assertEquals("x".repeat(64), PhoneAppsAnswer.label("x".repeat(80)))
    }

    // Never half of an emoji.
    @Test fun aLabelIsNotCutInsideASurrogatePair() {
        val label = PhoneAppsAnswer.label("x".repeat(63) + "😀" + "y")
        assertEquals("x".repeat(63), label)
    }

    @Test fun theAnswerIsSortedByLabel() {
        val apps = listOf(app(1, label = "Zap"), app(2, label = "câmera"), app(3, label = "Chrome"))
        val decoded = PhoneAppList.decode(PhoneAppsAnswer.encode(apps))!!
        assertEquals(listOf("câmera", "Chrome", "Zap"), decoded.map { it.label })
    }

    @Test fun atMost300AppsAreSent() {
        val decoded = PhoneAppList.decode(PhoneAppsAnswer.encode((1..350).map { app(it, icon = 10) }))!!
        assertEquals(300, decoded.size)
    }

    @Test fun aTooBigIconIsLeftOut() {
        val decoded = PhoneAppList.decode(PhoneAppsAnswer.encode(listOf(app(1, icon = 70_000), app(2))))!!
        assertEquals(0, decoded.first { it.pkg == "com.example.app1" }.iconPng.size)
        assertEquals(3000, decoded.first { it.pkg == "com.example.app2" }.iconPng.size)
    }

    @Test fun iconsAreLeftOutWhenTheAnswerWouldBeTooBig() {
        val payload = PhoneAppsAnswer.encode((1..300).map { app(it, icon = 20_000) })
        assertTrue(payload.size <= PhoneAppsAnswer.MAX_PAYLOAD)
        val decoded = PhoneAppList.decode(payload)!!
        assertEquals(300, decoded.size)
        assertTrue(decoded.all { it.iconPng.isEmpty() })
    }

    @Test fun duplicatesAndBadNamesAreLeftOut() {
        val apps = listOf(app(1), app(1), PhoneApp("not a package", "X", ByteArray(0)))
        assertEquals(listOf("com.example.app1"), PhoneAppList.decode(PhoneAppsAnswer.encode(apps))!!.map { it.pkg })
    }

    // The list is sorted and cut before any icon is drawn.
    @Test fun pickSortsByLabelAndKeepsAtMost300() {
        val labels = (1..320).map { "app %03d".format(321 - it) }
        val picked = PhoneAppsAnswer.pick(labels) { it }
        assertEquals(300, picked.size)
        assertEquals("app 001", picked.first())
        assertEquals("app 300", picked.last())
    }
}
