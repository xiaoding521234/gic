namespace GIC.Data
{
    /// <summary>具名 Buff 资产名常量（buffKey=资产名，决策五十七「资产即身份」）——代码内以字符串
    /// 引用 buffKey 的唯一合法入口（字面量散写=资产改名时静默回落错资产）。新增代码侧引用的具名 buff
    /// 在此登记；资产↔资产的引用走 buffAsset 直连不受此限（SkillEffectConfig.ApplyBuff）。</summary>
    public static class BuffKeys
    {
        /// <summary>超导减防（决策七十四：冰+雷反应施加——数值真源=Buff_Superconduct 资产）</summary>
        public const string Superconduct = "Buff_Superconduct";

        /// <summary>冰棱减防（凯亚寒冰之棱 2命 tick 施加——数值真源=Buff_DefenseDown 资产单源）</summary>
        public const string DefenseDown = "Buff_DefenseDown";

        /// <summary>视野暴露（安柏变奏与分支命座共用资产）</summary>
        public const string VisionExposure = "Buff_VisionExposure";
    }
}
