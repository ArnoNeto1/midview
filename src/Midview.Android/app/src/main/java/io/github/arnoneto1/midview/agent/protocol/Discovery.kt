package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONException
import org.json.JSONObject
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.util.UUID

// Being found by the teacher, as src/Midview.Protocol/Discovery.cs: the
// Master broadcasts a probe to UDP PORT and the device answers the sender
// with one reply, about the probe's size. Nothing here is trusted: a reply
// only tells the teacher what is there; identity comes from pairing.
object Discovery {
    const val PORT = 28901
    const val MAX_PACKET_SIZE = 2048
    const val VERSION = 1

    // What .NET writes for DeviceKind.Android. A Windows Agent sends no kind.
    const val ANDROID_KIND = "android"

    private val NO_PROBE = UUID(0, 0)

    // The .NET Guid format ("D"): 8-4-4-4-12 hex digits, any case.
    private val GUID = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    data class Probe(val id: UUID, val masterThumbprint: String)

    data class Reply(
        val probeId: UUID,
        val name: String,
        val agentThumbprint: String,
        val teachers: String,
        val newTeacherOpen: Boolean,
    )

    // A probe as the Master sends it, or null for anything else: a wrong
    // version, no or an empty master, no or an empty probe id, and a reply
    // (it carries "agent", which a probe never does).
    fun parseProbe(packet: ByteArray): Probe? {
        if (packet.isEmpty() || packet.size > MAX_PACKET_SIZE) {
            return null
        }

        val json = try {
            JSONObject(decodeUtf8(packet))
        } catch (e: JSONException) {
            return null
        } catch (e: CharacterCodingException) {
            return null
        }

        val version = json.opt("midview")
        val master = json.opt("master")
        val probe = json.opt("probe")
        if (version !is Int || version != VERSION || master !is String || master.isEmpty() || !json.isNull("agent")) {
            return null
        }
        if (probe !is String || !GUID.matches(probe)) {
            return null
        }

        val id = UUID.fromString(probe)
        return if (id == NO_PROBE) null else Probe(id, master)
    }

    // The reply with the fields and order .NET writes, always with
    // "kind":"android" so the teacher shows a phone.
    fun encodeReply(r: Reply): ByteArray {
        require(r.name.isNotEmpty()) { "A reply needs the device's name" }
        require(r.agentThumbprint.isNotEmpty()) { "A reply needs the agent's thumbprint" }
        require(TeacherStatus.isKnown(r.teachers)) { "Unknown teacher status ${r.teachers}" }

        val text = JsonText.obj(
            "midview" to VERSION.toString(),
            "probe" to JsonText.string(r.probeId.toString()),
            "name" to JsonText.string(r.name),
            "agent" to JsonText.string(r.agentThumbprint),
            "teachers" to JsonText.string(r.teachers),
            "newTeacherOpen" to r.newTeacherOpen.toString(),
            "kind" to JsonText.string(ANDROID_KIND),
        )
        val bytes = text.toByteArray(Charsets.UTF_8)
        // The Master drops a bigger reply.
        require(bytes.size <= MAX_PACKET_SIZE) { "Reply of ${bytes.size} bytes is over $MAX_PACKET_SIZE" }
        return bytes
    }

    // Invalid UTF-8 is refused, as .NET's JSON reader does.
    private fun decodeUtf8(bytes: ByteArray): String =
        Charsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
}

// Reply.teachers: whom the device trusts, as the asking teacher sees it.
object TeacherStatus {
    const val NONE = "none"
    const val YOU = "you"
    const val OTHERS = "others"

    fun isKnown(value: String) = value == NONE || value == YOU || value == OTHERS
}
