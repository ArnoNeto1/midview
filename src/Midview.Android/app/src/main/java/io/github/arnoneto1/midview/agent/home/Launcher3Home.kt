package io.github.arnoneto1.midview.agent.home

// Arranges the home screen of a device whose launcher is Android's own
// (Launcher3, com.android.launcher3) over the network, step by step as the
// Master's src/Midview.Master/PhoneLauncher3Home.cs does over the cable (its
// comments record what was measured on the school's Multilaser M10, Android
// 10, on 2026-11-13): every icon, folder and widget taken off the home pages,
// the class's apps in the dock (Chrome, Câmera, Fotos, Files and Calculadora,
// those the student can open), and, in the launcher's settings, "Adicionar
// ícone à tela inicial" and "Mostrar Google App" off. A long press shows no
// "Remover" there: an item is dragged to the "Remover" target the launcher
// shows at the top only while dragging, centred when alone, on the left half
// when "Desinstalar" shows beside it (an app the student installed), so it is
// aimed at the middle first and at the left quarter after a miss, never at
// the right half. A folder dropped there goes with every shortcut in it (the
// apps stay in the drawer). The date and the search bar cannot be removed and
// stay. Every step looks at the screen first; anything unexpected ends it,
// and the teacher arranges the rest by hand. The same limits as the cable's
// (looks, taps, swipes, misses, and LIMIT_MILLIS on the whole), so a device
// that does not behave never keeps the agent busy for long.
class Launcher3Home(
    private val d: HomeDriver,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // True once the settings are off, the home pages empty and the dock
    // right. False on any failure, with the device left as far as it got
    // (on its home screen).
    fun run(): Boolean =
        try {
            drive()
        } finally {
            d.home()
        }

    private val inTime: Boolean get() = d.elapsedMillis() < LIMIT_MILLIS

    private fun drive(): Boolean {
        if (!settingsOff()) return stop("the launcher's settings did not turn off")
        val dock = DOCK_APPS.filter { d.isLaunchable(it.pkg) }.map { it.names }
        if (!emptied(dock)) return stop("the home pages were not emptied")
        if (!docked(dock)) return stop("the dock did not end with the class's apps")
        return true
    }

    private fun stop(why: String): Boolean {
        log("Home screen left for the teacher's hands: $why.", null)
        return false
    }

    private class Row(val title: String, val switch: HomeNode)

    // The launcher's settings: each switch to turn off tapped until it reads
    // off. "Adicionar ícone" must be there; the Google one may not.
    private fun settingsOff(): Boolean {
        if (!d.openLauncherSettings()) return false
        d.pause(SETTLE)
        var taps = 0
        var looks = 0
        while (inTime && taps < 4 && looks < 10) {
            looks++
            val screen = d.screen()
            val rows = if (screen == null) emptyList() else switchRows(screen)
            val addIcon = rows.firstOrNull { row -> ADD_ICON_TITLES.any { row.title.contains(it) } }
            if (addIcon == null) {
                d.pause(SECOND)
                continue
            }
            val on = rows.firstOrNull { it.switch.checked && (it.title == addIcon.title || it.title.contains(GOOGLE_TITLE)) }
            if (on == null) {
                d.home()
                d.pause(SETTLE)
                return true
            }
            d.tap(on.switch.x, on.switch.y)
            taps++
            d.pause(SECOND)
        }
        return false
    }

    // A settings screen's rows that have a switch: the row's title (plain)
    // and its switch. A row is the element holding one title and a switch.
    private fun switchRows(screen: HomeNode): List<Row> =
        screen.descendants()
            .filter { n -> n.pkg == LAUNCHER && n.shown && n.descendants().count { it.id == TITLE } == 1 && n.has(SWITCH) }
            .map { Row(HomeNode.plain(it.find(TITLE)!!.text), it.find(SWITCH)!!) }
            .filter { it.switch.shown }
            .toList()

    // Every home page emptied (icons, folders, widgets), and the dock too
    // unless it already holds the class's apps in order.
    private fun emptied(dock: List<List<String>>): Boolean {
        d.home()
        d.pause(SETTLE)
        var failures = 0
        var backs = 0
        var misses = 0
        var swipes = 0
        var dropped = -1
        while (inTime && failures < 4 && backs < 8 && swipes < 12) {
            val screen = d.screen()
            if (screen == null) {
                failures++
                d.pause(SECOND)
                continue
            }
            if (screen.descendants().none { it.pkg == LAUNCHER }) {
                // Something else on the screen (the uninstall question): not for the agent.
                d.back()
                return false
            }
            val (width, height) = size(screen)
            // A menu, the drawer, the widgets, or "Item removido" left open: closed.
            val workspace = ours(screen, "${ID}workspace")
            val apps = ours(screen, "${ID}apps_view")
            if (workspace == null || apps != null && apps.top < height / 2 ||
                screen.has("${ID}deep_shortcuts_container") || screen.has("${ID}folder_content")
            ) {
                backs++
                d.back()
                d.pause(SECOND)
                continue
            }
            backs = 0
            val pages = workspace.children.filter { it.shown }
            val page = pages.firstOrNull { it.left < width / 2 && it.right > width / 2 }
            if (page == null) {
                failures++
                d.home()
                d.pause(SETTLE)
                continue
            }
            val items = pageItems(page)
            val docked = dockItems(screen)
            val count = items.size + docked.size
            // The last item dropped is still there: "Remover" was not where it was aimed.
            misses = if (dropped < 0) misses else if (count < dropped) 0 else misses + 1
            dropped = -1
            if (misses >= 3) return false

            // The dock keeps the class's apps it starts with, in order; what follows them goes.
            val item = items.firstOrNull() ?: docked.drop(kept(docked, dock)).firstOrNull()
            if (item == null) {
                // This page is empty: the next one, while there is one on its right.
                if (pages.any { it.left >= width / 2 }) {
                    swipes++
                    d.swipe(width * 85 / 100, height / 2, width * 15 / 100, height / 2, PAGE_SWIPE)
                    d.pause(SETTLE)
                    continue
                }
                d.home()
                d.pause(SETTLE)
                return true
            }
            // Over "Remover": alone, it is in the middle; beside "Desinstalar", on the left.
            val x = if (misses % 2 == 0) width / 2 else width / 4
            val y = maxOf(1, pages.minOf { it.top } / 2)
            d.drag(item.x, item.y, x, y)
            d.pause(SECOND)
            // "Item removido" hides the home from the screen read for a few seconds.
            d.back()
            d.pause(MOMENT)
            dropped = count
        }
        return false
    }

    // The class's apps dragged from the drawer to the dock's end, in order,
    // each after the ones already there; the dock read again after them all,
    // and once more for what did not land.
    private fun docked(dock: List<List<String>>): Boolean {
        var round = 0
        while (round < 2 && inTime) {
            round++
            d.home()
            d.pause(SETTLE)
            val home = d.screen() ?: continue
            val labels = dockLabels(home)
            if (matches(labels, dock)) return true
            // Something else in the dock: not for the agent to sort out.
            val hotseat = ours(home, "${ID}hotseat")
            if (labels.size >= dock.size || !matches(labels, dock.take(labels.size)) || hotseat == null) return false
            val cells = hotseat.children.firstOrNull { it.shown } ?: hotseat
            val dropX = cells.right - minOf(40, cells.width / 10)
            val dropY = cells.y
            for (names in dock.drop(labels.size)) {
                if (!inTime) break
                val app = inDrawer(home, names) ?: break
                d.drag(app.x, app.y, dropX, dropY)
                d.pause(SETTLE)
            }
        }
        d.home()
        d.pause(SETTLE)
        val last = d.screen() ?: return false
        return matches(dockLabels(last), dock)
    }

    // The app in the drawer (swiped up from the home), scrolled to when it is
    // further down; null when the drawer did not open or has no such app.
    private fun inDrawer(home: HomeNode, names: List<String>): HomeNode? {
        val (width, height) = size(home)
        d.swipe(width / 2, height * 7 / 10, width / 2, height * 2 / 10, DRAWER_SWIPE)
        d.pause(SETTLE)
        var scrolls = 0
        while (scrolls <= 8 && inTime) {
            scrolls++
            val screen = d.screen() ?: return null
            val list = ours(screen, "${ID}apps_list_view") ?: return null
            val app = list.descendants().firstOrNull { it.shown && list.holds(it) && named(it, names) }
            if (app != null) return app
            d.swipe(list.x, list.bottom - list.height / 6, list.x, list.top + list.height / 6, LIST_SWIPE)
            d.pause(MOMENT)
        }
        return null
    }

    // A home page's icons, folders and widgets: what its cell layout holds,
    // but the date and search bar, which cannot be removed.
    private fun pageItems(page: HomeNode): List<HomeNode> =
        page.children.flatMap { it.children }
            .filter { it.shown && it.id != "${ID}search_container_workspace" && !it.has("${ID}smartspace_content") }

    private fun dockItems(screen: HomeNode): List<HomeNode> =
        ours(screen, "${ID}hotseat")?.children?.flatMap { it.children }?.filter { it.shown } ?: emptyList()

    private fun dockLabels(screen: HomeNode): List<String> = dockItems(screen).map { it.description }

    // How many of the dock's first items are the class's first apps, in order.
    private fun kept(docked: List<HomeNode>, dock: List<List<String>>): Int =
        docked.zip(dock).takeWhile { (node, names) -> named(node, names) }.size

    private fun matches(labels: List<String>, apps: List<List<String>>): Boolean =
        labels.size == apps.size &&
            labels.zip(apps).all { (label, names) -> names.any { HomeNode.plain(it) == HomeNode.plain(label) } }

    private fun named(node: HomeNode, names: List<String>): Boolean =
        node.description.isNotEmpty() && names.any { HomeNode.plain(it) == HomeNode.plain(node.description) }

    // Taps and drags only ever start on the launcher's own screens.
    private fun ours(screen: HomeNode, id: String): HomeNode? =
        screen.descendants().firstOrNull { it.id == id && it.pkg == LAUNCHER && it.shown }

    private fun size(screen: HomeNode): Pair<Int, Int> {
        val nodes = screen.descendants().toList()
        return if (nodes.isEmpty()) 800 to 1232 else nodes.maxOf { it.right } to nodes.maxOf { it.bottom }
    }

    // One of the class's apps for the dock: its package and the names
    // Launcher3 gives it.
    class DockApp(val pkg: String, val names: List<String>)

    companion object {
        const val LAUNCHER = "com.android.launcher3"
        const val SETTINGS_ACTION = "android.intent.action.APPLICATION_PREFERENCES"

        // As the cable: about 3 s a screen read, 7 s an item removed, 9 s an
        // app put in the dock; nine items on two pages, four dock apps taken
        // off and five put in took 168 s on the M10.
        const val LIMIT_MILLIS = 4 * 60_000L

        // The class's apps, in the dock's order (PhoneLauncher3Home.DockApps).
        val DOCK_APPS: List<DockApp> = listOf(
            DockApp("com.android.chrome", listOf("Chrome")),
            DockApp("com.android.camera2", listOf("Câmera", "Camera")),
            DockApp("com.google.android.apps.photos", listOf("Fotos", "Photos", "Google Fotos")),
            DockApp("com.google.android.apps.nbu.files", listOf("Files", "Files by Google", "Arquivos")),
            DockApp("com.google.android.calculator", listOf("Calculadora", "Calculator")),
        )

        private const val ID = "$LAUNCHER:id/"
        private const val TITLE = "android:id/title"
        private const val SWITCH = "android:id/switch_widget"

        // The settings rows turned off: "Adicionar ícone à tela inicial" (a
        // new app's icon on the home) and, where there is one, the Google app
        // left of the home. Matched without case or accents (HomeNode.plain).
        private val ADD_ICON_TITLES = listOf("adicionar icone", "add icon", "add app icon")
        private const val GOOGLE_TITLE = "google"

        private const val SETTLE = 1_500L
        private const val SECOND = 1_000L
        private const val MOMENT = 500L
        private const val PAGE_SWIPE = 300L
        private const val DRAWER_SWIPE = 300L
        private const val LIST_SWIPE = 500L
    }
}
