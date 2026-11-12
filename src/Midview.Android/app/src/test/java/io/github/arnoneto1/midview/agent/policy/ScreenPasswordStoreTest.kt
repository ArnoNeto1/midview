package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class ScreenPasswordStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "screen-password.txt") }
    private val token = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF"
    private val state = ScreenPasswordState(token, 7, "1234", changedByOther = true)

    @Test fun nothingKeptAtFirst() {
        assertEquals(ScreenPasswordState.EMPTY, ScreenPasswordStore(file).current)
        assertFalse(file.exists())
    }

    @Test fun whatIsSavedIsThereAfterARestart() {
        assertTrue(ScreenPasswordStore(file).save(state))
        assertEquals(state, ScreenPasswordStore(file).current)
        val none = ScreenPasswordState(token, 7, null, false)
        ScreenPasswordStore(file).save(none)
        assertEquals(none, ScreenPasswordStore(file).current)
    }

    @Test fun theFileIsKeyValueLines() {
        ScreenPasswordStore(file).save(state)
        assertEquals("token=$token\ntokenBoot=7\npin=1234\nchanged=1\n", file.readText(Charsets.US_ASCII))
        ScreenPasswordStore(file).save(ScreenPasswordState.EMPTY)
        assertEquals("changed=0\n", file.readText(Charsets.US_ASCII))
    }

    // With no copy to read, the teacher's PIN is unknown: never "no PIN",
    // which would remove the teacher's.
    @Test fun aDamagedFileWithoutACopyLeavesThePinUnknown() {
        val logged = mutableListOf<String>()
        for (text in listOf("token=xyz\n", "pin=12345\n", "pin=\n", "tokenBoot=a\n", "changed=2\n", "garbage\n")) {
            file.writeText(text, Charsets.US_ASCII)
            assertEquals(text, ScreenPasswordState.UNKNOWN, ScreenPasswordStore(file) { t, _ -> logged += t }.current)
        }
        assertTrue(logged.isNotEmpty())
    }

    @Test fun aDamagedFileIsReadFromItsCopy() {
        ScreenPasswordStore(file).save(state)
        file.writeText("garbage\n", Charsets.US_ASCII)
        assertEquals(state, ScreenPasswordStore(file).current)
    }

    @Test fun theUnknownPinIsKeptUntilTheTeacherSendsOne() {
        file.writeText("garbage\n", Charsets.US_ASCII)
        val store = ScreenPasswordStore(file)
        store.save(store.current.copy(token = token, tokenBoot = 3))
        assertTrue(ScreenPasswordStore(file).current.pinUnknown)
        store.save(store.current.copy(teacherPin = "1234", pinUnknown = false))
        assertEquals("1234", ScreenPasswordStore(file).current.teacherPin)
        assertFalse(ScreenPasswordStore(file).current.pinUnknown)
    }

    @Test fun decodeRefusesWhatItDidNotWrite() {
        assertNull(ScreenPasswordStore.decode("token=${token.lowercase()}\n".toByteArray()))
        assertEquals(state, ScreenPasswordStore.decode(ScreenPasswordStore.encode(state)))
    }

    @Test fun aDiskThatRefusesIsReported() {
        val blocked = File(folder.newFile("not-a-folder"), "screen-password.txt")
        val store = ScreenPasswordStore(blocked)
        assertFalse(store.save(state))
        // Still counts until the agent starts again.
        assertEquals(state, store.current)
    }
}
