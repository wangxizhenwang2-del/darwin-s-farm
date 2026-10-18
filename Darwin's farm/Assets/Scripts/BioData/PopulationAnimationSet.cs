using UnityEngine;

public enum PopulationAnimationMode { WalkAndIdle, CommonLoop }

// Shared by every population of a species; portraits remain independent.
[CreateAssetMenu(menuName = "Darwin's Farm/Population Animation Set")]
public sealed class PopulationAnimationSet : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private Sprite idleSprite;
    [SerializeField, Min(0.01f)] private float referenceMoveSpeed = 1.8f;
    [SerializeField] private bool artworkFacesLeft = true;
    [SerializeField] private PopulationAnimationMode mode;
    [Header("Ordered frame sequences (one-based inclusive ranges)")]
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] idleFrames;
    [Header("Common animation (independent of movement)")]
    [SerializeField] private Sprite[] commonFrames;
    [SerializeField] private Sprite[] transitionFrames;
    [SerializeField, Min(1)] private int transitionStartFrame = 1;
    [SerializeField, Min(1)] private int transitionEndFrame = 2;
    [SerializeField, Min(1)] private int transitionFps = 8;
    [SerializeField, Min(1)] private int commonStartFrame = 1;
    [SerializeField, Min(1)] private int commonEndFrame = 1;
    [SerializeField, Min(1)] private int commonStopFrame = 1;
    [SerializeField, Min(1)] private int commonFps = 8;
    [SerializeField, Min(0f)] private float commonHoldMin = 1f;
    [SerializeField, Min(0f)] private float commonHoldMax = 3f;
    [SerializeField, Min(1)] private int walkStartFrame = 1;
    [SerializeField, Min(1)] private int walkEndFrame = 1;
    [SerializeField, Min(1)] private int idleStartFrame = 1;
    [SerializeField, Min(1)] private int idleEndFrame = 1;
    [SerializeField, Min(1)] private int stopFrame = 1;
    [SerializeField, Min(1)] private int walkFps = 8;
    [SerializeField, Min(1)] private int idleFps = 8;
    [SerializeField, Min(0f)] private float idleDelayMin = 2f;
    [SerializeField, Min(0f)] private float idleDelayMax = 5f;
    [SerializeField, Min(0f)] private float movementHoldMin = 4f;
    [SerializeField, Min(0f)] private float movementHoldMax = 8f;

    public RuntimeAnimatorController Controller => controller;
    public Sprite IdleSprite => idleSprite;
    public float ReferenceMoveSpeed => Mathf.Max(0.01f, referenceMoveSpeed);
    public bool ArtworkFacesLeft => artworkFacesLeft;
    public PopulationAnimationMode Mode => mode;
    public bool IsUsable => controller != null && idleSprite != null;
    public bool HasFrameSequences => mode == PopulationAnimationMode.CommonLoop ?
        commonFrames != null && commonFrames.Length > 0 :
        walkFrames != null && idleFrames != null && walkFrames.Length > 0 && idleFrames.Length > 0;
    public Sprite[] WalkFrames => walkFrames;
    public Sprite[] IdleFrames => idleFrames;
    public Sprite[] CommonFrames => commonFrames;
    public Sprite[] TransitionFrames => transitionFrames;
    public int TransitionStart => Mathf.Clamp(transitionStartFrame - 1, 0, Mathf.Max(0, transitionFrames.Length - 1));
    public int TransitionEnd => Mathf.Clamp(transitionEndFrame - 1, TransitionStart, Mathf.Max(0, transitionFrames.Length - 1));
    public int TransitionFps => Mathf.Max(1, transitionFps);
    public bool HasTransition => mode == PopulationAnimationMode.WalkAndIdle &&
        transitionFrames != null && transitionFrames.Length > 0;
    public int CommonStart => Mathf.Clamp(commonStartFrame - 1, 0, Mathf.Max(0, commonFrames.Length - 1));
    public int CommonEnd => Mathf.Clamp(commonEndFrame - 1, CommonStart, Mathf.Max(0, commonFrames.Length - 1));
    public int CommonStopFrame => Mathf.Clamp(commonStopFrame, 1, Mathf.Max(1, commonFrames.Length));
    public int CommonFps => Mathf.Max(1, commonFps);
    public float CommonCycleSeconds => (CommonEnd - CommonStart + 1) / (float)CommonFps;
    public float CommonHoldMin => Mathf.Max(0f, commonHoldMin);
    public float CommonHoldMax => Mathf.Max(CommonHoldMin, commonHoldMax);
    public int WalkStart => Mathf.Clamp(walkStartFrame - 1, 0, Mathf.Max(0, walkFrames.Length - 1));
    public int WalkEnd => Mathf.Clamp(walkEndFrame - 1, WalkStart, Mathf.Max(0, walkFrames.Length - 1));
    public int IdleStart => Mathf.Clamp(idleStartFrame - 1, 0, Mathf.Max(0, idleFrames.Length - 1));
    public int IdleEnd => Mathf.Clamp(idleEndFrame - 1, IdleStart, Mathf.Max(0, idleFrames.Length - 1));
    public int StopFrame => Mathf.Clamp(stopFrame, 1, Mathf.Max(1, idleFrames.Length));
    public Sprite StopSprite => HasFrameSequences ?
        mode == PopulationAnimationMode.CommonLoop ? commonFrames[CommonStopFrame - 1] :
        idleFrames[Mathf.Clamp(stopFrame - 1, 0, idleFrames.Length - 1)] : IdleSprite;
    public int WalkFps => Mathf.Max(1, walkFps);
    public int IdleFps => Mathf.Max(1, idleFps);
    public float WalkCycleSeconds => (WalkEnd - WalkStart + 1) / (float)WalkFps;
    public float IdleCycleSeconds => (IdleEnd - IdleStart + 1) / (float)IdleFps;
    public float IdleDelayMin => Mathf.Max(0f, idleDelayMin);
    public float IdleDelayMax => Mathf.Max(IdleDelayMin, idleDelayMax);
    public float MovementHoldMin => Mathf.Max(0f, movementHoldMin);
    public float MovementHoldMax => Mathf.Max(MovementHoldMin, movementHoldMax);
}
