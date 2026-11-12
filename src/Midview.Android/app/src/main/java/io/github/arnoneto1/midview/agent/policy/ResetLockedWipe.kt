package io.github.arnoneto1.midview.agent.policy

// The wipe of a school phone with its "no factory reset" lock lifted for the
// wipe only: if the wipe fails, the lock goes back on before the failure is
// passed on, so a refused wipe never leaves the phone resettable from
// Settings. Apart from the DevicePolicyManager so it is tested on the JVM.
object ResetLockedWipe {
    const val NO_FACTORY_RESET = "no_factory_reset"

    // lift and lock set and clear the restriction; a refusal to lift it is
    // not fatal (the wipe is tried anyway), and it is then not locked again.
    fun run(lift: () -> Unit, lock: () -> Unit, wipe: () -> Unit, log: (String, Throwable?) -> Unit = { _, _ -> }) {
        var lifted = true
        try {
            lift()
        } catch (e: RuntimeException) {
            lifted = false
            log("Could not lift the factory reset lock before wiping", e)
        }
        try {
            wipe()
        } catch (e: Throwable) {
            if (lifted) {
                try {
                    lock()
                } catch (again: RuntimeException) {
                    log("Could not put the factory reset lock back", again)
                }
            }
            throw e
        }
    }
}
