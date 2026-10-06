# Builds Midview's icon and the setup's wizard images from the logo
# (midview-logo.jpg: the symbol over the word "midview", on black):
#
#   python assets\gerar-icone.py
#
# Needs Python 3 with Pillow. Writes, next to this script:
#   midview.ico               the programs' and the setup's icon (16 to 256 px)
#   instalador-pequena-*.bmp  the setup's top-right image, one per screen scale
#   instalador-grande-*.bmp   the setup's left-hand image on its first and last pages
#
# The icon is the symbol (no word: unreadable at icon sizes) on a black
# rounded square, as on the logo itself. From 48 px down, where the whole
# network of monitors turns to mush, it is the middle monitor only: that
# covers the title bar and the taskbar at every screen scale up to 200%
# (the taskbar asks for the 40 px one at 125%, the 48 px one at 150%).
from pathlib import Path
import io
import struct

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
LOGO = HERE / "midview-logo.jpg"

# Where things are in midview-logo.jpg (1024 x 572), measured on it:
# x0, y0, x1, y1 (x1 and y1 exclusive).
SYMBOL = (411, 123, 613, 319)        # the whole symbol
MONITOR = (455, 173, 571, 274)       # the middle monitor, with its stand
FULL_LOGO = (301, 123, 723, 423)     # symbol and word

BACKGROUND = (0, 0, 0, 255)          # the logo's own black
TILE_BORDER = (58, 58, 58, 255)      # keeps the square's edge visible on a dark taskbar
WHITE = (255, 255, 255, 255)
WORK = 1024                           # the icon is drawn this big, then scaled down


def tile(art, fill_ratio):
    """art centered on a black rounded square, transparent around it."""
    canvas = Image.new("RGBA", (WORK, WORK), (0, 0, 0, 0))
    radius = int(WORK * 0.20)
    border = max(int(WORK * 0.014), 1)
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle((0, 0, WORK - 1, WORK - 1), radius=radius, fill=TILE_BORDER)
    draw.rounded_rectangle((border, border, WORK - 1 - border, WORK - 1 - border), radius=radius - border, fill=BACKGROUND)
    side = int(WORK * fill_ratio)
    w, h = art.size
    scale = side / max(w, h)
    art = art.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    canvas.paste(art, ((WORK - art.size[0]) // 2, (WORK - art.size[1]) // 2))
    return canvas


def dib(image):
    """One icon image as a 32-bit DIB (what every Windows tool reads)."""
    size = image.size[0]
    rgba = image.convert("RGBA")
    r, g, b, a = rgba.split()
    bgra = Image.merge("RGBA", (b, g, r, a)).transpose(Image.FLIP_TOP_BOTTOM).tobytes()
    mask_row = ((size + 31) // 32) * 4
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    return header + bgra + bytes(mask_row * size)


def png(image):
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def write_ico(path, images):
    """PNG for 256 px (as Windows itself does), DIB for the rest."""
    blobs = [png(image) if image.size[0] == 256 else dib(image) for image in images]
    offset = 6 + 16 * len(images)
    out = struct.pack("<HHH", 0, 1, len(images))
    for image, blob in zip(images, blobs):
        size = image.size[0]
        out += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    path.write_bytes(out + b"".join(blobs))


def flat(image, size, background):
    """For the setup's BMP images, which have no transparency."""
    canvas = Image.new("RGBA", size, background)
    canvas.alpha_composite(image.convert("RGBA"), ((size[0] - image.size[0]) // 2, (size[1] - image.size[1]) // 2))
    return canvas.convert("RGB")


def main():
    logo = Image.open(LOGO).convert("RGB")
    symbol = logo.crop(SYMBOL)
    monitor = logo.crop(MONITOR)

    big_tile = tile(symbol, 0.78)
    small_tile = tile(monitor, 0.84)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    images = [(small_tile if size <= 48 else big_tile).resize((size, size), Image.LANCZOS) for size in sizes]
    write_ico(HERE / "midview.ico", images)

    # Inno Setup picks the best fit for the screen's scale (100% to 200%),
    # and shows the small one on its white top band.
    for size in (55, 69, 83, 110):
        icon = big_tile.resize((round(size * 0.92), round(size * 0.92)), Image.LANCZOS)
        flat(icon, (size, size), WHITE).save(HERE / f"instalador-pequena-{size}.bmp")

    full = logo.crop(FULL_LOGO)
    for width, height in ((164, 314), (328, 628)):
        target = round(width * 0.84)
        art = full.resize((target, round(full.size[1] * target / full.size[0])), Image.LANCZOS)
        flat(art, (width, height), BACKGROUND).save(HERE / f"instalador-grande-{width}.bmp")


if __name__ == "__main__":
    main()
