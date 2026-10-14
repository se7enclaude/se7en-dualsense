"""Logo de se7en DualSense, dans la famille des applis se7en (tuile pleine, gros « 7 ») :
tuile citron, « 7 » épais encre dessiné à la main, anneau violet (grandes tailles seulement).
Produit l'icône Windows (.ico multi-tailles) et le logo PNG.

Usage : python tools/make_icon.py src/Se7en.DualSense.App/Assets
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

LIME_TOP, LIME = (226, 250, 100), (212, 245, 60)
INK, VIOLET = (17, 17, 20), (91, 45, 224)


def seven(d: ImageDraw.ImageDraw, S: int, fill):
    """« 7 » épais : barre du haut + jambage oblique (proche de celui de se7en Download)."""
    top, bar, x0, x1, leg, bottom = 0.25 * S, 0.17 * S, 0.24 * S, 0.78 * S, 0.20 * S, 0.79 * S
    d.polygon([(x0, top), (x1, top), (x1, top + bar * 0.95), (0.52 * S, bottom), (0.52 * S - leg, bottom),
               (x1 - leg * 1.08, top + bar), (x0, top + bar)], fill=fill)


def logo(size: int, ring: bool = True) -> Image.Image:
    S = size * 4  # suréchantillonnage pour des bords nets
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.17), fill=255)
    bg = Image.new("RGBA", (S, S))
    g = ImageDraw.Draw(bg)
    for y in range(S):
        t = y / S
        g.line([(0, y), (S, y)], fill=tuple(int(LIME_TOP[i] + (LIME[i] - LIME_TOP[i]) * t) for i in range(3)) + (255,))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(bg, (0, 0), mask)
    d = ImageDraw.Draw(img)
    seven(d, S, INK + (255,))
    if ring:
        r, cx, cy = S * 0.055, S * 0.80, S * 0.80
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=VIOLET + (255,), width=int(S * 0.028))
    return img.resize((size, size), Image.LANCZOS)


def main():
    out = Path(sys.argv[1])
    logo(1024).save(out / "logo.png")
    sizes = [16, 20, 24, 32, 48, 64, 128, 256]
    # Chaque taille est dessinée à part : l'anneau disparaît sous 64 px, où il deviendrait un point flou
    frames = [logo(s, ring=s >= 64) for s in sizes]
    frames[-1].save(out / "app.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    print("ok")


if __name__ == "__main__":
    main()
