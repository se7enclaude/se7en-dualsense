"""Extrait les chapeaux de sticks de la photo détourée (disques à bord doux) pour les animer."""
import sys, os
import numpy as np
from PIL import Image

src, outdir = sys.argv[1], sys.argv[2]
img = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32)
R = 29  # rayon extrait (chapeau + un peu d'anneau)

for name, (cx, cy) in {"stick-left.png": (181.7, 175.3), "stick-right.png": (324.2, 175.3)}.items():
    x0, y0 = int(round(cx - R)), int(round(cy - R))
    size = 2 * R
    crop = img[y0:y0 + size, x0:x0 + size].copy()
    yy, xx = np.mgrid[0:size, 0:size]
    d = np.hypot(xx + 0.5 - (cx - x0), yy + 0.5 - (cy - y0))
    a = np.clip((R - d) / 1.5, 0, 1)
    crop[..., 3] = crop[..., 3] * a
    Image.fromarray(crop.clip(0, 255).astype(np.uint8), "RGBA").save(os.path.join(outdir, name))
    print(name, x0, y0, size)
