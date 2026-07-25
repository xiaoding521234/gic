using UnityEngine;

public class UnitManager : IWargameManager
{
    public UnitConfig unitConfig;



    public void Start()
    {
        unitConfig = Wargame.Instance.ConfigManager.GetUnitConfig();
    }

    public void Update(float deltaTime)
    {

    }
}