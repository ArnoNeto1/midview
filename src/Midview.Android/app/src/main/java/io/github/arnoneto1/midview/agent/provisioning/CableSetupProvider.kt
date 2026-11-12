package io.github.arnoneto1.midview.agent.provisioning

import android.app.admin.DevicePolicyManager
import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.Binder
import android.os.Bundle
import io.github.arnoneto1.midview.agent.agent.DeviceName
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TRUSTED_MASTERS_FILE
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import java.io.File

// The cable door: exported with no permission, because adb's shell holds
// none; CableSetupDoor checks the caller's uid. "call" is the only way in:
// nothing is queried, inserted, changed nor deleted.
class CableSetupProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun call(method: String, arg: String?, extras: Bundle?): Bundle? {
        if (method != METHOD_PREPARE) return null
        val context = context ?: return null
        val dir = context.filesDir
        val setup = CableSetup(File(dir, CableSetup.FILE_NAME))
        // The uid first: an extras Bundle from any other app is never read.
        val uid = Binder.getCallingUid()
        val payload = if (uid == CableSetupDoor.SHELL_UID) extras?.getString("setup") else null
        val door = CableSetupDoor(
            setup,
            TrustedMasters(File(dir, TRUSTED_MASTERS_FILE)),
            { context.getSystemService(DevicePolicyManager::class.java).isDeviceOwnerApp(context.packageName) },
            {
                val name = DeviceName.read(context)
                AgentIdentity(dir, name).thumbprint to name
            },
        )
        val answer = door.prepare(uid, payload)
        return Bundle().apply {
            for ((key, value) in answer) {
                when (value) {
                    is Boolean -> putBoolean(key, value)
                    else -> putString(key, value.toString())
                }
            }
        }
    }

    override fun query(uri: Uri, projection: Array<String>?, selection: String?, selectionArgs: Array<String>?, sortOrder: String?): Cursor? = null

    override fun getType(uri: Uri): String? = null

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<String>?): Int = 0

    override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<String>?): Int = 0

    companion object {
        const val AUTHORITY = "io.github.arnoneto1.midview.agent.setup"
        const val METHOD_PREPARE = "prepare"
    }
}
