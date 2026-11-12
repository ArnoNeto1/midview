package io.github.arnoneto1.midview.agent.ui

// MIUI lets a new Device Owner open screens from the background only about a
// second after it became owner (it grants the permission then); a start made
// before that is dropped without any error (seen on the Redmi 10C,
// 2026-11-12). So the "Pronto" screen is asked for again a few times, until
// it has been seen.
object ReadyShow {
    val RETRY_AFTER_MILLIS = listOf(2_000L, 5_000L, 10_000L)

    fun start(open: () -> Unit, seen: () -> Boolean, later: (Long, () -> Unit) -> Unit) {
        open()
        for (delay in RETRY_AFTER_MILLIS) {
            later(delay) { if (!seen()) open() }
        }
    }
}
