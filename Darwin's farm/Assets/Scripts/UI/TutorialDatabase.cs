using System;
using UnityEngine;

[CreateAssetMenu(menuName="Darwin's Farm/Tutorial Database")]
public sealed class TutorialDatabase : ScriptableObject
{
    [Serializable] public sealed class Page { public string id,title; [TextArea(5,16)]public string content; [TextArea(5,16)]public string mobileContent; public Sprite illustration; public int sortOrder; }
    [Serializable] public sealed class Category { public string id,title; public int sortOrder; public Page[] pages; }
    public Category[] categories;
}
