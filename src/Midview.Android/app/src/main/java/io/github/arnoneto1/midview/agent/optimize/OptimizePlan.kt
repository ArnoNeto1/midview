package io.github.arnoneto1.midview.agent.optimize

// What "Otimizar" does to the device's apps: those to hide and those to
// uninstall. BloatList's are hidden; UnusedApps' and the cable's lists for
// the device's kind (CableApps) are uninstalled, but those that came with
// the system, which a Device Owner cannot uninstall, are hidden too. Only
// what the device has, never one it never hides (BloatList.neverHide), each
// list's own kept apps aside, each app once.
class OptimizePlan(val hide: List<String>, val uninstall: List<String>) {
    companion object {
        // cameWithPhone: a system app or an update of one (true when it
        // cannot be told, so it is never uninstalled).
        fun of(catalog: BloatCatalog, tablet: Boolean, cameWithPhone: (String) -> Boolean): OptimizePlan {
            val never = catalog.neverHide()
            val bloat = BloatList.toHide(BloatList.PACKAGES.filter(catalog::isPresent).toSet(), never).toList()
            val unused = UnusedApps.toRemove(UnusedApps.PACKAGES.filter(catalog::isPresent).toSet(), never)
            val cable = CableApps.toRemove(CableApps.listed(tablet).filter(catalog::isPresent).toSet(), never, tablet)
            val (system, other) = (unused + cable).distinct().filter { it !in bloat }.partition(cameWithPhone)
            return OptimizePlan(bloat + system, other)
        }
    }
}
