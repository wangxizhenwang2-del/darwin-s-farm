"""Summarize the current network; historical route experiments are retained."""
import json
from collections import Counter
from pathlib import Path
OUT = Path(__file__).resolve().parent / "Results"
d = json.loads((OUT / "local-food-support.json").read_text(encoding="utf-8"))
diag = json.loads((OUT / "local-food-diagnostics.json").read_text(encoding="utf-8"))
assert d["formalEvolutionEdges"] == 20 and d["selectedLandDirections"] == 18
assert d["foodEdges"] == 25 and len(d["rows"]) == 40
assert not any({r["from"], r["to"]} == {"鸡", "孔雀"} for r in d["rows"])
lines = [
"# V4.6 生物网替代猎物与当地供给检查", "",
"2026-10-12。已按用户决定删除鸡↔孔雀及鲨母↔虾兔，保留同格已有目标物种时禁止转化。正式网络为20条双向进化边、40个方向、25条食物关系。本次陆地进化范围为9条边18方向。", "",
"全网检查了非目标合法猎物、当地食物空间兼容性，以及排除目标后能否追溯到完整的生产者食物链。结果区分如下。", "",
"| 结果 | 方向数 |", "|---|---:|"]
for status, n in Counter(r["status"] for r in d["rows"]).items():
    lines.append(f"| {status} | {n} |")
lines += ["", "## 明确的结构缺口", "",
"鸡↔孔雀及鲨母↔虾兔已删除，当前网络不再包含这两处唯一猎物就是转化目标的结构冲突。", "",
"其余捕食者方向都有非目标合法猎物及不含目标的完整食物链。这项结构结果不等于已证明长期维持或进化可达。", "",
"## 实际当地供给实验", "",
f"检查了{d['supplyCases']:,}个候选配置，每次最多600日。单一猎物配置覆盖27种生境、源群20/50只、不同猎物数量和库存；再检查不含目标且食物关系完整的五物种群落，每个群落取按气候与供给排序的前8个生境。", "",
"实验仅关闭正式物种转化，以隔离供给；正常性状选择、原地分化、生命结算仍用生产代码。初始性状均为正式模板。验收要求源物种及所用猎物持续至少10只；五物种实验还要求全部初始物种保持至少10只。末100日猎手平均饱食≥90%，猎物净补充为正，猎物数量下降≤20%。这是有限时段的维护定义，不代表永久稳态。", "",
"当前找到鹿驯由鸡供食的600日配置，可支持源鹿驯向鹿、鲨鹿、鹿飞、雀狮、雪狼这5个方向的食物前提。跨生态位目标只确认源鹿驯当地供给，完整转化路径仍待接入。"]
seen = set()
for r in d["rows"]:
    w = r["witness"]
    if w and (w["predator"],w["prey"]) not in seen:
        seen.add((w["predator"],w["prey"]))
        lines += ["", f"供给见证：{w['habitat']}，初始{w['initialPredator']}只{w['predator']}、{w['initialPrey']}只{w['prey']}，库存{w['initialStock']:,}。末100日最少{w['minimumPredatorLast100']}只猎手和{w['minimumPreyLast100']}只猎物，平均饱食{w['averageSatisfactionLast100']:.2%}。"]
lines += ["",
"其余13个陆地捕食者方向尚未找到符合维护标准的配置，不能判为满足。这不证明所有合法操作都无法维持：尚未搜索持续的温湿/植物反馈控制、不同储备初值、任意种群数量或所有群落组合。水域与空中的9个捕食者方向只有结构证据，完整生命结算尚未接入。", "",
"| 猎手／猎物 | 单猎物配置最长日数 | 满足600日维护标准 |",
"|---|---:|---|"]
for r in diag:
    lines.append(f"| {r['predator']}／{r['prey']} | {r['longest']} | {'是' if r['witnessFound'] else '否'} |")
lines += ["", "## 新的维护风险与暂停建议", "",
"孔雀／鸡最长单猎物案例：阴热雨林，初始孔雀50、鸡1031、库存50w；第500日孔雀264、鸡2961；第550日孔雀569、鸡1322；第581日孔雀跌至7，鸡仍388。发生了猎手分化及猎物性状变化。跌破10只不等于已经触发连续10日的濒危注销。", "",
"替代猎物存在仍可能发生供给失稳，捕食者增长、分化和猎物可捕获性需共同审查。建议下一步单独实验：捕食者繁殖同时参考猎物净补充与当前饱食；分化后重新核对完整成年及幼体需求与猎物更新速度。这些是待验证的规则建议，未改动捕食/繁殖规则，也不据此直接调整动物模板。", "",
"## 每个捕食者方向的检查结果", "",
"食物链按猎物→捕食者书写，每条均排除了本行目标。", "",
"| 转化方向 | 非目标合法猎物 | 当地食物链举例 | 结果 |", "|---|---|---|---|"]
for r in d["rows"]:
    if r["sourceTrophicLevel"] > 0:
        lines.append(f"| {r['from']}→{r['to']} | {'、'.join(r['alternatives']) or '无'} | {'；'.join(r['completeFoodPaths'][:2]) or '无'} | {r['status']} |")
lines += ["", "## 验证与复现", "",
"批准的植物预算修正继续生效，36个资源状态、实际与预测一致性、共享账本守恒、库存不提高长期补给及承载量的回归检查通过。审计读取正式资产和JSON并校验引用一致，直接编译生产规则。无画面版使用System.Random，序列与Unity不同，不能据此声称最终高概率进化。", "",
"运行 dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release，再运行 python Tools/EvolutionDesignAudit/build_report.py。", "",
"当前证据为local-food-support.json、local-food-diagnostics.json、structure.json和stock-buffer-regression.json。first-edge-single-operations、target-presence-probes、food-graph-candidates及baseline文件属于鸡↔孔雀删除前的历史证据。"]
(OUT / "FOOD_SUPPORT.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(OUT / "FOOD_SUPPORT.md")
