package io.github.arnoneto1.midview.agent.leave

import android.content.Context
import android.util.Log
import io.github.arnoneto1.midview.agent.protocol.PhoneLeaveRequest
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

// The process's one PhoneLeave and its record, in the device-protected
// storage (so the lock screen's check at a locked boot sees it too). The
// teacher's command, the "Tirar o Midview deste celular" screen and every
// start of the agent share them. isActive is what every part of the agent
// that locks something asks first: once the leaving started, nothing locks
// the phone again.
object PhoneLeaving {
    private const val TAG = "Midview"
    const val FILE_NAME = "leaving.txt"
    const val INVALID_REQUEST = "pedido inválido"

    @Volatile private var store: LeaveStore? = null
    @Volatile private var leave: PhoneLeave? = null
    private val resuming = AtomicBoolean(false)

    private val log: (String, Throwable?) -> Unit = { text, e -> if (e == null) Log.i(TAG, text) else Log.w(TAG, text, e) }

    private fun file(context: Context) = File(context.applicationContext.createDeviceProtectedStorageContext().filesDir, FILE_NAME)

    private fun store(context: Context): LeaveStore =
        store ?: synchronized(this) { store ?: LeaveStore(file(context), log).also { store = it } }

    fun get(context: Context): PhoneLeave =
        leave ?: synchronized(this) {
            leave ?: PhoneLeave(AndroidLeaveGateway(context, file(context)), store(context), log = log).also { leave = it }
        }

    // True from the first ask on, for good (the app is uninstalled next).
    fun isActive(context: Context): Boolean = store(context).phase != null

    // The Device Owner already given up (the uninstall is all that is left).
    fun ownerGivenUp(context: Context): Boolean = store(context).phase == LeaveStore.Phase.OWNER_CLEARED

    // The teacher's REMOVE_MIDVIEW_FROM_PHONE payload in, the CommandAck's
    // result out: the locks undone now (on the connection's thread: up to a
    // minute with many system apps to bring back). finish follows once the
    // answer is on its way, only when every lock was lifted.
    fun teacherAsked(context: Context, payload: ByteArray): Pair<Boolean, String> {
        if (!PhoneLeaveRequest.isConfirmed(payload)) return false to INVALID_REQUEST
        val report = get(context).start()
        return report.complete to report.text()
    }

    // A start of the agent (or a watchdog tick) with the leaving kept from
    // before: it is finished on a thread of its own, once at a time.
    fun resumeInBackground(context: Context) {
        val app = context.applicationContext
        if (!resuming.compareAndSet(false, true)) return
        Thread({
            try {
                get(app).resume()
            } catch (e: RuntimeException) {
                Log.e(TAG, "The Midview's leaving could not go on", e)
            } finally {
                resuming.set(false)
            }
        }, "Midview leaving").start()
    }
}
