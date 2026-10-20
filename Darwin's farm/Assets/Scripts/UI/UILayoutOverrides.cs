using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Presentation-only overrides. Text, selection, callbacks and simulation state are never stored.
public sealed class UILayoutOverrides : ScriptableObject
{
    public const string ResourcePath = "GameUI/UILayoutOverrides";
    public const string PersistentScene = "@DontDestroyOnLoad";
    [Serializable] public sealed class Value
    {
        public string property;
        public Vector4 number;
        public UnityEngine.Object reference;
        public string serialized;
    }
    [Serializable] public sealed class Entry
    {
        public string scene, path, component;
        public int componentIndex;
        public List<Value> values = new List<Value>();
    }
    public List<Entry> entries = new List<Entry>();

    public static List<Value> Capture(Component component)
    {
        var result = new List<Value>();
        foreach (string name in Properties(component))
        {
            PropertyInfo property = component.GetType().GetProperty(name);
            if (property == null || !property.CanRead || !property.CanWrite) continue;
            object value = property.GetValue(component);
            var item = new Value { property = name };
            if (value is UnityEngine.Object asset) item.reference = asset;
            else if (value is Vector2 v2) item.number = v2;
            else if (value is Vector3 v3) item.number = v3;
            else if (value is Vector4 v4) item.number = v4;
            else if (value is Color color) item.number = color;
            else if (value is Rect rect) item.number = new Vector4(rect.x, rect.y, rect.width, rect.height);
            else if (value is Quaternion q) item.number = new Vector4(q.x, q.y, q.z, q.w);
            else if (value is RectOffset padding) item.number = new Vector4(padding.left, padding.right, padding.top, padding.bottom);
            else if (value is ColorBlock block) item.serialized = JsonUtility.ToJson(block);
            else if (value != null && (value is bool || value is float || value is int || value.GetType().IsEnum))
                item.number.x = Convert.ToSingle(value);
            else if (!typeof(UnityEngine.Object).IsAssignableFrom(property.PropertyType)) continue;
            result.Add(item);
        }
        return result;
    }

    public static bool Same(Value a, Value b) => a.reference == b.reference && a.serialized == b.serialized &&
        (a.number - b.number).sqrMagnitude < .000001f;

    public static string[] Properties(Component c)
    {
        if (c is RectTransform) return new[] { "anchorMin", "anchorMax", "pivot", "sizeDelta", "anchoredPosition3D", "localRotation", "localScale" };
        if (c is TMP_Text) return new[] { "font", "fontSharedMaterial", "fontSize", "enableAutoSizing", "fontSizeMin", "fontSizeMax", "fontStyle", "alignment", "margin", "characterSpacing", "wordSpacing", "lineSpacing", "paragraphSpacing", "textWrappingMode", "overflowMode", "color" };
        if (c is Text) return new[] { "font", "fontSize", "fontStyle", "alignment", "lineSpacing", "resizeTextForBestFit", "resizeTextMinSize", "resizeTextMaxSize", "horizontalOverflow", "verticalOverflow", "color" };
        // Dynamic portraits and tool icons retain their current sprite and material.
        if (c is Image) return new[] { "color", "type", "preserveAspect", "fillCenter", "pixelsPerUnitMultiplier" };
        if (c is GridLayoutGroup) return new[] { "padding", "childAlignment", "cellSize", "spacing", "startCorner", "startAxis", "constraint", "constraintCount" };
        if (c is HorizontalOrVerticalLayoutGroup) return new[] { "padding", "childAlignment", "spacing", "childControlWidth", "childControlHeight", "childForceExpandWidth", "childForceExpandHeight", "childScaleWidth", "childScaleHeight", "reverseArrangement" };
        if (c is LayoutElement) return new[] { "ignoreLayout", "minWidth", "minHeight", "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight", "layoutPriority" };
        if (c is ContentSizeFitter) return new[] { "horizontalFit", "verticalFit" };
        if (c is AspectRatioFitter) return new[] { "aspectMode", "aspectRatio" };
        if (c is CanvasScaler) return new[] { "uiScaleMode", "referenceResolution", "screenMatchMode", "matchWidthOrHeight", "referencePixelsPerUnit" };
        if (c is RawImage) return new[] { "color", "uvRect" };
        if (c is Selectable) return new[] { "transition", "colors" };
        return Array.Empty<string>();
    }

    public static string PathOf(Transform target)
    {
        var parts = new List<string>();
        for (Transform node = target; node != null; node = node.parent)
        {
            int ordinal = 0;
            if (node.parent != null)
                for (int i = 0; i < node.GetSiblingIndex(); i++)
                    if (node.parent.GetChild(i).name == node.name) ordinal++;
            else foreach (var root in node.gameObject.scene.GetRootGameObjects())
            { if (root == node.gameObject) break; if (root.name == node.name) ordinal++; }
            parts.Add(Uri.EscapeDataString(node.name) + "#" + ordinal);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static Component Resolve(Entry entry)
    {
        Scene scene = SceneManager.GetSceneByPath(entry.scene);
        if (entry.scene == PersistentScene)
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                if (canvas.gameObject.scene.name == "DontDestroyOnLoad") { scene = canvas.gameObject.scene; break; }
        if (!scene.IsValid() || !scene.isLoaded) return null;
        Transform current = null;
        foreach (string part in entry.path.Split('/'))
        {
            int split = part.LastIndexOf('#');
            string name = Uri.UnescapeDataString(part.Substring(0, split));
            int ordinal = int.Parse(part.Substring(split + 1));
            var candidates = new List<Transform>();
            if (current == null) foreach (var root in scene.GetRootGameObjects()) candidates.Add(root.transform);
            else for (int i = 0; i < current.childCount; i++) candidates.Add(current.GetChild(i));
            Transform found = null;
            foreach (var candidate in candidates)
                if (candidate.name == name && ordinal-- == 0) { found = candidate; break; }
            if (found == null) return null;
            current = found;
        }
        Type type = Type.GetType(entry.component);
        if (type == null || current == null) return null;
        Component[] matches = current.GetComponents(type);
        return entry.componentIndex < matches.Length ? matches[entry.componentIndex] : null;
    }

    public static void Apply(Component target, Entry entry)
    {
        foreach (var value in entry.values)
        {
            PropertyInfo p = target.GetType().GetProperty(value.property);
            if (p == null || !p.CanWrite) continue;
            Type type = p.PropertyType;
            object decoded;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) decoded = value.reference;
            else if (type == typeof(Vector2)) decoded = (Vector2)value.number;
            else if (type == typeof(Vector3)) decoded = (Vector3)value.number;
            else if (type == typeof(Vector4)) decoded = value.number;
            else if (type == typeof(Color)) decoded = (Color)value.number;
            else if (type == typeof(Rect)) decoded = new Rect(value.number.x, value.number.y, value.number.z, value.number.w);
            else if (type == typeof(Quaternion)) decoded = new Quaternion(value.number.x, value.number.y, value.number.z, value.number.w);
            else if (type == typeof(RectOffset)) decoded = new RectOffset((int)value.number.x, (int)value.number.y, (int)value.number.z, (int)value.number.w);
            else if (type == typeof(ColorBlock)) decoded = JsonUtility.FromJson<ColorBlock>(value.serialized);
            else if (type.IsEnum) decoded = Enum.ToObject(type, (int)value.number.x);
            else if (type == typeof(bool)) decoded = value.number.x != 0;
            else decoded = Convert.ChangeType(value.number.x, type);
            p.SetValue(target, decoded);
        }
        if (target is RectTransform rect && rect.TryGetComponent<InformationWindow>(out var window))
            foreach (var value in entry.values)
                if (value.property == "anchoredPosition3D") window.SetAuthoredPosition(rect.anchoredPosition);
    }
}

// Apply once per generated component, after its builder has run. Never pin draggable UI every frame.
internal sealed class UILayoutOverridePlayer : MonoBehaviour
{
    private UILayoutOverrides settings;
    private readonly Dictionary<UILayoutOverrides.Entry, Component> applied = new Dictionary<UILayoutOverrides.Entry, Component>();
    private float nextCheck;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartPlayer()
    {
        if (FindFirstObjectByType<UILayoutOverridePlayer>() != null) return;
        var settings = Resources.Load<UILayoutOverrides>(UILayoutOverrides.ResourcePath);
        if (settings == null || settings.entries.Count == 0) return;
        var owner = new GameObject("UI Layout Overrides");
        DontDestroyOnLoad(owner);
        owner.AddComponent<UILayoutOverridePlayer>().settings = settings;
    }
    private void LateUpdate()
    {
        if (settings == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .25f;
        foreach (var entry in settings.entries)
        {
            if (applied.TryGetValue(entry, out var previous) && previous != null) continue;
            Component target = UILayoutOverrides.Resolve(entry);
            if (target == null) continue;
            UILayoutOverrides.Apply(target, entry);
            applied[entry] = target;
        }
    }
}
