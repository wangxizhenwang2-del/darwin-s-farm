using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SimulationTime : MonoBehaviour
{
    // Standard game loop: one simulated day per second at 1x, 1800 days in
    // fifteen minutes at the existing 2x speed.
    public const float DefaultSecondsPerDay = 1f;
    public const int StandardLoopDays = 1800;
    public const float StandardLoopSpeed = 2f;
    private float basicDayLength = DefaultSecondsPerDay;
    private float simulationSpeed = 1f;
    private float lastRunningSpeed = 1f;
    private float time=0;
    public int currentDay;
    public event Action<int> OnDayChanged;
    public event Action<int> OnDayReset;
    public float SecondsPerDay => basicDayLength;
    public float Speed => simulationSpeed;
    public bool IsPaused => simulationSpeed <= 0f;

    public void SetSecondsPerDay(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        basicDayLength = Mathf.Max(0.05f, seconds);
    }

    public void NextDay()
    {
        currentDay++;
        OnDayChanged?.Invoke(currentDay);
    }

    void Start()
    {
        ResetDay();
    }
    
    void Update()
    {
        // 2026-10-09 14:50 +08:00: player shortcuts change simulation speed
        // only; Unity rendering and map controls continue while paused.
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
            if (keyboard.digit2Key.wasPressedThisFrame ||
                keyboard.numpad2Key.wasPressedThisFrame) DoubleSpeed();
        }
#else
        if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) DoubleSpeed();
#endif
        time += Time.deltaTime * simulationSpeed;
        // 一帧跨过多天时逐天补算
        while (time >= basicDayLength)
        {
            time -= basicDayLength;
            NextDay(); //授时
        }
    }

    public void Pause()
    {
        if (!IsPaused) lastRunningSpeed = simulationSpeed;
        simulationSpeed = 0f;
    }
    public void Play() { simulationSpeed = 1f; lastRunningSpeed = 1f; }
    public void DoubleSpeed() { simulationSpeed = 2f; lastRunningSpeed = 2f; }
    public void TogglePause()
    {
        if (IsPaused) simulationSpeed = lastRunningSpeed > 0f ? lastRunningSpeed : 1f;
        else Pause();
    }
    public void ResetDay()  
    {
        currentDay=0;
        time=0;
        OnDayReset?.Invoke(currentDay);
    }
}
