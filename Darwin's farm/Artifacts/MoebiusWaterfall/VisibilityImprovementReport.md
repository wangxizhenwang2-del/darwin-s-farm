# 冲击泡沫与涟漪可见度增强

根据用户反馈，调整现有参数与默认值，没有改变传播算法、水帘、云状水花、阴影或描边。之前泡沫范围大部分藏在云状 Mesh 下，细线又受到亚像素覆盖、强度和早期淡出的共同削弱，因此远景中不明显。

| 参数 | 之前 | 当前 |
|---|---:|---:|
| Impact Radius / Half Depth | 1.05 m | 2.1 m |
| Turbulence Strength | 0.8 | 0.95 |
| Foam Density | 0.58 | 0.68 |
| Impact Foam Color | RGB 0.82/0.93/0.93 | RGB 0.90/0.97/0.96 |
| Ripple Width | 0.024 m | 0.065 m |
| Ripple Intensity | 0.52 | 0.85 |
| Ripple Color | RGB 0.66/0.82/0.84 | RGB 0.86/0.95/0.95 |
| Arc Breakup | 0.67 | 0.58 |
| Distance Fade | 最后 55% 范围 | 最后 40% 范围 |
| Lifetime Fade | 最后 45% 寿命 | 最后 35% 寿命 |

泡沫从云状水花下方延伸到水面，浅青白色与原水色形成更清楚的对比；涟漪约加粗 2.7 倍，断续弧线更容易辨认。稍推迟淡出，使中距离仍能看到传播过程，末尾仍逐渐消失。没有恢复常驻的黄色闭合边框。

修改文件（相对于 `D:/GameAssignment/darwin-s-farm/Darwin's farm`）：

- `Assets/Rendering/Moebius/Samples/Waterfall/Materials/LowerWater.mat`：现有样例材质应用新参数。
- `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWater.shader`：同步可选冲击模块的默认参数，开关仍默认 Off。
- `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallImpact.shader`：同步旧 Mesh 入口的参数默认值，样例仍不启用旧贴片。
- `Assets/Rendering/Moebius/Samples/Waterfall/README.md`：更新推荐参数。
- `Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs`：沿用现有检查，渲染测试读取样例实际强度，不再硬编码旧强度。
- 本报告和原报告入口提示；实际截图与 `Report.txt` 已更新。

在隔离的 Unity 6000.2.9f1 / URP 17.2.0 项目重新编译、渲染和检查：**51 项 PASS，零错误**。单波测试一秒平均传播距离 1.75 → 2.83 米；周期切换最大通道变化 1/255；中心与涟漪在每次 1/60 秒采样均连续变化；水帘与水花的阴影验证继续通过。以上不代表实际游戏帧率保证。

已观察 `Reopened.png`、`Closeup.png`、`IntegratedImpact.png`，远景和近景泡沫/弧线均更清楚。调整前截图位于 `BeforeVisibility`，可与当前截图对比。原有正交视角水面外边缘描边斑点仍存在，本次未修改其模块。场景和 Prefab 本次未重写，只调整已有水材质；资产 GUID 保留。
