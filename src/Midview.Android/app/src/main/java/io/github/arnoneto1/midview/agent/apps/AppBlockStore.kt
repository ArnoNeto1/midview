package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import java.io.File
import java.io.IOException
import java.nio.file.AccessDeniedException
import java.nio.file.NoSuchFileException
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

// The app-blocking policy on this phone (SetPhoneAppBlock and
// GetPhoneAppBlock), as the Windows AppBlockCommandHandler: kept in the app's
// own storage until the teacher lifts it, handed to the enforcer
// (AppSuspender). The newest SavedAt wins, so two teachers' Masters don't keep
// overwriting each other's. apply returns a warning for the teacher (the block
// does not hold as asked on this phone), said when a block is applied.
class AppBlockStore(
    private val file: File,
    private val apply: (PhoneAppPolicy) -> String?,
    private val zone: ZoneId = ZoneId.systemDefault(),
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    @Volatile private var policy: PhoneAppPolicy = load(file, log) ?: PhoneAppPolicy.lifted(0)

    val current: PhoneAppPolicy get() = policy

    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val asked = PhoneAppPolicy.decode(payload) ?: return false to INVALID
        synchronized(gate) {
            val held = policy
            if (asked.savedAt < held.savedAt) {
                return true to "mantido o bloqueio mais novo, salvo em ${savedAtText(held.savedAt)}: ${describe(held)}"
            }
            if (asked.savedAt == held.savedAt) {
                return if (asked.sameRulesAs(held)) {
                    true to describe(held)
                } else {
                    false to "outro bloqueio foi salvo no mesmo segundo; aplique de novo"
                }
            }

            try {
                file.absoluteFile.parentFile?.mkdirs()
                writeAtomically(file, asked.encode())
            } catch (e: IOException) {
                log("Couldn't save the app block", e)
                return false to "não foi possível gravar o bloqueio: ${saveError(e)}"
            }

            policy = asked
            val warning = apply(asked)
            return true to if (asked.isActive && warning != null) "${describe(asked)} (atenção: $warning)" else describe(asked)
        }
    }

    // The held policy applied again (an app installed, updated or removed),
    // under the same lock as a new policy, so an older one is never applied
    // after a newer one.
    fun reapply() {
        synchronized(gate) { apply(policy) }
    }

    private fun savedAtText(savedAt: Long): String =
        DateTimeFormatter.ofPattern("dd/MM HH:mm").withZone(zone).format(Instant.ofEpochSecond(savedAt))

    companion object {
        const val INVALID = "pedido inválido"

        // What the teacher reads when the policy could not be saved, as the
        // Windows ErrorText: Android's own messages are in English.
        val SAVE_ERRORS = setOf(
            "sem espaço no celular",
            "acesso negado",
            "armazenamento só de leitura",
            "pasta não encontrada",
            "erro ao gravar no celular",
        )

        fun saveError(e: IOException): String {
            val text = e.message.orEmpty()
            fun has(vararg words: String) = words.any { text.contains(it, ignoreCase = true) }
            return when {
                has("ENOSPC", "No space left") -> "sem espaço no celular"
                e is AccessDeniedException || has("EACCES", "EPERM", "Permission denied") -> "acesso negado"
                has("EROFS", "Read-only") -> "armazenamento só de leitura"
                e is NoSuchFileException ||
                    has("ENOENT", "ENOTDIR", "No such file", "Not a directory", "cannot find the path") -> "pasta não encontrada"
                else -> "erro ao gravar no celular"
            }
        }

        fun describe(policy: PhoneAppPolicy): String = when (policy.mode) {
            AppBlockMode.BLOCK_LIST -> "bloqueando ${policy.packages.size} app(s)"
            AppBlockMode.ALLOW_ONLY -> if (policy.packages.isEmpty()) {
                "permitindo só o essencial"
            } else {
                "permitindo só o essencial e ${policy.packages.size} app(s)"
            }
            AppBlockMode.NONE -> "nenhum app bloqueado"
        }

        // Unreadable means nothing blocked: the side the teacher can always fix
        // remotely. A file with bad content is kept aside, so it isn't read (and
        // fails) at every start; one that couldn't be read is left for the next.
        private fun load(file: File, log: (String, Throwable?) -> Unit): PhoneAppPolicy? {
            if (!file.exists()) return null
            val bytes = try {
                file.readBytes()
            } catch (e: IOException) {
                log("Couldn't read the app block file, blocking nothing", e)
                return null
            }
            return PhoneAppPolicy.decode(bytes) ?: run {
                log("Couldn't read the app block, blocking nothing", null)
                file.renameTo(File(file.path + ".corrupt"))
                null
            }
        }
    }
}
