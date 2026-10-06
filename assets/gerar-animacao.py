# Builds the animated Midview logo from the logo image (midview-logo.jpg):
#
#   python assets\gerar-animacao.py
#
# Needs Python 3 with Pillow and NumPy. Writes, next to this script:
#   midview-animado.gif, midview-animado.webp   to share (6.2 s, looping)
#   animacao\*.png                               the logo's pieces, for the
#                                                teacher program's opening
#                                                (SplashWindow), all the same
#                                                size and position
#
# The pieces are the logo's own shapes, found by connected areas: the middle
# monitor, its eye, the inner and outer signal waves, the network (lines
# and corner monitors) and the word. The animation: the monitor grows in,
# the eye lights up, the network spreads from the middle to the corners,
# the waves go out (inner, then outer), the word rises in; then the signal
# pulses twice and everything fades, to loop. SplashWindow plays the first
# part with the same timings.
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
LOGO = HERE / "midview-logo.jpg"
PIECES = HERE / "animacao"
FPS = 25
CROP = (250, 85, 774, 460)       # the logo with a black margin: x0, y0, x1, y1
CENTER = (512, 213)              # the eye, in the logo image (262, 128 in the pieces)


def label_shapes(ink):
    """Connected areas of ink (8-neighbours), numbered from 1."""
    height, width = ink.shape
    labels = np.zeros((height, width), int)
    count = 0
    for y0, x0 in zip(*np.nonzero(ink)):
        if labels[y0, x0]:
            continue
        count += 1
        labels[y0, x0] = count
        queue = deque([(y0, x0)])
        while queue:
            y, x = queue.popleft()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < height and 0 <= xx < width and ink[yy, xx] and not labels[yy, xx]:
                        labels[yy, xx] = count
                        queue.append((yy, xx))
    return labels


def pieces(image):
    """The logo's pieces as masks over the whole image, by where they are."""
    value = image.max(axis=2)
    labels = label_shapes(value > 70)
    # Edge pixels (dim, anti-aliased) join the shape next to them.
    for _ in range(3):
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(labels, dy, axis=0), dx, axis=1)
            take = (labels == 0) & (shifted > 0) & (value > 8)
            labels[take] = shifted[take]

    found = {name: np.zeros(value.shape, bool) for name in ("monitor", "olho", "ondas-dentro", "ondas-fora", "rede", "palavra")}
    cx, cy = CENTER
    for number in range(1, labels.max() + 1):
        ys, xs = np.nonzero(labels == number)
        if len(ys) < 15:
            continue
        x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
        width, height = x1 - x0, y1 - y0
        middle_x = (x0 + x1) / 2
        if y0 >= 330:
            name = "palavra"
        elif width > 100 and height > 80 and x0 > 440 and x1 < 590:
            name = "monitor"
        elif abs(middle_x - cx) < 12 and width < 30 and height < 30:
            name = "olho"
        # The waves' middles are about 18 px (inner) and 28 px (outer) from the eye.
        elif height > 20 and width < 20 and abs(middle_x - cx) < 23:
            name = "ondas-dentro"
        elif height > 20 and width < 20 and abs(middle_x - cx) < 35:
            name = "ondas-fora"
        else:
            name = "rede"
        found[name] |= labels == number
    return found


def ease(t):
    t = min(max(t, 0.0), 1.0)
    return 1 - (1 - t) ** 3


def window(t, start, end):
    return ease((t - start) / (end - start))


def pulse(t, start, length=0.5):
    """1, dipping smoothly to 0.25 and back during [start, start + length]."""
    if not start <= t <= start + length:
        return 1.0
    return 1 - 0.75 * np.sin(np.pi * (t - start) / length)


def scaled(layer, scale):
    """An RGB layer scaled about the middle monitor's screen."""
    if scale >= 0.999:
        return layer
    picture = Image.fromarray(layer.clip(0, 255).astype(np.uint8))
    width, height = picture.size
    small = picture.resize((max(1, int(width * scale)), max(1, int(height * scale))), Image.LANCZOS)
    out = Image.new("RGB", (width, height))
    cx, cy = CENTER
    out.paste(small, (int(cx - cx * scale), int(cy - cy * scale)))
    return np.asarray(out).astype(float)


def main():
    image = np.asarray(Image.open(LOGO).convert("RGB")).astype(float)
    masks = pieces(image)
    layer = {name: image * mask[..., None] for name, mask in masks.items()}
    height, width = masks["rede"].shape
    ys, xs = np.mgrid[0:height, 0:width]
    distance = np.hypot(xs - CENTER[0], ys - CENTER[1])
    far = distance[masks["rede"]].max()

    def frame(t):
        out = np.zeros_like(image)
        grow = window(t, 0.0, 0.55)
        out += scaled(layer["monitor"], 0.6 + 0.4 * grow) * grow
        light = window(t, 0.4, 0.75)
        out += scaled(layer["olho"], 0.3 + 0.7 * light) * light
        radius = window(t, 0.6, 1.6) * (far + 20)
        out += layer["rede"] * np.clip((radius - distance) / 18, 0, 1)[..., None]
        out += layer["ondas-dentro"] * window(t, 1.35, 1.65) * min(pulse(t, 3.2), pulse(t, 4.4))
        out += layer["ondas-fora"] * window(t, 1.6, 1.9) * min(pulse(t, 3.35), pulse(t, 4.55))
        rise = window(t, 2.0, 2.7)
        # Rolled down a little, so it rises into place.
        out += np.roll(layer["palavra"], int(round(18 * (1 - rise))), axis=0) * rise
        out *= 1 - window(t, 5.5, 6.0)
        x0, y0, x1, y1 = CROP
        return Image.fromarray(out[y0:y1, x0:x1].clip(0, 255).astype(np.uint8))

    frames = [frame(i / FPS) for i in range(int(6.2 * FPS))]

    # One palette for every frame (the finished logo at several
    # brightnesses, for the fades), so colors don't flicker.
    finished = np.asarray(frame(3.0)).astype(float)
    strip = Image.new("RGB", (frames[0].size[0], frames[0].size[1] * 4))
    for i, k in enumerate((1.0, 0.7, 0.45, 0.22)):
        strip.paste(Image.fromarray((finished * k).astype(np.uint8)), (0, i * frames[0].size[1]))
    palette = strip.quantize(256, method=Image.Quantize.MEDIANCUT)
    gif = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    durations = [1000 // FPS] * len(frames)
    gif[0].save(HERE / "midview-animado.gif", save_all=True, append_images=gif[1:], duration=durations, loop=0, optimize=True)
    frames[0].save(HERE / "midview-animado.webp", save_all=True, append_images=frames[1:], duration=durations, loop=0, quality=92, method=6)

    # The pieces, transparent: on black they give back the logo exactly
    # (each pixel's brightness becomes its opacity).
    PIECES.mkdir(exist_ok=True)
    x0, y0, x1, y1 = CROP
    for name, piece in layer.items():
        rgb = piece[y0:y1, x0:x1] / 255.0
        alpha = rgb.max(axis=2)
        color = rgb / np.where(alpha > 0, alpha, 1)[..., None]
        rgba = np.dstack([color, alpha]) * 255
        Image.fromarray(rgba.round().astype(np.uint8), "RGBA").save(PIECES / f"{name}.png")


if __name__ == "__main__":
    main()
