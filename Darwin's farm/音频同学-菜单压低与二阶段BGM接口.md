# 音频对接：菜单音量抑制与二阶段 BGM

## 游戏侧已完成

统一调度入口是 [`Assets/Scripts/Audio/DarwinAudioDispatch.cs`](Assets/Scripts/Audio/DarwinAudioDispatch.cs)。它只报告游戏状态，不指定衰减分贝数、淡入淡出时间或音乐内容；这些由 Wwise 工程决定。

| 信号 | 当前值 | 触发时机 |
| --- | --- | --- |
| `MenuDuckingChanged(bool)` | `IsMenuDucked` | 标题、暂停、设置、教程、信息、存档、确认菜单或物种图打开时为 `true`；最后一个菜单关闭时为 `false` |
| `GameplayChanged(bool)` | `IsGameplayActive` | 新局或读档进入游戏时为 `true`；离开游戏场景时为 `false`，可用于启动/停止 BGM |
| `MusicPhaseChanged(MusicPhase)` | `CurrentMusicPhase` | 首次达到 8 个已解锁物种时由 `First` 切到 `Second`；新局或读档时按实际进度重算 |
| `MusicVolumeChanged(float)` | `CurrentMusicVolume` | 设置中的“音乐音量”变化时发送，范围为 `0..1` |

`UnlockedSpeciesCount` 是当前已解锁的**不同物种资产**数量。使用 `HasEverUnlocked`，所以物种灭绝后仍计入；重复的解锁通知不会重复切歌。新游戏开始和读档完成后会从存档中的物种状态重算。这里的“二阶段”仅指这个音乐阶段，不是手机 UI 开发阶段。

菜单来源目前是 `DarwinJournalCanvas` 和 `SpeciesMapsUI`。两种菜单重叠时，必须等两者都关闭才恢复音量。将来新增全屏菜单，在打开/关闭处调用 `DarwinAudioDispatch.SetMenuOpen("唯一菜单名", true/false)`；对象销毁或禁用时也要传 `false`。

## 音频侧需要制作

1. 准备 BGM 的首阶段、二阶段素材和音乐 SoundBank（建议命名 `SB_Music`），提供开始播放的 Event（建议 `Play_BGM`）和必要的停止 Event。若使用 Wwise State 控制音乐，建议使用 `BGM_Phase` 组中的 `First`、`Second` 两个状态。具体切换节拍与交叉淡化在 Wwise 中配置。
2. 为菜单态设计混音抑制（例如音乐与环境声 Bus 的 State 音量）；建议使用 `UI_Mix` 组中的 `Gameplay`、`Menu`。请明确哪些声音要压低、压低多少 dB、恢复时间；通常 UI 点击声可以保持可听。
3. 将音乐音量 `0..1` 接到 Wwise BGM Bus 的音量参数（建议 RTPC 名为 `Music_Volume`，具体取值范围由双方约定）。游戏现有的 Master 设置已经作用于 Wwise 输出；音乐滑杆原先只控制 Unity `AudioSource`，现在增加了独立信号供 Wwise BGM 接入。
4. 为 Windows 与 Android 都生成新的 SoundBanks，至少确保包含音乐 Event、相关 State/RTPC 和媒体文件。游戏目前只有 `SB_Ambience`，因此交付音乐资源之前不会播放新 BGM，也不会实际压低 Wwise 声音。

上面的 Wwise 对象名是双方对接约定，音频侧如果采用其他命名，请在合入前同步给程序侧。

## 程序接线方式

在音频桥接组件启用时先读取当前菜单、游戏、音乐阶段和音乐音量状态，再订阅事件。不要只依赖事件，因为桥接组件可能在菜单已经打开或读档已经恢复后才初始化。
下面的 `Apply*` 方法是桥接组件需要实现的接点；调度层不会在 Wwise 对象尚未交付时发送不存在的 State 或 Event。

```csharp
private void OnEnable()
{
    DarwinAudioDispatch.MenuDuckingChanged += ApplyMenu;
    DarwinAudioDispatch.GameplayChanged += ApplyGameplay;
    DarwinAudioDispatch.MusicPhaseChanged += ApplyPhase;
    DarwinAudioDispatch.MusicVolumeChanged += ApplyMusicVolume;
    ApplyMenu(DarwinAudioDispatch.IsMenuDucked);
    ApplyGameplay(DarwinAudioDispatch.IsGameplayActive);
    ApplyPhase(DarwinAudioDispatch.CurrentMusicPhase);
    ApplyMusicVolume(DarwinAudioDispatch.CurrentMusicVolume);
}

private void OnDisable()
{
    DarwinAudioDispatch.MenuDuckingChanged -= ApplyMenu;
    DarwinAudioDispatch.GameplayChanged -= ApplyGameplay;
    DarwinAudioDispatch.MusicPhaseChanged -= ApplyPhase;
    DarwinAudioDispatch.MusicVolumeChanged -= ApplyMusicVolume;
}
```

`ApplyMenu` 可映射为 Wwise `UI_Mix` State；`ApplyPhase` 可映射为 `BGM_Phase` State；`ApplyGameplay` 决定何时播放/停止音乐。等 SoundBank 加载且 Wwise 引擎初始化后，再调用 `AkUnitySoundEngine.SetState(...)` 和播放 BGM Event；菜单音量渐变和音乐过渡由 Wwise 内容决定。Wwise Unity API 支持设置 State，Bus 也可应用 State 音量。[Wwise Unity 组件说明](https://www.audiokinetic.com/ko/public-library/2025.1.4_9062/?id=pg__wwise_components.html&source=Unity)、[Bus 属性说明](https://www.audiokinetic.com/ja/library/edge/?id=defining_properties_of_bus&source=Help)。

## 验收点

- 标题进入新游戏：菜单抑制状态从 `true` 变 `false`。
- 游戏中打开暂停菜单，再进入设置，返回暂停，最后恢复游戏：只在最初打开和最终关闭时切换抑制状态。
- 打开/关闭物种图：抑制状态正确切换；它与其他菜单重叠时不提前恢复。
- 从 7 个不同已解锁物种变成 8 个：只切到二阶段一次；物种灭绝不退回。
- 读入已有 8 个以上解锁物种的存档：进入游戏时直接为二阶段；新局重新回首阶段。
- Windows、Android 的构建中都能加载音乐 SoundBank，且音乐滑杆、菜单压低、阶段切换均可听辨。
