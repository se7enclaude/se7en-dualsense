import sys, os
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

src, out = sys.argv[1], sys.argv[2]
NAMES = {400: "Regular", 500: "Medium", 600: "SemiBold", 700: "Bold"}


def set_names(t, family, w):
    sub = NAMES[w]
    name = t["name"]
    for nid in (1, 2, 4, 6, 16, 17, 21, 22, 25):
        name.removeNames(nameID=nid)
    ps = family.replace(" ", "") + "-" + sub
    # Nommage « legacy » RIBBI : Regular/Bold partagent la famille ; Medium/SemiBold ont leur propre famille legacy
    ribbi = w in (400, 700)
    legacy_family = family if ribbi else f"{family} {sub}"
    legacy_sub = sub if ribbi else "Regular"
    for nid, val in ((1, legacy_family), (2, legacy_sub), (4, f"{family} {sub}"), (6, ps), (16, family), (17, sub)):
        name.setName(val, nid, 3, 1, 0x409)
    os2 = t["OS/2"]
    os2.usWeightClass = w
    fs = os2.fsSelection & ~(0x01 | 0x20 | 0x40)
    fs |= 0x20 if w == 700 else (0x40 if w == 400 else 0)
    os2.fsSelection = fs | 0x80  # USE_TYPO_METRICS
    t["head"].macStyle = (t["head"].macStyle & ~0x01) | (0x01 if w == 700 else 0)
    if "STAT" in t:
        del t["STAT"]


def static(file, family, w, outname):
    t = TTFont(os.path.join(src, file))
    if "fvar" in t:
        t = instancer.instantiateVariableFont(t, {"wght": w})
    set_names(t, family, w)
    t.flavor = None
    t.save(os.path.join(out, outname))
    print("ok", outname)


static("Anton-400-latin.woff2", "Anton", 400, "Anton-Regular.ttf")
for w in (400, 500, 600, 700):
    static("HankenGrotesk-400-latin.woff2", "Hanken Grotesk", w, f"HankenGrotesk-{NAMES[w]}.ttf")
for w in (400, 500):
    static("GeistMono-400-latin.woff2", "Geist Mono", w, f"GeistMono-{NAMES[w]}.ttf")
