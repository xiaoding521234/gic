using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    [Component]
    public class UnitManager : IWargameManager
    {
        public readonly UnitConfig unitConfig;

        public UnitManager(UnitConfig unitConfig)
        {
            this.unitConfig = unitConfig;
        }

        public void Start() { }

        public void Update(float deltaTime)
        {

        }
    }
}

