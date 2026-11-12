package io.github.arnoneto1.midview.agent.provisioning

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class CableSetupTest {
    @get:Rule val folder = TemporaryFolder()

    // Produced by the Master (CableSetupPayload.Encode) for the tricky text:
    // quotes, ";", an accent, an apostrophe, "@" and spaces.
    private val vector = "eyJudW1iZXIiOiIwNyIsIm1hc3RlciI6IkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTIiLCJzc2lkIjoiTGFiIFwiMVwiOyDDqSIsInBhc3N3b3JkIjoicyduaEAgMTsifQ"

    private fun encode(json: String) =
        java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(json.toByteArray())

    private fun setupJson(number: String = "07", master: String = "AB".repeat(20), ssid: String? = "x") =
        buildString {
            append("{\"number\":\"").append(number).append("\",\"master\":\"").append(master).append("\"")
            if (ssid != null) append(",\"ssid\":\"").append(ssid).append("\"")
            append(",\"password\":\"y\"}")
        }

    @Test fun decodesTheMastersFixedVector() {
        val data = CableSetupPayload.decode(vector)!!
        assertEquals("07", data.number)
        assertEquals("AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12", data.master)
        assertEquals("Lab \"1\"; é", data.ssid)
        assertEquals("s'nh@ 1;", data.password)
    }

    @Test fun anOpenNetworkHasNoPassword() {
        val json = "{\"number\":\"7\",\"master\":\"" + "AB".repeat(20) + "\",\"ssid\":\"Aberta\",\"password\":\"\"}"
        assertNull(CableSetupPayload.decode(encode(json))!!.password)
    }

    @Test fun refusesWhatIsNotASetup() {
        assertNull(CableSetupPayload.decode(null))
        assertNull(CableSetupPayload.decode(""))
        assertNull(CableSetupPayload.decode("!!not base64!!"))
        assertNull(CableSetupPayload.decode(encode("not json")))
        assertNull(CableSetupPayload.decode(encode("[1]")))
        assertNull(CableSetupPayload.decode(encode(setupJson(number = "1234"))))
        assertNull(CableSetupPayload.decode(encode(setupJson(number = ""))))
        assertNull(CableSetupPayload.decode(encode(setupJson(number = "ab"))))
        assertNull(CableSetupPayload.decode(encode(setupJson(master = "AB".repeat(19)))))
        assertNull(CableSetupPayload.decode(encode(setupJson(master = "AB".repeat(33)))))
        assertNull(CableSetupPayload.decode(encode(setupJson(master = "ZZ".repeat(20)))))
        assertNull(CableSetupPayload.decode(encode(setupJson(ssid = ""))))
        assertNull(CableSetupPayload.decode(encode(setupJson(ssid = null))))
    }

    @Test fun takeWifiGivesThePasswordOnce() {
        val file = File(folder.root, "cable-setup.json")
        val setup = CableSetup(file)
        setup.save(CableSetupData("07", "AB".repeat(20), "Lab \"1\"", "s'nh@ 1;"))
        assertEquals("07", setup.number())
        assertEquals("Lab \"1\"" to "s'nh@ 1;", setup.takeWifi())
        assertEquals("Lab \"1\"" to null, setup.takeWifi())
        // The number stays: it is the phone's name.
        assertEquals("07", CableSetup(file).number())
        assertFalse(file.readText().contains("nh@"))
    }

    @Test fun withoutAFileThereIsNothing() {
        val setup = CableSetup(File(folder.root, "none.json"))
        assertNull(setup.number())
        assertNull(setup.takeWifi())
        assertFalse(setup.markReadyShown())
    }

    @Test fun readyIsShownOnce() {
        val file = File(folder.root, "cable-setup.json")
        val setup = CableSetup(file)
        setup.save(CableSetupData("07", "AB".repeat(20), "x", null))
        assertTrue(setup.markReadyShown())
        assertFalse(setup.markReadyShown())
        assertFalse(CableSetup(file).markReadyShown())
        assertEquals("07", setup.number())
    }

    @Test fun savingAgainStartsOver() {
        val setup = CableSetup(File(folder.root, "cable-setup.json"))
        setup.save(CableSetupData("07", "AB".repeat(20), "x", null))
        setup.markReadyShown()
        setup.save(CableSetupData("08", "AB".repeat(20), "x", "p"))
        assertEquals("08", setup.number())
        assertTrue(setup.markReadyShown())
    }
}
