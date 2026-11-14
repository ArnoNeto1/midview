package io.github.arnoneto1.midview.agent.wallpaper

import java.io.File
import java.io.IOException

// The screens carrying the teacher's picture (TeacherWallpaper), in the
// app's private storage so a restart keeps them: one number, PhoneWallpaper's
// bits (1 home, 2 lock); no file, none.
class TeacherWallpaperScreens(private val file: File) : TeacherWallpaper.TeacherScreens {
    override fun read(): Int = try {
        if (file.isFile) file.readText().trim().toIntOrNull() ?: 0 else 0
    } catch (e: IOException) {
        0
    }

    override fun write(screens: Int) {
        try {
            if (screens == 0) file.delete() else file.writeText(screens.toString())
        } catch (e: IOException) {
            // Kept in the next change; the picker is only a convenience.
        }
    }
}
