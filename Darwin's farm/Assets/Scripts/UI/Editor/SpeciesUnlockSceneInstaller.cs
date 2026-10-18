using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Author the card once in WhiteBox. No presentation GameObjects are made at runtime.
public static class SpeciesUnlockSceneInstaller
{
    private const string Scene = "Assets/Scenes/WhiteBox.unity";
    private const string Art = "Assets/Resources/DarwinJournal/SpeciesUnlock/";
    private static TMP_FontAsset font;

    [MenuItem("Tools/Darwin's Farm/UI/Repair species unlock layout")]
    public static void RepairLayout()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var presenter = UnityEngine.Object.FindFirstObjectByType<SpeciesUnlockPresenter>(FindObjectsInactive.Include);
        if (presenter == null) throw new InvalidOperationException("Open the existing WhiteBox scene first.");
        Undo.RegisterFullObjectHierarchyUndo(presenter.gameObject, "Repair species discovery card layout");
        ConfigureLayout(presenter.transform);
        EditorSceneManager.MarkSceneDirty(presenter.gameObject.scene);
    }

    private static void ConfigureLayout(Transform root)
    {
        RectTransform At(string path) => (RectTransform)root.Find("CardRoot" + path);
        Center(At(""), new Vector2(430, 700), new Vector2(0, 12));
        Center(At("/CardPaperBackground"), new Vector2(402, 650), new Vector2(0, 14));
        Center(At("/CardFrame"), new Vector2(330, 440), new Vector2(0, 85));
        Center(At("/TitleText"), new Vector2(144, 34), new Vector2(0, 257));
        Center(At("/CreatureArea"), new Vector2(250, 220), new Vector2(0, 110));
        Center(At("/CreatureArea/SilhouetteImage"), new Vector2(210, 210), Vector2.zero);
        Center(At("/CreatureArea/CreatureImage"), new Vector2(210, 210), Vector2.zero);
        Center(At("/CreatureArea/QuestionMarkGroup"), new Vector2(205, 150), new Vector2(88, 60));
        Center(At("/NameText"), new Vector2(220, 34), new Vector2(0, -17));
        Center(At("/DescriptionGroup"), new Vector2(332, 142), new Vector2(0, -187));
        Center(At("/DescriptionGroup/DescriptionText"), new Vector2(304, 126), Vector2.zero);
        Center(At("/ContinueButton"), new Vector2(190, 44), new Vector2(0, -285));
        Center(At("/ContinueButton/Label"), new Vector2(170, 36), Vector2.zero);
        void Text(string path, float maximum, float minimum, TextAlignmentOptions alignment, bool wrap)
        {
            var label = At(path).GetComponent<TMP_Text>();
            label.fontSize = label.fontSizeMax = maximum; label.fontSizeMin = minimum;
            label.enableAutoSizing = true;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.margin = Vector4.zero;
            label.characterSpacing = 0; label.lineSpacing = 0; label.paragraphSpacing = wrap ? 4 : 0;
            EditorUtility.SetDirty(label);
        }
        Text("/TitleText", 20, 18, TextAlignmentOptions.Center, false);
        Text("/NameText", 24, 18, TextAlignmentOptions.Center, false);
        Text("/DescriptionGroup/DescriptionText", 17, 14, TextAlignmentOptions.TopLeft, true);
        Text("/ContinueButton/Label", 21, 19, TextAlignmentOptions.Center, false);
    }

    [MenuItem("Tools/Darwin's Farm/UI/Play sample species unlock")]
    public static void PlaySample()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Enter Play Mode and start a game first.");
        var presenter = UnityEngine.Object.FindFirstObjectByType<SpeciesUnlockPresenter>();
        var species = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/灵感菇狸-761468a5.asset");
        if (presenter == null || species == null)
            throw new InvalidOperationException("Unlock UI or sample species is missing.");
        presenter.Show(new SpeciesUnlockPresenter.SpeciesUnlockData {
            CreatureSprite = species.Portrait,
            SpeciesId = "editor-demo:" + Guid.NewGuid().ToString("N"),
            SpeciesName = "灵感菇狸",
            Description = "一种长着蘑菇尾巴的奇妙生物，喜欢在温暖潮湿的环境中生活。"
        });
    }

    [MenuItem("Tools/Darwin's Farm/UI/Install species unlock card")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = EditorSceneManager.OpenScene(Scene);
        var safe = GameObject.Find("JournalCanvas")?.transform.Find("SafeArea");
        if (safe == null) throw new InvalidOperationException("JournalCanvas/SafeArea missing.");
        if (safe.Find("SpeciesUnlockUI") != null)
        {
            RepairLayout();
            EditorSceneManager.SaveScene(scene);
            return;
        }
        font = GameUITheme.TMPFont;
        Sprite paper = Import("CardPaperBackground");
        Sprite frame = Import("CardFrame");
        Sprite[] questions = Slice("QuestionMarks");
        Sprite[] stars = Slice("Sparkles");

        var root = Rect("SpeciesUnlockUI", safe);
        Stretch(root);
        var overlay = root.gameObject.AddComponent<CanvasGroup>();
        overlay.alpha = 0f; overlay.blocksRaycasts = false; overlay.interactable = false;
        var shade = Picture("DimBackground", root, null);
        Stretch((RectTransform)shade.transform);
        shade.color = new Color(.15f, .16f, .11f, .67f);
        shade.raycastTarget = true;

        var card = Rect("CardRoot", root);
        Center(card, new Vector2(430, 675), Vector2.zero);
        Picture("CardPaperBackground", card, paper, new Vector2(402, 626), new Vector2(0, 21));
        Picture("CardFrame", card, frame, new Vector2(365, 490), new Vector2(0, 48));
        Label("TitleText", card, "新的观察发现", 26,
            new Vector2(0, 252), new Vector2(255, 44));

        var creature = Rect("CreatureArea", card);
        Center(creature, new Vector2(285, 315), new Vector2(0, 48));
        var silhouette = Picture("SilhouetteImage", creature, null,
            new Vector2(245, 260), Vector2.zero);
        silhouette.preserveAspect = true;
        silhouette.color = Color.black;
        var colored = Picture("CreatureImage", creature, null,
            new Vector2(245, 260), Vector2.zero);
        colored.preserveAspect = true;
        colored.color = new Color(1, 1, 1, 0);

        var questionRoot = Rect("QuestionMarkGroup", creature);
        Center(questionRoot, new Vector2(205, 150), new Vector2(104, 68));
        var questionGroup = questionRoot.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < 3; i++)
            Picture("Question" + i, questionRoot, questions[i],
                new Vector2(62, 88), new Vector2(-65 + i * 65, i == 1 ? 12 : -10));

        var sparkleRoot = Rect("SparkleGroup", creature);
        Center(sparkleRoot, new Vector2(285, 260), Vector2.zero);
        var sparkleGroup = sparkleRoot.gameObject.AddComponent<CanvasGroup>();
        sparkleGroup.alpha = 0f;
        for (int i = 0; i < 3; i++)
            Picture("Star" + i, sparkleRoot, stars[i],
                new Vector2(66, 95), new Vector2(i == 0 ? -118 : i == 1 ? 112 : 105,
                    i == 2 ? -92 : 88));

        var name = Label("NameText", card, "我是谁？？？", 27,
            new Vector2(0, -142), new Vector2(290, 46));
        var descriptionRoot = Rect("DescriptionGroup", card);
        Center(descriptionRoot, new Vector2(332, 100), new Vector2(0, -214));
        var descriptionGroup = descriptionRoot.gameObject.AddComponent<CanvasGroup>();
        descriptionGroup.alpha = 0f;
        var descriptionWash = Picture("DescriptionPaper", descriptionRoot, null);
        Stretch((RectTransform)descriptionWash.transform);
        descriptionWash.color = new Color(.96f, .91f, .76f, .82f);
        Label("DescriptionText", descriptionRoot, "", 19, Vector2.zero,
            new Vector2(304, 87));
        var continueButton = Picture("ContinueButton", card, null,
            new Vector2(190, 45), new Vector2(0, -315));
        continueButton.color = new Color(.77f, .82f, .60f, .95f);
        continueButton.raycastTarget = true;
        var button = continueButton.gameObject.AddComponent<Button>();
        button.targetGraphic = continueButton;
        Label("Label", continueButton.transform, "继续观察", 21, Vector2.zero,
            new Vector2(170, 42));
        var continueGroup = continueButton.gameObject.AddComponent<CanvasGroup>();
        continueGroup.alpha = 0f;
        continueGroup.blocksRaycasts = false;
        continueGroup.interactable = false;

        var presenter = root.gameObject.AddComponent<SpeciesUnlockPresenter>();
        var data = new SerializedObject(presenter);
        Set(data, "safeArea", safe);
        Set(data, "cardRoot", card);
        Set(data, "questionMarkGroup", questionRoot);
        Set(data, "sparkleGroup", sparkleRoot);
        Set(data, "silhouetteImage", silhouette);
        Set(data, "creatureImage", colored);
        Set(data, "overlayGroup", overlay);
        Set(data, "questionGroup", questionGroup);
        Set(data, "sparkleCanvasGroup", sparkleGroup);
        Set(data, "descriptionGroup", descriptionGroup);
        Set(data, "continueGroup", continueGroup);
        Set(data, "nameText", name);
        Set(data, "descriptionText", descriptionRoot.Find("DescriptionText").GetComponent<TMP_Text>());
        Set(data, "continueButton", button);
        data.ApplyModifiedPropertiesWithoutUndo();
        ConfigureLayout(root);
        root.SetAsLastSibling();
        var hud = safe.Find("EcologyNewsToast");
        if (hud != null) hud.SetAsLastSibling();

        // The supplied testing species has no presentation text yet.
        var inspiration = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/灵感菇狸-761468a5.asset");
        if (inspiration != null && inspiration.Description == "暂无物种介绍")
        {
            var speciesAsset = new SerializedObject(inspiration);
            speciesAsset.FindProperty("description").stringValue =
                "一种长着蘑菇尾巴的奇妙生物，喜欢在温暖潮湿的环境中生活。";
            speciesAsset.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(inspiration);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save unlock card scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("SpeciesUnlockSceneInstaller: authored card, four supplied images, portrait bindings.");
    }

    private static Sprite Import(string name)
    {
        string path = Art + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new IOException("Missing unlock art: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite[] Slice(string name)
    {
        string path = Art + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new IOException("Missing unlock stickers: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        var slices = new SpriteMetaData[6];
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
            {
                int index = row * 3 + column;
                slices[index] = new SpriteMetaData {
                    name = name + index,
                    rect = new Rect(column * 418, (1 - row) * 627, 418, 627),
                    alignment = (int)SpriteAlignment.Center
                };
            }
        importer.spritesheet = slices;
        importer.SaveAndReimport();
        var results = new Sprite[6];
        foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(path))
            if (item is Sprite sprite)
                for (int i = 0; i < results.Length; i++)
                    if (sprite.name == name + i) results[i] = sprite;
        for (int i = 0; i < 3; i++)
            if (results[i] == null) throw new IOException("Sprite slice missing: " + name + i);
        return results;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.layer = LayerMask.NameToLayer("UI");
        item.transform.SetParent(parent, false);
        return (RectTransform)item.transform;
    }
    private static void Stretch(RectTransform item)
    {
        item.anchorMin = Vector2.zero;
        item.anchorMax = Vector2.one;
        item.offsetMin = item.offsetMax = Vector2.zero;
    }
    private static void Center(RectTransform item, Vector2 size, Vector2 position)
    {
        item.anchorMin = item.anchorMax = new Vector2(.5f, .5f);
        item.sizeDelta = size;
        item.anchoredPosition = position;
    }
    private static Image Picture(string name, Transform parent, Sprite sprite,
        Vector2? size = null, Vector2? position = null)
    {
        var item = Rect(name, parent);
        if (size.HasValue) Center(item, size.Value, position ?? Vector2.zero);
        var image = item.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }
    private static TMP_Text Label(string name, Transform parent, string text, int size,
        Vector2 position, Vector2 dimensions)
    {
        var item = Rect(name, parent);
        Center(item, dimensions, position);
        var label = item.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.color = new Color(.27f, .22f, .15f);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.text = text;
        label.raycastTarget = false;
        return label;
    }
    private static void Set(SerializedObject data, string field, UnityEngine.Object value)
        => data.FindProperty(field).objectReferenceValue = value;
}
