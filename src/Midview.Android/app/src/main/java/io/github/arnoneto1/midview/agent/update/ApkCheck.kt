package io.github.arnoneto1.midview.agent.update

import android.content.Context
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import java.io.File
import java.security.MessageDigest
import java.util.Locale

// An APK as Android reads it: its package, its version and the SHA-256 (hex)
// of each certificate that signs it.
data class ApkFacts(val packageName: String, val versionCode: Long, val versionName: String, val signers: Set<String>)

// Whether a received APK may take the agent's place, and if not, why, for
// the teacher: it must be the same app, signed with the same key (only the
// school's own build may become the agent; Android would refuse another key
// anyway) and a newer version (an older one would undo fixes). The key is
// said before the version: an APK from elsewhere is never the agent,
// whatever its version.
object ApkCheck {
    const val NOT_AN_APP = "o arquivo recebido não é um aplicativo"
    const val OTHER_APP = "outro aplicativo"
    const val SIGNATURE_UNREADABLE = "a assinatura não pôde ser conferida"
    const val OTHER_KEY = "assinado com outra chave"

    fun refusal(candidate: ApkFacts?, installed: ApkFacts): String? = when {
        candidate == null -> NOT_AN_APP
        candidate.packageName != installed.packageName -> OTHER_APP
        candidate.signers.isEmpty() || installed.signers.isEmpty() -> SIGNATURE_UNREADABLE
        candidate.signers != installed.signers -> OTHER_KEY
        candidate.versionCode <= installed.versionCode -> "versão igual ou mais antiga (${candidate.versionName})"
        else -> null
    }

    // Each certificate (its DER bytes) as its SHA-256 in hex.
    fun signers(certificates: List<ByteArray>): Set<String> =
        certificates.map { Hex.lower(MessageDigest.getInstance("SHA-256").digest(it)) }.toSet()
}

// Bytes as hex digits, the same in every language the phone is set to.
// upper is how .NET's Convert.ToHexString writes them (the Master's attempt
// ids and fingerprints).
internal object Hex {
    private const val DIGITS = "0123456789ABCDEF"

    fun upper(bytes: ByteArray): String {
        val text = StringBuilder(bytes.size * 2)
        for (byte in bytes) {
            val value = byte.toInt() and 0xFF
            text.append(DIGITS[value ushr 4]).append(DIGITS[value and 0x0F])
        }
        return text.toString()
    }

    fun lower(bytes: ByteArray): String = upper(bytes).lowercase(Locale.ROOT)
}

// ApkFacts of the agent as it runs, and of an APK file.
interface ApkReader {
    fun installed(): ApkFacts

    // Null when Android cannot read the file as an app (signed) at all.
    fun read(apk: File): ApkFacts?
}

// ApkReader by the PackageManager. The signers are the certificates the
// APK's contents are signed with (SigningInfo.apkContentsSigners), the same
// for the installed app and the file.
class PackageApkReader(context: Context) : ApkReader {
    private val app = context.applicationContext
    private val pm = app.packageManager

    @Suppress("DEPRECATION")
    override fun installed(): ApkFacts = facts(pm.getPackageInfo(app.packageName, PackageManager.GET_SIGNING_CERTIFICATES))

    // GET_SIGNATURES too: Android 10 reads a file's certificates only for
    // that flag, and leaves signingInfo empty without it.
    @Suppress("DEPRECATION")
    override fun read(apk: File): ApkFacts? =
        pm.getPackageArchiveInfo(apk.path, PackageManager.GET_SIGNING_CERTIFICATES or PackageManager.GET_SIGNATURES)
            ?.let(::facts)

    private fun facts(info: PackageInfo): ApkFacts = ApkFacts(
        info.packageName,
        info.longVersionCode,
        info.versionName ?: info.longVersionCode.toString(),
        ApkCheck.signers(info.signingInfo?.apkContentsSigners.orEmpty().map { it.toByteArray() }),
    )
}
