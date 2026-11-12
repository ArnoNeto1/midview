package io.github.arnoneto1.midview.agent.programs

import io.github.arnoneto1.midview.agent.net.Channel
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.protocol.ProgramInstall
import io.github.arnoneto1.midview.agent.update.ApkFacts
import io.github.arnoneto1.midview.agent.update.ApkUpload
import io.github.arnoneto1.midview.agent.update.UploadResult
import java.io.File
import java.io.IOException
import java.util.concurrent.atomic.AtomicBoolean

// An app on the phone as the teacher's install and uninstall need it
// (AppOrigin): system, it came with the phone; byMidview, Android records the
// Midview as its installer.
data class PhoneAppFacts(val facts: ApkFacts, val label: String, val system: Boolean, val byMidview: Boolean = false)

// What installing and uninstalling need of Android (AndroidPackages), behind
// interfaces so TeacherInstalls is tested on the JVM.
interface PackageFacts {
    // Null when Android cannot read the file as an app (signed) at all.
    fun read(apk: File): ApkFacts?

    // Null when the app is not on the phone for the student.
    fun installed(pkg: String): PhoneAppFacts?

    fun isSuspended(pkg: String): Boolean

    // Free space where the APK is received.
    fun freeBytes(): Long
}

interface PackageChanger {
    // Installs the APK silently and waits for Android: null once installed,
    // otherwise why not, for the teacher.
    fun install(apk: File, pkg: String): String?

    // The same for removing an app.
    fun uninstall(pkg: String): String?
}

// The teacher's "Instalar programa" and "Desinstalar programa" on this phone,
// as a PC's Agent takes them (ProgramInstall, UninstallPrograms): an .apk the
// teacher picked on the PC, received whole (ApkUpload, as "Atualizar
// celulares" receives the agent's own) and installed silently by the Device
// Owner, nobody asked anything on the phone; and the apps the Midview
// installed removed the same way. Only those: whatever the teacher's program
// asks, an app goes only when it is not a system app, not one the phone needs
// (the home screen, the phone, the settings, the keyboard: ProtectedApps),
// not Chrome (the phones' browser for "Abrir site" and the site block) and
// Android records the agent as its installer; anything else (the maker's apps,
// the store's) is the teacher's to block instead. The agent itself is never
// installed over here (that is "Atualizar os celulares", which checks the
// school's key) nor removed. An older version than the
// one installed is refused: it would undo fixes, and Android would refuse it
// anyway. A new app is under the teacher's app block at once ("Liberar só
// estes": suspended), before the teacher hears it is installed. One install
// at a time.
class TeacherInstalls(
    private val upload: ApkUpload,
    private val facts: PackageFacts,
    private val changer: PackageChanger,
    private val selfPackage: String,
    private val isDeviceOwner: () -> Boolean,
    private val protectedApps: () -> Set<String>,
    private val reapplyBlock: () -> Unit,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val busy = AtomicBoolean(false)

    // Everything after an INSTALL_PROGRAM, on the teacher's connection and
    // answered here; returns when the connection is no longer needed.
    fun install(payload: ByteArray, channel: Channel) {
        val request = ProgramInstall.decodeRequest(payload) ?: return answer(channel, false, INVALID)
        val refusal = when {
            !ProgramInstall.isApkName(request.fileName) -> NOT_APK
            request.size > ProgramInstall.MAX_APK_SIZE -> TOO_BIG
            !isDeviceOwner() -> NOT_DEVICE_OWNER
            facts.freeBytes() < request.size * 2 + SPARE_BYTES -> NO_SPACE
            else -> null
        }
        if (refusal != null) return answer(channel, false, refusal)
        if (!busy.compareAndSet(false, true)) return answer(channel, false, BUSY)
        try {
            channel.send(Message(MessageType.INSTALL_ACCEPTED, ByteArray(0)))
            when (val received = upload.receive(request.size, null) { channel.receive() }) {
                is UploadResult.Refused -> return answer(channel, false, received.detail)
                is UploadResult.Interrupted -> {
                    log("The teacher's app ${request.fileName} did not arrive: ${received.detail}", null)
                    return
                }
                UploadResult.Received -> {}
            }
            channel.send(Message(MessageType.INSTALL_STARTED, ByteArray(0)))
            val (installed, detail) = installReceived()
            log("The teacher's app ${request.fileName}: $installed, $detail", null)
            answer(channel, installed, detail)
        } catch (e: IOException) {
            // The teacher's program is gone; what was done stays done.
            log("The teacher's app ${request.fileName}: the connection ended", e)
        } finally {
            upload.delete()
            busy.set(false)
        }
    }

    private fun installReceived(): Pair<Boolean, String> {
        val apk = facts.read(upload.file) ?: return false to NOT_AN_APP
        if (apk.packageName == selfPackage) return false to SELF_INSTALL
        val present = facts.installed(apk.packageName)
        if (present != null) {
            val now = present.facts
            if (apk.versionCode < now.versionCode) {
                return false to "o celular já tem este app numa versão mais nova (${now.versionName}): desinstale-o antes para instalar a ${apk.versionName}"
            }
            if (apk.signers.isNotEmpty() && now.signers.isNotEmpty() && apk.signers != now.signers) return false to OTHER_KEY
            // Nothing installed: the same number may hide another build.
            if (apk.versionCode == now.versionCode) return true to "${present.label} ${now.versionName}: $SAME_VERSION; nada foi instalado"
        }
        val failure = try {
            changer.install(upload.file, apk.packageName)
        } catch (e: Exception) {
            log("Android did not start the install", e)
            "o Android não começou a instalação: ${e.message ?: e.javaClass.simpleName}"
        }
        if (failure != null) return false to failure
        // Before the teacher hears of it: under "Liberar só estes" it is suspended.
        try {
            reapplyBlock()
        } catch (e: RuntimeException) {
            log("The app block after an install", e)
        }
        val label = facts.installed(apk.packageName)?.label ?: apk.packageName
        val done = "instalado: $label (versão ${apk.versionName})"
        return true to if (facts.isSuspended(apk.packageName)) "$done; fica bloqueado pelo \"Liberar só estes\"" else done
    }

    // UNINSTALL_PROGRAMS's payload (the apps' packages) in, the CommandAck's
    // result out: each app's outcome, by its name. Not being on the phone is
    // not a failure (nothing to do here), as on a PC.
    fun uninstall(payload: ByteArray): Pair<Boolean, String> {
        val packages = ProgramInstall.decodeList(payload)
        if (packages == null || packages.isEmpty() || packages.any { !PhoneAppPolicy.isValidPackageName(it) }) {
            return false to INVALID
        }
        if (!isDeviceOwner()) return false to NOT_DEVICE_OWNER_UNINSTALL
        var succeeded = true
        val protected = protectedApps()
        val lines = packages.distinct().map { pkg ->
            val app = facts.installed(pkg)
            val (ok, outcome) = when {
                pkg == selfPackage -> false to SELF_UNINSTALL
                app == null -> true to NOT_HERE
                pkg in protected -> false to ESSENTIAL
                pkg == CHROME -> false to BROWSER
                app.system -> false to SYSTEM_APP
                !app.byMidview -> false to NOT_BY_MIDVIEW
                else -> remove(pkg)
            }
            if (!ok) succeeded = false
            "${app?.label ?: pkg}: $outcome"
        }
        return succeeded to lines.joinToString("; ")
    }

    private fun remove(pkg: String): Pair<Boolean, String> {
        val failure = try {
            changer.uninstall(pkg)
        } catch (e: Exception) {
            log("Android did not start the uninstall of $pkg", e)
            "o Android não desinstalou: ${e.message ?: e.javaClass.simpleName}"
        }
        log("The teacher's uninstall of $pkg: ${failure ?: "done"}", null)
        return if (failure == null) true to UNINSTALLED else false to failure
    }

    private fun answer(channel: Channel, succeeded: Boolean, detail: String) {
        try {
            channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(succeeded, detail)))
        } catch (e: IOException) {
            // The teacher's program is gone.
        }
    }

    companion object {
        // What the folder of the APK has to keep free besides the APK twice
        // (the file, then Android's copy of it).
        const val SPARE_BYTES = 100L * 1024 * 1024

        const val INVALID = "pedido inválido"
        const val NOT_APK = "no celular só instalam aplicativos de Android (.apk)"
        const val TOO_BIG = "o aplicativo é grande demais (até 200 MB)"
        const val NOT_DEVICE_OWNER = "este celular não instala aplicativos pelo Midview"
        const val NOT_DEVICE_OWNER_UNINSTALL = "este celular não desinstala aplicativos pelo Midview"
        const val NO_SPACE = "sem espaço no celular para este aplicativo"
        const val BUSY = "outra instalação já está em andamento neste celular"
        const val NOT_AN_APP = "o arquivo recebido não é um aplicativo de Android"
        const val SELF_INSTALL = "é o próprio Midview do celular: use \"Atualizar os celulares\", em \"Máquinas da sala\""
        const val OTHER_KEY = "o celular tem este app assinado com outra chave: desinstale-o antes"

        const val UNINSTALLED = "desinstalado"
        const val NOT_HERE = "não está neste celular"
        const val SELF_UNINSTALL = "é o Midview: não sai do celular"
        const val ESSENTIAL = "o celular precisa dele (tela inicial, telefone, configurações, teclado): não dá para desinstalar"
        const val SYSTEM_APP = "veio com o celular: não dá para desinstalar; use \"Bloquear programas\""
        const val NOT_BY_MIDVIEW = "este app não foi instalado pelo Midview: use \"Bloquear programas\""
        const val BROWSER = "é o navegador do celular (\"Abrir site\" e \"Bloquear sites\" usam o Chrome): não sai"
        const val SAME_VERSION = "esta versão já está instalada (mesmo número de versão)"
        const val CHROME = "com.android.chrome"
    }
}
