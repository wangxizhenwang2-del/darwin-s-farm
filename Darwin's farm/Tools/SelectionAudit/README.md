# 陆地选择与稳态回归

运行 `dotnet run --project Tools/SelectionAudit/SelectionAudit.csproj -c Release`。此项目直接编译 `Assets/Scripts` 中的正式模型源码，不使用试算副本，也不改变 Unity 的 JSONL 导出接口。

默认回归覆盖：出生系数 0.1、变异步长 1／周期 1 天；单群鸡的 90% 环境 K 分化；同格两支鸡和直接同格鸡鹿的第 5000～20000 天数量、性状和植物库存稳态；高初始密度下库存不清零；出生系数 0.05 的对照；零更新量时库存可消耗；温湿改变后的趋向。测试没有模拟玩家操作或鸡实际转鹿，Unity Play 模式及四物种链仍按主测试清单验收。
