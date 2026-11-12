package io.github.arnoneto1.midview.agent.provisioning

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class CableSetupDoorTest {
    @get:Rule val folder = TemporaryFolder()

    private val vector = "eyJudW1iZXIiOiIwNyIsIm1hc3RlciI6IkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTIiLCJzc2lkIjoiTGFiIFwiMVwiOyDDqSIsInBhc3N3b3JkIjoicyduaEAgMTsifQ"
    private val master = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12"

    private lateinit var setup: CableSetup
    private lateinit var trusted: TrustedMasters
    private var owner = false
    private var numberWhenIdentityWasMade: String? = null

    private fun door(): CableSetupDoor {
        setup = CableSetup(File(folder.root, "cable-setup.json"))
        trusted = TrustedMasters(File(folder.root, "trusted.txt"))
        return CableSetupDoor(setup, trusted, { owner }, {
            // Made after the number is saved: the certificate is born with the name.
            numberWhenIdentityWasMade = setup.number()
            "THUMB1234" to "Celular $numberWhenIdentityWasMade"
        })
    }

    @Test fun theShellWithAGoodSetupPreparesThePhone() {
        val answer = door().prepare(2000, vector)
        assertEquals(true, answer["ok"])
        assertEquals("THUMB1234", answer["thumbprint"])
        assertEquals("Celular 07", answer["name"])
        assertTrue(trusted.isTrusted(master))
        assertEquals("07", setup.number())
        assertEquals("07", numberWhenIdentityWasMade)
    }

    @Test fun anyOtherCallerIsRefusedAndNothingIsSaved() {
        for (uid in listOf(10123, 0, 1000, 2001, 1999, -1)) {
            val answer = door().prepare(uid, vector)
            assertEquals("not-shell", answer["error"])
            assertFalse(answer.containsKey("ok"))
            assertNull(setup.number())
            assertEquals(0, trusted.count())
        }
    }

    @Test fun theDeviceOwnerIsLeftAlone() {
        owner = true
        val answer = door().prepare(2000, vector)
        assertEquals("already-owner", answer["error"])
        assertNull(setup.number())
        assertEquals(0, trusted.count())
    }

    @Test fun aPhoneThatAlreadyTrustsSomeoneIsRefused() {
        val d = door()
        trusted.add("FF".repeat(20))
        val answer = d.prepare(2000, vector)
        assertEquals("already-trusted", answer["error"])
        assertNull(setup.number())
        assertEquals(1, trusted.count())
    }

    @Test fun garbageIsABadSetupAndNothingIsSaved() {
        for (payload in listOf(null, "", "garbage", "e30")) {
            val answer = door().prepare(2000, payload)
            assertEquals("bad-setup", answer["error"])
            assertNull(setup.number())
            assertEquals(0, trusted.count())
        }
    }

    @Test fun theUidIsCheckedBeforeThePayload() {
        assertEquals("not-shell", door().prepare(10123, null)["error"])
    }
}
