"""Logo de se7en DualSense : tuile encre, « 7 » citron (police Syne du logo), anneau violet.
Produit l'icône Windows (.ico multi-tailles) et le logo PNG.

Usage : python tools/make_icon.py src/Se7en.DualSense.App/Assets
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

INK_TOP, INK = (38, 38, 46), (17, 17, 20)
VIOLET, LIME = (91, 45, 224), (212, 245, 60)


def logo(size: int, font_path: str) -> Image.Image:
    S = size * 4  # suréchantillonnage pour des bords nets
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.23), fill=255)

    # Fond : dégradé vertical encre + reflet doux en haut
    bg = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(bg)
    for y in range(S):
        t = y / S
        c = tuple(int(INK_TOP[i] + (INK[i] - INK_TOP[i]) * min(1, t * 1.4)) for i in range(3))
        glow = int(22 * max(0, 1 - t / 0.45) ** 2)
        d.line([(0, y), (S, y)], fill=tuple(min(255, v + glow) for v in c) + (255,))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(bg, (0, 0), mask)

    # « 7 »
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(font_path, int(S * 0.72))
    bb = d.textbbox((0, 0), "7", font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    d.text(((S - w) / 2 - bb[0] - S * 0.02, (S - h) / 2 - bb[1]), "7", font=f, fill=LIME + (255,))

    # Anneau violet (signature DualSense de l'application)
    r, cx, cy = S * 0.058, S * 0.775, S * 0.765
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=VIOLET + (255,), width=int(S * 0.03))

    # Liseré intérieur discret
    d.rounded_rectangle((2, 2, S - 3, S - 3), radius=int(S * 0.23), outline=(255, 255, 255, 22), width=max(2, S // 160))
    return img.resize((size, size), Image.LANCZOS)


def main():
    out = Path(sys.argv[1])
    font = str(out / "Fonts" / "Syne-Brand.ttf")
    big = logo(1024, font)
    big.save(out / "logo.png")
    logo(256, font).save(out / "app.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("ok")


if __name__ == "__main__":
    main()
