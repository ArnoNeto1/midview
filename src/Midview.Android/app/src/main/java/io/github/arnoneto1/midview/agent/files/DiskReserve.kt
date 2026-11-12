package io.github.arnoneto1.midview.agent.files

// The room every receive leaves free on the phone ("Enviar arquivo", the
// class folder, "Instalar programa", "Atualizar celulares"): with the
// storage full nothing could be written, not even a block the teacher sends
// (it would be refused with "sem espaço"), so what the teacher sends is
// refused first, with the reason, while SPARE_BYTES would not be left.
object DiskReserve {
    const val SPARE_BYTES = 100L * 1024 * 1024

    const val NO_SPACE = "sem espaço no celular: é preciso deixar pelo menos 100 MB livres"

    // Whether size more bytes fit and leave the spare room (no overflow).
    fun fits(freeBytes: Long, size: Long): Boolean =
        size >= 0 && freeBytes - SPARE_BYTES >= size

    // Null when size fits, otherwise the teacher's reason.
    fun refusal(freeBytes: Long, size: Long): String? = if (fits(freeBytes, size)) null else NO_SPACE
}
