package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.UUID

class DiscoveryTest {
    private val id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301"

    private fun parse(json: String) = Discovery.parseProbe(json.toByteArray(Charsets.UTF_8))

    @Test fun portAndVersionMatchDotNet() {
        assertEquals(28901, Discovery.PORT)
        assertEquals(1, Discovery.VERSION)
        assertEquals(2048, Discovery.MAX_PACKET_SIZE)
    }

    @Test fun probeFromDotNetIsRead() {
        val probe = parse("""{"midview":1,"probe":"$id","master":"ABC"}""")
        assertEquals(Discovery.Probe(UUID.fromString(id), "ABC"), probe)
    }

    @Test fun probeWithAgentFieldIsIgnored() {
        assertNull(parse("""{"midview":1,"probe":"$id","master":"ABC","agent":"DEF"}"""))
    }

    @Test fun probeWithAnAgentOfNullIsStillAProbe() {
        assertEquals("ABC", parse("""{"midview":1,"probe":"$id","master":"ABC","agent":null}""")?.masterThumbprint)
    }

    @Test fun probeWithAnUppercaseGuidIsRead() {
        assertEquals(UUID.fromString(id), parse("""{"midview":1,"probe":"${id.uppercase()}","master":"ABC"}""")?.id)
    }

    @Test fun probesDotNetRefusesAreRefused() {
        assertNull(parse("""{"midview":2,"probe":"$id","master":"ABC"}"""))
        assertNull(parse("""{"midview":"1","probe":"$id","master":"ABC"}"""))
        assertNull(parse("""{"probe":"$id","master":"ABC"}"""))
        assertNull(parse("""{"midview":1,"probe":"$id","master":""}"""))
        assertNull(parse("""{"midview":1,"probe":"$id"}"""))
        assertNull(parse("""{"midview":1,"probe":"$id","master":7}"""))
        assertNull(parse("""{"midview":1,"probe":"00000000-0000-0000-0000-000000000000","master":"ABC"}"""))
        assertNull(parse("""{"midview":1,"probe":"not-a-guid","master":"ABC"}"""))
        assertNull(parse("""{"midview":1,"probe":"1-2-3-4-5","master":"ABC"}"""))
        assertNull(parse("""{"midview":1,"master":"ABC"}"""))
        assertNull(parse("isto não é json"))
        assertNull(parse("[1,2]"))
        assertNull(Discovery.parseProbe(ByteArray(0)))
    }

    @Test fun anOversizedProbeIsRefused() {
        val padding = "x".repeat(2048)
        assertNull(parse("""{"midview":1,"probe":"$id","master":"ABC","pad":"$padding"}"""))
    }

    @Test fun replyHasAndroidKindAndDotNetFields() {
        val bytes = Discovery.encodeReply(
            Discovery.Reply(UUID.fromString(id), "Cel 01", "ABCDEF", TeacherStatus.NONE, newTeacherOpen = true),
        )
        val text = String(bytes, Charsets.UTF_8)
        assertEquals(
            """{"midview":1,"probe":"$id","name":"Cel 01","agent":"ABCDEF","teachers":"none","newTeacherOpen":true,"kind":"android"}""",
            text,
        )
        assertEquals(
            setOf("midview", "probe", "name", "agent", "teachers", "newTeacherOpen", "kind"),
            JSONObject(text).keys().asSequence().toSet(),
        )
    }

    @Test fun replyEscapesTheName() {
        val bytes = Discovery.encodeReply(
            Discovery.Reply(UUID.fromString(id), "Sala \"2\" é", "AB", TeacherStatus.OTHERS, newTeacherOpen = false),
        )
        val json = JSONObject(String(bytes, Charsets.UTF_8))
        assertEquals("Sala \"2\" é", json.getString("name"))
        assertEquals("others", json.getString("teachers"))
        assertFalse(json.getBoolean("newTeacherOpen"))
    }

    @Test fun teacherStatusMatchesDotNet() {
        assertEquals("none", TeacherStatus.NONE)
        assertEquals("you", TeacherStatus.YOU)
        assertEquals("others", TeacherStatus.OTHERS)
    }
}
