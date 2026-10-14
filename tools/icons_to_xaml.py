"""Convertit les icônes Lucide (iconData.js) en géométries XAML WPF."""
import json, re, sys

src, out = sys.argv[1], sys.argv[2]
WANTED = ["house", "crosshair", "sliders-horizontal", "vibrate", "library-big", "settings", "usb",
          "bluetooth", "minus", "copy", "x", "moon", "sun", "gamepad-2", "zap", "triangle-alert",
          "layers", "activity", "power", "lightbulb", "info", "refresh-cw", "check", "circle-check",
          "battery-full", "battery-medium", "battery-low", "battery-charging", "joystick", "wand-sparkles"]

text = open(src, encoding="utf-8").read()
data = json.loads(text[text.index("{"): text.rindex("}") + 1])

NUM = r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"
ARGS = {"m": 2, "l": 2, "h": 1, "v": 1, "c": 6, "s": 4, "q": 4, "t": 2, "a": 7, "z": 0}


def fmt(v):
    s = ("%.4f" % v).rstrip("0").rstrip(".")
    return s if s not in ("-0", "") else "0"


def normalize_path(d):
    """Réécrit un attribut d SVG en syntaxe WPF sûre (séparateurs explicites, drapeaux d'arc)."""
    i, res, cmd = 0, [], None
    while i < len(d):
        c = d[i]
        if c.isspace() or c == ",":
            i += 1
            continue
        if c.isalpha():
            cmd = c
            if c.lower() == "z":
                res.append(c)
            i += 1
            continue
        n = ARGS[cmd.lower()]
        vals = []
        for k in range(n):
            while i < len(d) and (d[i].isspace() or d[i] == ","):
                i += 1
            if cmd.lower() == "a" and k in (3, 4):
                vals.append(d[i])
                i += 1
            else:
                m = re.match(NUM, d[i:])
                vals.append(fmt(float(m.group(0))))
                i += m.end()
        # Un m initial est absolu pour sa 1re paire, les suivantes restent des l relatifs (spec SVG)
        emitted = "M" if (cmd == "m" and not res) else cmd
        res.append(emitted + " " + " ".join(vals))
        # répétitions implicites : un M devient L
        if cmd == "M":
            cmd = "L"
        elif cmd == "m":
            cmd = "l"
    return " ".join(res)


def attrs(el):
    return dict(re.findall(r'([\w-]+)="([^"]*)"', el))


def element_to_path(tag, a):
    f = lambda k, d=0.0: float(a.get(k, d))
    if tag == "path":
        return normalize_path(a["d"])
    if tag == "line":
        return f"M {fmt(f('x1'))} {fmt(f('y1'))} L {fmt(f('x2'))} {fmt(f('y2'))}"
    if tag == "circle":
        cx, cy, r = f("cx"), f("cy"), f("r")
        return (f"M {fmt(cx - r)} {fmt(cy)} A {fmt(r)} {fmt(r)} 0 1 0 {fmt(cx + r)} {fmt(cy)} "
                f"A {fmt(r)} {fmt(r)} 0 1 0 {fmt(cx - r)} {fmt(cy)} Z")
    if tag == "rect":
        x, y, w, h = f("x"), f("y"), f("width"), f("height")
        rx = f("rx", 0)
        if rx <= 0:
            return f"M {fmt(x)} {fmt(y)} H {fmt(x + w)} V {fmt(y + h)} H {fmt(x)} Z"
        r = min(rx, w / 2, h / 2)
        return (f"M {fmt(x + r)} {fmt(y)} H {fmt(x + w - r)} A {fmt(r)} {fmt(r)} 0 0 1 {fmt(x + w)} {fmt(y + r)} "
                f"V {fmt(y + h - r)} A {fmt(r)} {fmt(r)} 0 0 1 {fmt(x + w - r)} {fmt(y + h)} "
                f"H {fmt(x + r)} A {fmt(r)} {fmt(r)} 0 0 1 {fmt(x)} {fmt(y + h - r)} "
                f"V {fmt(y + r)} A {fmt(r)} {fmt(r)} 0 0 1 {fmt(x + r)} {fmt(y)} Z")
    if tag in ("polyline", "polygon"):
        pts = [fmt(float(p)) for p in re.findall(NUM, a["points"])]
        pairs = [f"{pts[k]} {pts[k + 1]}" for k in range(0, len(pts), 2)]
        return "M " + " L ".join(pairs) + (" Z" if tag == "polygon" else "")
    raise ValueError(tag)


lines = ['<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
         '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
         '    <!-- Icônes Lucide (ISC), générées depuis le design system Axis. Grille 24×24, à tracer en contour. -->']
for name in WANTED:
    markup = data[name]
    parts = [element_to_path(tag, attrs(body)) for tag, body in re.findall(r"<(\w+)([^>]*)/?>", markup)]
    key = "Icon." + "".join(p.capitalize() for p in name.split("-"))
    lines.append(f'    <Geometry x:Key="{key}">{" ".join(parts)}</Geometry>')
lines.append("</ResourceDictionary>")
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("ok", len(WANTED))


