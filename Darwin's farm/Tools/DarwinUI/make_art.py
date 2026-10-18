"""Generate small, repeatable UI sprites inspired by the project's paper HUD art."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import random

ROOT = Path(__file__).resolve().parents[2] / 'Assets' / 'Resources' / 'DarwinJournal'
ROOT.mkdir(parents=True, exist_ok=True)
random.seed(214)

def paper(name, size, edge, fill, shadow=True, ornament=True):
    w, h = size
    im = Image.new('RGBA', size, (0, 0, 0, 0))
    d = ImageDraw.Draw(im, 'RGBA')
    if shadow:
        d.rounded_rectangle((6, 7, w-2, h-2), radius=20, fill=(42, 49, 46, 65))
    d.rounded_rectangle((2, 2, w-8, h-9), radius=18, fill=fill, outline=edge, width=5)
    d.rounded_rectangle((12, 12, w-18, h-19), radius=12, outline=(123, 137, 100, 105), width=2)
    for _ in range(w*h//250):
        x, y = random.randrange(18, w-22), random.randrange(18, h-24)
        c = random.choice([(123,99,66,10),(255,255,255,18),(114,135,89,8)])
        d.point((x,y), fill=c)
    if ornament and w > 180:
        for x, flip in ((25,1),(w-32,-1)):
            for k in range(3):
                yy=24+k*12
                d.line((x, yy, x+flip*15, yy+11), fill=(83,110,75,180), width=2)
                d.ellipse((x+flip*11-4, yy+7, x+flip*11+4, yy+15), fill=(151,174,119,140))
    im.save(ROOT / name)

paper('PaperPanel.png',(384,256),(70,82,70,255),(249,243,222,255))
paper('PaperButton.png',(320,84),(72,91,77,255),(237,229,192,255),ornament=False)
paper('PaperButtonPressed.png',(320,84),(72,91,77,255),(201,216,172,255),ornament=False)
paper('SaveCard.png',(384,136),(70,82,70,255),(248,240,215,255),ornament=False)
paper('NewsToast.png',(520,126),(70,82,70,255),(248,243,225,255))

# Reuse the project's own rendered biome board as the title illustration.
# Its flat preview background is replaced with paper; the existing game art
# remains intact and a broad quiet wash keeps the central buttons legible.
source = Path(__file__).resolve().parents[2] / 'Artifacts' / 'VincentMoebiusParity' / 'Overview.png'
board = Image.open(source).convert('RGB').resize((1600, 978), Image.Resampling.LANCZOS)
board = board.crop((0, 78, 1600, 978))
w, h = board.size
im = Image.new('RGB', (w, h), (229, 229, 202))
d = ImageDraw.Draw(im, 'RGBA')
for y in range(h):
    d.line((0, y, w, y), fill=(220, 229-int(19*y/h), 200-int(26*y/h), 255))
pixels = board.load()
alpha = Image.new('L', (w, h), 0)
a = alpha.load()
background = board.getpixel((0, 0))
for y in range(h):
    for x in range(w):
        p = pixels[x, y]
        distance = max(abs(p[c]-background[c]) for c in range(3))
        if distance > 34:
            a[x, y] = min(255, (distance-27)*14)
alpha = alpha.filter(ImageFilter.GaussianBlur(1.0))
im.paste(board, (0, 0), alpha)
wash = Image.new('RGBA', (w, h), (0, 0, 0, 0))
wd = ImageDraw.Draw(wash, 'RGBA')
wd.ellipse((330, -110, 1270, 1050), fill=(247, 240, 215, 169))
wash = wash.filter(ImageFilter.GaussianBlur(95))
im = Image.alpha_composite(im.convert('RGBA'), wash).convert('RGB')
im.save(ROOT / 'TitleLandscape.png', optimize=True)
