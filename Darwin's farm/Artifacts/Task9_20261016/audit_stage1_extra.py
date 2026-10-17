"""Three-group same-lane input order invariant from real Unity records."""
import json
from pathlib import Path

runs = Path(__file__).resolve().parent / "runs"
states = []
for stem in ("stage1-C-three-forward", "stage1-C-three-reverse"):
    text = (runs / (stem + ".jsonl")).read_text(encoding="utf-8-sig")
    records = [json.loads(line) for line in text.splitlines()
               if line.startswith("{")]
    tile = next(t for t in records[-1]["tiles"] if t["x"] == 1)
    states.append({p["species"]: p["allocatedFood"]
                   for p in tile["populations"]})
assert states[0] == states[1], states
print("PASS three-population two-small-one-large allocation order", states[0])
