"""Build one auditable ZIP from completed Unity runs. No simulation logic."""
import csv
import difflib
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
RUNS = ROOT / "Artifacts/Task9_20261016/runs"
OUT = HERE / "handoff"
INPUTS = HERE / "inputs"
OLD_ZIP = ROOT / "Artifacts/Task9_Acceptance_20261017/TASK9_ACCEPTANCE_PACKAGE.zip"
BASE = HERE / "source_package/task10/baseline/Assets/Scripts/Simulation"
PACKAGE = HERE / "INTEGRATED_UNITY_ACCEPTANCE_RESULT.zip"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write_diff():
    names = ["Assets/Scripts/Simulation/LandLifeRules.cs",
             "Assets/Scripts/Simulation/LandRuleTuning.cs",
             "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs"]
    sections = []
    with ZipFile(OLD_ZIP) as old_zip:
        for name in names:
            previous = (old_zip.read(name) if name.endswith("V048ScenarioReplay.cs")
                        else (BASE / Path(name).name).read_bytes())
            current = (ROOT/name).read_bytes()
            sections.append(f"# {name}\n# old SHA256 {digest(previous)}\n"
                            f"# new SHA256 {digest(current)}\n")
            a = previous.decode("utf-8-sig").splitlines(keepends=True)
            b = current.decode("utf-8-sig").splitlines(keepends=True)
            sections.extend(difflib.unified_diff(a,b,fromfile="Task9/base/"+name,
                                                  tofile="integrated/"+name))
            sections.append("\n")
    (OUT/"PATCH_DIFF.txt").write_text("".join(sections),encoding="utf-8")


def write_daily():
    with (OUT/"ROUTE_DAILY.csv").open("w",encoding="utf-8-sig",newline="") as f:
        writer=csv.writer(f)
        writer.writerow(("route","day","plantStock","recovery","chicken",
                         "target","targetCount","chickenBirths","chickenDeaths",
                         "chickenPredationDeaths","chickenQuota","chickenG0",
                         "chickenKeq","targetBirths","targetDeaths",
                         "targetReserve","totalMaterial","plantGrowthToday"))
        for name in ("int-color-s1","int-color-s2","int-mushroom-s1",
                     "int-mushroom-s2","int-mushroom-stop-s2"):
            path=RUNS/(name+".jsonl")
            for line in path.open(encoding="utf-8-sig"):
                if not line.startswith("{") or '"modelHash"' not in line:
                    continue
                row=json.loads(line)
                tile=row["tiles"][0]
                pop=tile["populations"]
                chicken=next((p for p in pop if p["species"]=="鸡"),None)
                target=next((p for p in pop if p["species"] not in ("鸡","鹿")),None)
                q=chicken.get("t10",{}) if chicken else {}
                writer.writerow((name,row["day"],tile["stock"],tile["recovery"],
                                 chicken["count"] if chicken else 0,
                                 target["species"] if target else "",
                                 target["count"] if target else 0,
                                 chicken["births"] if chicken else 0,
                                 chicken["deaths"] if chicken else 0,
                                 chicken["predationDeaths"] if chicken else 0,
                                 q.get("quota"),q.get("unhuntedGrowth"),
                                 q.get("equilibrium"),
                                 target["births"] if target else 0,
                                 target["deaths"] if target else 0,
                                 target["reserve"] if target else 0,
                                 row["totalMaterial"],row["plantGrowthToday"]))


def write_summary(result, regression):
    color=result["task9"]["color"]["s2"]
    civet=result["task9"]["mushroom"]["s2"]
    stopped=result["task9"]["mushroom"]["stopS2"]
    lines=[
        "# Task 9 + Task 10 Unity 联合验收\n",
        "Unity 6000.2.9f1 隔离工程；玩家费用关闭，出生与创始物质付款照常；E/Q 生产默认 OFF。\n",
        f"- Task 9：**{result['task9']['status']}**。彩鸡第 {color['firstDay']} 日正式转化 "
        f"{color['founders']} 只，付款 {color['plantPayment']}；第 +500 日 {color['counts']['plus500']} 只，"
        f"末100日 {color['late100Start']}→{color['late100End']}，持续衰退。菇狸第 {civet['firstDay']} "
        f"日正式转化 {civet['founders']} 只，付款 {civet['plantPayment']}；继续玩家操作的 S2 "
        f"第 {civet['targetFirstExtinctDay']} 日灭绝。\n",
        f"- Task 10：**{result['task10']['status']}**。C/D 5000日群落完成，L0 猎物配额违例 "
        f"{sum(x['on']['quota']['quotaViolations'] for x in result['task10']['communities'].values())}；"
        f"菇狸停止后续操作的 S2 在 +500 日 {stopped['counts']['plus500']} 只、"
        f"鸡 {stopped['sourceMinimum500']} 只最低值，但菇狸峰值 {stopped['targetMaximum500']} >1000。"
        f"同机 D 场景重复测试 ON/OFF 耗时比 {regression['performanceRatio']:.3f}。\n",
        "- 未通过时未调参。双猎手首次夹具因重复初始种群被 Unity 拒绝，改为菇狸与飞狸两个合法猎手后重跑。"
        "L2 捕食 L1 不受本候选配额保护，标为 OUT_OF_SCOPE。\n",
        "逐日原始 JSONL、Unity 日志、输入、源码差异和 SHA256 清单位于本 ZIP。\n"
    ]
    (OUT/"EXECUTION_SUMMARY.md").write_text("\n".join(lines),encoding="utf-8")


def write_provenance():
    package = HERE/"source_package"
    a = package/"task9/task9-mushroom-formal.json"
    b = package/"task10/Artifacts/Task9_20261016/inputs/task9-mushroom-formal.json"
    old = json.loads(a.read_text(encoding="utf-8-sig"))
    other = json.loads(b.read_text(encoding="utf-8-sig"))
    data={"formalMushroomPackageCopies":{
        "task9Path":str(a),"task9Sha256":digest(a.read_bytes()),
        "task10Path":str(b),"task10Sha256":digest(b.read_bytes()),
        "semanticJsonEqual":old==other,
        "selected":"task9Path",
        "selectedInputChanges":["scenarioId","t95FinalGate","t10Quota",
                                "t10Diagnostics"]},
          "initialTwoHunterFixture":{
              "status":"FAIL_FIXTURE",
              "reason":"Identical initial groups need a single population",
              "unityLocation":"SimulationController.Determinism.cs:173",
              "replacement":"Chicken + civet + flying civet, distinct legal hunters",
              "productionRulesChanged":False}}
    (OUT/"INPUT_PROVENANCE.json").write_text(
        json.dumps(data,ensure_ascii=False,indent=2),encoding="utf-8")


def build():
    result=json.loads((OUT/"INTEGRATED_RESULT.json").read_text(encoding="utf-8"))
    regression=json.loads((OUT/"REGRESSION.json").read_text(encoding="utf-8"))
    write_diff()
    write_daily()
    write_summary(result,regression)
    write_provenance()
    members=[]
    for item in sorted(OUT.iterdir()):
        if item.is_file() and item.name != "EVIDENCE_MANIFEST.json":
            members.append((item,item.name))
    for item in sorted(INPUTS.glob("int-*.json")):
        members.append((item,"inputs/"+item.name))
    run_names={p.stem for p in INPUTS.glob("int-*.json")}
    run_names.update(("stage0-final-deer10-rich","stage0-final-predator",
                      "T95E-community-D"))
    for name in sorted(run_names):
        for suffix in (".jsonl",".log",".execution.json"):
            item=RUNS/(name+suffix)
            if item.exists():members.append((item,"unity_runs/"+item.name))
    source_names=("Assets/Scripts/Simulation/LandLifeRules.cs",
                  "Assets/Scripts/Simulation/LandRuleTuning.cs",
                  "Assets/Scripts/Simulation/SimulationController.Evolution.cs",
                  "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs")
    for name in source_names:
        members.append((ROOT/name,"source/"+name))
    package = HERE/"source_package"
    for name in ("INTEGRATED_AGENT_TASK.md",
                 "task10/TASK10_DESIGN_AND_VERIFICATION.md",
                 "task10/TASK10_DYNAMIC_K_CANDIDATE.patch",
                 "task10/TASK10_SOURCE_MANIFEST.json"):
        members.append((package/name,"supplied_spec/"+name))
    members.append((HERE/"fixture_attempts/int-neg-two-hunters-duplicate.json",
                    "fixture_attempts/int-neg-two-hunters-duplicate.json"))
    manifest={"unityVersion":"6000.2.9f1",
              "isolationProject":str(ROOT.parent/"Task9_Unity_Isolated_20261016"),
              "files":[{"archivePath":dest,"originalPath":str(path),
                        "bytes":path.stat().st_size,"sha256":digest(path.read_bytes())}
                       for path,dest in members]}
    (OUT/"EVIDENCE_MANIFEST.json").write_text(
        json.dumps(manifest,ensure_ascii=False,indent=2),encoding="utf-8")
    members.append((OUT/"EVIDENCE_MANIFEST.json","EVIDENCE_MANIFEST.json"))
    with ZipFile(PACKAGE,"w",ZIP_DEFLATED,compresslevel=6) as z:
        for path,dest in members:
            z.write(path,dest)
    with ZipFile(PACKAGE) as z:
        invalid=z.testzip()
        if invalid:raise RuntimeError("ZIP CRC failure: "+invalid)
    print(PACKAGE,PACKAGE.stat().st_size,"bytes",len(members),"members")


if __name__=="__main__":
    build()
