package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class AgentKeeperTest {
    private var starts = 0
    private var failNext = false
    private val keeper = AgentKeeper {
        if (failNext) {
            failNext = false
            throw IllegalStateException("not allowed")
        }
        starts++
    }

    // A process the system started again (after the student's "clear all")
    // starts the agent: Android's own restart of the service may be late or
    // stopped before it runs.
    @Test fun aNewProcessStartsTheAgent() {
        assertTrue(keeper.ensureRunning())
        assertEquals(1, starts)
    }

    // The watchdog's every check does not start the agent again while it runs.
    @Test fun aRunningAgentIsNotStartedAgain() {
        keeper.ensureRunning()
        assertFalse(keeper.ensureRunning())
        keeper.created()
        assertFalse(keeper.ensureRunning())
        assertEquals(1, starts)
    }

    // Created by Android's own restart, without a start of ours: nothing to do.
    @Test fun anAgentCreatedByAndroidIsNotStartedAgain() {
        keeper.created()
        assertFalse(keeper.ensureRunning())
        assertEquals(0, starts)
    }

    // Stopped by the system (an "idle" app's service): the next check starts it.
    @Test fun aStoppedAgentIsStartedAgain() {
        keeper.ensureRunning()
        keeper.created()
        keeper.destroyed()
        assertTrue(keeper.ensureRunning())
        assertEquals(2, starts)
    }

    // A start Android refused is tried again at the next check.
    @Test fun aRefusedStartIsTriedAgain() {
        failNext = true
        try {
            keeper.ensureRunning()
            fail("the refusal reaches the caller, who logs it")
        } catch (e: IllegalStateException) {
            // expected
        }
        assertTrue(keeper.ensureRunning())
        assertEquals(1, starts)
    }

    // Exact alarms where Android allows them (before Android 12 always; from
    // 12 with the exact alarm permission or the battery exemption), or else
    // the inexact ones, a few seconds later at most.
    @Test fun exactAlarmWhenAllowed() {
        assertEquals(WatchdogAlarm.Kind.EXACT, WatchdogAlarm.kind(sdkInt = 29, canScheduleExact = false))
        assertEquals(WatchdogAlarm.Kind.EXACT, WatchdogAlarm.kind(sdkInt = 30, canScheduleExact = false))
        assertEquals(WatchdogAlarm.Kind.EXACT, WatchdogAlarm.kind(sdkInt = 33, canScheduleExact = true))
        assertEquals(WatchdogAlarm.Kind.INEXACT, WatchdogAlarm.kind(sdkInt = 31, canScheduleExact = false))
        assertEquals(WatchdogAlarm.Kind.INEXACT, WatchdogAlarm.kind(sdkInt = 35, canScheduleExact = false))
    }

    // The next check comes within seconds, never sooner than Android's
    // floor for an alarm (5 s).
    @Test fun nextCheckWithinSeconds() {
        assertEquals(1_000L + WatchdogAlarm.INTERVAL_MILLIS, WatchdogAlarm.nextAt(1_000L))
        assertTrue(WatchdogAlarm.INTERVAL_MILLIS in 5_000L..10_000L)
    }
}
