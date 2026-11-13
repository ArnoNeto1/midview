package io.github.arnoneto1.midview.agent.optimize

// The phone's network as a script: whether the internet check answers,
// whether the DNS host answers on its first address (Android's own check)
// and on its IPv4 addresses, and whether the network validates the private
// DNS; every call recorded.
class FakeDnsNetwork(
    var internet: Boolean = true,
    var firstAddress: Boolean = true,
    var ipv4: Boolean = true,
    var validates: Boolean = true,
) : DnsNetwork {
    val calls = mutableListOf<String>()
    var failCheck: RuntimeException? = null

    override fun internetOk(): Boolean {
        calls += "check"
        failCheck?.let { throw it }
        return internet
    }

    override fun checkHost(host: String): HostCheck {
        calls += "check $host"
        return HostCheck(firstAddress, ipv4)
    }

    override fun awaitPrivateDns(host: String, timeoutMillis: Long): Boolean {
        calls += "await $host ${timeoutMillis}ms"
        return validates
    }
}

// The phone's apps for the optimizer: those on the phone (hidden or not),
// those never hidden and those that came with the phone's system.
class FakeBloatCatalog(
    var present: Set<String>,
    var neverHide: Set<String> = BloatList.NEVER_HIDE,
    var system: Set<String> = emptySet(),
) : BloatCatalog {
    override fun isPresent(pkg: String) = pkg in present
    override fun neverHide() = neverHide
    override fun cameWithPhone(pkg: String) = pkg in system
}

// The Device Owner's uninstall: an app uninstalled leaves the catalog; one
// in refuse is refused with its reason, one in noAnswer gets no answer in
// time, one in throwing throws. before runs ahead of each (a clock moved).
class FakeRemover(private val catalog: FakeBloatCatalog) : AppRemover {
    val calls = mutableListOf<Pair<String, Long>>()
    var refuse = emptyMap<String, String>()
    var noAnswer = emptySet<String>()
    var throwing = emptySet<String>()
    var before: (String) -> Unit = {}

    override fun uninstall(pkg: String, waitMillis: Long): String? {
        calls += pkg to waitMillis
        before(pkg)
        if (pkg in throwing) throw SecurityException("no")
        if (pkg in noAnswer) return AppRemover.NO_ANSWER
        refuse[pkg]?.let { return it }
        catalog.present -= pkg
        return null
    }
}
