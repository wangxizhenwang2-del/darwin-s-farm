using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SimulationTime : MonoBehaviour
{
    // One simulated day per second at 1x. At 4x, fifteen minutes spans 3600 days.
    public const float DefaultSecondsPerDay = 1f;
    public const int StandardLoopDays = 3600;
    public const float StandardLoopSpeed = 4f;
    private float basicDayLength = DefaultSecondsPerDay;
    private float simulationSpeed = 1f;
    private float lastRunningSpeed = 1f;
    private float time=0;
    public int currentDay;
    public event Action<int> OnDayChanged;
    public event Action<int> OnDayReset;
    public event Action<float> OnSpeedChanged;
    public float SecondsPerDay => basicDayLength;
    public float Speed => simulationSpeed;
    public float RunningSpeed => IsPaused ? lastRunningSpeed : simulationSpeed;
    public bool IsPaused => simulationSpeed <= 0f;
    private bool modalPause;
    private bool wasPausedBeforeModal;
    private float speedBeforeModal;

    [Serializable] public sealed class SaveState
    {
        public int day;
        public float fraction, secondsPerDay, runningSpeed;
        public bool paused;
    }
    public SaveState CaptureSaveState() => new SaveState {
        day=currentDay, fraction=time, secondsPerDay=basicDayLength,
        runningSpeed=modalPause?speedBeforeModal:RunningSpeed,
        paused=modalPause?wasPausedBeforeModal:IsPaused
    };
    public void RestoreSaveState(SaveState state)
    {
        if (state == null || state.day < 0 || state.secondsPerDay < .05f ||
            state.fraction < 0 || state.fraction >= state.secondsPerDay ||
            state.runningSpeed <= 0 || float.IsNaN(state.fraction))
            throw new ArgumentException("Invalid saved game clock.");
        currentDay=state.day; time=state.fraction;
        basicDayLength=state.secondsPerDay;
        lastRunningSpeed=state.runningSpeed;
        wasPausedBeforeModal=state.paused;
        speedBeforeModal=state.runningSpeed;
        simulationSpeed=0f;
        OnSpeedChanged?.Invoke(simulationSpeed);
    }

    public void AcquireModalPause()
    {
        if (modalPause) return;
        wasPausedBeforeModal = IsPaused;
        speedBeforeModal = RunningSpeed;
        modalPause = true;
        Pause();
    }

    public void ReleaseModalPause()
    {
        if (!modalPause) return;
        modalPause = false;
        if (!wasPausedBeforeModal) SetSpeed(speedBeforeModal);
    }

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
        if (keyboard != null && !modalPause)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
            if (keyboard.digit2Key.wasPressedThisFrame ||
                keyboard.numpad2Key.wasPressedThisFrame) DoubleSpeed();
            if (keyboard.digit4Key.wasPressedThisFrame ||
                keyboard.numpad4Key.wasPressedThisFrame) QuadrupleSpeed();
        }
#else
        if (!modalPause && Input.GetKeyDown(KeyCode.Space)) TogglePause();
        if (!modalPause && (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))) DoubleSpeed();
        if (!modalPause && (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))) QuadrupleSpeed();
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
        OnSpeedChanged?.Invoke(simulationSpeed);
    }
    public void Play() => SetSpeed(1f);
    public void DoubleSpeed() => SetSpeed(2f);
    public void QuadrupleSpeed() => SetSpeed(4f);
    // Shared by desktop and mobile: 1x -> 2x -> 4x -> pause -> 1x.
    public void CycleSpeed()
    {
        if (modalPause) return;
        if (IsPaused) Play();
        else if (Speed >= 4f) Pause();
        else SetSpeed(Speed < 2f ? 2f : 4f);
    }
    private void SetSpeed(float speed)
    {
        if (modalPause) return;
        simulationSpeed = speed;
        lastRunningSpeed = speed;
        OnSpeedChanged?.Invoke(simulationSpeed);
    }
    public void TogglePause()
    {
        if (modalPause) return;
        if (IsPaused) simulationSpeed = lastRunningSpeed > 0f ? lastRunningSpeed : 1f;
        else Pause();
        if (!IsPaused) OnSpeedChanged?.Invoke(simulationSpeed);
    }
    public void ResetDay()  
    {
        currentDay=0;
        time=0;
        OnDayReset?.Invoke(currentDay);
    }
}
