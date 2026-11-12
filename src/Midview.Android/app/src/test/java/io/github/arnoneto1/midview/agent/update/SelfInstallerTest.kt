package io.github.arnoneto1.midview.agent.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// What the teacher reads when Android's installer answers (the values of
// PackageInstaller.STATUS_*).
class SelfInstallerTest {
    @Test fun successIsNoFailure() {
        assertNull(InstallStatus.failure(0, null))
        assertNull(InstallStatus.failure(0, "INSTALL_SUCCEEDED"))
    }

    // Nobody is there to answer a question on the school phone.
    @Test fun aQuestionForTheUserIsAFailure() {
        assertEquals("o Android pediu uma confirmação na tela; nada foi instalado", InstallStatus.failure(-1, null))
    }

    @Test fun eachFailureSaysWhatHappened() {
        val rows = mapOf(
            1 to "o Android não instalou",
            2 to "o Android bloqueou a instalação",
            3 to "a instalação foi cancelada",
            4 to "o Android recusou o arquivo",
            5 to "o Android recusou: conflito com o aplicativo instalado",
            6 to "sem espaço no celular",
            7 to "o aplicativo não é compatível com este celular",
            // STATUS_FAILURE_TIMEOUT (Android 14) and anything newer.
            8 to "o Android não instalou",
        )
        for ((status, detail) in rows) {
            assertEquals("$status", detail, InstallStatus.failure(status, null))
        }
    }

    // Android's own code, for whoever looks into it; its English text is
    // left out.
    @Test fun androidsCodeIsKept() {
        assertEquals(
            "o Android bloqueou a instalação (INSTALL_FAILED_USER_RESTRICTED)",
            InstallStatus.failure(2, "INSTALL_FAILED_USER_RESTRICTED: Install canceled by user"),
        )
        assertEquals(
            "o Android recusou o arquivo (INSTALL_PARSE_FAILED_NO_CERTIFICATES)",
            InstallStatus.failure(4, "INSTALL_PARSE_FAILED_NO_CERTIFICATES: Failed collecting certificates"),
        )
        assertEquals("o Android não instalou", InstallStatus.failure(1, "Session was abandoned"))
        assertEquals("o Android não instalou", InstallStatus.failure(1, ""))
    }

    // Only sessions nobody is using: a committed one Android is installing
    // stays active and is left alone.
    @Test fun onlyInactiveSessionsAreStale() {
        assertEquals(listOf(2, 3), InstallSessions.stale(listOf(1 to true, 2 to false, 3 to false, 4 to true)))
        assertEquals(emptyList<Int>(), InstallSessions.stale(emptyList()))
    }
}
