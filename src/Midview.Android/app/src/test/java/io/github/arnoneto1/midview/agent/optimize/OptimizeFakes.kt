package io.github.arnoneto1.midview.agent.optimize

// The phone's network as a script: whether the internet check answers,
// whether the DNS host takes a TLS connection (Android's own check) and
// whether the network validates the private DNS; every call recorded.
class FakeDnsNetwork(
    var internet: Boolean = true,
    var serves: Boolean = true,
    var validates: Boolean = true,
) : DnsNetwork {
    val calls = mutableListOf<String>()
    var failCheck: RuntimeException? = null

    override fun internetOk(): Boolean {
        calls += "check"
        failCheck?.let { throw it }
        return internet
    }

    override fun hostServes(host: String): Boolean {
        calls += "serves $host"
        return serves
    }

    override fun awaitPrivateDns(host: String, timeoutMillis: Long): Boolean {
        calls += "await $host ${timeoutMillis}ms"
        return validates
    }
}

// The phone's apps for the optimizer: those on the phone (hidden or not)
// and those never hidden.
class FakeBloatCatalog(
    var present: Set<String>,
    var neverHide: Set<String> = BloatList.NEVER_HIDE,
) : BloatCatalog {
    override fun isPresent(pkg: String) = pkg in present
    override fun neverHide() = neverHide
}
