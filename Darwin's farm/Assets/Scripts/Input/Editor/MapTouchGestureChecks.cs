#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public static class MapTouchGestureChecks
{
    [MenuItem("Tools/Darwin Farm/Validation/Input/Check Touch Gesture Rules", false, 510)]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        var gesture = new MapTouchGesture();
        const float drag = 16f, hold = 0.45f;
        MapTouchGesture.Result Frame(float time, params MapTouchGesture.Contact[] contacts) =>
            gesture.Step(contacts, time, drag, hold);
        MapTouchGesture.Contact Finger(int id, TouchPhase phase, float x = 100f, float y = 100f,
            bool blocked = false) => new MapTouchGesture.Contact(id, new Vector2(x, y), phase, blocked);
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        Require(Frame(0f, Finger(1, TouchPhase.Began)).Kind == MapTouchGesture.Action.None,
            "Touch-down issued a scene command.");
        Require(Frame(.1f, Finger(1, TouchPhase.Ended)).Kind == MapTouchGesture.Action.Tap,
            "A short tap did not dispatch on release.");

        Frame(1f, Finger(1, TouchPhase.Began));
        Require(Frame(1.5f, Finger(1, TouchPhase.Stationary)).Kind == MapTouchGesture.Action.LongPress,
            "Long-press did not fire while held.");
        Require(Frame(1.6f, Finger(1, TouchPhase.Stationary)).Kind == MapTouchGesture.Action.None &&
            Frame(1.7f, Finger(1, TouchPhase.Ended)).Kind == MapTouchGesture.Action.None,
            "Long-press repeated or also issued a tap.");

        Frame(2f, Finger(1, TouchPhase.Began));
        Require(Frame(2.1f, Finger(1, TouchPhase.Moved, 130f)).Kind == MapTouchGesture.Action.None,
            "Crossing the drag threshold jumped the camera.");
        var pan = Frame(2.2f, Finger(1, TouchPhase.Moved, 140f));
        Require(pan.Kind == MapTouchGesture.Action.Pan && pan.Previous.x == 130f,
            "Pan did not use the last finger position.");
        Frame(2.3f, Finger(1, TouchPhase.Moved));
        Require(Frame(2.4f, Finger(1, TouchPhase.Ended)).Kind == MapTouchGesture.Action.None,
            "Returning a dragged finger to its origin turned it into a click.");

        Frame(3f, Finger(1, TouchPhase.Began));
        Require(Frame(3.1f, Finger(1, TouchPhase.Ended, 140f)).Kind == MapTouchGesture.Action.None,
            "A large movement on the release frame counted as a tap.");

        Frame(4f, Finger(1, TouchPhase.Began, blocked: true));
        Require(!gesture.HasWorldGesture &&
            Frame(4.5f, Finger(1, TouchPhase.Moved, 240f)).Kind == MapTouchGesture.Action.None &&
            Frame(4.6f, Finger(1, TouchPhase.Ended, 240f)).Kind == MapTouchGesture.Action.None,
            "UI-origin touch escaped into the scene.");

        Frame(5f, Finger(1, TouchPhase.Began));
        Require(Frame(5.1f, Finger(1, TouchPhase.Stationary), Finger(2, TouchPhase.Began, 200f)).Kind ==
            MapTouchGesture.Action.None, "Adding a second finger jumped the camera.");
        var pinch = Frame(5.2f, Finger(2, TouchPhase.Moved, 220f), Finger(1, TouchPhase.Moved, 80f));
        Require(pinch.Kind == MapTouchGesture.Action.Pinch && Mathf.Abs(pinch.ZoomFactor - 100f / 140f) < .001f,
            "Pinch scale or reordered finger identity was wrong.");
        Require(Frame(5.3f, Finger(1, TouchPhase.Stationary, 80f), Finger(2, TouchPhase.Ended, 220f)).Kind ==
            MapTouchGesture.Action.None &&
            Frame(5.4f, Finger(1, TouchPhase.Moved, 70f)).Kind == MapTouchGesture.Action.None &&
            Frame(5.5f, Finger(1, TouchPhase.Ended, 70f)).Kind == MapTouchGesture.Action.None,
            "Lifting one finger after a pinch triggered a pan or click.");

        Frame(6f, Finger(1, TouchPhase.Began));
        Require(Frame(6.1f, Finger(1, TouchPhase.Stationary), Finger(2, TouchPhase.Began, 200f, blocked: true)).Kind ==
            MapTouchGesture.Action.None && !gesture.HasWorldGesture,
            "The second finger on UI started a map pinch.");
        Frame(6.2f);

        Frame(7f, Finger(1, TouchPhase.Began));
        Require(Frame(7.1f, Finger(1, TouchPhase.Canceled)).Kind == MapTouchGesture.Action.None,
            "Canceled system touch issued a tap.");
        Frame(8f, Finger(1, TouchPhase.Began));
        gesture.Cancel();
        Require(Frame(8.8f, Finger(1, TouchPhase.Stationary)).Kind == MapTouchGesture.Action.None &&
            Frame(8.9f, Finger(1, TouchPhase.Ended)).Kind == MapTouchGesture.Action.None,
            "Focus/pause cancellation left an old long-press or tap pending.");

        Frame(9f, Finger(1, TouchPhase.Began), Finger(2, TouchPhase.Began, 200f));
        Require(Frame(9.1f, Finger(1, TouchPhase.Stationary), Finger(2, TouchPhase.Stationary, 200f),
            Finger(3, TouchPhase.Began, 300f)).Kind == MapTouchGesture.Action.None && !gesture.HasWorldGesture,
            "A third finger retained a stale two-finger gesture.");
        Frame(9.2f);

        Frame(10f, Finger(1, TouchPhase.Began));
        Require(Frame(10.1f, Finger(1, TouchPhase.Ended), Finger(2, TouchPhase.Began, 200f)).Kind ==
            MapTouchGesture.Action.None && !gesture.HasWorldGesture,
            "Another finger adopted an unfinished touch sequence.");
        Frame(10.2f);
        Require(Frame(11f, Finger(1, TouchPhase.Moved)).Kind == MapTouchGesture.Action.None && !gesture.HasWorldGesture,
            "Input resumed in the middle of a touch and adopted it as a new gesture.");
        Frame(11.1f);
        Frame(12f, Finger(4, TouchPhase.Began));
        Require(Frame(12.1f, Finger(4, TouchPhase.Ended)).Kind == MapTouchGesture.Action.Tap &&
            gesture.CurrentState == MapTouchGesture.State.Idle,
            "Fresh input did not recover after gesture cancellation.");
        return "Touch gesture checks passed: tap-on-release, one-shot hold, sticky drag, no threshold jump, " +
            "UI ownership, pinch scale, finger order, pinch release, mixed UI fingers, canceled touches, " +
            "focus/pause recovery, third finger and finger replacement.";
    }
}
#endif
