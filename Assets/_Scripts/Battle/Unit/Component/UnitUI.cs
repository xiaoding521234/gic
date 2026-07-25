using UnityEngine;

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