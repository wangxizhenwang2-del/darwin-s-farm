# Darwin Atlas

独立于 Unity `Assets` 的生物网络编辑网站。`dist/` 是无构建步骤的静态页面，`data/bioweb.json` 是团队共享的数据源。

## 使用

在仓库根目录运行 `python -m http.server 8080`，访问 `http://localhost:8080/web-studio/dist/`。发布到静态托管时，将 `dist/` 作为网站根目录。

网站启动时请求 GitHub `main` 分支的 `web-studio/data/bioweb.json`。如果浏览器里已有未提交的编辑，页面会保留本地工作副本；点击「读取 GitHub 数据」可主动替换。编辑自动保存在浏览器 `localStorage`。完成后导出 `bioweb.json`，替换仓库中的同名文件并提交 Pull Request。合并后，其他成员刷新网站即可读到新数据。当前页面不持有 GitHub 访问令牌，也不会直接写仓库。

## JSON v1

- `species`：物种 ID、名称、图片、画布坐标，以及与 Unity `SpeciesData` 一致的八项基础字段。
- `evolutionLinks`：祖先 `from` 指向后代 `to`。
- `foodLinks`：猎物 `from` 指向捕食者 `to`。
- 图片可以是 HTTPS URL 或上传后内嵌的 data URL。团队长期维护建议把图片文件提交到仓库，再填其 HTTPS 地址，避免 JSON 膨胀。

`foodLinks` 是显式食物网数据。**现有 Unity 模拟器仍按 `trophicLevel` 计算捕食，不读取这些边**。网页导出的 JSON 可以由 Unity 编辑器导入工具转换为 `SpeciesData` 资产；将 `unity/DarwinAtlasImporter.cs` 复制到 Unity 项目的 `Assets/Editor/` 后，在 Unity 菜单选择 `Tools/Darwin Atlas/Import bioweb.json`。导入器把进化边写入 `evolutionTargets`，将原始 JSON 同时复制到 `Assets/Resources/DarwinAtlas/bioweb.json`，供未来运行时代码读取显式食物关系。

食物网示例用于演示编辑方式。正式数据请由团队确认后再提交。
