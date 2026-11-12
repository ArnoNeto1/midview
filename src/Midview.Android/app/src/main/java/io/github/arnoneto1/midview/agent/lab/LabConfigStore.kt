package io.github.arnoneto1.midview.agent.lab

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.LabConfig
import io.github.arnoneto1.midview.agent.sites.ChromePolicy
import java.io.File
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

// The phone's side of the teacher's "Painel do laboratório" (SET_LAB_CONFIG,
// GET_LAB_CONFIG, RESET_LAB_CONFIG), as the Windows LabConfigCommandHandler:
// kept in the app's own storage, the newest savedAt winning, applied again
// at every start (the agent writes Chrome's configuration with the kept
// download block, ChromeConfig; the Optimizer reads adblock). A phone takes
// two of its functions:
// - "Travar downloads": Chrome's DownloadRestrictions (downloads, through
//   ChromeConfig, so the site block stays in the same bundle). The teacher's
//   "Enviar arquivo" is written by the agent, not Chrome, and keeps coming.
// - "Bloqueador de anúncios": the ad-free DNS of "Otimizar" wanted or not;
//   the Optimizer reads it here (adblock) and is told of a change
//   (adblockChanged), turning the DNS on in the background or off at once.
// RESET puts the phone back to its default (LabConfig.DEFAULT): downloads
// free, the ad-free DNS on, as "Otimizar" leaves a phone.
class LabConfigStore(
    private val file: File,
    private val isDeviceOwner: () -> Boolean,
    private val downloads: (Boolean) -> Boolean,
    private val adblockChanged: () -> Unit,
    // Chrome's major version (null: no Chrome), for the teacher to read
    // that an old Chrome ignores the download block.
    private val chromeVersion: () -> Int? = { null },
    private val nowSeconds: () -> Long = { System.currentTimeMillis() / 1000 },
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    @Volatile private var config: LabConfig = load(file, log) ?: LabConfig.DEFAULT

    val current: LabConfig get() = config

    // Whether the ad-free DNS is wanted (the Optimizer's source of truth).
    val adblock: Boolean get() = config.adblock

    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val asked = LabConfig.decode(payload) ?: return false to INVALID_REQUEST
        synchronized(gate) {
            val held = config
            if (asked.savedAt < held.savedAt) {
                return true to "mantida a configuração mais nova, salva em ${savedAtText(held.savedAt)}: ${describe(held)}"
            }
            if (asked.savedAt == held.savedAt) {
                return if (asked.sameFunctionsAs(held)) true to describe(held) else false to SAME_SECOND
            }
            return change(asked)
        }
    }

    fun reset(): Pair<Boolean, String> = synchronized(gate) {
        // A reset always wins: it carries the current second, so a later
        // SET_LAB_CONFIG can still take over.
        change(LabConfig.DEFAULT.copy(savedAt = maxOf(config.savedAt, nowSeconds())))
    }

    private fun change(next: LabConfig): Pair<Boolean, String> {
        val held = config
        if (!isDeviceOwner()) return false to NOT_PREPARED
        if (!downloads(next.blockDownloads)) {
            downloads(held.blockDownloads)
            return false to CHROME_REFUSED
        }
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, next.encode())
        } catch (e: IOException) {
            log("Couldn't save the lab panel's configuration; keeping the previous one", e)
            downloads(held.blockDownloads)
            return false to NOT_SAVED
        }
        config = next
        if (next.adblock != held.adblock) {
            try {
                adblockChanged()
            } catch (e: RuntimeException) {
                log("The ad-free DNS could not follow the lab panel", e)
            }
        }
        log("Lab panel: ${describe(next)}.", null)
        // Said whenever the download block is asked for, not when only the
        // ad blocker changed beside it.
        val onlyAdblock = held.blockDownloads && next.adblock != held.adblock
        val version = chromeVersion()
        if (next.blockDownloads && !onlyAdblock && version != null && version < ChromePolicy.DOWNLOADS_FROM_VERSION) {
            return false to oldChrome(version)
        }
        return true to describe(next)
    }

    companion object {
        const val INVALID_REQUEST = "pedido inválido"
        const val SAME_SECOND = "outra configuração foi salva no mesmo segundo; aplique de novo"
        const val NOT_PREPARED = "Este celular não foi preparado pelo Midview."
        const val CHROME_REFUSED = "o Chrome do celular não aceitou a configuração"
        const val NOT_SAVED = "não foi possível gravar a configuração no celular"

        // The download block is kept and holds once Chrome is updated.
        fun oldChrome(version: Int): String =
            "guardado, mas o Chrome deste celular (versão $version) só trava downloads a partir da versão " +
                "${ChromePolicy.DOWNLOADS_FROM_VERSION}: atualize o Chrome"

        fun describe(config: LabConfig): String {
            val on = listOfNotNull(
                "travar downloads".takeIf { config.blockDownloads },
                "bloqueador de anúncios (DNS)".takeIf { config.adblock },
            )
            return if (on.isEmpty()) "nenhuma configuração aplicada" else on.joinToString(", ")
        }

        private fun savedAtText(savedAt: Long): String =
            SimpleDateFormat("dd/MM HH:mm", Locale.ROOT).format(Date(savedAt * 1000))

        // Unreadable means the default (the side the teacher can always fix
        // remotely). A file with bad content is kept aside.
        private fun load(file: File, log: (String, Throwable?) -> Unit): LabConfig? {
            if (!file.exists()) return null
            val bytes = try {
                file.readBytes()
            } catch (e: IOException) {
                log("Couldn't read the lab panel's configuration file", e)
                return null
            }
            return LabConfig.decode(bytes) ?: run {
                log("Couldn't read the lab panel's configuration; using the default", null)
                file.renameTo(File(file.path + ".corrupt"))
                null
            }
        }
    }
}
