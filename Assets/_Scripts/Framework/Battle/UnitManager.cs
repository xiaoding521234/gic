using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


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
}


