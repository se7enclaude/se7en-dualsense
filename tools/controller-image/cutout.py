"""Détoure la photo de la DualSense (fond blanc) en PNG transparent.

1. Silhouette = contours (gradient) + zones sombres, fermés puis remplis -> masque opaque.
2. Hors silhouette : noir semi-transparent (alpha = 1 - luminance) pour garder l'ombre
   portée en « multiply » sur n'importe quel fond clair.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

src, out, preview, maskdbg = sys.argv[1:5]
pil = Image.open(src).convert("RGB")
im = np.asarray(pil).astype(np.float32)
lum = im.mean(axis=2)
sat = im.max(axis=2) - im.min(axis=2)

# Fond de référence (coins)
bgval = np.median(np.concatenate([lum[:20, :20].ravel(), lum[:20, -20:].ravel()]))

# Contours : gradient de luminance
gx = ndimage.sobel(ndimage.gaussian_filter(lum, 0.8), axis=1)
gy = ndimage.sobel(ndimage.gaussian_filter(lum, 0.8), axis=0)
grad = np.hypot(gx, gy)

seed = (grad > 26) | (lum < bgval - 60) | (sat > 25)
seed = ndimage.binary_closing(seed, structure=np.ones((3, 3)), iterations=4)
filled = ndimage.binary_fill_holes(seed)
filled = ndimage.binary_opening(filled, structure=np.ones((3, 3)), iterations=2)

labels, n = ndimage.label(filled)
sizes = ndimage.sum(filled, labels, range(1, n + 1))
mask = labels == (np.argmax(sizes) + 1)
mask = ndimage.binary_fill_holes(mask)

# Épluchage : retirer depuis l'extérieur les pixels de bord qui sont du fond (blanc pur) ou de l'ombre douce
outside_like = (lum > bgval - 6) | ((lum > bgval - 45) & (grad < 10) & (sat < 10))
for _ in range(25):
    boundary = mask & ~ndimage.binary_erosion(mask)
    peel = boundary & outside_like
    if not peel.any():
        break
    mask &= ~peel
# Bas des poignées : l'ombre au sol (grise, plate) a pu être englobée -> on la retire
ys_all = np.where(mask)[0]
low = np.zeros_like(mask)
low[ys_all.max() - 30:, :] = True
mask &= ~(low & (lum > 200) & (sat < 12) & (grad < 30))
mask = ndimage.binary_opening(mask, structure=np.ones((3, 3)), iterations=1)
labels, n = ndimage.label(mask)
sizes = ndimage.sum(mask, labels, range(1, n + 1))
mask = labels == (np.argmax(sizes) + 1)
Image.fromarray((mask * 255).astype(np.uint8)).save(maskdbg)

# Masque adouci (anti-crénelage) puis légère érosion pour ne pas garder de liseré blanc
m = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.9))
ma = np.asarray(m).astype(np.float32) / 255.0

shadow_a = np.clip((bgval - lum) / bgval, 0, 1) * 1.15
shadow_a = np.zeros_like(lum)  # ombre retirée : l'application dessine sa propre ombre animée

alpha = ma + (1 - ma) * shadow_a
rgb = im * (ma / np.maximum(alpha, 1e-4))[..., None]

rgba = np.dstack([rgb, alpha * 255]).clip(0, 255).astype(np.uint8)
img = Image.fromarray(rgba, "RGBA")

ys, xs = np.where(mask)
pad = 30
box = (max(0, xs.min() - pad), max(0, ys.min() - pad), min(img.width, xs.max() + pad), min(img.height, ys.max() + pad + 20))
img = img.crop(box)
img.save(out)
print("box", box, "size", img.size, "bg", bgval)

bg = Image.new("RGBA", img.size, (0xE6, 0xE9, 0xF0, 255))
bg.alpha_composite(img)
dark = Image.new("RGBA", img.size, (0x17, 0x19, 0x1F, 255))
dark.alpha_composite(img)
both = Image.new("RGB", (img.width * 2, img.height))
both.paste(bg.convert("RGB"), (0, 0))
both.paste(dark.convert("RGB"), (img.width, 0))
both.resize((both.width * 2, both.height * 2)).save(preview)


