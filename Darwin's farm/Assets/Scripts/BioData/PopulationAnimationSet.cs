using UnityEngine;

// Shared by every population of a species; portraits remain independent.
[CreateAssetMenu(menuName = "Darwin's Farm/Population Animation Set")]
public sealed class PopulationAnimationSet : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private Sprite idleSprite;
    [SerializeField, Min(0.01f)] private float referenceMoveSpeed = 1.8f;
    [SerializeField] private bool artworkFacesLeft = true;

    public RuntimeAnimatorController Controller => controller;
    public Sprite IdleSprite => idleSprite;
    public float ReferenceMoveSpeed => Mathf.Max(0.01f, referenceMoveSpeed);
    public bool ArtworkFacesLeft => artworkFacesLeft;
    public bool IsUsable => controller != null && idleSprite != null;
}
