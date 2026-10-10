# Moebius Lambert Color Ramp 实现报告

已给现有 `Darwin/Moebius NPR` 增加一套可选的 Lambert 双色程序化 Tint。默认关闭，没有修改任何现有材质资产、场景、Renderer Feature 或描边材质。

## 读取与架构确认

修改前读取了 NPR Shader 的完整四个 Pass、NPRInput / NPRLighting / NPRHatching / NPRDetail include、相机 Outline / Print、Renderer Feature 和现有材质参数。项目没有为 Moebius 指定 CustomEditor，也没有 Moebius ShaderGUI；原界面由 Shader Properties 与默认材质 Inspector 提供。本次沿用该界面。

原 Shader 已有独立的 `_UseRamp` / `_LightingRamp` 贴图功能。为了兼容保留它的名称与行为；新增的 `Enable Color Ramp` 不采样这张贴图，也不添加模式选项。

## 文件改动

| 文件（相对项目根） | 改动 |
| --- | --- |
| Assets/Rendering/Moebius/Shaders/MoebiusNPR.shader | Properties 末尾增加一组 Moebius Lambert Color Ramp 参数；Fragment 与各 Pass 函数体不变。 |
| Assets/Rendering/Moebius/Shaders/MoebiusNPRInput.hlsl | 在共享 UnityPerMaterial CBUFFER 末尾添加两个颜色和四个标量；所有 Pass 使用一致布局。没有添加纹理、采样器或顶点数据。 |
| Assets/Rendering/Moebius/Shaders/MoebiusNPRLighting.hlsl | 增加 ApplyLambertColorRamp 函数；现有 NdotL 计算后调用一次，只改变进入原颜色计算的局部 baseColor。 |
| Assets/Rendering/Moebius/BiomeTests/Editor/MoebiusRampThresholdDrawer.cs | 新阈值的 PropertyDrawer；编辑时将两个阈值限制在 0～1，保证 Shadow < Light，支持撤销与多材质编辑。不替换原 Inspector，也不处理旧参数。 |
| Assets/Rendering/Moebius/BiomeTests/Editor/MoebiusColorRampValidation.cs | 隔离项目验证，临时克隆现有 Ivory 材质；记录前后颜色、效果遮罩、光照与物体旋转、双色端点及数据 Pass。不会保存场景或材质。 |
| Tools/ValidateMoebiusColorRamp.ps1 | 串行启动隔离 Unity 验证；Before 使用保留的实际修改前 Shader 源码。 |
| Artifacts/MoebiusColorRamp | 报告、源码基准和前后 PNG。图片位于 Assets 外，不作为纹理或 Cubemap 导入。 |

新 Editor 脚本配有 .meta。NPRHatching、NPRDetail、Outline、Print 的源码哈希与修改前一致；ShadowCaster、DepthOnly、DepthNormals 源码文本一致。

## Lambert 与颜色流程

原有 Lambert 位于 NPRLighting 的 GetToonLighting：

```hlsl
result.ndotl = saturate(dot(normalWS, mainLight.direction));
```

新增函数直接复用 `result.ndotl`，不再次获取主光、不重复点积，也不把 NdotL 额外乘到颜色上。输入为表面朝向对应的 NdotL，不使用包含投影遮挡的 `result.brightness`。所以冷暖色面跟随表面与主光方向，投影继续由原来的明暗与排线表现。

```hlsl
if (_EnableColorRamp <= 0.5 || _RampStrength <= 0) return baseColor;
float shadowThreshold = clamp(_RampShadowThreshold, 0, 0.999);
float lightThreshold = clamp(_RampLightThreshold, shadowThreshold + 0.001, 1);
float rampMask = smoothstep(shadowThreshold, lightThreshold, saturate(ndotl));
half3 rampColor = lerp(_RampShadowColor.rgb, _RampLightColor.rgb, rampMask);
half3 rampTint = lerp(half3(1,1,1), rampColor, saturate(_RampStrength));
return baseColor * rampTint;
```

Shadow Threshold 是离开阴影色的起点；Light Threshold 是完全进入受光色的终点。低于起点时 Mask 为 0，高于终点时为 1，只有两者之间使用 smoothstep 柔和过渡。默认区间 0.4～0.55，因此保留大块稳定色面，不让整个 Lambert 范围都连续渐变。

第一次 lerp 根据 Mask 混合冷色与暖色；第二次 lerp 根据 Strength 将该颜色从白色乘法单位值逐渐加入。颜色参数按 Unity Color 属性的规则传入，Alpha 不参与计算。

应用位置在 GetToonLighting 内、原阴影与受光颜色混合前。此时输入底色已完成 Base Map × Base Color 和现有 Paper Lift。局部底色乘 Tint 后，继续原来的阴影、受光提亮与可选旧贴图 Ramp。原始材质 Base Color 不被修改；排线、点描和白色高光依然在后续按原顺序合成，墨色与高光颜色不被整体染色。

因为是底色乘法，不同 Albedo 特征会保留，不会直接用同一组粉紫色替换所有材质。很深或很饱和的底色会限制冷暖色偏的可见程度，这是乘法 Tint 的正常表现。

## 默认与测试参数

| 参数 | 新 Shader 默认 | 隔离验证的临时参数 |
| --- | --- | --- |
| Enable Color Ramp | Off | On |
| Shadow Ramp Color | (0.94, 0.92, 1) | (0.9, 0.85, 1) |
| Light Ramp Color | (1, 0.98, 0.94) | (1, 0.94, 0.87) |
| Shadow Threshold | 0.4 | 0.4 |
| Light Threshold | 0.55 | 0.55 |
| Ramp Strength | 0.35 | 0.4 |

开关关闭与强度为 0 均直接返回原色，不是通过很小的混合值近似原色。运行时从脚本设置超界或反向阈值，Shader 仍会使用有序且至少相差 0.001 的安全区间；Inspector 修改新阈值时同步修正存储值。

## 验证结果

Unity 6000.2.9f1 / URP 17.2 的当前桌面渲染环境，256×256 离线图像比较，Shader/运行/断言错误为 0。详细记录见 After/Report.txt。

- 修改前与新 Shader 默认关闭：0 个变化像素。
- 修改前与配置非白双色、仍关闭开关：0 个变化像素。
- 修改前与开关开启、Strength = 0：0 个变化像素。
- 启用 Strength = 0.4 后颜色产生变化，主光旋转后画面响应。
- 隔离颜色端点测试：低 NdotL 输出阴影色 Tint，高 NdotL 输出受光色 Tint；固定灯光、旋转平面后由受光 Tint 进入阴影 Tint。
- 表面法线、NdotL、明暗、排线、点描、高光、投影的原调试遮罩，修改前后及 Ramp 开关前后逐像素一致。
- 相机最终描边遮罩逐像素一致；相机描边 Shader 与 Renderer Feature 未改动。
- DepthOnly 和 DepthNormals 的直接 Pass 输出均实际覆盖 28,680 个非黑像素，前后比较及开关比较一致。
- ShadowCaster 执行无错误、源码不变，场景投影遮罩前后相同。该 Pass 的 ColorMask 为 0，不能通过它的颜色截图直接证明深度内容；投影结果由场景阴影遮罩检查。
- 原 Base Color 参数未变；非法阈值经 Inspector 校验后满足严格大小关系。

验证截图使用仅存在于 Editor 隔离测试中的临时 RenderTexture 读取像素；新增 Ramp 的游戏运行路径没有 RenderTexture、Render Pass、Renderer Feature、屏幕空间处理或额外纹理采样。测试不代表所有平台的构建验证或性能基准。

## 简化流程

```text
Surface Normal + Main Light Direction
                 ↓
Existing Lambert / NdotL
                 ↓
smoothstep(Shadow Threshold, Light Threshold, NdotL)
                 ↓
Ramp Mask
                 ↓
Shadow Ramp Color ↔ Light Ramp Color
                 ↓
Ramp Tint = lerp(white, rampColor, Ramp Strength)
                 ↓
Existing Base Color（Base Map × Base Color，已应用原 Paper Lift）× Ramp Tint
                 ↓
Existing NPR Lighting
                 ↓
Existing Hatching / Stippling / Highlight
                 ↓
Final Surface Output → Existing Camera Outline
```
