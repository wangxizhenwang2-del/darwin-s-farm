# 前景水雾顶点动画调整

前景主水雾增加了上缘起伏和向外呼吸；贴水泡沫增加了下游边缘的涨退与局部形变。主水雾底部、贴水泡沫靠水帘的一侧保持稳定，避免整块上下跳动或脱离水面。时间连续，无新增噪声贴图、C# 动画或渲染 Pass。

## 调整后的材质值

| 参数 | 主水雾 SplashLayer2 | 贴水泡沫 SplashLayer3 |
|---|---:|---:|
| Breathing Speed | 1.3 | 1.1 |
| Breathing Amplitude | 0.045 | 0.055 |
| Wobble (Local Metres) | 0.09 | 0.055 |
| Wobble Speed | 1.15 | 1.15 |

主层加入受高度 Mask 限定的垂直起伏；前层使用下游 Mask，让前缘更明显地向外推开和收回，并保持上游连接处稳定。后方两层参数保持原值。现有 Mesh、场景布局、颜色、落水线绑定及水帘没有改动。

修改文件：
- Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallSplashMotion.hlsl
- Assets/Rendering/Moebius/Samples/Waterfall/Materials/SplashLayer2.mat
- Assets/Rendering/Moebius/Samples/Waterfall/Materials/SplashLayer3.mat
- Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs（同步新建样例的默认值，避免以后生成时恢复成弱动画）
- Assets/Rendering/Moebius/Samples/Waterfall/README.md
- Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs（检查实际轮廓变化；水平泡沫使用俯视测试）

独立 Unity 验证报告见 Report.txt。检查了连续动画、固定时间重复一致性、实际轮廓变化、Depth/Normals 与可见轮廓一致性、主光阴影及真实 Play Mode 落水线同步。

在材质 Inspector 的 Motion 分组中，可以继续调整 Breathing Amplitude 控制整体呼吸幅度、Wobble (Local Metres) 控制局部起伏、两个 Speed 控制快慢。编辑模式下需打开 Animate Edit Mode Preview 才能持续观察动画；Play Mode 使用正常游戏时间。