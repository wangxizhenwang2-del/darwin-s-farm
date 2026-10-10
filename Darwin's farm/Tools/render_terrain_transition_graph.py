"""Render the proposed land biome transition graph as editable vector art."""

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
from matplotlib.patches import FancyArrowPatch, FancyBboxPatch, PathPatch
from matplotlib.path import Path as MplPath


ROOT = Path(__file__).resolve().parent.parent
FONT = FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
BOLD = FontProperties(fname="C:/Windows/Fonts/msyhbd.ttc")

NODES = {
    "草原": ((0.29, 0.55), "#A8D879", "#35572E"),
    "沙漠": ((0.10, 0.35), "#F1D18A", "#6E511A"),
    "雨林": ((0.30, 0.18), "#70C79F", "#17563E"),
    "森林": ((0.54, 0.34), "#9BCBA0", "#244D33"),
    "苔原": ((0.46, 0.77), "#C1DDD3", "#285B59"),
    "高山": ((0.74, 0.59), "#B9CDD3", "#34545D"),
    "雪山": ((0.73, 0.87), "#E7F1F5", "#527381"),
    "火山": ((0.91, 0.30), "#D7A5A0", "#733B36"),
}

# The two text lines are the two directions of the same undirected edge.
# Heights are deliberately absent; only the uplift/subside operation is shown.
EDGES = [
    ("草原", "沙漠", (0.13, 0.49), "草→沙  降湿50 · 抑制50%", "沙→草  增湿50 · 增产5万", 0.19),
    ("草原", "森林", (0.43, 0.48), "草→森  抬升 · 降温25 · 降湿25 · 增产2.5万", "森→草  下沉 · 升温25 · 增湿25 · 抑制25%", 0.28),
    ("草原", "雨林", (0.29, 0.36), "草→雨  增湿25 · 增产2.5万", "雨→草  降湿25 · 抑制25%", 0.21),
    ("草原", "苔原", (0.31, 0.73), "草→苔  抬升 · 降温50", "苔→草  下沉 · 升温50", 0.20),
    ("沙漠", "雨林", (0.13, 0.20), "沙→雨  增湿50 · 增产5万+2.5万", "雨→沙  降湿50 · 抑制50%×2", 0.24),
    ("沙漠", "火山", (0.54, 0.12), "沙→火  抬升×2 · 升温25", "火→沙  下沉×2 · 降湿50", 0.22),
    ("森林", "雨林", (0.48, 0.19), "森→雨  下沉 · 升温25 · 增湿25", "雨→森  抬升 · 降温25 · 降湿25", 0.24),
    ("森林", "苔原", (0.48, 0.57), "森→苔  降温50 · 抑制25%", "苔→森  升温25 · 增产5万", 0.22),
    ("森林", "高山", (0.70, 0.39), "森→高  抬升", "高→森  下沉 · 增产2.5万", 0.19),
    ("苔原", "高山", (0.62, 0.75), "苔→高  抬升 · 升温25", "高→苔  下沉 · 降温25", 0.20),
    ("苔原", "雪山", (0.57, 0.87), "苔→雪  抬升 · 抑制50%", "雪→苔  下沉", 0.19),
    ("高山", "雪山", (0.82, 0.78), "高→雪  降温25 · 抑制50%", "雪→高  升温25", 0.20),
    ("高山", "火山", (0.88, 0.52), "高→火  升温25 · 抑制50%", "火→高  降温25", 0.20),
]


def draw():
    plt.rcParams["svg.fonttype"] = "none"
    figure, axes = plt.subplots(figsize=(25, 16), dpi=150)
    figure.patch.set_facecolor("#F5F8F6")
    axes.set_facecolor("#F5F8F6")
    axes.set_xlim(0, 1)
    axes.set_ylim(0, 1)
    axes.set_aspect("auto")
    axes.axis("off")

    axes.text(0.055, 0.965, "地形转换网络", fontproperties=BOLD, fontsize=30,
              color="#233B3A", ha="left", va="center", zorder=10)
    axes.text(0.056, 0.925, "8 种地形 · 13 条邻接边 · 操作仅为示意",
              fontproperties=FONT, fontsize=14, color="#5C7471",
              ha="left", va="center", zorder=10)

    # The long desert–volcano connection is routed below the central network.
    for source, target, _, _, _, _ in EDGES:
        start = NODES[source][0]
        end = NODES[target][0]
        if {source, target} == {"沙漠", "火山"}:
            path = MplPath([start, (0.50, 0.035), end],
                           [MplPath.MOVETO, MplPath.CURVE3, MplPath.CURVE3])
            axes.add_patch(PathPatch(path, facecolor="none", edgecolor="#B5C7C2",
                                     lw=2.2, zorder=1))
        else:
            axes.add_patch(FancyArrowPatch(start, end, arrowstyle="-",
                                           mutation_scale=10, lw=2.2,
                                           color="#B5C7C2", zorder=1))

    for source, target, (x, y), forward, reverse, width in EDGES:
        height = 0.066
        box = FancyBboxPatch((x - width / 2, y - height / 2), width, height,
                             boxstyle="round,pad=0.008,rounding_size=0.012",
                             linewidth=1, edgecolor="#D8E4DF", facecolor="#FFFFFF",
                             zorder=3)
        axes.add_patch(box)
        axes.text(x, y + 0.013, forward, fontproperties=FONT, fontsize=10.3,
                  color="#344B48", ha="center", va="center", zorder=4)
        axes.text(x, y - 0.014, reverse, fontproperties=FONT, fontsize=10.3,
                  color="#657A75", ha="center", va="center", zorder=4)

    for name, ((x, y), fill, ink) in NODES.items():
        width, height = 0.095, 0.068
        axes.add_patch(FancyBboxPatch((x - width / 2, y - height / 2), width, height,
                                      boxstyle="round,pad=0.01,rounding_size=0.024",
                                      linewidth=2.4, edgecolor=ink, facecolor=fill,
                                      zorder=6))
        axes.text(x, y, name, fontproperties=BOLD, fontsize=16,
                  color=ink, ha="center", va="center", zorder=7)

    axes.text(0.055, 0.035,
              "边上的两行是非唯一的操作示例；同一地形可有不同海拔，图中不设固定海拔。",
              fontproperties=FONT, fontsize=12, color="#617672",
              ha="left", va="center", zorder=10)
    axes.text(0.95, 0.035, "程序仅保存邻接 · 示例路线不进入程序",
              fontproperties=FONT, fontsize=11, color="#8C9D99",
              ha="right", va="center", zorder=10)

    svg = ROOT / "TERRAIN-TRANSITION-GRAPH.svg"
    png = ROOT / "TERRAIN-TRANSITION-GRAPH.png"
    figure.savefig(svg, transparent=False, bbox_inches="tight", pad_inches=0.1)
    figure.savefig(png, transparent=False, bbox_inches="tight", pad_inches=0.1)
    plt.close(figure)
    print(svg)
    print(png)


if __name__ == "__main__":
    draw()
