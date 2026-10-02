using UnityEngine;
using System;

public class SimulationTime : MonoBehaviour
{
    private float basicDayLength = 5f;
    private float simulationSpeed = 1f;
    private float time=0;
    public int currrentDay;
    public event Action<int> OnDayChanged;

    void Start()
    {
        ResetDay();
    }
    
    void Update()
    {
        //计算天数
        float dayLength = basicDayLength / simulationSpeed;
        if (time == dayLength)
        {
            currrentDay++;
            //授时
            OnDayChanged?.Invoke(currrentDay);
            Debug.Log("Day "+currrentDay);
        } else
        {
            time += Time.timeScale;
        }
    }

    public void Pause() {simulationSpeed=0;}
    public void Play()  {simulationSpeed=1;}
    public void DoubleSpeed()   {simulationSpeed=2;}
    public void ResetDay()  {currrentDay=0;}


}





