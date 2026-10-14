"""Prépare un rendu Blender (PNG transparent) pour l'application.

Recadre autour de la manette au format 505:386 (repère des calques animés), puis exporte
en 3× (1515 × 1158) pour rester net à l'écran. Extrait aussi les chapeaux de sticks.

Usage : python prepare_render.py rendu.png dossier_sortie
"""
import sys, os
import numpy as np
from PIL import Image

src, outdir = sys.argv[1], sys.argv[2]
REF_W, REF_H, SCALE = 505, 386, 3

im = Image.open(src).convert("RGBA")
a = np.asarray(im)[..., 3]
ys, xs = np.where(a > 8)
x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()

# Marge identique à l'ancienne photo (~6 % de la largeur), puis ajustement au ratio cible
cw = (x1 - x0) * 1.12
ch = cw * REF_H / REF_W
cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
box = (int(round(cx - cw / 2)), int(round(cy - ch / 2)), int(round(cx + cw / 2)), int(round(cy + ch / 2)))

canvas = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
canvas.paste(im.crop((max(0, box[0]), max(0, box[1]), min(im.width, box[2]), min(im.height, box[3]))),
             (max(0, -box[0]), max(0, -box[1])))
out = canvas.resize((REF_W * SCALE, REF_H * SCALE), Image.LANCZOS)
out.save(os.path.join(outdir, "dualsense.png"), optimize=True)
print("crop", box, "->", out.size)

# Chapeaux de sticks (centres et rayon dans le repère 505 × 386, relevés sur le rendu)
STICKS = {"stick-left.png": (175.0, 193.0), "stick-right.png": (326.0, 193.0)}
R = 28
px = np.asarray(out).astype(np.float32)
for name, (cx, cy) in STICKS.items():
    x0, y0 = int(round((cx - R) * SCALE)), int(round((cy - R) * SCALE))
    size = 2 * R * SCALE
    crop = px[y0:y0 + size, x0:x0 + size].copy()
    yy, xx = np.mgrid[0:size, 0:size]
    d = np.hypot(xx + 0.5 - (cx * SCALE - x0), yy + 0.5 - (cy * SCALE - y0))
    crop[..., 3] *= np.clip((R * SCALE - d) / (1.5 * SCALE), 0, 1)
    Image.fromarray(crop.clip(0, 255).astype(np.uint8), "RGBA").save(os.path.join(outdir, name))
    print(name, "origine", round(cx - R, 1), round(cy - R, 1), "taille", 2 * R)
