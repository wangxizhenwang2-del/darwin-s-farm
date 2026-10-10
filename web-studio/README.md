# Darwin Atlas

独立于 Unity `Assets` 的生物网络编辑网站。`dist/` 是无构建步骤的静态页面，`data/bioweb.json` 是团队共享的数据源。

## 使用

在仓库根目录运行 `python -m http.server 8080`，访问 `http://localhost:8080/web-studio/dist/`。发布到静态托管时，将 `dist/` 作为网站根目录。

网站启动时请求 GitHub `main` 分支的 `web-studio/data/bioweb.json`。如果浏览器里已有未提交的编辑，页面会保留本地工作副本；点击「读取 GitHub 数据」可主动替换。编辑自动保存在浏览器 `localStorage`。完成后导出 `bioweb.json`，替换仓库中的同名文件并提交 Pull Request。合并后，其他成员刷新网站即可读到新数据。当前页面不持有 GitHub 访问令牌，也不会直接写仓库。

## JSON v1

- `species`：物种 ID、名称、物种简介 `description`、图片、进化图坐标 `x/y`、食物图坐标 `foodX/foodY`，以及初始参数和 0–100 的资源价值 `value`。旧 JSON 若没有 `foodX/foodY`，首次打开时会使用 `x/y` 作为食物图初始位置；之后两张图的位置独立保存。旧字段 `scientificName` 会在读取时转入 `description`，缺少 `value` 时默认 50。旧 `baseHabitatNiche` 字段不再出现在网页导出的 JSON 中。
- `evolutionLinks`：每条记录连接两个可相互演化的物种。`from`、`to` 仅保存端点，不表示方向；网站显示无箭头实线，Unity 导入器会向双方的 `evolutionTargets` 都加入对方。导入旧版单向记录时也按双向关系处理。
- `foodLinks`：猎物 `from` 指向捕食者 `to`。
- 图片可以是 HTTPS URL 或上传后内嵌的 data URL。图片上传接受最大 10 MB 的 PNG、JPEG、WebP、GIF 原图；超过 256 KiB 的图片会缩到最长边不超过 768 像素并转成 WebP，压缩后的图片不超过 256 KiB。大 GIF 会变为静态首帧。团队长期维护建议把图片文件提交到仓库，再填其 HTTPS 地址，避免 JSON 膨胀。

`foodLinks` 是显式食物网数据。**现有 Unity 模拟器仍按 `trophicLevel` 计算捕食，不读取这些边**。网页导出的 JSON 可以由 Unity 编辑器导入工具转换为 `SpeciesData` 资产；将 `unity/DarwinAtlasImporter.cs` 复制到 Unity 项目的 `Assets/Editor/` 后，在 Unity 菜单选择 `Tools/Darwin Atlas/Import bioweb.json`。导入器把进化边写入 `evolutionTargets`，将原始 JSON 同时复制到 `Assets/Resources/DarwinAtlas/bioweb.json`，供未来运行时代码读取显式食物关系。

物种编辑栏逐项解释基础属性，并给出第一轮试填参考。数值是游戏刻度，不是摄氏度或现实湿度；建议值需要结合地块与模拟结果校正。`value` 是经济系统的价值基数，未来每日可自然产出其中一部分；日产比例由经济系统决定，网站不自行计算。现有 Unity 模拟尚未消费这个字段；导入器将 Unity 中仍存在的旧 `baseHabitatNiche` 设为默认值 50。

「物种数据表」将所有物种的初始属性并排显示，可直接修改并自动保存到浏览器；导出 JSON 后即可共享这些修改。在进化网络或食物网点击「导出图片」，会分别输出当前图的 PNG，包含所有物种与连线，即使画布上有部分节点未显示在视口内也会纳入图片。外部图片地址若不允许跨域读取，导出图中该物种会显示占位图标；节点数据不受影响。

食物网示例用于演示编辑方式。正式数据请由团队确认后再提交。
