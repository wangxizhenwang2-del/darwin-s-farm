using System;
using UnityEngine;
using UnityEngine.Events;

public enum PopulationFrameAction { Walk, Idle, Common, TransitionForward, TransitionReverse }
public enum PopulationFramePhase { Hold, Walk, IdleAction, CommonAction, TransitionToIdle, TransitionToWalk }

[Serializable]
public sealed class PopulationFrameEvent
{
    public PopulationFrameAction action;
    [Min(1)] public int frame = 1;
    public UnityEvent callback = new UnityEvent();
}

// Owns the animation clock. Navigation consumes the distance returned by Tick,
// so a walk cycle and its planned displacement share exactly the same boundary.
[DisallowMultipleComponent]
public sealed class PopulationFrameAnimationController : MonoBehaviour
{
    [SerializeField] private PopulationFrameEvent[] frameEvents = Array.Empty<PopulationFrameEvent>();
    [SerializeField] private bool overrideWalkTiming;
    [SerializeField, Min(1)] private int walkStartFrame = 1;
    [SerializeField, Min(1)] private int walkEndFrame = 1;
    [SerializeField, Min(1)] private int walkFps = 8;
    [SerializeField] private bool overrideIdleTiming;
    [SerializeField, Min(1)] private int idleStartFrame = 1;
    [SerializeField, Min(1)] private int idleEndFrame = 1;
    [SerializeField, Min(1)] private int idleFps = 8;
    [SerializeField, Min(1)] private int stopFrame = 1;
    [SerializeField] private bool overrideCommonTiming;
    [SerializeField, Min(1)] private int commonStartFrame = 1;
    [SerializeField, Min(1)] private int commonEndFrame = 1;
    [SerializeField, Min(1)] private int commonStopFrame = 1;
    [SerializeField, Min(1)] private int commonFps = 8;
    [SerializeField] private bool overrideTransitionTiming;
    [SerializeField, Min(1)] private int transitionStartFrame = 1;
    [SerializeField, Min(1)] private int transitionEndFrame = 2;
    [SerializeField, Min(1)] private int transitionFps = 8;
    [SerializeField] private bool overrideArtworkFacing;
    [SerializeField] private bool artworkFacesLeft = true;
    private PopulationAnimationSet set;
    private SpriteRenderer image;
    private Color tint;
    private float elapsed;
    private float walkSpeed;
    private float nextIdleAt;
    private int cyclesLeft;
    private int shownFrame = -1;
    private bool stopRequested;
    private bool startRequested;
    private bool transitionReadyToWalk;

    public PopulationFramePhase Phase { get; private set; } = PopulationFramePhase.Hold;
    public int CurrentFrame { get; private set; }
    public bool HasStartRequest => startRequested;
    public bool CanBeginWalk => set != null && set.Mode == PopulationAnimationMode.WalkAndIdle &&
        Phase == PopulationFramePhase.Hold && startRequested && transitionReadyToWalk;
    public float WalkSpeed => walkSpeed;
    public float DistanceUntilWalkBoundary => Phase == PopulationFramePhase.Walk ?
        walkSpeed * Mathf.Max(0f, WalkCycleSeconds - elapsed) : 0f;
    public PopulationAnimationSet AnimationSet => set;
    public bool IsCommonLoop => set != null && set.Mode == PopulationAnimationMode.CommonLoop;
    public bool HasTransition => set != null && set.HasTransition;
    public bool ArtworkFacesLeft => overrideArtworkFacing ? artworkFacesLeft :
        set != null && set.ArtworkFacesLeft;
    public float WalkCycleSeconds => (WalkEnd - WalkStart + 1) / (float)WalkFps;
    private float IdleCycleSeconds => (IdleEnd - IdleStart + 1) / (float)IdleFps;
    private int WalkStart => overrideWalkTiming ?
        Mathf.Clamp(walkStartFrame - 1, 0, set.WalkFrames.Length - 1) : set.WalkStart;
    private int WalkEnd => overrideWalkTiming ?
        Mathf.Clamp(walkEndFrame - 1, WalkStart, set.WalkFrames.Length - 1) : set.WalkEnd;
    private int WalkFps => overrideWalkTiming ? Mathf.Max(1, walkFps) : set.WalkFps;
    private int IdleStart => overrideIdleTiming ?
        Mathf.Clamp(idleStartFrame - 1, 0, set.IdleFrames.Length - 1) : set.IdleStart;
    private int IdleEnd => overrideIdleTiming ?
        Mathf.Clamp(idleEndFrame - 1, IdleStart, set.IdleFrames.Length - 1) : set.IdleEnd;
    private int IdleFps => overrideIdleTiming ? Mathf.Max(1, idleFps) : set.IdleFps;
    private Sprite StopSprite => overrideIdleTiming ?
        set.IdleFrames[Mathf.Clamp(stopFrame - 1, 0, set.IdleFrames.Length - 1)] : set.StopSprite;
    private int CommonStart => overrideCommonTiming ?
        Mathf.Clamp(commonStartFrame - 1, 0, set.CommonFrames.Length - 1) : set.CommonStart;
    private int CommonEnd => overrideCommonTiming ?
        Mathf.Clamp(commonEndFrame - 1, CommonStart, set.CommonFrames.Length - 1) : set.CommonEnd;
    private int CommonFps => overrideCommonTiming ? Mathf.Max(1, commonFps) : set.CommonFps;
    private float CommonCycleSeconds => (CommonEnd - CommonStart + 1) / (float)CommonFps;
    private Sprite CommonStopSprite => overrideCommonTiming ?
        set.CommonFrames[Mathf.Clamp(commonStopFrame - 1, 0, set.CommonFrames.Length - 1)] : set.StopSprite;
    private int TransitionStart => overrideTransitionTiming ?
        Mathf.Clamp(transitionStartFrame - 1, 0, set.TransitionFrames.Length - 1) : set.TransitionStart;
    private int TransitionEnd => overrideTransitionTiming ?
        Mathf.Clamp(transitionEndFrame - 1, TransitionStart, set.TransitionFrames.Length - 1) : set.TransitionEnd;
    private int TransitionFps => overrideTransitionTiming ? Mathf.Max(1, transitionFps) : set.TransitionFps;
    private float TransitionSeconds => (TransitionEnd - TransitionStart + 1) / (float)TransitionFps;

    public void Bind(SpriteRenderer renderer, PopulationAnimationSet animationSet, Color color)
    {
        image = renderer;
        set = animationSet != null && animationSet.HasFrameSequences ? animationSet : null;
        tint = color;
        Phase = PopulationFramePhase.Hold;
        elapsed = 0f;
        cyclesLeft = 0;
        stopRequested = false;
        startRequested = false;
        transitionReadyToWalk = true;
        shownFrame = -1;
        nextIdleAt = Time.time + RandomIdleDelay();
        if (IsCommonLoop) BeginCommon();
        else ShowStop();
    }

    public void RefreshTiming()
    {
        if (set != null && image != null) Bind(image, set, tint);
    }

    public void RequestStart()
    {
        if (!IsCommonLoop) startRequested = true;
    }

    public void RequestStop()
    {
        if (IsCommonLoop) return;
        startRequested = false;
        if (Phase == PopulationFramePhase.Walk) stopRequested = true;
    }

    public bool BeginWalk(float routeLength, float nominalSpeed)
    {
        if (!CanBeginWalk || routeLength <= 0f || nominalSpeed <= 0f) return false;
        float cycle = WalkCycleSeconds;
        cyclesLeft = Mathf.Max(1, Mathf.CeilToInt(routeLength / (nominalSpeed * cycle)));
        walkSpeed = routeLength / (cyclesLeft * cycle);
        Phase = PopulationFramePhase.Walk;
        elapsed = 0f;
        shownFrame = -1;
        stopRequested = false;
        startRequested = false;
        ShowFrame(PopulationFrameAction.Walk, WalkStart);
        return true;
    }

    // Return requested path distance. If navigation cannot supply it, the caller
    // must invoke InterruptBlocked instead of letting the picture walk in place.
    public float Tick(float deltaTime, float now)
    {
        if (set == null || deltaTime <= 0f) return 0f;
        float remaining = deltaTime;
        float distance = 0f;
        while (remaining > 0.000001f)
        {
            if (Phase == PopulationFramePhase.CommonAction)
            {
                float slice = Mathf.Min(remaining, CommonCycleSeconds - elapsed);
                elapsed += slice;
                remaining -= slice;
                ShowElapsedFrame(PopulationFrameAction.Common);
                if (elapsed + 0.000001f < CommonCycleSeconds) break;
                Phase = PopulationFramePhase.Hold;
                elapsed = 0f;
                shownFrame = -1;
                nextIdleAt = now + UnityEngine.Random.Range(set.CommonHoldMin, set.CommonHoldMax);
                ShowStop();
                break;
            }
            if (Phase == PopulationFramePhase.TransitionToIdle ||
                Phase == PopulationFramePhase.TransitionToWalk)
            {
                bool reverse = Phase == PopulationFramePhase.TransitionToWalk;
                float duration = TransitionSeconds;
                float slice = Mathf.Min(remaining, duration - elapsed);
                elapsed += slice;
                remaining -= slice;
                ShowTransitionElapsed(reverse);
                if (elapsed + 0.000001f < duration) break;
                if (reverse)
                {
                    Phase = PopulationFramePhase.Hold;
                    transitionReadyToWalk = true;
                    startRequested = true;
                    nextIdleAt = float.PositiveInfinity;
                }
                else BeginIdle();
                break;
            }
            if (Phase == PopulationFramePhase.Walk)
            {
                float slice = Mathf.Min(remaining, WalkCycleSeconds - elapsed);
                distance += walkSpeed * slice;
                elapsed += slice;
                remaining -= slice;
                ShowElapsedFrame(PopulationFrameAction.Walk);
                if (elapsed + 0.000001f < WalkCycleSeconds) break;
                cyclesLeft--;
                if (stopRequested || cyclesLeft <= 0)
                {
                    if (HasTransition) BeginTransitionToIdle();
                    else EnterHold(now);
                    break;
                }
                elapsed = 0f;
                shownFrame = -1;
                ShowFrame(PopulationFrameAction.Walk, WalkStart);
            }
            else if (Phase == PopulationFramePhase.IdleAction)
            {
                float slice = Mathf.Min(remaining, IdleCycleSeconds - elapsed);
                elapsed += slice;
                remaining -= slice;
                ShowElapsedFrame(PopulationFrameAction.Idle);
                if (elapsed + 0.000001f < IdleCycleSeconds) break;
                if (HasTransition) EnterTransitionHold(now);
                else EnterHold(now);
                break;
            }
            else
            {
                if (IsCommonLoop)
                {
                    if (now >= nextIdleAt) BeginCommon();
                }
                else if (HasTransition && !transitionReadyToWalk)
                {
                    if (now >= nextIdleAt) BeginTransitionToWalk();
                }
                else if (!startRequested && now >= nextIdleAt) BeginIdle();
                break;
            }
        }
        return distance;
    }

    public void InterruptBlocked(float now)
    {
        if (HasTransition) BeginTransitionToIdle();
        else EnterHold(now);
    }

    public void FaceTravel(Vector3 travel, Camera camera)
    {
        if (set == null || image == null || camera == null || travel.sqrMagnitude < 0.000001f) return;
        Vector3 right = camera.transform.right;
        right.y = 0f;
        travel.y = 0f;
        float horizontal = Vector3.Dot(travel.normalized, right.normalized);
        if (Mathf.Abs(horizontal) > 0.15f)
            FaceHorizontal(horizontal);
    }

    public void FaceHorizontal(float direction)
    {
        if (image == null || Mathf.Abs(direction) < 0.15f) return;
        // flipX is absolute: repeated direction changes never accumulate scale changes.
        image.flipX = ArtworkFacesLeft ? direction > 0f : direction < 0f;
    }

    private void BeginIdle()
    {
        Phase = PopulationFramePhase.IdleAction;
        elapsed = 0f;
        shownFrame = -1;
        ShowFrame(PopulationFrameAction.Idle, IdleStart);
    }

    private void BeginCommon()
    {
        Phase = PopulationFramePhase.CommonAction;
        elapsed = 0f;
        shownFrame = -1;
        ShowFrame(PopulationFrameAction.Common, CommonStart);
    }

    private void BeginTransitionToIdle()
    {
        Phase = PopulationFramePhase.TransitionToIdle;
        transitionReadyToWalk = false;
        elapsed = 0f;
        shownFrame = -1;
        ShowFrame(PopulationFrameAction.TransitionForward, TransitionStart);
    }

    private void EnterTransitionHold(float now)
    {
        Phase = PopulationFramePhase.Hold;
        elapsed = 0f;
        shownFrame = -1;
        nextIdleAt = now + UnityEngine.Random.Range(set.MovementHoldMin, set.MovementHoldMax);
        ShowStop();
    }

    private void BeginTransitionToWalk()
    {
        Phase = PopulationFramePhase.TransitionToWalk;
        elapsed = 0f;
        shownFrame = -1;
        ShowFrame(PopulationFrameAction.TransitionReverse, TransitionEnd);
    }

    private void ShowTransitionElapsed(bool reverse)
    {
        int step = Mathf.Clamp(Mathf.FloorToInt(elapsed * TransitionFps), 0, TransitionEnd - TransitionStart);
        int target = reverse ? TransitionEnd - step : TransitionStart + step;
        if (reverse)
        {
            for (int i = shownFrame < 0 ? TransitionEnd : shownFrame - 1; i >= target; i--)
                ShowFrame(PopulationFrameAction.TransitionReverse, i);
        }
        else
        {
            for (int i = Mathf.Max(0, shownFrame + 1); i <= target; i++)
                ShowFrame(PopulationFrameAction.TransitionForward, i);
        }
    }

    private void EnterHold(float now)
    {
        Phase = PopulationFramePhase.Hold;
        elapsed = 0f;
        cyclesLeft = 0;
        stopRequested = false;
        shownFrame = -1;
        nextIdleAt = now + RandomIdleDelay();
        ShowStop();
    }

    private float RandomIdleDelay() => set == null ? 0f :
        UnityEngine.Random.Range(set.IdleDelayMin, set.IdleDelayMax);

    private void ShowStop()
    {
        if (set != null && image != null)
            PopulationSpriteDisplay.Configure(image, IsCommonLoop ? CommonStopSprite :
                HasTransition ? set.IdleFrames[IdleEnd] : StopSprite, tint, set.ReferenceVisualHeight);
        CurrentFrame = 0;
    }

    private void ShowElapsedFrame(PopulationFrameAction action)
    {
        int start = action == PopulationFrameAction.Walk ? WalkStart :
            action == PopulationFrameAction.Idle ? IdleStart : CommonStart;
        int end = action == PopulationFrameAction.Walk ? WalkEnd :
            action == PopulationFrameAction.Idle ? IdleEnd : CommonEnd;
        int fps = action == PopulationFrameAction.Walk ? WalkFps :
            action == PopulationFrameAction.Idle ? IdleFps : CommonFps;
        int frame = Mathf.Clamp(start + Mathf.FloorToInt(elapsed * fps), start, end);
        for (int index = Mathf.Max(start, shownFrame + 1); index <= frame; index++)
            ShowFrame(action, index);
    }

    private void ShowFrame(PopulationFrameAction action, int index)
    {
        if (set == null || image == null) return;
        Sprite[] frames = action == PopulationFrameAction.Walk ? set.WalkFrames :
            action == PopulationFrameAction.Idle ? set.IdleFrames :
            action == PopulationFrameAction.Common ? set.CommonFrames : set.TransitionFrames;
        if (index < 0 || index >= frames.Length) return;
        shownFrame = index;
        CurrentFrame = index + 1;
        PopulationSpriteDisplay.Configure(image, frames[index], tint, set.ReferenceVisualHeight);
        if (frameEvents == null) return;
        foreach (PopulationFrameEvent frameEvent in frameEvents)
            if (frameEvent != null && frameEvent.action == action && frameEvent.frame == CurrentFrame)
                frameEvent.callback?.Invoke();
    }
}
