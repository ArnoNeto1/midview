package io.github.arnoneto1.midview.agent.home

// Android's own launcher (Launcher3) as Launcher3Home sees it through the
// accessibility service, a port of the Master's FakeLauncher3
// (tests/Midview.Master.Tests/FakeLauncher3.cs), shaped like the Multilaser
// M10's (Android 10, 800x1232) on 2026-11-13: its settings (rows with a
// title and a switch), home pages of 5x5 cells with the date on the first
// page's top row and a sliver of the next page on the right, the dock growing
// with its apps, the "Remover" target shown only while an item is dragged
// (centred, or on the left beside "Desinstalar"), "Item removido" hiding the
// home until it goes, and the drawer, scrolled two rows at a time. Every
// action is recorded as text ("drag 144 375 400 64", "tap 753 208",
// "swipe ...", "home", "back", "settings", "read").
class FakeLauncher3 : HomeDriver {
    class Item(
        val label: String,
        val widget: Boolean = false,
        val folder: Boolean = false,
        // An app the student installed: "Desinstalar" shows beside "Remover".
        val userApp: Boolean = false,
        // Only "Desinstalar" shows, in the middle (never on the school's tablet).
        val uninstallOnly: Boolean = false,
    )

    private enum class View { HOME, SETTINGS, DRAWER, UNINSTALL }

    var startFails = false
    var readsFail = false
    var hasGoogleRow = true
    var switchesStuck = false
    var addIcon = true
    var googleApp = true
    var pages: MutableList<MutableList<Item>> = mutableListOf(mutableListOf())
    var dock: MutableList<String> = mutableListOf()
    var drawerApps: List<String> = listOf("Calculadora", "Câmera", "Chrome", "Configurações", "Contatos", "Files", "Fotos", "Play Store", "Relógio")
    val launchable: MutableSet<String> = Launcher3Home.DOCK_APPS.map { it.pkg }.toMutableSet()
    val commands = mutableListOf<String>()

    // The apps the uninstall question was asked about (never answered).
    val uninstallAsked = mutableListOf<String>()
    var elapsed = 0L

    // What one screen read costs (none: only the pauses count, as in the Master's tests).
    var readMillis = 0L

    private var view = View.HOME
    private var page = 0
    private var scroll = 0
    private var removedNote = false

    val empty: Boolean get() = pages.all { it.isEmpty() }

    private class B(val l: Int, val t: Int, val r: Int, val b: Int) {
        fun has(x: Int, y: Int) = x in l..r && y in t..b
    }

    override fun screen(): HomeNode? {
        commands += "read"
        elapsed += readMillis
        return if (readsFail) null else dump()
    }

    override fun home() {
        commands += "home"
        view = View.HOME
        page = 0
    }

    override fun back() {
        commands += "back"
        if (removedNote) removedNote = false else if (view != View.HOME) view = View.HOME
    }

    override fun tap(x: Int, y: Int) {
        commands += "tap $x $y"
        if (view != View.SETTINGS || switchesStuck) return
        if (B(730, 184, 776, 211).has(x, y)) {
            addIcon = !addIcon
        } else if (hasGoogleRow && B(730, 257, 776, 284).has(x, y)) {
            googleApp = !googleApp
        }
    }

    override fun swipe(x1: Int, y1: Int, x2: Int, y2: Int, millis: Long) {
        commands += "swipe $x1 $y1 $x2 $y2 $millis"
        if (view == View.HOME && !removedNote && y1 - y2 > 300) {
            view = View.DRAWER
            scroll = 0
        } else if (view == View.HOME && !removedNote && x1 - x2 > 300) {
            page = minOf(page + 1, pages.size - 1)
        } else if (view == View.DRAWER && y1 - y2 > 300) {
            scroll = minOf(scroll + 2, maxOf(0, (drawerApps.size + 4) / 5 - 6))
        }
    }

    override fun drag(x1: Int, y1: Int, x2: Int, y2: Int) {
        commands += "drag $x1 $y1 $x2 $y2"
        if (view == View.DRAWER) {
            val app = drawerApps.indices.firstOrNull { appCell(it).has(x1, y1) && APP_LIST.has(x1, y1) }
            view = View.HOME
            if (app != null && HOTSEAT.has(x2, y2) && dock.size < 5) dock.add(drawerApps[app])
            return
        }
        if (view != View.HOME || removedNote) return
        val items = pages[page]
        val cell = items.indices.firstOrNull { cell(page, it).has(x1, y1) }
        val docked = dock.indices.firstOrNull { dockCell(it).has(x1, y1) }
        val item = when {
            cell != null -> items[cell]
            docked != null -> Item(dock[docked])
            else -> null
        }
        if (item == null || y2 > 110) return
        // "Remover" and "Desinstalar" as the M10 lays them out: about 100 px wide each.
        val remove = if (item.uninstallOnly) null else if (item.userApp) 150..250 else 350..450
        val uninstall = if (item.uninstallOnly) 350..450 else if (item.userApp) 550..650 else null
        if (remove != null && x2 in remove) {
            if (cell != null) items.removeAt(cell) else dock.removeAt(docked!!)
            removedNote = true
        } else if (uninstall != null && x2 in uninstall) {
            uninstallAsked += item.label
            view = View.UNINSTALL
        }
    }

    override fun openLauncherSettings(): Boolean {
        commands += "settings"
        if (startFails) return false
        view = View.SETTINGS
        return true
    }

    override fun isLaunchable(pkg: String) = pkg in launchable

    override fun pause(millis: Long) {
        elapsed += millis
    }

    override fun elapsedMillis() = elapsed

    private fun node(
        id: String,
        cls: String,
        b: B,
        desc: String = "",
        text: String = "",
        checked: Boolean = false,
        children: List<HomeNode> = emptyList(),
        pkg: String = Launcher3Home.LAUNCHER,
    ) = HomeNode(id, cls, pkg, text, desc, checked, b.l, b.t, b.r, b.b, children)

    private fun l(id: String) = "${Launcher3Home.LAUNCHER}:id/$id"

    private fun dump(): HomeNode {
        val body: List<HomeNode> = when {
            view == View.SETTINGS -> listOf(
                node(
                    l("recycler_view"), "androidx.recyclerview.widget.RecyclerView", B(0, 88, 800, 1232),
                    children = listOfNotNull(
                        row(88, "Pontos de notificação", null),
                        row(161, "Adicionar ícone à tela inicial", addIcon),
                        if (hasGoogleRow) row(234, "Mostrar Google App", googleApp) else null,
                        row(307, "Ecrã inicial contínuo", true),
                    ),
                ),
            )
            view == View.UNINSTALL -> listOf(
                node(
                    "android:id/message", "android.widget.TextView", B(40, 500, 760, 600),
                    text = "Deseja desinstalar este app?", pkg = INSTALLER,
                ),
            )
            view == View.DRAWER -> listOf(
                node(
                    l("apps_view"), "android.widget.RelativeLayout", B(0, 0, WIDTH, HEIGHT),
                    children = listOf(
                        node(
                            l("apps_list_view"), "androidx.recyclerview.widget.RecyclerView", APP_LIST,
                            children = drawerApps.indices
                                .filter { appCell(it).t < APP_LIST.b && appCell(it).b > APP_LIST.t }
                                .map { node(l("icon"), "android.widget.TextView", appCell(it), drawerApps[it], drawerApps[it]) },
                        ),
                    ),
                ),
            )
            removedNote -> listOf(
                node(l("label"), "android.widget.TextView", B(80, 1162, 647, 1194), text = "Item removido"),
                node(l("action"), "android.widget.TextView", B(647, 1162, 720, 1194), text = "Desfazer"),
            )
            else -> homeDump()
        }
        val uninstall = view == View.UNINSTALL
        val inner = if (uninstall) body else listOf(node(l("drag_layer"), "android.widget.FrameLayout", B(0, 0, WIDTH, HEIGHT), children = body))
        return node("", "android.widget.FrameLayout", B(0, 0, WIDTH, HEIGHT), children = inner, pkg = if (uninstall) INSTALLER else Launcher3Home.LAUNCHER)
    }

    private fun row(top: Int, title: String, on: Boolean?): HomeNode =
        node(
            "", "android.widget.LinearLayout", B(0, top, 800, top + 73),
            children = listOfNotNull(
                node(
                    "", "android.widget.RelativeLayout", B(80, top, 714, top + 73),
                    children = listOf(node("android:id/title", "android.widget.TextView", B(80, top + 16, 300, top + 38), text = title)),
                ),
                on?.let {
                    node(
                        "android:id/widget_frame", "android.widget.LinearLayout", B(714, top, 776, top + 73),
                        children = listOf(
                            node(
                                "android:id/switch_widget", "android.widget.Switch", B(730, top + 23, 776, top + 50),
                                text = if (it) "ATIVADO" else "DESATIVADO", checked = it,
                            ),
                        ),
                    )
                },
            ),
        )

    private fun homeDump(): List<HomeNode> {
        val items = pages[page]
        var cells = items.mapIndexed { i, item ->
            when {
                item.widget -> node(
                    "", "com.android.launcher3.widget.LauncherAppWidgetHostView", cell(page, i), item.label,
                    children = listOf(node("com.android.chrome:id/dino", "android.widget.FrameLayout", cell(page, i), pkg = "com.android.chrome")),
                )
                item.folder -> node(
                    "", "android.widget.FrameLayout", cell(page, i), "Pasta: ${item.label}",
                    children = listOf(node(l("folder_icon_name"), "android.widget.TextView", cell(page, i))),
                )
                else -> node("", "android.widget.TextView", cell(page, i), item.label, item.label)
            }
        }
        if (page == 0) {
            val date = node(l("date_text"), "android.widget.TextView", B(298, 197, 501, 225), "sexta-feira, nov 13", "sexta-feira, nov 13")
            val smartspace = node(l("smartspace_content"), "android.widget.FrameLayout", B(80, 129, 720, 293), children = listOf(date))
            cells = listOf(node(l("search_container_workspace"), "android.widget.FrameLayout", B(80, 129, 720, 293), children = listOf(smartspace))) + cells
        }
        val workspacePages = listOfNotNull(
            if (page > 0) node("", "android.view.ViewGroup", B(0, 129, 48, 950)) else null,
            node(
                "", "android.view.ViewGroup", B(56, 129, 744, 950),
                children = listOf(node("", "android.view.ViewGroup", B(80, 129, 720, 950), children = cells)),
            ),
            if (page < pages.size - 1) node("", "android.view.ViewGroup", B(752, 129, 800, 950)) else null,
        )
        return listOf(
            node(l("workspace"), "android.widget.ScrollView", B(0, 0, WIDTH, HEIGHT), children = workspacePages),
            node(l("page_indicator"), "android.view.View", B(0, 1047, 800, 1071)),
            node(
                l("hotseat"), "android.view.ViewGroup", HOTSEAT,
                children = listOf(
                    node(
                        "", "android.view.ViewGroup", B(80, 1071, 720, 1160),
                        children = dock.mapIndexed { i, label -> node("", "android.widget.TextView", dockCell(i), label, label) },
                    ),
                ),
            ),
            node(
                l("apps_view"), "android.widget.RelativeLayout", B(0, 1128, 800, 1232),
                children = listOf(node(l("btn_qsb_search"), "android.widget.TextView", B(88, 1180, 712, 1228), text = "Pesquisar")),
            ),
        )
    }

    // The first page's top row holds the date.
    private fun cell(page: Int, i: Int): B {
        val at = if (page == 0) i + 5 else i
        return B(80 + 128 * (at % 5), 129 + 164 * (at / 5), 80 + 128 * (at % 5 + 1), 129 + 164 * (at / 5 + 1))
    }

    private fun dockCell(i: Int) = B(80 + 128 * i, 1071, 208 + 128 * i, 1160)

    private fun appCell(i: Int) =
        B(32 + 147 * (i % 5), 106 + 164 * (i / 5 - scroll), 32 + 147 * (i % 5 + 1), 106 + 164 * (i / 5 - scroll + 1))

    companion object {
        val CLASS_APPS = listOf("Chrome", "Câmera", "Fotos", "Files", "Calculadora")
        private const val WIDTH = 800
        private const val HEIGHT = 1232
        private const val INSTALLER = "com.google.android.packageinstaller"
        private val HOTSEAT = B(0, 1071, 800, 1232)
        private val APP_LIST = B(0, 76, 800, 1232)
    }
}
