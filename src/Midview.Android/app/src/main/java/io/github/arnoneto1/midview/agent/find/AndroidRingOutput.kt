package io.github.arnoneto1.midview.agent.find

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.media.ToneGenerator
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.util.Log

// "Tocar para encontrar" on Android (FindRinger decides): the alarm stream's
// volume, the device's alarm sound looping as an alarm (USAGE_ALARM, so it
// plays with the ringer on silent or vibrate; "Não perturbe" lets it through
// only where alarms are allowed), a long vibration pattern where there is a
// vibrator, and FindActivity over the lock screen.
class AndroidRingOutput(context: Context) : RingOutput {
    private val app = context.applicationContext
    private val audio = app.getSystemService(AudioManager::class.java)
    private val vibrator = app.getSystemService(Vibrator::class.java)
    private val main = Handler(Looper.getMainLooper())

    private var player: MediaPlayer? = null
    private var tone: ToneGenerator? = null

    override fun alarmVolume(): Int = audio.getStreamVolume(AudioManager.STREAM_ALARM)

    override fun maxAlarmVolume(): Int = audio.getStreamMaxVolume(AudioManager.STREAM_ALARM)

    override fun setAlarmVolume(level: Int) {
        try {
            audio.setStreamVolume(AudioManager.STREAM_ALARM, level, 0)
        } catch (e: RuntimeException) {
            Log.w(TAG, "The alarm volume was not changed", e)
        }
    }

    // The alarm sound, else the ringtone or the notification sound; with
    // none of them a loud tone of the system's own.
    override fun startSound(): Boolean {
        stopSound()
        for (type in listOf(RingtoneManager.TYPE_ALARM, RingtoneManager.TYPE_RINGTONE, RingtoneManager.TYPE_NOTIFICATION)) {
            val uri = RingtoneManager.getDefaultUri(type) ?: continue
            if (play(uri)) return true
        }
        return try {
            tone = ToneGenerator(AudioManager.STREAM_ALARM, ToneGenerator.MAX_VOLUME).also {
                it.startTone(ToneGenerator.TONE_CDMA_EMERGENCY_RINGBACK)
            }
            true
        } catch (e: RuntimeException) {
            Log.w(TAG, "No sound to ring with", e)
            false
        }
    }

    // Built in a local first: one that fails halfway is released, never kept.
    private fun play(uri: Uri): Boolean {
        val built = MediaPlayer()
        return try {
            built.setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ALARM)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build(),
            )
            built.setDataSource(app, uri)
            built.isLooping = true
            built.prepare()
            built.start()
            player = built
            true
        } catch (e: Exception) {
            Log.w(TAG, "The sound $uri did not play", e)
            built.release()
            false
        }
    }

    override fun stopSound() {
        player?.let {
            try {
                it.stop()
            } catch (e: IllegalStateException) {
                // Never started.
            }
            it.release()
        }
        player = null
        tone?.release()
        tone = null
    }

    override fun startVibration() {
        if (vibrator?.hasVibrator() != true) return
        try {
            vibrator.vibrate(
                VibrationEffect.createWaveform(longArrayOf(0, 800, 400), 0),
                AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).build(),
            )
        } catch (e: RuntimeException) {
            Log.w(TAG, "No vibration", e)
        }
    }

    override fun stopVibration() {
        try {
            vibrator?.cancel()
        } catch (e: RuntimeException) {
            // Nothing to stop.
        }
    }

    override fun showScreen() {
        try {
            app.startActivity(FindActivity.intent(app))
        } catch (e: RuntimeException) {
            Log.w(TAG, "The find screen did not show", e)
        }
    }

    override fun hideScreen() {
        main.post { FindActivity.close() }
    }

    companion object {
        private const val TAG = "Midview"

        // The alarm volume from before the ring, in the app's private storage.
        fun savedVolume(context: Context): SavedVolume {
            val file = java.io.File(context.filesDir, "find-volume.txt")
            return object : SavedVolume {
                override fun read(): Int? = try {
                    if (file.isFile) file.readText().trim().toIntOrNull() else null
                } catch (e: java.io.IOException) {
                    null
                }

                override fun write(level: Int?) {
                    try {
                        if (level == null) file.delete() else file.writeText(level.toString())
                    } catch (e: java.io.IOException) {
                        Log.w(TAG, "The alarm volume from before the ring was not kept", e)
                    }
                }
            }
        }

        // The timer FindRinger needs, on the main thread.
        fun timer(): RingTimer {
            val handler = Handler(Looper.getMainLooper())
            return RingTimer { delayMillis, task ->
                val runnable = Runnable { task() }
                handler.postDelayed(runnable, delayMillis)
                return@RingTimer { handler.removeCallbacks(runnable) }
            }
        }
    }
}
