package io.github.arnoneto1.midview.agent.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class ReadyShowTest {
    private val delays = mutableListOf<Long>()
    private val pending = mutableListOf<() -> Unit>()
    private val later: (Long, () -> Unit) -> Unit = { delay, run ->
        delays += delay
        pending += run
    }

    // On the Redmi 10C (2026-11-12) the first start was dropped: MIUI gave the
    // new Device Owner its "open screens in the background" a second later.
    @Test
    fun asks_again_while_the_screen_was_not_seen() {
        var opened = 0
        ReadyShow.start(open = { opened++ }, seen = { false }, later = later)

        assertEquals(1, opened)
        assertEquals(listOf(2_000L, 5_000L, 10_000L), delays)
        pending.forEach { it() }
        assertEquals(4, opened)
    }

    @Test
    fun stops_once_the_screen_was_seen() {
        var opened = 0
        var seen = false
        ReadyShow.start(open = { opened++; seen = true }, seen = { seen }, later = later)

        pending.forEach { it() }
        assertEquals(1, opened)
    }

    @Test
    fun the_second_try_that_works_is_the_last() {
        var opened = 0
        var seen = false
        ReadyShow.start(open = { opened++; if (opened == 2) seen = true }, seen = { seen }, later = later)

        pending.forEach { it() }
        assertEquals(2, opened)
    }
}
