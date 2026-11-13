package io.github.arnoneto1.midview.agent.ui

import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.ZoneId

class LeaveTextsTest {
    @Test fun aWrongPasswordSaysHowManyTriesAreLeft() =
        assertEquals("Senha errada. Restam 3 tentativas.", LeaveTexts.after(AdminUnlock.Attempt.Wrong(3)))

    @Test fun theRightPasswordGoesOnToTheChoices() = assertNull(LeaveTexts.after(AdminUnlock.Attempt.Verified))

    @Test fun withoutAPasswordTheTeacherMustSetOne() {
        assertEquals(LeaveTexts.NEEDS_PASSWORD, LeaveTexts.after(AdminUnlock.Attempt.NoPassword))
        assertTrue(LeaveTexts.NEEDS_PASSWORD.contains("Liberar Wi-Fi e modo avião"))
    }

    @Test fun tooManyWrongOnesSayWhenToTryAgain() {
        val at = java.time.ZonedDateTime.now(ZoneId.systemDefault()).withHour(10).withMinute(25).toInstant().toEpochMilli()
        assertEquals("Muitas senhas erradas. Tente de novo às 10:25.", LeaveTexts.after(AdminUnlock.Attempt.LockedOut(at)))
    }

    // The two choices say what stays and what goes.
    @Test fun theChoicesSayWhatHappensToTheStudentsFiles() {
        assertTrue(LeaveTexts.KEEP_TEXT.contains("Mantém as fotos e os arquivos do aluno"))
        assertTrue(LeaveTexts.WIPE_TEXT.startsWith("Apaga tudo"))
        assertTrue(LeaveTexts.CONFIRM_WIPE.contains("não dá para desfazer"))
    }
}
