package io.github.arnoneto1.midview.agent.provisioning

import io.github.arnoneto1.midview.agent.identity.TrustedMasters

// The rule of the "door" the Master opens over the cable (adb shell content
// call ... --method prepare): only the shell (uid 2000) may use it, only on a
// phone that is not yet the Device Owner nor trusts any teacher. Anything
// refused saves and trusts nothing. identity makes (or reads) the agent's
// certificate and returns its thumbprint and name; it runs after the number
// is saved, so the certificate is born named "Celular 07". If it fails (the
// key store can throw exceptions Kotlin does not see as RuntimeException),
// the answer is "no-identity" and nothing is left saved nor trusted.
class CableSetupDoor(
    private val setup: CableSetup,
    private val trusted: TrustedMasters,
    private val isOwner: () -> Boolean,
    private val identity: () -> Pair<String, String>,
) {
    fun prepare(callerUid: Int, payload: String?): Map<String, Any> {
        if (callerUid != SHELL_UID) return error("not-shell")
        if (isOwner()) return error("already-owner")
        if (trusted.count() > 0) return error("already-trusted")
        val data = CableSetupPayload.decode(payload) ?: return error("bad-setup")
        setup.save(data)
        // The identity before the trust: if it fails, nothing is left half
        // done (no setup file, nobody trusted) and a retry starts clean. The
        // failure is an answer, not a throw on the caller's binder thread.
        val (thumbprint, name) = try {
            identity()
        } catch (_: Exception) {
            setup.clear()
            return error("no-identity")
        }
        trusted.add(data.master)
        return mapOf("ok" to true, "thumbprint" to thumbprint, "name" to name)
    }

    private fun error(code: String): Map<String, Any> = mapOf("ok" to false, "error" to code)

    companion object {
        // Process.SHELL_UID: what adb shell runs as.
        const val SHELL_UID = 2000
    }
}
