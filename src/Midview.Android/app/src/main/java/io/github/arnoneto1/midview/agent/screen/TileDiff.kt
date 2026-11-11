package io.github.arnoneto1.midview.agent.screen

// Pixels, no Android classes (JVM tests).
data class Rect(val x: Int, val y: Int, val width: Int, val height: Int)

// Finds the changed tiles of a frame and turns them into few rectangles; mirrors
// src/Midview.Protocol/TileDiff.cs and TileRegions.cs. Pixels are ARGB ints.
class TileDiff(private val tile: Int = 64) {
    companion object {
        // Changed tiles of a group filling at least this share of the area around them go as one JPEG.
        private const val WHOLE_AREA_SHARE = 0.5

        // Changed tiles at most this many tiles apart belong to the same group.
        private const val GROUP_REACH = 2

        // No JPEG bigger than this many tiles; a bigger rectangle goes in bands.
        private const val MAX_REGION_TILES = 1024
    }

    // The tiles (edge ones clamped to the picture) that differ from the previous
    // picture, row by row. `previous` null, or of another width or height
    // (first frame, rotation): every tile.
    fun changed(
        previous: IntArray?, prevWidth: Int, prevHeight: Int,
        current: IntArray, width: Int, height: Int,
    ): List<Rect> {
        val all = previous == null || prevWidth != width || prevHeight != height
        val columns = (width + tile - 1) / tile
        val rows = (height + tile - 1) / tile
        val result = ArrayList<Rect>()
        for (row in 0 until rows) {
            for (column in 0 until columns) {
                val x = column * tile
                val y = row * tile
                val w = minOf(tile, width - x)
                val h = minOf(tile, height - y)
                if (all || !tileEqual(previous!!, current, width, x, y, w, h)) result.add(Rect(x, y, w, h))
            }
        }
        return result
    }

    private fun tileEqual(a: IntArray, b: IntArray, stride: Int, x: Int, y: Int, w: Int, h: Int): Boolean {
        for (row in y until y + h) {
            val start = row * stride + x
            for (i in start until start + w) if (a[i] != b[i]) return false
        }
        return true
    }

    private data class Coord(val row: Int, val column: Int)
    private data class Span(val top: Int, val left: Int, val bottom: Int, val right: Int)

    // Tiles (as returned by changed) to a few rectangles: neighbours in a row
    // join, equal runs on consecutive rows stack, dense groups go whole.
    fun merge(tiles: List<Rect>): List<Rect> {
        if (tiles.isEmpty()) return emptyList()
        // The picture's clamped edges, read off the tiles themselves.
        val rightEdge = HashMap<Int, Int>()
        val bottomEdge = HashMap<Int, Int>()
        for (t in tiles) {
            rightEdge[t.x / tile] = t.x + t.width
            bottomEdge[t.y / tile] = t.y + t.height
        }
        val coords = tiles.map { Coord(it.y / tile, it.x / tile) }.distinct()
        return groups(coords)
            .flatMap { mergeGroup(it) }
            .flatMap { bands(it) }
            .map { span ->
                val x = span.left * tile
                val y = span.top * tile
                Rect(x, y, rightEdge.getValue(span.right) - x, bottomEdge.getValue(span.bottom) - y)
            }
    }

    private fun mergeGroup(tiles: List<Coord>): List<Span> {
        val top = tiles.minOf { it.row }
        val bottom = tiles.maxOf { it.row }
        val left = tiles.minOf { it.column }
        val right = tiles.maxOf { it.column }
        val areaTiles = (bottom - top + 1) * (right - left + 1)
        if (tiles.size >= areaTiles * WHOLE_AREA_SHARE) return listOf(Span(top, left, bottom, right))

        // Runs of side-by-side tiles in each row; a run that lines up exactly
        // with one ending on the row above makes that rectangle taller.
        val spans = ArrayList<Span>()
        var endingAbove = HashMap<Pair<Int, Int>, Int>()
        for ((rowKey, rowTiles) in tiles.groupBy { it.row }.toSortedMap()) {
            val columns = rowTiles.map { it.column }.sorted()
            val endingHere = HashMap<Pair<Int, Int>, Int>()
            var runStart = 0
            for (i in 1..columns.size) {
                if (i < columns.size && columns[i] == columns[i - 1] + 1) continue
                val run = Pair(columns[runStart], columns[i - 1])
                val above = endingAbove[run]
                val index: Int
                if (above != null && spans[above].bottom == rowKey - 1) {
                    spans[above] = spans[above].copy(bottom = rowKey)
                    index = above
                } else {
                    spans.add(Span(rowKey, run.first, rowKey, run.second))
                    index = spans.size - 1
                }
                endingHere[run] = index
                runStart = i
            }
            endingAbove = endingHere
        }
        return spans
    }

    // The changed tiles split into groups of tiles near each other, in the order
    // of each group's first tile (top to bottom, left to right).
    private fun groups(tiles: List<Coord>): List<List<Coord>> {
        val ordered = tiles.sortedWith(compareBy({ it.row }, { it.column }))
        val remaining = HashSet(ordered)
        val result = ArrayList<List<Coord>>()
        for (start in ordered) {
            if (!remaining.remove(start)) continue
            val group = arrayListOf(start)
            var i = 0
            while (i < group.size) {
                for (row in group[i].row - GROUP_REACH..group[i].row + GROUP_REACH) {
                    for (column in group[i].column - GROUP_REACH..group[i].column + GROUP_REACH) {
                        val near = Coord(row, column)
                        if (remaining.remove(near)) group.add(near)
                    }
                }
                i++
            }
            result.add(group)
        }
        return result
    }

    private fun bands(span: Span): List<Span> {
        val columns = span.right - span.left + 1
        val rowsPerBand = maxOf(1, MAX_REGION_TILES / columns)
        val result = ArrayList<Span>()
        var top = span.top
        while (top <= span.bottom) {
            result.add(span.copy(top = top, bottom = minOf(span.bottom, top + rowsPerBand - 1)))
            top += rowsPerBand
        }
        return result
    }
}
