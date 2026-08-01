using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{

    /// <summary>
    /// 地块件接口
    /// </summary>
    public interface ITileComponent
    {
        void Initialize(Tile owner);
    }
}

