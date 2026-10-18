"""Compare tracked public declaration names and integration surfaces to HEAD."""

from pathlib import Path
import re
import subprocess

root = Path(__file__).resolve().parents[3]
prefix = subprocess.check_output(
    ["git", "rev-parse", "--show-prefix"], cwd=root, text=True).strip()
tracked = [
    "Assets/Scripts/Simulation/Economy/BioenergyAccount.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentRules.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentState.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentTechnologyController.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentWorld.cs",
    "Assets/Scripts/Simulation/Environment/SimulationEnvironmentController.cs",
    "Assets/Scripts/Simulation/LandLifeRules.cs",
    "Assets/Scripts/Simulation/SimulationController.Determinism.cs",
    "Assets/Scripts/Simulation/SimulationController.Evolution.cs",
    "Assets/Scripts/Simulation/SimulationController.FoodWeb.cs",
    "Assets/Scripts/Simulation/SimulationController.Influence.cs",
    "Assets/Scripts/Simulation/SimulationController.LandEvents.cs",
    "Assets/Scripts/Simulation/SimulationController.LandTraits.cs",
    "Assets/Scripts/Simulation/SimulationController.Migration.cs",
    "Assets/Scripts/Simulation/SimulationController.cs",
    "Assets/Scripts/Simulation/WhiteboxBiowebTestSetup.cs",
]


def at_head(relative):
    return subprocess.check_output(
        ["git", "show", "HEAD:" + prefix + relative], cwd=root,
        encoding="utf-8", errors="replace")


def declarations(source):
    result = set()
    for line in source.splitlines():
        line = line.strip()
        if not re.match(r"(?:public|protected)\s", line):
            continue
        head = re.split(r"\s*(?:\(|=>|\{|;|=)", line, maxsplit=1)[0]
        result.add(head.strip())
    return result


missing = []
for relative in tracked:
    before = declarations(at_head(relative))
    after = declarations((root / relative).read_text(
        encoding="utf-8", errors="replace"))
    for declaration in sorted(before - after):
        missing.append((relative, declaration))

changed = subprocess.check_output(
    ["git", "diff", "--name-only", "--", *tracked], cwd=root,
    encoding="utf-8", errors="replace", stderr=subprocess.DEVNULL).splitlines()
diff = subprocess.check_output(
    ["git", "diff", "--unified=0", "--", *tracked], cwd=root,
    encoding="utf-8", errors="replace", stderr=subprocess.DEVNULL)
removed_serialized = [line for line in diff.splitlines()
                      if line.startswith("-") and not line.startswith("---")
                      and "SerializeField" in line]
surface = subprocess.check_output(
    ["git", "diff", "--name-only", "--", "Assets/Scenes", "Assets/Prefabs",
     "Assets/Scripts/UI"], cwd=root, encoding="utf-8", errors="replace",
    stderr=subprocess.DEVNULL).splitlines()
surface = [x for x in surface if "PopulationSimulatorWindow.LiveExport.cs" not in x]
ui = at_head("Assets/Scripts/UI/EnvironmentToolSession.cs")
model = "\n".join(at_head(x) for x in (
    "Assets/Scripts/Simulation/Environment/EnvironmentTechnologyController.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentState.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentRules.cs"))
unmatched = [name for name in ("MonsoonAnchor", "PlantChange", "HasMonsoon",
                              "TemperatureOffset", "HumidityOffset")
             if name in ui and name not in model]

print("HEAD:", subprocess.check_output(
    ["git", "rev-parse", "HEAD"], cwd=root, text=True).strip())
print("Tracked integration files changed:", len(changed))
print("Removed public/protected declaration headers:", len(missing))
for item in missing:
    print("  ", *item)
print("Scene/Prefab/UI changes by this integration:", len(surface))
print("Removed serialized-field declaration lines:", len(removed_serialized))
for item in surface:
    print("  ", item)
print("UI references already unmatched in HEAD model:", ", ".join(unmatched))
raise SystemExit(1 if missing or surface or removed_serialized else 0)
