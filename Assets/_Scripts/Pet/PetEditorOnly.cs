using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 编辑器专用对象的构建期门（2026-08-24 单场景方案）：挂在只应在编辑器出现的东西上
    /// （PaimonPet 的动作测试面板 AnimUICanvas / EventSystem）——构建版 Awake 即自禁用，
    /// 桌宠进程永不渲染测试 UI；编辑器 Play 不受影响。
    /// </summary>
    public class PetEditorOnly : MonoBehaviour
    {
        private void Awake()
        {
#if !UNITY_EDITOR
            gameObject.SetActive(false);
#endif
        }
    }
}
