using UnityEngine;
using GIC.Battle;

/// <summary>
/// 冻结霜化测试面板（Assets/Scenes/FrozenTest.unity）：F 键或屏幕按钮切换冻结↔解冻——直接验证
/// 霜化 shader 表现（2026-10-02 拍板「像真的结冰」）。Play 起始重播一次上冻蔓延；编辑模式下
/// 立牌由场景内静态霜化材质（FrozenSpriteTest.mat，_FrozenAmount=1）直接显示结冰态，无需 Play。
/// </summary>
public class FrozenTestPanel : MonoBehaviour
{
    [Tooltip("被测立牌（场景里预先摆好的 UnitView）")]
    [SerializeField] private UnitView 立牌;

    private bool _frozen;

    private void Start()
    {
        // Play 起始重播上冻（SetFrozenVisual 幂等——静态冻结起步时 from≈1 则单次落地不重放）
        if (立牌 != null) SetFrozen(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F)) Toggle();
    }

    private void Toggle() => SetFrozen(!_frozen);

    private void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        if (立牌 != null) 立牌.SetFrozenVisual(frozen);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16f, 16f, 240f, 110f), GUI.skin.box);
        GUILayout.Label("冻结霜化测试\nF 键：冻结 / 解冻");
        if (GUILayout.Button(_frozen ? "解冻（退冰）" : "冻结（结冰）"))
            Toggle();
        GUILayout.EndArea();
    }
}
