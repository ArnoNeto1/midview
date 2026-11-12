package io.github.arnoneto1.midview.agent.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Duration
import java.time.Instant

class ApkUpdateStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "update-attempt.txt") }
    private val at = Instant.parse("2026-11-12T15:30:45.25Z")
    private val id = "000102030405060708090A0B0C0D0E0F"
    private val receiving = ApkAttempt(id, UpdateStates.RECEIVING, "recebendo", at)
    private val applying = ApkAttempt(id, UpdateStates.APPLYING, "instalando a versão 0.0.51", at, expectedVersionCode = 51)

    @Test fun noAttemptYet() {
        assertNull(ApkUpdateStore(file).current)
        assertFalse(file.exists())
    }

    @Test fun whatIsSavedIsThereAfterARestart() {
        assertTrue(ApkUpdateStore(file).save(applying))
        assertEquals(applying, ApkUpdateStore(file).current)
        ApkUpdateStore(file).save(receiving)
        assertEquals(receiving, ApkUpdateStore(file).current)
    }

    // The install's answer comes to a store of its own (InstallResult): the
    // agent's store must see it at once.
    @Test fun twoStoresOfTheSameFileSeeEachOthersWrites() {
        val agent = ApkUpdateStore(file)
        val receiver = ApkUpdateStore(file)
        agent.save(applying)
        receiver.save(applying.copy(state = UpdateStates.FAILED, detail = "falhou"))
        assertEquals(UpdateStates.FAILED, agent.current!!.state)
    }

    @Test fun updateChangesWhatIsOnFileOrLeavesIt() {
        val store = ApkUpdateStore(file)
        store.save(applying)
        assertEquals(applying, store.update { null })
        val failed = store.update { it!!.copy(state = UpdateStates.FAILED) }
        assertEquals(UpdateStates.FAILED, failed!!.state)
        assertEquals(failed, ApkUpdateStore(file).current)
    }

    @Test fun savingLeavesNoTemporaryFileBehind() {
        val store = ApkUpdateStore(file)
        store.save(receiving)
        store.save(applying)
        assertEquals(listOf("update-attempt.txt"), folder.root.list()!!.toList())
    }

    @Test fun aDamagedFileIsSetAside() {
        file.writeText("{\"id\":\"00\",\"state\":\"nonsense\"}")
        assertNull(ApkUpdateStore(file).current)
        assertTrue(File(file.path + ".corrupt").exists())
        assertFalse(file.exists())
    }

    @Test fun nonsenseIsNotAnAttempt() {
        assertNull(ApkUpdateStore.decode("nonsense".toByteArray()))
        assertNull(ApkUpdateStore.decode(String(ApkUpdateStore.encode(receiving)).replace("receiving", "provisional").toByteArray()))
        assertNull(ApkUpdateStore.decode(String(ApkUpdateStore.encode(receiving)).replace("2026-11-12T15:30:45.25+00:00", "ontem").toByteArray()))
        assertEquals(receiving, ApkUpdateStore.decode(ApkUpdateStore.encode(receiving)))
        assertEquals(applying, ApkUpdateStore.decode(ApkUpdateStore.encode(applying)))
    }

    @Test fun aSaveTheDiskRefusesSaysSo() {
        val blocked = folder.newFolder("attempt-dir")
        File(blocked, "inside").writeText("x")
        assertFalse(ApkUpdateStore(blocked).save(receiving))
    }

    // When the agent starts: what an attempt left becomes.
    private fun atStart(attempt: ApkAttempt?, installing: Boolean = false) =
        ApkUpdateStore.atStart(attempt, runningVersionCode = 50, versionName = "0.0.50", installing = installing, now = at.plusSeconds(9))

    @Test fun atStartAnUploadOrCheckCutShortFailed() {
        for (state in listOf(UpdateStates.RECEIVING, UpdateStates.VERIFYING)) {
            assertEquals(
                ApkAttempt(id, UpdateStates.FAILED, "interrompida (o Midview reiniciou)", at.plusSeconds(9)),
                atStart(receiving.copy(state = state)),
            )
        }
    }

    @Test fun atStartTheVersionThatWasInstalledCommitsIt() {
        assertEquals(
            ApkAttempt(id, UpdateStates.COMMITTED, "atualizado para 0.0.50", at.plusSeconds(9), expectedVersionCode = 50),
            atStart(applying.copy(expectedVersionCode = 50)),
        )
    }

    // The install failed, or the phone restarted while Android installed.
    @Test fun atStartTheOldVersionMeansTheInstallDidNotHappen() {
        assertEquals(
            applying.copy(
                state = UpdateStates.FAILED,
                detail = "a instalação não terminou; o celular continua na versão 0.0.50",
                updatedAt = at.plusSeconds(9),
            ),
            atStart(applying),
        )
    }

    // The old agent started again while Android still installs the new one.
    @Test fun atStartAnInstallStillGoingOnIsLeftToFinish() {
        assertNull(atStart(applying, installing = true))
    }

    @Test fun atStartFinalAttemptsAreLeftAsTheyAre() {
        assertNull(atStart(null))
        assertNull(atStart(applying.copy(state = UpdateStates.COMMITTED)))
        assertNull(atStart(applying.copy(state = UpdateStates.FAILED, expectedVersionCode = 50)))
    }

    // Android's answer to an install (InstallResult): only the attempt being
    // installed can fail by it.
    @Test fun androidsRefusalFailsOnlyTheAttemptBeingInstalled() {
        val store = ApkUpdateStore(file)
        store.save(applying)
        assertTrue(store.installFailed(id, "o Android bloqueou a instalação", at.plusSeconds(5)))
        assertEquals(
            applying.copy(state = UpdateStates.FAILED, detail = "o Android bloqueou a instalação", updatedAt = at.plusSeconds(5)),
            store.current,
        )

        store.save(applying)
        assertFalse(store.installFailed("FF".repeat(16), "x", at))
        store.save(applying.copy(state = UpdateStates.COMMITTED))
        assertFalse(store.installFailed(id, "x", at))
        assertEquals(UpdateStates.COMMITTED, store.current!!.state)
    }

    // An install Android never answered for.
    @Test fun anInstallWithNoAnswerFailsAfterThreeMinutes() {
        val limit = at.plus(ApkUpdateStore.APPLY_TIMEOUT)
        assertEquals(Duration.ofMinutes(3), ApkUpdateStore.APPLY_TIMEOUT)
        assertNull(ApkUpdateStore.expired(applying, limit.minusSeconds(1), "0.0.50"))
        assertEquals(
            applying.copy(state = UpdateStates.FAILED, detail = "a instalação não terminou; o celular continua na versão 0.0.50", updatedAt = limit),
            ApkUpdateStore.expired(applying, limit, "0.0.50"),
        )
        // A clock set back must not hold the next update off either.
        assertEquals(UpdateStates.FAILED, ApkUpdateStore.expired(applying, at.minus(Duration.ofHours(2)), "0.0.50")!!.state)
        assertNull(ApkUpdateStore.expired(receiving, limit.plusSeconds(3600), "0.0.50"))
        assertNull(ApkUpdateStore.expired(null, limit, "0.0.50"))
    }
}
