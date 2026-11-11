package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class TileDiffTest {
    private val diff = TileDiff()

    @Test fun firstFrameAndResizeAreFullScreen() {
        val current = IntArray(130 * 70)
        val all = diff.changed(null, 0, 0, current, 130, 70)
        assertEquals(3 * 2, all.size) // 3 columns x 2 rows of 64
        assertEquals(Rect(128, 64, 2, 6), all.last()) // edge tiles clamped
        // Rotation: same pixel count, other width and height.
        assertEquals(6, diff.changed(IntArray(70 * 130), 70, 130, current, 130, 70).size)
        // Same size and same pixels: nothing.
        assertEquals(0, diff.changed(current.copyOf(), 130, 70, current, 130, 70).size)
        assertEquals(listOf(Rect(0, 0, 130, 70)), diff.merge(all))
    }

    @Test fun onlyChangedTilesAreReportedAndMergedPerRow() {
        val w = 320
        val h = 128 // 5 columns x 2 rows
        val previous = IntArray(w * h)
        val current = previous.copyOf()
        fun touch(x: Int, y: Int) { current[y * w + x] = 1 }
        touch(1, 1) // tile (row 0, col 0)
        touch(70, 2) // tile (row 0, col 1)
        touch(300, 100) // tile (row 1, col 4)
        val changed = diff.changed(previous, w, h, current, w, h)
        assertEquals(listOf(Rect(0, 0, 64, 64), Rect(64, 0, 64, 64), Rect(256, 64, 64, 64)), changed)
        // Neighbours in a row join; the far tile (3 columns away) is another group.
        assertEquals(listOf(Rect(0, 0, 128, 64), Rect(256, 64, 64, 64)), diff.merge(changed))
        assertTrue(diff.changed(current, w, h, current.copyOf(), w, h).isEmpty())
        assertTrue(diff.merge(emptyList()).isEmpty())
    }

    @Test fun mergeStacksEqualRunsAndTakesDenseAreaWhole() {
        // Two aligned runs of 2 tiles on rows 0 and 1 become one taller rectangle.
        val tiles = listOf(Rect(0, 0, 64, 64), Rect(64, 0, 64, 64), Rect(0, 64, 64, 64), Rect(64, 64, 64, 64))
        assertEquals(listOf(Rect(0, 0, 128, 128)), diff.merge(tiles))
        // L shape inside a 3x3 box with 5 of 9 tiles (at least half): the whole box in one JPEG.
        val l = listOf(Rect(0, 0, 64, 64), Rect(0, 64, 64, 64), Rect(0, 128, 64, 64), Rect(64, 128, 64, 64), Rect(128, 128, 64, 64))
        assertEquals(listOf(Rect(0, 0, 192, 192)), diff.merge(l))
        // Sparse diagonal (3 of 9, under half) stays as separate rectangles.
        val diagonal = listOf(Rect(0, 0, 64, 64), Rect(64, 64, 64, 64), Rect(128, 128, 64, 64))
        assertEquals(3, diff.merge(diagonal).size)
    }

    @Test fun hugeRectangleGoesInBands() {
        // 64 columns x 20 rows = 1280 tiles > 1024: bands of 16 rows.
        val tiles = ArrayList<Rect>()
        for (r in 0 until 20) for (c in 0 until 64) tiles.add(Rect(c * 64, r * 64, 64, 64))
        assertEquals(listOf(Rect(0, 0, 4096, 1024), Rect(0, 1024, 4096, 256)), diff.merge(tiles))
    }

    @Test fun picturesMustHoldTheirSize() {
        assertThrows(IllegalArgumentException::class.java) { diff.changed(null, 0, 0, IntArray(10), 4, 4) }
        assertThrows(IllegalArgumentException::class.java) { diff.changed(IntArray(10), 4, 4, IntArray(16), 4, 4) }
        assertEquals(1, diff.changed(null, 0, 0, IntArray(16), 4, 4).size)
    }
}
