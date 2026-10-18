using System.Collections.Generic;
using UnityEngine;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

// One touch sequence owns one action. This has no scene or input-device writes,
// so the same rules can be checked without moving the map camera.
public sealed class MapTouchGesture
{
    public enum State { Idle, Pending, Dragging, LongPressed, Pinching, Suppressed }
    public enum Action { None, Tap, LongPress, Pan, Pinch }

    public readonly struct Contact
    {
        public readonly long Id;
        public readonly Vector2 Position;
        public readonly TouchPhase Phase;
        public readonly bool BlockedAtStart;
        public Contact(long id, Vector2 position, TouchPhase phase, bool blockedAtStart = false)
        { Id = id; Position = position; Phase = phase; BlockedAtStart = blockedAtStart; }
        public bool Active => Phase != TouchPhase.Ended && Phase != TouchPhase.Canceled;
    }

    public readonly struct Result
    {
        public readonly Action Kind;
        public readonly Vector2 Previous, Position;
        public readonly float ZoomFactor;
        public Result(Action kind, Vector2 previous, Vector2 position, float zoomFactor = 1f)
        { Kind = kind; Previous = previous; Position = position; ZoomFactor = zoomFactor; }
    }

    public State CurrentState { get; private set; }
    public bool HasWorldGesture => CurrentState == State.Pending || CurrentState == State.Dragging ||
        CurrentState == State.LongPressed || CurrentState == State.Pinching;
    private long primaryId, secondaryId;
    private Vector2 pressPosition, previousPosition, previousCenter;
    private float pressedAt, previousDistance;

    public void Cancel() => CurrentState = State.Suppressed;
    public void Reset() => CurrentState = State.Idle;

    public Result Step(IReadOnlyList<Contact> contacts, float now, float dragThreshold, float holdSeconds)
    {
        int active = 0;
        Contact first = default, second = default, primary = default;
        bool foundPrimary = false;
        for (int i = 0; i < contacts.Count; i++)
        {
            Contact contact = contacts[i];
            if (contact.Id == primaryId) { primary = contact; foundPrimary = true; }
            if (contact.Phase == TouchPhase.Began && contact.BlockedAtStart) Cancel();
            if (!contact.Active) continue;
            if (active == 0) first = contact;
            else if (active == 1) second = contact;
            active++;
        }

        if (active == 0)
        {
            Result result = default;
            if (CurrentState == State.Pending && foundPrimary && primary.Phase == TouchPhase.Ended &&
                (primary.Position - pressPosition).sqrMagnitude <= dragThreshold * dragThreshold)
                result = new Result(now - pressedAt >= holdSeconds ? Action.LongPress : Action.Tap,
                    pressPosition, primary.Position);
            Reset();
            return result;
        }
        if (CurrentState == State.Suppressed) return default;
        if (active > 2) { Cancel(); return default; }
        if (active == 2)
        {
            Vector2 center = (first.Position + second.Position) * 0.5f;
            float distance = Vector2.Distance(first.Position, second.Position);
            if (CurrentState != State.Pinching)
            {
                // Don't adopt fingers that were already down when input resumed.
                if (CurrentState == State.Idle && first.Phase != TouchPhase.Began && second.Phase != TouchPhase.Began)
                { Cancel(); return default; }
                primaryId = first.Id; secondaryId = second.Id;
                previousCenter = center; previousDistance = distance;
                CurrentState = State.Pinching;
                return default;
            }
            if (!((first.Id == primaryId && second.Id == secondaryId) ||
                  (first.Id == secondaryId && second.Id == primaryId)))
            { Cancel(); return default; }
            float factor = distance > 1f && previousDistance > 1f ? previousDistance / distance : 1f;
            Result pinch = new Result(Action.Pinch, previousCenter, center, factor);
            previousCenter = center; previousDistance = distance;
            return pinch;
        }
        if (CurrentState == State.Pinching) { Cancel(); return default; }
        if (CurrentState == State.Idle)
        {
            if (first.Phase != TouchPhase.Began) { Cancel(); return default; }
            primaryId = first.Id; pressPosition = previousPosition = first.Position; pressedAt = now;
            CurrentState = State.Pending;
            return default;
        }
        if (first.Id != primaryId) { Cancel(); return default; }
        if (CurrentState == State.LongPressed) return default;
        if (CurrentState == State.Pending)
        {
            if ((first.Position - pressPosition).sqrMagnitude > dragThreshold * dragThreshold)
            {
                CurrentState = State.Dragging;
                previousPosition = first.Position; // Crossing the threshold must not jump the camera.
                return default;
            }
            if (now - pressedAt >= holdSeconds)
            {
                CurrentState = State.LongPressed;
                return new Result(Action.LongPress, pressPosition, first.Position);
            }
            return default;
        }
        Result pan = new Result(Action.Pan, previousPosition, first.Position);
        previousPosition = first.Position;
        return pan;
    }
}
