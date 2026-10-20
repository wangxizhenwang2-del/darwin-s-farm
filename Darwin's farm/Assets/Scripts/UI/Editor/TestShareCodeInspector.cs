using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class TestShareCodeInspector
{
    [MenuItem("Darwin's Farm/测试分享码/解析剪贴板")]
    public static void DecodeClipboard()
    {
        try
        {
            string json = TestShareCodeCodec.Decode(GUIUtility.systemCopyBuffer.Trim());
            var session = JObject.Parse(json);
            if ((int?)session["schema"] != 1 || session["initialSave"] == null ||
                session["entries"] is not JArray entries)
                throw new FormatException("分享码缺少测试记录。");
            int actions = 0, days = 0, maps = 0, transitions = 0, ecology = 0, errors = 0;
            foreach (JToken entry in entries)
            {
                switch ((string)entry["kind"])
                {
                    case "action": actions++; break;
                    case "day": days++; break;
                    case "map": maps++; break;
                    case "transition": transitions++; break;
                    case "ecology": ecology++; break;
                    case "error": errors++; break;
                }
            }
            string path = EditorUtility.SaveFilePanel("保存解码后的测试记录",
                Application.persistentDataPath, "Darwin_Test_Session.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, session.ToString(Newtonsoft.Json.Formatting.Indented));
            string reportPath = Path.ChangeExtension(path, ".txt");
            if (File.Exists(reportPath))
            {
                string stem = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                int suffix = 1;
                do { reportPath = stem + ".report-" + suffix++ + ".txt"; }
                while (File.Exists(reportPath));
            }
            File.WriteAllText(reportPath, BuildReport(session, entries), new UTF8Encoding(false));
            Debug.Log("测试分享码已解析：第 " + session["startDay"] + " 至 " + session["endDay"] +
                " 日；操作 " + actions + "，每日变化 " + days + "，地图变化 " + maps +
                "，种群转变 " + transitions +
                "，生态事件 " + ecology + "，错误 " + errors +
                ((bool?)session["truncated"] == true ? "；记录已达容量上限" : "") +
                "。\n详细日志：" + path + "\n阅读报告：" + reportPath);
            EditorUtility.RevealInFinder(reportPath);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("测试分享码解析失败", e.Message, "确定");
            Debug.LogWarning("测试分享码解析失败：" + e);
        }
    }
    private static string BuildReport(JObject session, JArray entries)
    {
        var text = new StringBuilder();
        text.AppendLine("达尔文农场 · 测试记录");
        text.AppendLine("游戏版本：" + session["gameVersion"] + "  场景：" + session["scene"] +
            "  随机种子：" + session["seed"]);
        text.AppendLine("第 " + session["startDay"] + " 天至第 " + session["endDay"] + " 天");
        if ((bool?)session["truncated"] == true)
            text.AppendLine("注意：记录达到容量上限，后续事件未收集。");
        int lastDay = int.MinValue;
        foreach (JToken entry in entries)
        {
            int day = (int?)entry["day"] ?? -1;
            if (day != lastDay) { text.AppendLine(); text.AppendLine("=== 第 " + day + " 天 ==="); lastDay = day; }
            string kind = (string)entry["kind"];
            switch (kind)
            {
                case "initial":
                case "day":
                    text.AppendLine((kind == "initial" ? "开局状态" : "每日变化") +
                        "，生物能：" + entry["balance"] +
                        (entry["modelHash"] == null ? "" : "，模型校验：" + entry["modelHash"]));
                    if (entry["changed"] is JArray states)
                        foreach (JToken state in states)
                            text.AppendLine("  " + state["key"] + " " + state["species"] +
                                (state["count"] == null ? " 环境变化" :
                                    " 数量=" + state["count"] + " 出生=" + state["births"] +
                                    " 死亡=" + state["deaths"]));
                    if (entry["removed"] is JArray removed)
                        foreach (JToken key in removed) text.AppendLine("  移除 " + key);
                    break;
                default:
                    text.AppendLine("[" + kind + "] " + entry["source"] + " " +
                        entry["operation"] + " @(" + entry["x"] + "," + entry["y"] + ") " +
                        entry["detail"]);
                    break;
            }
        }
        return text.ToString();
    }
}
