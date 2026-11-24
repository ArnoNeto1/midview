package io.github.arnoneto1.midview.agent.ui

import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.ZoneId
import java.time.ZonedDateTime

class SchoolSettingsTextsTest {
    @Test fun wrongSaysHowManyTriesAreLeft() {
        assertEquals("Senha errada. Restam 4 tentativas.", SchoolSettingsTexts.wrong(4))
        assertEquals("Senha errada. Resta 1 tentativa.", SchoolSettingsTexts.wrong(1))
    }

    @Test fun afterAnAttemptOnlyWrongSaysSomething() {
        assertEquals("Senha errada. Restam 2 tentativas.", SchoolSettingsTexts.after(AdminUnlock.Attempt.Wrong(2)))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.Verified))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.LockedOut(0)))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.NoPassword))
    }

    @Test fun theChoicesAreTheThreeLocks() {
        assertEquals(
            listOf("Wi-Fi e modo avião", "Depuração USB", "Contas do aparelho"),
            AdminUnlock.Freeable.entries.map(SchoolSettingsTexts::choice),
        )
    }

    // Only a refusal is said under the choices: the freed lines say the rest.
    @Test fun afterAChoiceOnlyARefusalSaysSomething() {
        assertNull(SchoolSettingsTexts.afterFree(true to "depuração USB liberada e ligada por 10 minutos"))
        assertEquals("O Android não liberou as contas do aparelho.", SchoolSettingsTexts.afterFree(false to "o Android não liberou as contas do aparelho"))
        assertEquals(AdminUnlock.UNLOCK_ENDED, SchoolSettingsTexts.afterFree(false to AdminUnlock.UNLOCK_ENDED))
    }

    @Test fun theFreedLinesSayEachLockThatIsFree() {
        assertEquals("", SchoolSettingsTexts.freed(null, null, null))
        assertEquals(
            "Depuração USB liberada e ligada até 09:05.\nDepois travam de novo sozinhos.",
            SchoolSettingsTexts.freed(null, "09:05", null),
        )
        assertEquals(
            "Wi-Fi e modo avião liberados até 09:05.\nDepuração USB liberada e ligada até 09:07.\n" +
                "Contas do aparelho liberadas até 09:10.\nDepois travam de novo sozinhos.",
            SchoolSettingsTexts.freed("09:05", "09:07", "09:10"),
        )
    }

    // The screen speaks of the three locks, before and without the password.
    @Test fun theAskSaysWhatThePasswordFrees() {
        for (text in listOf(SchoolSettingsTexts.ASK, SchoolSettingsTexts.NO_PASSWORD)) {
            assertTrue(text, text.contains("depuração USB") && text.contains("contas") && text.contains("Wi-Fi"))
        }
    }

    @Test fun theClockIsHoursAndMinutes() {
        val zone = ZoneId.of("America/Sao_Paulo")
        val millis = ZonedDateTime.of(2026, 11, 13, 9, 5, 30, 0, zone).toInstant().toEpochMilli()
        assertEquals("09:05", SchoolSettingsTexts.clock(millis, zone))
        assertEquals(
            "Wi-Fi e modo avião liberados até 09:05. Depois eles travam de novo sozinhos.",
            SchoolSettingsTexts.freeUntil("09:05"),
        )
    }
}
