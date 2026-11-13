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

    // Discovery asks at every packet: the file is read again only when it changed.
    @Test fun theListIsReadAgainOnlyWhenTheFileChanges() {
        val file = file()
        TrustedMasters(file).add(a)
        val masters = TrustedMasters(file)
        assertTrue(masters.isTrusted(a))
        // Changed behind the agent's back, same time and size: still the memory.
        val stamp = file.lastModified()
        file.writeText("$b\n")
        file.setLastModified(stamp)
        assertTrue(masters.isTrusted(a))
        // A newer file is read again.
        file.setLastModified(stamp + 2_000)
        assertTrue(masters.isTrusted(b))
        assertFalse(masters.isTrusted(a))
    }

    @Test fun aRemovedFileTrustsNobody() {
        val file = file()
        val masters = TrustedMasters(file)
        masters.add(a)
        file.delete()
        assertFalse(masters.isTrusted(a))
        assertEquals(0, masters.count())
    }

    // "Esquecer os outros professores", seen at once by every instance.
    @Test fun keepOnlyLeavesThatTeacherAlone() {
        val file = file()
        val reader = TrustedMasters(file)
        TrustedMasters(file).apply { add(a); add(b) }
        assertTrue(reader.isTrusted(a))

        assertEquals(1, TrustedMasters(file).keepOnly(" ${b.lowercase()}"))

        assertFalse(reader.isTrusted(a))
        assertTrue(reader.isTrusted(b))
        assertEquals(listOf(b), file.readLines().filter { it.isNotBlank() })
        assertEquals(1, TrustedMasters(file).count())
    }

    // Forgotten a moment before by another teacher: it must not come back
    // as the only one.
    @Test fun keepOnlyForATeacherNoLongerOnTheListChangesNothing() {
        val file = file()
        TrustedMasters(file).add(a)

        assertEquals(null, TrustedMasters(file).keepOnly(b))
        assertEquals(listOf(a), file.readLines().filter { it.isNotBlank() })
        assertEquals(null, TrustedMasters(file()).keepOnly(b))
    }

    // A rewrite of the same size goes through the memory every instance
    // shares, whatever the file clock says.
    @Test fun aSameSizeRewriteIsSeenByAnotherInstance() {
        val c = "FEDCBA9876543210FEDCBA9876543210FEDCBA98"
        val file = file()
        TrustedMasters(file).apply { add(a); add(b) }
        val reader = TrustedMasters(file)
        assertTrue(reader.isTrusted(b))
        val stamp = file.lastModified()

        TrustedMasters(file).apply { keepOnly(a); add(c) }
        file.setLastModified(stamp)

        assertFalse(reader.isTrusted(b))
        assertTrue(reader.isTrusted(c))
    }

    @Test fun keepOnlyWithNoOtherTeacherChangesNothing() {
        val file = file()
        TrustedMasters(file).add(a)
        val stamp = file.lastModified()
        file.setLastModified(stamp - 10_000)

        assertEquals(0, TrustedMasters(file).keepOnly(a))
        assertEquals(stamp - 10_000, file.lastModified())
    }
}
