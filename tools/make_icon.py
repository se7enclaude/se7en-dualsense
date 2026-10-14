"""Icône de l'application : disque encre, losange citron (repris du logo Axis)."""
import sys
from PIL import Image, ImageDraw

out = sys.argv[1]
S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.ellipse((8, 8, S - 8, S - 8), fill=(17, 17, 20, 255))
c, r = S / 2, 62
d.polygon([(c, c - r), (c + r, c), (c, c + r), (c - r, c)], fill=(212, 245, 60, 255))
d.ellipse((c + 52, c + 52, c + 82, c + 82), outline=(91, 45, 224, 255), width=9)
img.save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
