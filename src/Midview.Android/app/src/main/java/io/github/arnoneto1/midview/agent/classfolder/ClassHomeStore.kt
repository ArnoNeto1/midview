package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.ClassHome
import io.github.arnoneto1.midview.agent.protocol.ClassHomeClaim
import java.io.File
import java.io.IOException

// Which teacher PC keeps the class folders, as the PC Agent's ClassHomeState
// (src/Midview.Agent/ClassHomeState.cs): the one that claimed it first, or,
// for a Master that sends no claim, started the first lesson. Only the home
// may start a lesson or change the class; any trusted teacher may end one.
// The identity is the thumbprint (the TLS channel's) and the computer name
// (the claim's); the generation decides a take-over; the address is the
// connection's, refreshed by every claim of the home's.
//
// Kept in the app's storage as ClassHomeStatus's payload (empty: none),
// replaced whole; an unreadable file means no home and is set aside.
class ClassHomeStore(private val file: File, private val log: (String, Throwable?) -> Unit = { _, _ -> }) {
    private val gate = Any()

    @Volatile var current: ClassHome? = load()
        private set

    // senderThumbprint: the channel's; senderAddress: the connection's;
    // lessonOn: a take-over waits for the lesson's end.
    fun claim(senderThumbprint: String, senderAddress: String, claim: ClassHomeClaim, lessonOn: Boolean): Pair<Boolean, String> {
        if (!isSender(senderThumbprint, senderAddress) || claim.name.isEmpty() || claim.generation <= 0) return false to INVALID
        if (claim.generation > ClassHome.MAX_GENERATION) return false to GENERATION_TOO_LARGE_TEXT
        synchronized(gate) {
            val home = current
            val (claimed, detail) = when {
                home == null ->
                    ClassHome(senderThumbprint.uppercase(), claim.name, senderAddress, claim.port, claim.generation) to HOME_IS_HERE_TEXT
                home.isSamePc(senderThumbprint, claim.name, claim.generation) -> {
                    val name = if (home.name.equals(claim.name, ignoreCase = true)) home.name else claim.name
                    ClassHome(senderThumbprint.uppercase(), name, senderAddress, claim.port, maxOf(home.generation, claim.generation)) to
                        HOME_IS_HERE_TEXT
                }
                !claim.takeOver || claim.generation <= home.generation -> return false to home.refusalText
                lessonOn -> return false to END_LESSON_FIRST_TEXT
                else -> ClassHome(senderThumbprint.uppercase(), claim.name, senderAddress, claim.port, claim.generation) to MOVED_HERE_TEXT
            }
            if (claimed == home) return true to detail
            return try {
                save(claimed)
                true to detail
            } catch (e: IOException) {
                log("Couldn't save the class folders' home", e)
                false to "não foi possível gravar o PC das pastas no celular"
            }
        }
    }

    // May this teacher start a lesson or change the class? With no home yes,
    // and it becomes the home with no name; with one, only from its
    // certificate. The refusal (naming the home) when not; null when it may.
    fun refusalToStart(senderThumbprint: String, senderAddress: String): String? = synchronized(gate) {
        val home = current
        if (home == null) {
            if (isSender(senderThumbprint, senderAddress)) {
                val starter = ClassHome(senderThumbprint.uppercase(), "", senderAddress, REMOTE_PORT, 0)
                try {
                    save(starter)
                } catch (e: IOException) {
                    log("Couldn't save the class folders' home", e)
                    current = starter
                }
            }
            return null
        }
        if (home.thumbprint.equals(senderThumbprint, ignoreCase = true)) null else home.refusalText
    }

    private fun save(home: ClassHome) {
        file.absoluteFile.parentFile?.mkdirs()
        writeAtomically(file, home.encode())
        current = home
        log("Class folders' home: ${home.name.ifEmpty { "(no name)" }} at ${home.address}.", null)
    }

    private fun load(): ClassHome? {
        if (!file.exists()) return null
        val bytes = try {
            file.readBytes()
        } catch (e: IOException) {
            log("Couldn't read the class folders' home, following none", e)
            return null
        }
        val (ok, home) = ClassHome.decode(bytes)
        if (ok) return home?.let { it.copy(generation = minOf(it.generation, ClassHome.MAX_GENERATION)) }
        log("The class folders' home file is corrupt, following none; kept aside as .corrupt", null)
        file.renameTo(File(file.path + ".corrupt"))
        return null
    }

    companion object {
        const val HOME_IS_HERE_TEXT = "este PC guarda as pastas"
        const val MOVED_HERE_TEXT = "as pastas passaram para este PC"
        const val END_LESSON_FIRST_TEXT = "Encerre a aula antes de passar as pastas."
        const val GENERATION_TOO_LARGE_TEXT = "pedido inválido: data da troca além do limite"
        const val INVALID = "pedido inválido"

        // ClassFolderRemote.Port: where a home's Master listens for its colleague.
        const val REMOTE_PORT = 28903

        private fun isSender(thumbprint: String, address: String) =
            ClassHome.isThumbprint(thumbprint) && address.length in 1..ClassHome.MAX_ADDRESS_LENGTH && ClassHome.isIpLiteral(address)
    }
}
