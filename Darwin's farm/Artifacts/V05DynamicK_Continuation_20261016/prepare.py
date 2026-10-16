"""Stage source and inputs. All ecological calculations run in Unity."""
import hashlib, json, shutil
from pathlib import Path

B = Path(__file__).resolve().parent
ROOT = B.parent.parent
OLD = B.parent / 'V05DynamicK_20261016'
ISO = ROOT.parent / 'DynamicK_Unity_Isolated_20261016'
manifest = json.loads((OLD / 'checkpoint-final-candidate/sha256.json').read_text(encoding='utf8'))
changed = [p for p, sha in manifest.items()
           if hashlib.sha256((ROOT / p).read_bytes()).hexdigest() != sha]
assert not changed, changed
for name in ('run_unity.py', 'analyze_results.py', 'FORMAL_CASES.json'):
    shutil.copy2(OLD / name, B / name)
shutil.copytree(OLD / 'inputs', B / 'inputs', dirs_exist_ok=True)
for rel in manifest:
    target = B / 'checkpoint-before' / rel
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / rel, target)
(B / 'checkpoint-before/sha256.json').write_text(json.dumps(manifest, indent=2))
rel = 'Assets/Scripts/Simulation/LandLifeRules.cs'
src = (ROOT / rel).read_text(encoding='utf-8-sig')
before = '''                float adult = count * AdultNeed(p.size, p.movementAbility, rules);
                float kept = Mathf.Min(p.energyReserve, adult * rules.reserveDays);
                block.accountedBiomassSinkToday += p.energyReserve - kept;
                p.energyReserve = kept;
'''
assert src.count(before) == 1
src = src.replace(before, '')
before = '''            float fitness = Fitness(block, p.fitTemperature, p.fitHumidity);
            float adultDemand = count * p.energyNeed;
            float satisfaction = adultDemand > 0f
                ? Mathf.Clamp01(p.allocatedBiomass / adultDemand) : 1f;'''
after = '''            // The existing reserve stores food already removed from a real
            // plant/prey ledger. Settle adults before fully paid newborns;
            // retain the unspent balance across days, with the existing cap.
            float savedReserve = p.energyReserve;
            float availableResources = p.allocatedBiomass + savedReserve;
            float fitness = Fitness(block, p.fitTemperature, p.fitHumidity);
            float adultDemand = count * p.energyNeed;
            float satisfaction = adultDemand > 0f
                ? Mathf.Clamp01(availableResources / adultDemand) : 1f;'''
assert src.count(before) == 1
src = src.replace(before, after)
before = '''            float availableForBirth = Mathf.Max(0f, p.allocatedBiomass
                - count * AdultNeed(p.size, p.movementAbility, rules));'''
assert src.count(before) == 1
src = src.replace(before, '''            float availableForBirth = Mathf.Max(0f,
                availableResources - adultDemand);''')
src = src.replace('p.adultPaidToday = Mathf.Min(p.allocatedBiomass, p.adultDemandToday);',
                  'p.adultPaidToday = Mathf.Min(availableResources, p.adultDemandToday);')
before = '''            block.accountedBiomassSinkToday += Mathf.Max(0f,
                p.allocatedBiomass - born * bodyPerNewborn);'''
after = '''            p.energyReserve = Mathf.Min(
                Mathf.Max(0f, availableResources - p.adultPaidToday -
                    born * newbornCost), adultDemand * rules.reserveDays);
            block.accountedBiomassSinkToday += Mathf.Max(0f,
                p.allocatedBiomass - born * bodyPerNewborn -
                (p.energyReserve - savedReserve));'''
assert src.count(before) == 1
src = src.replace(before, after)
target = B / 'candidate' / rel
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(src, encoding='utf8')
for path in manifest:
    shutil.copy2(ROOT / path, ISO / path)
shutil.copy2(target, ISO / rel)
(B / 'runs').mkdir(exist_ok=True)
(B / 'EXECUTION.md').write_text('''# Task 5 继续执行

2026-10-16 19:22 开始，承接用户“继续”：纳入此前提交的现有储备跨日付款方案进行正式验证。
仅修改 LandLifeRules 的结算，未接入此前撤回的遭遇率实验，未改模板、正式进化门槛、身体及幼体成本、玩家工具。
先在隔离 Unity 工程运行；旧阶段全部证据保留。
完整世界检查点恢复仍 BLOCKED，复用模拟天数为 0。
''', encoding='utf8')
print(json.dumps({'beforeFiles': len(manifest), 'changed': changed,
                  'stagedFile': rel, 'isolated': str(ISO)}, ensure_ascii=False))
