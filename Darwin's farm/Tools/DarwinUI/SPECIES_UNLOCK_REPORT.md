# 新物种解锁卡牌

## 接入位置

- `Assets/Scenes/WhiteBox.unity` 的 `JournalCanvas/SafeArea/SpeciesUnlockUI` 保存了遮罩、卡牌、剪影、立绘、问号、星星、文字和继续按钮。运行时不创建 UI 对象。
- `SpeciesUnlockPresenter` 监听已有的 `SimulationController.OnSpeciesUnlocked`，使用 `SpeciesData.Portrait`、名称和简介填充同一张卡牌。公开 `Show(Sprite, string, string, bool)` 和 `Show(SpeciesUnlockData)`，重复的物种 ID 在一局内只入队一次。
- `DarwinJournalCanvas.GameEntered(bool loadedSave)` 在进入游戏时通知卡牌。新游戏等待默认 3 秒，再按物种 ID 顺序展示已有种群；读档不重放开局卡牌。
- 每张卡的入场、剪影、揭晓、简介分别约 0.5、1.3、0.7、0.5 秒。使用不受 `Time.timeScale` 影响的 Coroutine。时长、开局延迟和缩放幅度在场景组件 Inspector 中配置。
- `OnRevealStarted` 在剪影开始淡出时触发一次。现有 `EcologyNewsHUD` 此时接收新物种消息；其日末检测器稍后发出的同一新物种新闻被抑制。其他生态新闻仍用原有队列。

## 素材

- 已导入提供的卡牌羊皮纸底图、卡牌边框、问号贴纸、星星贴纸，后两张按 3×2 切为 Sprite。TMP 负责所有文字。
- 生物立绘直接取已有的 `SpeciesData.Portrait`，剪影和揭晓使用同一张 Sprite。未生成新的生物图片。
- 灵感菇狸原资产简介为空，已填入本任务给出的测试简介。其他未填写简介的物种仍使用原项目的“暂无物种介绍”占位文字。

## 编辑器中体验

1. 打开 `Ye-Branch` 项目的 `Assets/Scenes/WhiteBox.unity`，进入 Play Mode，点击“新游戏”。约 3 秒后开始顺序展示开局物种。
2. 点击菜单 `Tools > Darwin's Farm > UI > Play sample species unlock`，可随时播放“灵感菇狸”测试卡；连续点击会排队。
3. 在 `SpeciesUnlockUI` 的 `SpeciesUnlockPresenter` 组件里可调整时长和开局延迟。物种正式解锁已由 `OnSpeciesUnlocked` 自动接入，无需修改生态判定代码。

## 检查

- `SpeciesUnlockSceneChecks.RunBatch`：场景层级、绑定的四类素材、1280×720、1152×720、960×720 和 405×720 安全区的卡牌边界检查。
- `SpeciesUnlockPlaySmoke.RunBatch`：开局延迟、剪影不提前暴露名称和立绘、揭晓回调与 HUD、简介与继续按钮、三张连续卡牌队列。
- `SpeciesUnlockTriggerSmoke.RunBatch`：通过现有 `RefreshSpeciesStatuses()` 产生真正的 `OnSpeciesUnlocked` 事件，确认卡牌立即入场而 HUD 等到揭晓才出现。
- 已运行 Unity 6000.2.9f1 的场景和播放模式批处理测试；另用 `dotnet build Assembly-CSharp-Editor.csproj` 编译。
- 尚未在实体手机、iPad 或玩家构建上做屏幕截图与触摸验收。现有 CanvasScaler 与安全区约束提供布局基础。
