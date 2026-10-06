using UnityEngine;
using System;

public class SimulationTime : MonoBehaviour
{
    private float basicDayLength = 5f;
    private float simulationSpeed = 1f;
    private float time=0;
    public int currentDay;
    public event Action<int> OnDayChanged;
    public float SecondsPerDay => basicDayLength;

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
        time += Time.deltaTime * simulationSpeed;
        // 一帧跨过多天时逐天补算
        while (time >= basicDayLength)
        {
            time -= basicDayLength;
            NextDay(); //授时
            Debug.Log("Day "+currentDay);
        }
    }

    public void Pause() {simulationSpeed=0;}
    public void Play()  {simulationSpeed=1;}
    public void DoubleSpeed()   {simulationSpeed=2;}
    public void ResetDay()  
    {
        currentDay=0;
        time=0;
    }
}
