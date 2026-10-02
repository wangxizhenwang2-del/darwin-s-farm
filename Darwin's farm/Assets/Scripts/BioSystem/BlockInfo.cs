using System.Collections.Generic;
using UnityEngine;

public class BlockInfo : MonoBehaviour
{
    [SerializeField]List<BlockInfo> neighbors;
    [SerializeField]private int temperature;
    [SerializeField]private int humidity;
    [SerializeField]private int Elevation; 
    [SerializeField]private int WaterCoverage;
    [SerializeField]private int PlantBiomass;
    struct community{
        string species;
        int amount;
    }
    [SerializeField]private List<community> communities;

    void Start()
    {
        
    }
}
