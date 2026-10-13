"""Active five-direction scope and food-versus-conversion diagnosis."""
import json
from pathlib import Path

OUT = Path(__file__).resolve().parent / "Results"
data = json.loads((OUT / "mushroom-cause-diagnostics.json").read_text(encoding="utf-8"))
regression = json.loads((OUT / "whole-prey-regression.json").read_text(encoding="utf-8"))
normal = next(r for r in data if not r["freezeNewPredatorTraits"])
frozen = next(r for r in data if r["freezeNewPredatorTraits"])
assert normal["successDay"] == frozen["successDay"] == 226
assert len([e for e in normal["events"] if e["kind"] == "evolution"]) == 1
d = next(r for r in normal["days"] if r["day"] == 232)
before = d["before"][0]
prey = before["prey"][0]
claim = prey["wholePreyClaim"]
assert claim >= 1 and regression["predationDeathsToday"] >= 1
normal_counts = [(r["day"], sum(p["count"] for p in r["after"]
                  if p["species"] == "灵感菇狸")) for r in normal["days"]]
extinct_day = next(day for day, count in normal_counts if count == 0)
lines = ["# 从鸡开始的五步进化验证", "",
"2026-10-12。当前目标改为鸡→彩鸡→孔雀与鸡→灵感菇狸→鹿→鹿驯（用户称驯鹿），共5个方向。后续必须使用前一步实际产生的种群，不能把模板动物的隔离单步成功算成整条路线成功。正式网络为20条双向进化边，25条食物关系；鸡↔鹿、鸡↔孔雀、鲨母↔虾兔已删除。", "",
"## 当前进展", "",
"| 方向 | 已有证据 | 当前验收状态 |", "|---|---|---|",
"| 鸡→彩鸡 | 隔离场景单步流程通过90个扰动样本及转化后100日维护 | 待按最新鸡起点验证前后衔接 |",
"| 彩鸡→孔雀 | 既有单项及组合筛选未找到直接转化 | 未通过 |",
"| 鸡→灵感菇狸 | 能直接转化；整只捕猎缺陷已修复，已消除原来的立即断粮 | 第333日仍因小群连续低于10只而注销，未达到长期稳定 |",
"| 灵感菇狸→鹿 | 既有单项及组合筛选未找到直接转化 | 未通过 |",
"| 鹿→鹿驯 | 孤立场景中数量和时机敏感，30/90成功并维持100日 | 待优化并验证前后衔接 |", "",
"既有18方向筛选是历史证据，见LAND_SCREEN_HISTORY.md，不能据此声称最新两条完整路线已通过。", "",
"## 已落实的整只捕猎修正", "",
"复现场景为干燥荒漠640只模板鸡，邻接新建阴热雨林，种子1。第226日发生鸡→灵感菇狸，生成10只。全部事件记录只有这一次正式物种转化，没有灵感菇狸→鸡或灵感菇狸→鹿。", "",
"实际捕猎现在先按捕获能力计算申请量，再把覆盖剩余能量所需的猎物数向上取整；两者取小值并与猎物的捕食小数合并，只有最后扣除猎物时才向下取整。最后一只猎物的多余能量进入原有储备。进化前的捕食者食物验收使用同一整只口径。", "",
"| 修正后第232日结算前指标 | 数值 |", "|---|---:|",
f"| 灵感菇狸数量 | {before['count']} |",
f"| 当地合法鸡猎物 | {prey['count']} |",
f"| 当日成年及预期幼体能量需求 | {before['demand']:.3f} |",
f"| 已有能量储备 | {before['reserve']:.3f} |",
f"| 含额外10%成年储备的本次捕猎目标 | {before['targetEnergy']:.3f} |",
f"| 每只鸡能提供的捕猎能量 | {prey['preyEnergy']:.3f} |",
f"| 未按能量目标限额前的期望捕获数 | {prey['expectedAttempts']:.6f} |",
f"| 整只口径的捕猎申请 | {claim:.6f}只 |",
f"| 之前累积的捕猎小数 | {prey['remainder']:.6f}只 |",
f"| 合计申请加余数 | {claim + prey['remainder']:.6f}只 |",
"| 实际捕获数 | 1只 |", "",
"修正后当日获得428.571能量，饱食度100%，死亡0只，数量保持11只。独立回归也用11只灵感菇狸、94只鸡和原故障储备重放：捕获1只，当日饱食度100%，死亡0只。原来的“有足够猎物却因0.79只向下取整而断粮”已避免。", "",
"### 性状变化隔离实验", "",
"对照实验只在新灵感菇狸生成后暂停该群性状选择，保持适温75、适湿80、运动35、体型20、繁殖65；鸡的性状、环境、生命与食物结算继续正常。该群仍然消失。因此性状变化不是原整数断粮的必要原因，也不能靠冻结性状获得稳定路线。", "",
"## 长期复测发现的另一问题", "",
f"修正后的正常群没有迅速饿死，但在第{extinct_day}日当地注销。第314日首次降到9只后曾回到10只；第324日起连续低于10只，第333日达到现行连续10日注销条件。第324—332日饱食度每天均为100%，当地鸡从143只增至154只，消失不再是食物不足。", "",
"这次后期消失来自10只创始群接近注销线：满饱食时自然死亡与出生取整仍会让数量在8—10只间波动，连续10日低于10只便整群删除。整只捕猎修正解决了原故障，但尚未保证五步路线的长期稳定。暂不连带修改注销门槛、创始数量或出生死亡规则，需先确定希望哪一项承担保护新物种的职责。", "",
"下一步应针对鸡→灵感菇狸增加创始数量或新物种保护期做单项比较，再继续五步路线；每一步分别验收直接转化和后续100日维持，并扰动数量、操作时机与迁徙随机种子。", "",
"## 证据与复现", "",
"原始每日账本见mushroom-cause-diagnostics.json；定点回归见whole-prey-regression.json；转化和容错历史见land-direction-screen.json、land-direction-robustness.json。工具编译真实生产生命和进化模块；随机数沿用System.Random桩，不是Unity随机序列，因此尚无实机高概率保证。", "",
"```powershell",
"dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release -- --mushroom-cause --evolution-only",
"python Tools/EvolutionDesignAudit/build_chicken_report.py", "```", ""]
(OUT / "REPORT.md").write_text("\n".join(lines), encoding="utf-8")
print(OUT / "REPORT.md")
