package io.github.arnoneto1.midview.agent.identity

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class TrustedMastersTest {
    @get:Rule val folder = TemporaryFolder()

    private val a = "0123456789ABCDEF0123456789ABCDEF01234567"
    private val b = "89ABCDEF0123456789ABCDEF0123456789ABCDEF"

    private fun file() = File(folder.newFolder(), "trusted-masters.txt")

    @Test fun nobodyIsTrustedAtFirst() {
        val masters = TrustedMasters(file())
        assertEquals(0, masters.count())
        assertFalse(masters.isTrusted(a))
        assertFalse(masters.isTrusted(""))
    }

    @Test fun anAddedTeacherIsTrustedInAnyCase() {
        val masters = TrustedMasters(file())
        masters.add(a.lowercase())
        assertTrue(masters.isTrusted(a))
        assertTrue(masters.isTrusted(" ${a.lowercase()} "))
        assertFalse(masters.isTrusted(b))
    }

    @Test fun theListIsOneUppercaseThumbprintPerLineAndKeptOnDisk() {
        val file = file()
        TrustedMasters(file).apply { add(a.lowercase()); add(b); add(a); add("  ") }
        assertEquals(listOf(a, b), file.readLines().filter { it.isNotBlank() })
        val again = TrustedMasters(file)
        assertEquals(2, again.count())
        assertTrue(again.isTrusted(b))
        assertFalse(File(file.path + ".tmp").exists())
    }

    @Test fun anotherInstanceSeesANewTeacher() {
        val file = file()
        val reader = TrustedMasters(file)
        TrustedMasters(file).add(a)
        assertTrue(reader.isTrusted(a))
    }
}
