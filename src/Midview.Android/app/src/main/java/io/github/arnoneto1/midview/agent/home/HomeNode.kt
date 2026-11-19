package io.github.arnoneto1.midview.agent.home

import java.text.Normalizer
import java.util.Locale

// One element of the screen as the accessibility service reads it, with the
// elements inside it: the same facts the Master's HomeScreenNode reads from a
// uiautomator dump over the cable (a launcher's resource id, its package, its
// text and description, whether it is checked, where it is on the screen), so
// Launcher3Home can aim as PhoneLauncher3Home does.
data class HomeNode(
    val id: String = "",
    val cls: String = "",
    val pkg: String = "",
    val text: String = "",
    val description: String = "",
    val checked: Boolean = false,
    val left: Int = 0,
    val top: Int = 0,
    val right: Int = 0,
    val bottom: Int = 0,
    val children: List<HomeNode> = emptyList(),
) {
    val x: Int get() = (left + right) / 2
    val y: Int get() = (top + bottom) / 2
    val width: Int get() = right - left
    val height: Int get() = bottom - top

    // Drawn on the screen: a hidden title has no size.
    val shown: Boolean get() = width > 0 && height > 0

    fun descendants(): Sequence<HomeNode> = sequence {
        for (child in children) {
            yield(child)
            yieldAll(child.descendants())
        }
    }

    // The first element with this resource id, this one included.
    fun find(id: String): HomeNode? = if (this.id == id) this else descendants().firstOrNull { it.id == id }

    fun has(id: String): Boolean = find(id) != null

    // Inside this one's bounds (the drawer's list, the dock), whole.
    fun holds(node: HomeNode): Boolean =
        node.left >= left && node.right <= right && node.top >= top && node.bottom <= bottom

    companion object {
        private val MARKS = Regex("\\p{Mn}+")

        // The name the launcher gives an app, compared without case or
        // accents: "Câmera" is the same app as "Camera".
        fun plain(name: String): String =
            MARKS.replace(Normalizer.normalize(name.trim(), Normalizer.Form.NFD), "").lowercase(Locale.ROOT)
    }
}
