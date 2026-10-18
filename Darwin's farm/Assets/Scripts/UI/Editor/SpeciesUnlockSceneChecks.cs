using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SpeciesUnlockSceneChecks
{
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
            var journal = GameObject.Find("JournalCanvas");
            Require(journal != null, "JournalCanvas missing");
            var card = journal.transform.Find("SafeArea/SpeciesUnlockUI");
            Require(card != null, "authored SpeciesUnlockUI missing");
            Require(card.Find("CardRoot/CardFrame") != null, "card frame missing");
            Require(card.Find("CardRoot/CreatureArea/SilhouetteImage") != null,
                "silhouette missing");
            Require(card.Find("CardRoot/CreatureArea/CreatureImage") != null,
                "creature portrait missing");
            Require(card.Find("CardRoot/ContinueButton") != null,
                "continue button missing");
            var frame = card.Find("CardRoot/CardFrame").GetComponent<Image>();
            var paper = card.Find("CardRoot/CardPaperBackground").GetComponent<Image>();
            Require(frame.sprite != null && paper.sprite != null,
                "supplied card art not bound");
            Require(card.Find("CardRoot/CreatureArea/QuestionMarkGroup/Question0")
                .GetComponent<Image>().sprite != null, "question sticker not bound");
            Require(card.Find("CardRoot/CreatureArea/SparkleGroup/Star0")
                .GetComponent<Image>().sprite != null, "sparkle sticker not bound");
            var root = (RectTransform)card.Find("CardRoot");
            foreach (var size in new[] { new Vector2(1280,720), new Vector2(1152,720),
                         new Vector2(960,720), new Vector2(405,720) })
            {
                float scale = Mathf.Min(1f, size.x / 430f, size.y / 700f);
                Require(root.sizeDelta.x * scale <= size.x &&
                    root.sizeDelta.y * scale <= size.y,
                    "card exceeds a common safe-area size: " + size);
            }
            Debug.Log("SpeciesUnlockSceneChecks: PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("SpeciesUnlockSceneChecks: " + message);
    }
}
