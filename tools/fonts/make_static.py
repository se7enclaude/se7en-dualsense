"""Fige une police variable (axe wght) en TTF statique lisible par WPF.

Usage : python make_static.py source.ttf sortie.ttf "Nom de famille" poids
"""
import sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

src, out, family, weight = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
t = TTFont(src)
if "fvar" in t:
    t = instancer.instantiateVariableFont(t, {"wght": weight})

name = t["name"]
copyright_ = name.getDebugName(0)
for nid in (1, 2, 4, 6, 16, 17, 21, 22, 25):
    name.removeNames(nameID=nid)
sub = "Regular"
for nid, val in ((1, family), (2, sub), (4, f"{family} {sub}"), (6, family.replace(" ", "") + "-" + sub)):
    name.setName(val, nid, 3, 1, 0x409)
# Une seule graisse par famille : déclarée « Regular » pour que WPF la sélectionne sans synthèse
os2 = t["OS/2"]
os2.usWeightClass = 400
os2.fsSelection = (os2.fsSelection & ~(0x01 | 0x20)) | 0x40
t["head"].macStyle &= ~0x01
if "STAT" in t:
    del t["STAT"]
t.save(out)
print("ok", out, "|", copyright_)
