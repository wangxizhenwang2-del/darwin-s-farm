"""Guard public C# method arity used by pre-merge integrations."""

from pathlib import Path
import re

root = Path(__file__).resolve().parents[3]
contracts = {
    "Assets/Scripts/Controller/PopulationMovementController.cs": {
        "CreatePopulation": 2,
    },
    "Assets/Scripts/Simulation/PopulationTransitionVisual.cs": {
        "Begin": 11,
    },
}
errors = []
for relative, methods in contracts.items():
    source = (root / relative).read_text(encoding="utf-8")
    for name, count in methods.items():
        matches = re.findall(r"\bpublic\s+[\w<>]+\s+" + name +
                             r"\s*\(([^)]*)\)", source, re.S)
        found = [len([part for part in arguments.split(",") if part.strip()])
                 for arguments in matches]
        if count not in found:
            errors.append(f"{relative}: {name}({count} parameters) missing; found {found}")
for error in errors:
    print("FAIL", error)
if not errors:
    print("PASS pre-merge CreatePopulation(2) and Begin(11) overloads are retained")
raise SystemExit(bool(errors))
