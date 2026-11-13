package io.github.arnoneto1.midview.agent.leave

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// The leaving's record: written twice, read from the copy when one is
// damaged, and with both damaged the phone is not leaving (it never takes
// the Midview off on a spoiled file, and is locked again instead).
class LeaveStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "leaving.txt") }
    private val copy by lazy { File(folder.root, "leaving.txt" + LeaveStore.COPY_SUFFIX) }

    @Test fun noRecordIsNotLeaving() = assertNull(LeaveStore(file).phase)

    @Test fun eachPhaseIsKeptInBothFiles() {
        LeaveStore(file).save(LeaveStore.Phase.UNDONE)

        assertTrue(file.exists() && copy.exists())
        assertEquals(LeaveStore.Phase.UNDONE, LeaveStore(file).phase)
    }

    @Test fun aDamagedFileIsReadFromItsCopy() {
        LeaveStore(file).save(LeaveStore.Phase.OWNER_CLEARED)
        file.writeText("garbage")

        assertEquals(LeaveStore.Phase.OWNER_CLEARED, LeaveStore(file).phase)
    }

    @Test fun aDamagedCopyIsReadFromTheFile() {
        LeaveStore(file).save(LeaveStore.Phase.STARTED)
        copy.writeText("garbage")

        assertEquals(LeaveStore.Phase.STARTED, LeaveStore(file).phase)
    }

    @Test fun bothDamagedIsNotLeaving() {
        LeaveStore(file).save(LeaveStore.Phase.UNDONE)
        file.writeText("garbage")
        copy.writeBytes(ByteArray(0))

        assertNull(LeaveStore(file).phase)
    }

    // Nothing to finish on a spoiled record: no undo, no uninstall.
    @Test fun aSpoiledRecordNeverTakesTheMidviewOff() {
        file.writeText("garbage")
        val gw = FakeLeaveGateway()

        assertTrue(!PhoneLeave(gw, LeaveStore(file)).resume())
        assertTrue(gw.calls.isEmpty())
    }
}
