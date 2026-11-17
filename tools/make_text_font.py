"""Build Blip's UI text font: Inter (SIL OFL 1.1) minus the few symbols that are also emoji.

Why: font fallback picks the FIRST font that has a glyph. Inter carries monochrome
❤ ♥ ☀ ⚠ ▶ ◀ ⬆ ⬜ ▪ ⏏ ‼ ⁉, so typing ❤️ drew a black heart. Without them, those characters (and
their VS16 sequences) fall through to the colour emoji font, in the composer as well as in
read-only text. © ® ™, arrows and Ⓜ stay: they are text by default, and EmojiText switches
them to the emoji font only when they carry VS16.

    python3 tools/make_text_font.py <dir with Inter-*.ttf> src/Blip.Windows/Assets/Fonts

The Inter TTFs used are the ones inside the Avalonia.Fonts.Inter 11.3.23 package (Inter 4.x).
The output keeps the OFL; Assets/Fonts/OFL.txt ships beside it.
"""
import sys
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont

EMOJI_CAPABLE = {0x203C, 0x2049, 0x23CF, 0x25AA, 0x25B6, 0x25C0, 0x2600, 0x2665, 0x26A0, 0x2764, 0x2B06, 0x2B1C}
FAMILY = "Blip Inter"

src, dst = Path(sys.argv[1]), Path(sys.argv[2])
dst.mkdir(parents=True, exist_ok=True)
for weight in ("Regular", "Medium", "SemiBold", "Bold"):
    font = TTFont(src / f"Inter-{weight}.ttf")
    keep = set(font.getBestCmap()) - EMOJI_CAPABLE
    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.glyph_names = True
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=keep)
    sub.subset(font)
    style = weight if weight in ("Regular", "Bold") else weight
    for rec in font["name"].names:
        if rec.nameID in (1, 16):
            rec.string = FAMILY
        elif rec.nameID == 4:
            rec.string = f"{FAMILY} {weight}"
        elif rec.nameID == 6:
            rec.string = f"BlipInter-{weight}"
    font.save(dst / f"BlipInter-{weight}.ttf")
    print(weight, len(keep))
