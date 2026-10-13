# V4.6 进化与食物供给设计审计

当前目标已收窄为鸡→彩鸡→孔雀和鸡→灵感菇狸→鹿→鹿驯，共5个方向。此前18方向实验属于历史筛选，见 Results/LAND_SCREEN_HISTORY.md。最新结论与灵感菇狸消失的捕猎账本诊断见 Results/REPORT.md。

```powershell
dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release -- --mushroom-cause --evolution-only
python Tools/EvolutionDesignAudit/build_chicken_report.py
```

--mushroom-cause 对比正常结算与只暂停新灵感菇狸性状选择的人工诊断。暂停性状不是玩家操作，也不计为路线成功。整只捕猎修正已落实；后期小群注销仍待设计决策。

正式网络：20 条进化边、40 个方向、25 条食物关系。
陆地进化验证范围：9 条边18方向，排除两条涉及飞狸的边；全网食物检查仍包含它们。
已批准删除鸡↔鹿、鸡↔孔雀及鲨母↔虾兔，保留同格已有目标物种禁止转化。

从 Unity 项目目录执行：

```powershell
dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release
python Tools/EvolutionDesignAudit/build_report.py
python Tools/EvolutionDesignAudit/build_reachability_report.py
```

审计直接编译生产代码的环境、生命结算、性状选择、分化、迁徙和预设进化模块。
物种数值读正式 Unity 资产，进化网和食物网读项目内的正式 JSON；启动时检查资产与 JSON 的运动、体型、繁殖值一致。

默认执行：

- 18 个陆地方向的结构记录与模板取值边界检查。
- 全网40个方向的替代猎物及完整当地食物链检查。
- 36 个单日植物资源实验。
- 植物预算修正的预测一致性、长期补给与容量、多人群共享账本回归检查。
- 小捕食者能够结算整只猎物并把多余能量存入储备的回归检查。
- 陆地捕食者实际当地供给实验。

植物预算修正及回归已完成。两处唯一猎物与目标冲突已删除；多数陆地捕食者尚未找到满足维护标准的配置，详见 [REPORT.md](Results/REPORT.md)。
加 --single-operations 可对当前第一条陆地边进行单项操作实验。

当地供给实验只关闭正式物种转化，正常性状选择、生命结算及原地分化保留；试验不代表完整进化路线。

单日资源实验使用人工配置的隔离状态，只用来定位规则原因。
first-edge-single-operations、target-presence-probes、food-graph-candidates及baseline文件为鸡↔孔雀删除前的历史证据，数字不代表当前网络。

无画面审计中的随机数来自既有 UnityStubs 的 System.Random，序列与 Unity 不一致。当前结果属于结构与初步筛选证据，不是 Unity 中的最终成功率；候选流程确定后必须用 Unity 随机源校准或实机复测。实际运行时间与 CPU 相关，不作为性能保证。

加 --all-directions 对全部18个陆地方向做单项操作及少量组合操作筛选，结果写入 land-direction-screen.json。加 --evolution-only 可跳过重复的当地供给实验。
