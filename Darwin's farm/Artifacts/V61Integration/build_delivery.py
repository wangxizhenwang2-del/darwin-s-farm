"""Package only this integration's delta and evidence, leaving other work intact."""

from hashlib import sha256
from pathlib import Path
import subprocess
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = "3286061c50661bbc796ce3c57009c662be6dc600"
TRACKED = [
    "Assets/Scripts/Simulation/Economy/BioenergyAccount.cs",
    "Assets/Scripts/Simulation/Editor/EnvironmentFiveToolsAudit.cs",
    "Assets/Scripts/Simulation/Editor/EnvironmentIntegrationAudit.cs",
    "Assets/Scripts/Simulation/Editor/PopulationIdentityRegressionChecks.cs",
    "Assets/Scripts/Simulation/Editor/PopulationTransitionRegressionChecks.cs",
    "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentRules.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentState.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentTechnologyController.cs",
    "Assets/Scripts/Simulation/Environment/EnvironmentWorld.cs",
    "Assets/Scripts/Simulation/Environment/SimulationEnvironmentController.cs",
    "Assets/Scripts/Controller/PopulationMovementController.cs",
    "Assets/Scripts/Simulation/PopulationTransitionVisual.cs",
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
    "Assets/Scripts/UI/DesktopEnvironmentTools.cs",
    "Assets/Scripts/UI/Editor/EnvironmentToolsInstaller.cs",
    "Assets/Scripts/UI/Editor/MobileUIInstaller.cs",
    "Assets/Scripts/UI/EnvironmentToolSession.cs",
    "Assets/Scripts/UI/EnvironmentToolsInputChecks.cs",
    "Assets/Scripts/UI/MobileTechnologyPanel.cs",
    "Assets/Scripts/map/Transition System/MapTileColorTransitionSystem.Feedback.cs",
    "Assets/Rendering/WhiteBoxCalibratedBiomes/Resources/Shaders/WhiteBoxMoebiusRim.shader",
    "Assets/Rendering/Moebius/Shaders/Materials/MoebiusNPR.shader",
    "Assets/Rendering/Moebius/Shaders/Materials/MoebiusSnowMountain.shader",
    "Assets/Rendering/Moebius/Shaders/Materials/MoebiusVolcanoRockBaked.shader",
]
NEW = [
    ".gitattributes",
    "Assets/Resources/V61Bioweb13.json",
    "Assets/Resources/V61Bioweb13.json.meta",
    "Assets/Scripts/Simulation/LandMovementOpportunity.cs",
    "Assets/Scripts/Simulation/LandMovementOpportunity.cs.meta",
    "Assets/Scripts/Simulation/SimulationController.EcologyLedger.cs",
    "Assets/Scripts/Simulation/SimulationController.EcologyLedger.cs.meta",
    "Assets/Scripts/Simulation/V61BiowebValidator.cs",
    "Assets/Scripts/Simulation/V61BiowebValidator.cs.meta",
    "Assets/Rendering/Moebius/Editor/Inspector/MoebiusSurfaceDebugView.cs",
    "Assets/Rendering/Moebius/Editor/Inspector/MoebiusSurfaceDebugView.cs.meta",
]

patch = subprocess.run(
    ["git", "diff", BASE, "--binary", "--no-ext-diff", "--", *TRACKED],
    cwd=ROOT, check=True, stdout=subprocess.PIPE).stdout
patch_path = WORK / "patch" / "v61-current-head.patch"
patch_path.parent.mkdir(parents=True, exist_ok=True)
patch_path.write_bytes(patch)

payload: dict[str, bytes] = {
    "patch/v61-current-head.patch": patch,
    "changed-paths.txt": ("Modified:\n" + "\n".join(TRACKED) +
                          "\n\nAdded:\n" + "\n".join(NEW) + "\n").encode(),
}
for relative in NEW:
    payload["new-files/" + relative] = (ROOT / relative).read_bytes()
for path in WORK.rglob("*"):
    if not path.is_file() or any(part in ("bin", "obj", "patch") for part in path.parts):
        continue
    if path.name in ("build_delivery.py", "DarwinFarm_V61_Integration.zip"):
        continue
    payload["evidence/" + path.relative_to(WORK).as_posix()] = path.read_bytes()
payload["build_delivery.py"] = Path(__file__).read_bytes()

manifest = "".join(sha256(data).hexdigest() + "  " + name + "\n"
                   for name, data in sorted(payload.items()))
payload["manifest.sha256"] = manifest.encode()
output = WORK / "DarwinFarm_V61_Integration.zip"
with ZipFile(output, "w", compression=ZIP_DEFLATED, compresslevel=7) as archive:
    for name, data in sorted(payload.items()):
        archive.writestr(name, data)
print(output)
print(f"entries={len(payload)} bytes={output.stat().st_size} sha256={sha256(output.read_bytes()).hexdigest()}")
