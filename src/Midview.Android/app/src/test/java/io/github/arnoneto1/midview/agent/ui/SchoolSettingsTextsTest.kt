package io.github.arnoneto1.midview.agent.ui

import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.ZoneId
import java.time.ZonedDateTime

class SchoolSettingsTextsTest {
    @Test fun wrongSaysHowManyTriesAreLeft() {
        assertEquals("Senha errada. Restam 4 tentativas.", SchoolSettingsTexts.wrong(4))
        assertEquals("Senha errada. Resta 1 tentativa.", SchoolSettingsTexts.wrong(1))
    }

    @Test fun afterAnAttemptOnlyWrongAndRefusedSaySomething() {
        assertEquals("Senha errada. Restam 2 tentativas.", SchoolSettingsTexts.after(AdminUnlock.Attempt.Wrong(2)))
        assertEquals("o motivo", SchoolSettingsTexts.after(AdminUnlock.Attempt.Refused("o motivo")))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.Freed))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.LockedOut(0)))
        assertNull(SchoolSettingsTexts.after(AdminUnlock.Attempt.NoPassword))
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
