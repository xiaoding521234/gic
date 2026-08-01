using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    public class UnitUI : MonoBehaviour, IUnitComponent
    {

        private Unit _owner;

        [SerializeField] private Sprite avatar;


        public void Init(Unit unit)
        {
            _owner = unit;
            avatar = _owner.RawData.avatar;
            
               
        }
    }
}

