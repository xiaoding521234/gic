using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{

    public interface IWargameManager
    {
        void Start();
        void Update(float deltaTime);
    }
}

