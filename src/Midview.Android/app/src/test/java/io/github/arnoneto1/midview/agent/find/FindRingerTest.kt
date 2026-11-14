package io.github.arnoneto1.midview.agent.find

import io.github.arnoneto1.midview.agent.protocol.PhoneFind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FindRingerTest {
    private class FakeOutput : RingOutput {
        var volume = 3
        val max = 15
        var sound = false
        var vibrating = false
        var screen = false
        var soundWorks = true
        val volumes = mutableListOf<Int>()

        override fun alarmVolume() = volume
        override fun maxAlarmVolume() = max
        override fun setAlarmVolume(level: Int) {
            volume = level
            volumes += level
        }
        override fun startSound(): Boolean {
            sound = soundWorks
            return soundWorks
        }
        override fun stopSound() {
            sound = false
        }
        override fun startVibration() {
            vibrating = true
        }
        override fun stopVibration() {
            vibrating = false
        }
        var ringingWhenShown: (() -> Boolean)? = null
        var shownWhileRinging: Boolean? = null
        override fun showScreen() {
            screen = true
            shownWhileRinging = ringingWhenShown?.invoke()
        }
        override fun hideScreen() {
            screen = false
        }
    }

    // A clock the test moves by hand.
    private class FakeTimer : RingTimer {
        val pending = mutableListOf<Pair<Long, () -> Unit>>()
        override fun after(delayMillis: Long, task: () -> Unit): () -> Unit {
            val entry = delayMillis to task
            pending += entry
            return { pending.remove(entry) }
        }
        fun fire() = pending.toList().forEach { it.second() }
    }

    private class FakeSaved : SavedVolume {
        var level: Int? = null
        override fun read() = level
        override fun write(level: Int?) {
            this.level = level
        }
    }

    private val output = FakeOutput()
    private val timer = FakeTimer()
    private val saved = FakeSaved()
    private val ringer = FindRinger(output, timer, saved)

    // The volume from before is kept while it rings, and forgotten once given back.
    @Test fun theVolumeFromBeforeIsKeptWhileItRings() {
        ringer.start(60)
        assertEquals(3, saved.level)
        ringer.stop(FindRinger.Stop.TEACHER)
        assertEquals(null, saved.level)
    }

    // The agent's process killed while ringing: the next start gives the volume back.
    @Test fun aRingCutShortGivesTheVolumeBackOnTheNextStart() {
        ringer.start(60)
        val next = FindRinger(output, FakeTimer(), saved)
        next.recover()
        assertEquals(3, output.volume)
        assertEquals(null, saved.level)
        next.recover()
        assertEquals(listOf(15, 3), output.volumes)
    }

    @Test fun startRingsLoudAndShowsTheScreen() {
        assertEquals(true to "tocando por 1 min", ringer.handle(PhoneFind.encode(60)))
        assertTrue(ringer.isRinging)
        assertEquals(15, output.volume)
        assertTrue(output.sound && output.vibrating && output.screen)
        assertEquals(60_000L, timer.pending.single().first)
    }

    // The screen checks that it rings as it comes up (FindActivity.onResume).
    @Test fun itRingsBeforeTheScreenComesUp() {
        output.ringingWhenShown = { ringer.isRinging }
        ringer.start(60)
        assertEquals(true, output.shownWhileRinging)
    }

    @Test fun theStudentsPararStopsItAndGivesTheVolumeBack() {
        ringer.start(60)
        assertTrue(ringer.stop(FindRinger.Stop.STUDENT))
        assertFalse(ringer.isRinging)
        assertEquals(3, output.volume)
        assertFalse(output.sound || output.vibrating || output.screen)
        assertTrue(timer.pending.isEmpty())
    }

    @Test fun theTeacherStopsIt() {
        ringer.start(60)
        assertEquals(true to FindRinger.STOPPED, ringer.handle(PhoneFind.encode(PhoneFind.STOP)))
        assertEquals(3, output.volume)
        assertEquals(true to FindRinger.NOT_RINGING, ringer.handle(PhoneFind.encode(PhoneFind.STOP)))
    }

    @Test fun theTimeUpStopsIt() {
        ringer.start(15)
        timer.fire()
        assertFalse(ringer.isRinging)
        assertEquals(3, output.volume)
        assertFalse(output.screen)
    }

    // Asked again while ringing: the new time counts, the volume saved stays the first one.
    @Test fun askedAgainItKeepsRingingForTheNewTime() {
        ringer.start(60)
        assertEquals(true to "tocando por 15 s", ringer.start(15))
        assertEquals(15_000L, timer.pending.single().first)
        ringer.stop(FindRinger.Stop.AGENT)
        assertEquals(3, output.volume)
        assertEquals(listOf(15, 3), output.volumes)
    }

    @Test fun withoutAnySoundNothingIsLeftChanged() {
        output.soundWorks = false
        assertEquals(false to FindRinger.NO_SOUND, ringer.start(60))
        assertFalse(ringer.isRinging)
        assertEquals(3, output.volume)
        assertFalse(output.screen || output.vibrating)
        assertTrue(timer.pending.isEmpty())
    }

    @Test fun nonsenseIsRefused() {
        assertEquals(false to FindRinger.INVALID, ringer.handle(byteArrayOf(1)))
        assertFalse(ringer.isRinging)
    }

    @Test fun stoppingWhatDoesNotRingChangesNothing() {
        assertFalse(ringer.stop(FindRinger.Stop.AGENT))
        assertTrue(output.volumes.isEmpty())
    }
}
