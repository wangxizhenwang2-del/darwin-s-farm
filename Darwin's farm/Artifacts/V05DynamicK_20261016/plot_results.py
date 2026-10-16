"""Plot only values recorded by real Unity; no ecological model is evaluated."""
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

BASE = Path(__file__).resolve().parent
OUT = BASE / "plots"
OUT.mkdir(exist_ok=True)
COLORS = {"鸡": "#c48419", "鹿": "#26724c", "灵感菇狸": "#8b4a9c"}
LABELS = {"鸡": "Chicken", "鹿": "Deer", "灵感菇狸": "Civet"}

def read(name, fallback=None):
    path = BASE / "runs" / (name + "-run1.jsonl")
    if not path.exists() and fallback:
        path = BASE / "runs" / (fallback + "-run1.jsonl")
    data = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("{"):
            row = json.loads(line)
            data[row["day"]] = row
    return data

def series(data, species, key="count"):
    values = []
    for row in data.values():
        populations = [p for tile in row["tiles"] for p in tile["populations"]
                       if p["species"] == species]
        values.append(sum(p[key] for p in populations) if populations else 0)
    return list(data), values

plt.rcParams.update({"font.size": 10, "axes.spines.top": False,
                     "axes.spines.right": False, "figure.dpi": 140})
fig, axes = plt.subplots(2, 3, figsize=(15, 8.2), constrained_layout=True)
ax = axes[0, 0]
for n, style in [(50, "-"), (5000, "--")]:
    d = read("formal-T1-chicken" + str(n), "v2-T1-chicken" + str(n))
    ax.plot(*series(d, "鸡"), style, label="start N=" + str(n))
ax.axhline(1509, color="#555", ls=":", label="K_food=1509")
ax.set_yscale("log"); ax.set_title("T1  Same supply, different starting N")
ax.legend(fontsize=8)

for ax, side in [(axes[0, 1], "chicken"), (axes[0, 2], "deer")]:
    d = read("formal-T2-" + side + "-favored", "v2-T2-" + side + "-favored")
    for species in ("鸡", "鹿"):
        ax.plot(*series(d, species), color=COLORS[species], label=LABELS[species])
    ax.set_title("T2  " + side.capitalize() + " climate advantage")
    ax.legend(fontsize=8)

ax = axes[1, 0]
for n in (10, 20, 50, 100):
    d = read("formal-T3-deer" + str(n) + "-rich",
             "v2-T3-deer" + str(n) + "-rich")
    ax.plot(*series(d, "鹿"), label="start N=" + str(n))
ax.set_title("T3  Deer, abundant food, frozen payment")
ax.legend(fontsize=8)

ax = axes[1, 1]
d = read("formal-T4-predator", "v2-T4-predator")
for species in COLORS:
    ax.plot(*series(d, species), color=COLORS[species], label=LABELS[species])
control = read("formal-T4-no-predator", "v2-T4-no-predator")
ax.plot(*series(control, "鹿"), color=COLORS["鹿"], ls="--",
        label="Deer, no predator")
ax.set_title("T4  Predation and competition release")
ax.legend(fontsize=8)

ax = axes[1, 2]
d = read("formal-T5-Rchange", "control-T5-Rchange")
for species in COLORS:
    ax.plot(*series(d, species), color=COLORS[species], label=LABELS[species])
for day in (600, 1000):
    ax.axvline(day, color="#777", ls=":", lw=1)
ax.set_title("T5  R: 80000 -> 1000 -> 80000")
ax.legend(fontsize=8)
for ax in axes.flat:
    ax.set_xlabel("Simulation day"); ax.set_ylabel("Population")
    ax.grid(alpha=0.18)
fig.savefig(OUT / "population-tests.png")
fig.savefig(OUT / "population-tests.svg")
plt.close(fig)

fig, axes = plt.subplots(1, 2, figsize=(11, 4), constrained_layout=True)
control = read("formal-T3-deer10-rich", "v3-readonly-on")
prototype = read("prototype-deer10-rich")
for d, name in [(control, "Frozen payment"), (prototype, "Reserve prototype")]:
    axes[0].plot(*series(d, "鹿"), label=name)
axes[0].set_title("Controlled Unity A/B: 10 deer")
axes[0].set_xlabel("Day"); axes[0].set_ylabel("Population"); axes[0].legend()
small = {day: row for day, row in prototype.items() if day <= 10}
axes[1].plot(*series(small, "鹿", "birthBudget"), marker="o",
             label="Resources available before birth")
axes[1].plot(*series(small, "鹿", "reserve"), marker=".",
             label="Reserve after birth")
axes[1].axhline(2070, color="#777", ls="--", label="Full newborn cost: 2070")
axes[1].set_title("Prototype: full payment, retained remainder")
axes[1].set_xlabel("Day"); axes[1].set_ylabel("Biomass / energy units")
axes[1].legend(fontsize=8)
for ax in axes: ax.grid(alpha=0.18)
fig.savefig(OUT / "birth-account-ab.png")
fig.savefig(OUT / "birth-account-ab.svg")
plt.close(fig)
print("Unity evidence plots saved in", OUT)
