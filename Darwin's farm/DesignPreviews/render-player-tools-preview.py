"""Render a review image from the game's original UI sprites."""

from pathlib import Path
import math
import re
import sys
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/Resources/GameUI/ToolArt"
GAME_UI = ROOT / "Assets/Resources/GameUI"
OUT = Path(__file__).resolve().parent
SPRITES = OUT / "player-tool-sprites"
SPRITES.mkdir(exist_ok=True)

S = 2
EMPTY = "--empty" in sys.argv


def sprite(source: Path, asset: Path) -> Image.Image:
    metadata = asset.read_text(encoding="utf-8")
    match = re.search(
        r"m_Rect:\s*serializedVersion: 2\s*x: (\d+)\s*y: (\d+)\s*width: (\d+)\s*height: (\d+)",
        metadata,
    )
    if not match:
        raise ValueError(f"Sprite rectangle missing: {asset}")
    x, y, width, height = map(int, match.groups())
    atlas = Image.open(source).convert("RGBA")
    return atlas.crop((x, atlas.height - y - height, x + width, atlas.height - y))


def sized(image: Image.Image, width: int) -> Image.Image:
    height = round(image.height * width / image.width)
    return image.resize((width, height), Image.Resampling.LANCZOS)


def nine_tile(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Keep every source patch's aspect ratio; tile the center vertically."""
    width, height = size
    source_width, source_height = image.size
    left = right = 44
    # Keep the title and the full inner top frame together. The repeated section
    # starts in plain body paper so no horizontal frame line is duplicated.
    top, bottom = 300, 90
    ratio = width / source_width
    target_left = round(left * ratio)
    target_right = round(right * ratio)
    target_top = round(top * ratio)
    target_bottom = round(bottom * ratio)
    result = Image.new("RGBA", size)
    columns = [(0, left, 0, target_left),
               (left, source_width - right, target_left, width - target_right),
               (source_width - right, source_width, width - target_right, width)]
    for source_x0, source_x1, target_x0, target_x1 in columns:
        upper = image.crop((source_x0, 0, source_x1, top)).resize(
            (target_x1 - target_x0, target_top), Image.Resampling.LANCZOS)
        lower = image.crop((source_x0, source_height - bottom, source_x1, source_height)).resize(
            (target_x1 - target_x0, target_bottom), Image.Resampling.LANCZOS)
        result.alpha_composite(upper, (target_x0, 0))
        result.alpha_composite(lower, (target_x0, height - target_bottom))
        middle = image.crop((source_x0, top, source_x1, source_height - bottom))
        middle = sized(middle, target_x1 - target_x0)
        y = target_top
        limit = height - target_bottom
        while y < limit:
            segment = middle.crop((0, 0, middle.width, min(middle.height, limit - y)))
            result.alpha_composite(segment, (target_x0, y))
            y += segment.height
    return result


toolbar = sprite(ART / "Toolbar.png", ART / "Toolbar.asset")
toolbar_atlas = Image.open(ART / "Toolbar.png").convert("RGBA")
# Circle bounds are in the source atlas. The five files retain the original artwork.
circle_bounds = [
    (245, 305, 515, 575),
    (245, 585, 515, 855),
    (245, 855, 515, 1125),
    (245, 1125, 515, 1395),
    (245, 1395, 515, 1665),
]
source_order = ["temperature", "elevation", "humidity", "plant-increment", "plant-stock"]
categories = {}
for name, bounds in zip(source_order, circle_bounds):
    icon = toolbar_atlas.crop(bounds)
    # Transparent corners make the old circular marks reusable without the rail.
    mask = Image.new("L", icon.size)
    ImageDraw.Draw(mask).ellipse((3, 3, 267, 267), fill=255)
    icon.putalpha(ImageChops.multiply(icon.getchannel("A"), mask))
    icon.save(SPRITES / f"category-{name}.png")
    categories[name] = icon

options = []
for index in range(4):
    image = sprite(ART / "Temperature.png", ART / f"Temperature_{index}.asset")
    image.save(SPRITES / f"temperature-option-{index}.png")
    options.append(image)

paper = sprite(GAME_UI / "EnvironmentToolPanel.png", GAME_UI / "Editable/ToolPanelBackground.asset")
panel_x, panel_y, panel_width, panel_height = 100, 18, 350, 570
canvas = Image.new("RGBA", (474 * S, 612 * S), (198, 222, 214, 255))
panel = nine_tile(paper, (panel_width * S, panel_height * S))
canvas.alpha_composite(panel, (panel_x * S, panel_y * S))

draw = ImageDraw.Draw(canvas, "RGBA")
font_path = ROOT / "Assets/font/乐米拾光手札体.ttf"
fonts = {size: ImageFont.truetype(str(font_path), size * S) for size in (10, 11, 12, 13, 14, 15, 16, 17, 19)}


def p(xy):
    return tuple(round(v * S) for v in xy)


def text(x, y, value, size=13, fill=(51, 57, 46), anchor=None):
    draw.text(p((x, y)), value, font=fonts[size], fill=fill, anchor=anchor)


def line(points, fill, width=1, joint="curve"):
    draw.line([p(point) for point in points], fill=fill, width=round(width * S), joint=joint)


def rectangle(box, fill, outline=None, width=1):
    draw.rectangle(p(box), fill=fill, outline=outline, width=round(width * S))


def paste_uniform(image, box):
    x, y, target_width, target_height = box
    fitted = image.copy()
    fitted.thumbnail((target_width * S, target_height * S), Image.Resampling.LANCZOS)
    px = round((x + (target_width - fitted.width / S) / 2) * S)
    py = round((y + (target_height - fitted.height / S) / 2) * S)
    canvas.alpha_composite(fitted, (px, py))


# Preserve the source rail proportions; cover its old circles with independently
# sliced category sprites in the requested order.
rail_x, rail_y, rail_width = 7, 92, 80
rail = sized(toolbar, rail_width * S)
canvas.alpha_composite(rail, (rail_x * S, rail_y * S))
category_order = ["temperature", "humidity", "plant-stock", "plant-increment", "elevation"]
rail_centers = [136, 193, 250, 307, 364]
for index, (name, center_y) in enumerate(zip(category_order, rail_centers)):
    paste_uniform(categories[name], (rail_x + 12, center_y - 27, 55, 55))
    if index == 0:
        draw.ellipse(p((rail_x + 12, center_y - 27, rail_x + 67, center_y + 28)),
                     outline=(91, 144, 101, 255), width=3 * S)

# Header and original close badge.
text(panel_x + 31, panel_y + 22, "温 度 变 化 工 具", 19)
close = sprite(ART / "ExitBadge.png", ART / "ExitBadge.asset")
paste_uniform(close, (panel_x + panel_width - 50, panel_y + 13, 38, 38))
line([(panel_x + panel_width - 37, panel_y + 24),
      (panel_x + panel_width - 24, panel_y + 37)], (46, 54, 42), 1.5)
line([(panel_x + panel_width - 24, panel_y + 24),
      (panel_x + panel_width - 37, panel_y + 37)], (46, 54, 42), 1.5)

# Each original tier icon now carries its value and name, as in the reference.
# The selected card uses a quiet wash instead of repeating its name below.
option_centers = [164, 239, 314, 389]
tier_values = ["+50", "+25", "−25", "−50"]
tier_names = ["大幅升温", "小幅升温", "小幅降温", "大幅降温"]
for index, (icon, center_x) in enumerate(zip(options, option_centers)):
    left, right = center_x - 35, center_x + 35
    selected = index == 1
    draw.rounded_rectangle(p((left, 76, right, 174)), radius=7 * S,
                           fill=(221, 234, 202, 180) if selected else (243, 235, 210, 112),
                           outline=(98, 147, 94, 220) if selected else (172, 163, 137, 95),
                           width=(2 if selected else 1) * S)
    paste_uniform(icon, (center_x - 25, 79, 50, 50))
    text(center_x, 132, tier_values[index], 14, anchor="mt")
    tag_fill = (219, 215, 174, 190) if index < 2 else (207, 223, 220, 190)
    draw.rounded_rectangle(p((left + 4, 151, right - 4, 168)), radius=4 * S,
                           fill=tag_fill)
    text(center_x, 152, tier_names[index], 10, anchor="mt")

muted = (104, 107, 91)
line([(128, 180), (422, 180)], (168, 171, 148, 125), 1)
text(128, 186, "预计效果", 11, muted)
text(188, 183, "环境温度 +25", 12)
text(292, 184, "适温可能提升", 11, (189, 62, 49))
text(128, 210, "预计地形", 11, muted)
text(188, 207, "稀疏草原变为炎热山地", 12)

text(128, 233, "地块趋势", 14)
text(422, 236, "默认温度 50 到 70", 10, muted, anchor="rt")

# Slim chart: observed, natural, intervention, and changed terrain baseline.
chart_x, chart_y, chart_w, chart_h = 128, 256, 294, 145
rectangle((chart_x, chart_y, chart_x + chart_w, chart_y + chart_h), (246, 246, 232, 190), (151, 158, 137, 165))
plot_left, plot_right = chart_x + 34, chart_x + chart_w - 9
plot_top, plot_bottom = chart_y + 34, chart_y + chart_h - 27


def chart_xpos(day):
    if day <= 0:
        fraction = .27 * (day + 30) / 30
    elif day <= 25:
        fraction = .27 + .18 * day / 25
    elif day <= 175:
        fraction = .45 + .30 * (day - 25) / 150
    else:
        fraction = .75 + .25 * (day - 175) / 100
    return plot_left + fraction * (plot_right - plot_left)


def chart_ypos(value):
    return plot_top + (100 - max(0, min(100, value))) / 100 * (plot_bottom - plot_top)


def historical(day):
    return 50 + 3 * math.sin((day + 12) / 12) + math.cos(day / 6)


def natural(day):
    return 50 + (historical(0) - 50) * math.exp(-day / 95) + 1.5 * math.sin(day / 22)


def terrain(day):
    return 50 if day <= 25 else 70 if day >= 65 else 50 + (day - 25) / 40 * 20


def projection(day):
    factor = day / 25 if day < 25 else 1 if day <= 175 else max(0, 1 - (day - 175) / 100)
    return natural(day) + 25 * factor + .45 * (terrain(day) - 50)


for value in (0, 25, 50, 75, 100):
    y = chart_ypos(value)
    line([(plot_left, y), (plot_right, y)], (187, 192, 175, 110), .6)
    if value in (0, 50, 100):
        text(plot_left - 7, y, str(value), 10, (91, 97, 84), anchor="rm")

for day in (0, 25, 175):
    x = chart_xpos(day)
    for y in range(plot_top, plot_bottom, 7):
        line([(x, y), (x, min(y + 3, plot_bottom))], (132, 145, 130, 160), 1)

text((chart_xpos(0) + chart_xpos(25)) / 2, chart_y + 10, "生效25日", 10, anchor="mt")
text((chart_xpos(25) + chart_xpos(175)) / 2, chart_y + 10, "维持150日", 10, anchor="mt")
text((chart_xpos(175) + chart_xpos(275)) / 2, chart_y + 10, "恢复100日", 10, anchor="mt")

days = list(range(-30, 1, 5)) + list(range(5, 276, 5))
for first, second in zip(days, days[1:]):
    curve = historical if second <= 0 else projection
    a, b = curve(first), curve(second)
    baseline_a = 50 if first <= 0 else terrain(first)
    baseline_b = 50 if second <= 0 else terrain(second)
    polygon = [p((chart_xpos(first), chart_ypos(a))), p((chart_xpos(second), chart_ypos(b))),
               p((chart_xpos(second), chart_ypos(baseline_b))), p((chart_xpos(first), chart_ypos(baseline_a)))]
    draw.polygon(polygon, fill=(191, 76, 62, 41) if a + b > baseline_a + baseline_b else (89, 148, 101, 44))

line([(chart_xpos(-30), chart_ypos(50)), (chart_xpos(0), chart_ypos(50))], (20, 23, 20), 2.6)
line([(chart_xpos(day), chart_ypos(terrain(day))) for day in range(0, 276, 3)],
     (20, 23, 20), 2.6)
line([(chart_xpos(day), chart_ypos(historical(day))) for day in range(-30, 1, 2)], (88, 135, 163), 2.2)
line([(chart_xpos(day), chart_ypos(natural(day))) for day in range(0, 276, 3)], (125, 157, 172, 140), 1.5)
for first, second in zip(range(0, 275, 10), range(10, 285, 10)):
    if first % 20 == 0:
        line([(chart_xpos(first), chart_ypos(projection(first))),
              (chart_xpos(min(second, 275)), chart_ypos(projection(min(second, 275))))], (187, 65, 54), 2.2)
draw.ellipse(p((chart_xpos(0) - 3, chart_ypos(historical(0)) - 3,
                chart_xpos(0) + 3, chart_ypos(historical(0)) + 3)), fill=(88, 135, 163))
for day, label in [(-30, "−30"), (0, "现在"), (25, "+25"), (175, "+175"), (275, "+275")]:
    text(chart_xpos(day), chart_y + chart_h - 20, label, 10, (89, 94, 83), anchor="mt")

# Compact legend; the status rows deliberately remain separate from a new forecast.
for x, legend_y, color, label, dashed in [(130, 411, (84, 135, 161), "历史/自然", False),
                                           (274, 411, (20, 23, 20), "地块默认", False),
                                           (130, 428, (189, 62, 49), "工具预测", True)]:
    if dashed:
        line([(x, legend_y + 7), (x + 8, legend_y + 7)], color, 2)
        line([(x + 12, legend_y + 7), (x + 20, legend_y + 7)], color, 2)
    else:
        line([(x, legend_y + 7), (x + 20, legend_y + 7)], color, 2.6 if label == "地块默认" else 2)
    text(x + 24, legend_y + 1, label, 10, (91, 95, 84))

line([(128, 451), (422, 451)], (176, 179, 155, 130), 1)
text(128, 457, "当前已施加工具", 13)
text(130, 482, "温度 · 降低小档", 12)
text(286, 482, "维持中", 12, (55, 100, 67))
text(367, 483, "余68日", 11, (91, 95, 84))
text(130, 505, "湿度 · 提高小档", 12)
text(286, 505, "恢复中", 12, (166, 104, 52))
text(367, 506, "余42日", 11, (91, 95, 84))

line([(128, 526), (422, 526)], (176, 179, 155, 130), 1)
for x, label, fill in [(218, "应 用", (202, 221, 188, 230)),
                       (320, "撤销效果", (238, 235, 216, 245))]:
    rectangle((x, 535, x + 94, 566), fill, (143, 146, 124, 200))
    text(x + 47, 539, label, 14, anchor="mt")

if EMPTY:
    # Restore the untouched panel texture below the tier cards, then draw the
    # unselected-tile state in the same positions as the selected-tile layout.
    clear_top = 180
    local_top = (clear_top - panel_y) * S
    canvas.alpha_composite(panel.crop((0, local_top, panel_width * S, panel_height * S)),
                           (panel_x * S, clear_top * S))
    draw = ImageDraw.Draw(canvas, "RGBA")

    line([(128, 180), (422, 180)], (168, 171, 148, 125), 1)
    text(128, 186, "预计效果", 11, muted)
    text(188, 183, "环境温度 +25", 12)
    text(292, 184, "适温可能提升", 11, (189, 62, 49))
    text(128, 210, "预计地形", 11, muted)
    text(188, 207, "选择地块后计算", 12, muted)

    text(128, 233, "地块趋势", 14)
    text(422, 236, "等待选择地块", 10, muted, anchor="rt")
    rectangle((chart_x, chart_y, chart_x + chart_w, chart_y + chart_h),
              (246, 246, 232, 190), (151, 158, 137, 165))
    for y in (chart_y + 35, chart_y + 73, chart_y + 111):
        line([(chart_x + 34, y), (chart_x + chart_w - 12, y)],
             (187, 192, 175, 85), .6)
    for value, y in ((100, chart_y + 35), (50, chart_y + 73), (0, chart_y + 111)):
        text(chart_x + 27, y, str(value), 10, (130, 134, 119), anchor="rm")
    draw.ellipse(p((270, 290, 280, 300)), outline=(108, 127, 105, 200), width=2 * S)
    line([(275, 300), (275, 307)], (108, 127, 105, 200), 1.5)
    text(275, 310, "请点击地块以继续", 17, (67, 76, 62), anchor="mt")
    text(275, 338, "选择后显示历史与预测趋势", 11, muted, anchor="mt")

    for x, legend_y, label, dashed in [(130, 411, "历史/自然", False),
                                       (274, 411, "地块默认", False),
                                       (130, 428, "工具预测", True)]:
        if dashed:
            line([(x, legend_y + 7), (x + 8, legend_y + 7)], (151, 155, 140, 170), 2)
            line([(x + 12, legend_y + 7), (x + 20, legend_y + 7)], (151, 155, 140, 170), 2)
        else:
            line([(x, legend_y + 7), (x + 20, legend_y + 7)], (151, 155, 140, 170), 2)
        text(x + 24, legend_y + 1, label, 10, (137, 141, 127))

    line([(128, 451), (422, 451)], (176, 179, 155, 130), 1)
    text(128, 457, "当前已施加工具", 13)
    text(275, 491, "选择地块后查看", 12, muted, anchor="mt")
    line([(128, 526), (422, 526)], (176, 179, 155, 130), 1)
    for x, label in [(218, "应 用"), (320, "撤销效果")]:
        rectangle((x, 535, x + 94, 566), (228, 227, 214, 225), (168, 169, 153, 140))
        text(x + 47, 539, label, 14, (145, 147, 134), anchor="mt")

image_path = OUT / ("player-tools-ui-no-tile-preview.png" if EMPTY else "player-tools-ui-card-preview.png")
canvas.convert("RGB").save(image_path, quality=95)
print(image_path)
