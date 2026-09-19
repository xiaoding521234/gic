using GIC.Data;
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

