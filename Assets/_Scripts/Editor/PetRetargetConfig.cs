using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor.Retarget
{
    /// <summary>
    /// 方案B重定向配置（ScriptableObject）：模型路径/转换清单/骨映射规则/特殊骨/阈值全部数据化，
    /// 换 MMD 模型或加动画只改配置不改代码。默认值 = 2026-08-22 v8 验证通过的派蒙参数。
    /// 映射语义与原 MapMmd 一致：先剥 _shadow_/_dummy_/+ 前缀与 .L/.R 侧后缀，先查精确表，
    /// 再按前缀表顺序首匹配；{S} 占位符替换为 "L "/"R "。
    /// </summary>
    [CreateAssetMenu(fileName = "RetargetConfig", menuName = "GIC/桌宠重定向配置")]
    public class PetRetargetConfig : ScriptableObject
    {
        [System.Serializable]
        public class ExactRule
        {
            public string mmdName; // 精确 MMD 骨名（已剥前缀后）
            public string giName;  // GI 骨名（可含 {S}）
        }

        [System.Serializable]
        public class PrefixRule
        {
            public string mmdPrefix; // MMD 骨名前缀（已剥前缀后，按序首匹配）
            public string giTemplate; // GI 骨名模板（可含 {S}）
        }

        [Header("模型与输出路径")]
        public string mmdFbxPath = "Assets/Art/PaimonPet/Model/Paimon_MMD.fbx";
        public string giFbxPath = "Assets/Art/PaimonPet/Model/NPC_Kanban_Paimon_Model.fbx";
        public string animDir = "Assets/Art/PaimonPet/Animations";
        public string outDir = "Assets/Art/PaimonPet/Animations/MMD";
        public string testScene = "Assets/Scenes/PaimonRetargetTest.unity";

        [Header("MMD 骨架节点名")]
        public string armNodeName = "Paimon_arm";          // 缩放100节点（动画path前缀）
        public string rootBoneName = "\u5168\u3066\u306e\u89aa"; // 全ての親

        [Header("转换清单（GI 动画名，不含 Ani_NPC_Kanban_Paimon_ 前后缀）")]
        public string[] clips = { "Standby", "Greet", "Anger" };

        [Header("对齐关键骨（GI 侧为 model 节点下相对路径，MMD 侧为骨名）")]
        public string giPelvisPath = "Bip001/Bip001 Pelvis";
        public string giHeadPath = "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Neck/Bip001 Head";
        public string giHandLPath = "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 L Clavicle/Bip001 L UpperArm/Bip001 L Forearm/Bip001 L Hand";
        public string giHandRPath = "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 R Clavicle/Bip001 R UpperArm/Bip001 R Forearm/Bip001 R Hand";
        public string mmdPelvisName = "\u8170";     // 腰
        public string mmdHeadName = "\u982d";       // 頭
        public string mmdHandLName = "\u624b\u9996.L"; // 手首.L
        public string mmdHandRName = "\u624b\u9996.R"; // 手首.R

        [Header("骨映射——精确表（优先）")]
        public ExactRule[] exactRules =
        {
            new ExactRule { mmdName = "\u30bb\u30f3\u30bf\u30fc", giName = "Bip001 Pelvis" },            // センター
            new ExactRule { mmdName = "\u8170", giName = "Bip001 Pelvis" },                               // 腰
            new ExactRule { mmdName = "\u4e0b\u534a\u8eab", giName = "Bip001 Pelvis" },                   // 下半身
            new ExactRule { mmdName = "\u30b0\u30eb\u30fc\u30d6", giName = "Bip001 Pelvis" },             // グルーブ
            new ExactRule { mmdName = "\u8170\u30d1\u30fc\u30c4\u89aa", giName = "Bip001 Pelvis" },       // 腰パーツ親
            new ExactRule { mmdName = "\u5168\u3066\u306e\u89aa", giName = "Bip001 Pelvis" },             // 全ての親
            new ExactRule { mmdName = "\u64cd\u4f5c\u4e2d\u5fc3", giName = "Bip001 Pelvis" },             // 操作中心
            new ExactRule { mmdName = "\u4e0a\u534a\u8eab", giName = "Bip001 Spine" },                    // 上半身
            new ExactRule { mmdName = "\u4e0a\u534a\u8eab1", giName = "Bip001 Spine1" },                  // 上半身1
            new ExactRule { mmdName = "\u4e0a\u534a\u8eab2", giName = "Bip001 Spine1" },                  // 上半身2
            new ExactRule { mmdName = "\u9996", giName = "Bip001 Neck" },                                 // 首
            new ExactRule { mmdName = "\u982d", giName = "Bip001 Head" },                                 // 頭
            new ExactRule { mmdName = "\u4e21\u76ee", giName = "Bip001 Head" },                           // 両目
        };

        [Header("骨映射——前缀表（有序，首匹配）")]
        public PrefixRule[] prefixRules =
        {
            new PrefixRule { mmdPrefix = "\u8db3\u9996D", giTemplate = "Bip001 {S}Foot" },        // 足首D
            new PrefixRule { mmdPrefix = "\u3072\u3056", giTemplate = "Bip001 {S}Calf" },         // ひざ
            new PrefixRule { mmdPrefix = "\u8db3D", giTemplate = "Bip001 {S}Thigh" },             // 足D
            new PrefixRule { mmdPrefix = "\u80a9", giTemplate = "Bip001 {S}Clavicle" },           // 肩
            new PrefixRule { mmdPrefix = "\u8155\u6369", giTemplate = "Bip001 {S}UpperArm" },     // 腕捩
            new PrefixRule { mmdPrefix = "\u8155", giTemplate = "Bip001 {S}UpperArm" },           // 腕
            new PrefixRule { mmdPrefix = "\u3072\u3058", giTemplate = "Bip001 {S}Forearm" },      // ひじ
            new PrefixRule { mmdPrefix = "\u624b\u6369", giTemplate = "Bip001 {S}Forearm" },      // 手捩
            new PrefixRule { mmdPrefix = "\u624b\u9996", giTemplate = "Bip001 {S}Hand" },         // 手首
            new PrefixRule { mmdPrefix = "\u624b\u5148", giTemplate = "Bip001 {S}Hand" },         // 手先
            new PrefixRule { mmdPrefix = "\u89aa\u6307", giTemplate = "Bip001 {S}Hand" },         // 親指
            new PrefixRule { mmdPrefix = "\u4eba\u6307", giTemplate = "Bip001 {S}Hand" },         // 人指
            new PrefixRule { mmdPrefix = "\u4e2d\u6307", giTemplate = "Bip001 {S}Hand" },         // 中指
            new PrefixRule { mmdPrefix = "\u85ac\u6307", giTemplate = "Bip001 {S}Hand" },         // 薬指
            new PrefixRule { mmdPrefix = "\u5c0f\u6307", giTemplate = "Bip001 {S}Hand" },         // 小指
            new PrefixRule { mmdPrefix = "\u30c0\u30df\u30fc", giTemplate = "Bip001 {S}Hand" },   // ダミー
            new PrefixRule { mmdPrefix = "\u3064\u307e\u5148", giTemplate = "Bip001 {S}Toe0" },   // つま先
            new PrefixRule { mmdPrefix = "\u8db3\u5148EX", giTemplate = "Bip001 {S}Toe0" },       // 足先EX
            new PrefixRule { mmdPrefix = "\u8db3IK\u89aa", giTemplate = "Bip001 {S}Foot" },       // 足IK親
            new PrefixRule { mmdPrefix = "\u8db3\uff29\uff2b", giTemplate = "Bip001 {S}Foot" },   // 足ＩＫ
            new PrefixRule { mmdPrefix = "\u8db3\u9996", giTemplate = "Bip001 {S}Foot" },         // 足首
            new PrefixRule { mmdPrefix = "\u8db3", giTemplate = "Bip001 {S}Thigh" },              // 足
            new PrefixRule { mmdPrefix = "\u76ee", giTemplate = "+EyeBone {S}A01" },              // 目
            new PrefixRule { mmdPrefix = "\u8170\u30ad\u30e3\u30f3\u30bb\u30eb", giTemplate = "Bip001 {S}Thigh" }, // 腰キャンセル
        };

        [Header("特殊骨策略")]
        [Tooltip("完全不驱动（保持绑定）：平移虹膜骨/独立节点")]
        public string[] undrivenBones = { "\u76ee.L", "\u76ee.R", "\u64cd\u4f5c\u4e2d\u5fc3" }; // 目.L/R、操作中心
        [Tooltip("位置锚骨名（唯一传位置的骨，承载bob/根运动）")]
        public string anchorBoneName = "\u8170"; // 腰
        [Tooltip("躯干骨：姿势参考方向改'指向头骨'（规避Biped骨段结构角）")]
        public string[] torsoHeadRefBones = { "\u8170", "\u4e0a\u534a\u8eab", "\u4e0a\u534a\u8eab2", "\u9996" }; // 腰/上半身/上半身2/首

        [Header("代表骨优先级（每GI骨选一个MMD代表；0最高）")]
        [Tooltip("rank0 主链标准名（逗号分隔）")]
        public string[] rank0Bones =
        {
            "\u8170", "\u4e0a\u534a\u8eab", "\u4e0a\u534a\u8eab2", "\u9996", "\u982d",
            "\u8155.L", "\u8155.R", "\u3072\u3058.L", "\u3072\u3058.R", "\u624b\u9996.L", "\u624b\u9996.R",
            "\u8db3.L", "\u8db3.R", "\u3072\u3056.L", "\u3072\u3056.R", "\u8db3\u9996.L", "\u8db3\u9996.R",
            "\u80a9.L", "\u80a9.R", "\u76ee.L", "\u76ee.R"
        };
        [Tooltip("rank3 中枢备胎（逗号分隔）")]
        public string[] rank3Bones =
        {
            "\u30bb\u30f3\u30bf\u30fc", "\u30b0\u30eb\u30fc\u30d6", "\u4e0b\u534a\u8eab", "\u64cd\u4f5c\u4e2d\u5fc3", "\u8170\u30d1\u30fc\u30c4\u89aa", "\u4e0a\u534a\u8eab1", "\u4e21\u76ee"
        };
        [Tooltip("rank1 次要前缀（承载渐变权重的辅助链，逗号分隔）")]
        public string[] rank1Prefixes =
        {
            "\u8155\u6369", "\u624b\u6369", "\u8db3D", "\u3072\u3056D", "\u8db3\u9996D", "\u8170\u30ad\u30e3\u30f3\u30bb\u30eb",
            "\u3064\u307e\u5148", "\u8db3\u5148EX", "\u89aa\u6307", "\u4eba\u6307", "\u4e2d\u6307", "\u85ac\u6307", "\u5c0f\u6307",
            "\u624b\u5148", "\u30c0\u30df\u30fc", "\u80a9"
        };

        [Header("自检阈值")]
        public float anchorPosErrMax = 0.0001f;  // 锚位误差（米）
        public float rotErrMaxDeg = 0.5f;        // 旋转解算误差（度）
        public float dirErrMaxDeg = 20f;         // 参考方向跟踪误差上界（度，躯干链式合成宽松）
        public float alignEyeErrMax = 0.02f;     // 对齐质量眼距（米）
        public float anchorResidualMax = 0.005f; // Standby 盆锚残差（米）
        public float headResidualMax = 0.02f;    // Standby 头拟合残差（米）

        // ---------------- 运行时辅助（非序列化） ----------------

        /// <summary>与原 MapMmd 等价：剥前缀/侧缀 → 精确表 → 前缀表首匹配</summary>
        public Transform MapMmdBone(string raw, System.Func<string, Transform> giGet)
        {
            var n = raw;
            if (n.StartsWith("_shadow_")) n = n.Substring(8);
            else if (n.StartsWith("_dummy_")) n = n.Substring(7);
            if (n.StartsWith("+")) n = n.Substring(1);
            string s = n.EndsWith(".L") ? "L" : n.EndsWith(".R") ? "R" : "";
            string WithSide(string tmpl) => s.Length == 0 ? tmpl.Replace(" {S}", "").Replace("{S} ", "").Replace("{S}", "") : tmpl.Replace("{S}", s + " ");
            foreach (var r in exactRules)
                if (r.mmdName == n) return giGet(s.Length > 0 ? WithSide(r.giName) : r.giName);
            foreach (var r in prefixRules)
            {
                if (!n.StartsWith(r.mmdPrefix)) continue;
                var t = s.Length > 0 ? WithSide(r.giTemplate) : r.giTemplate.Replace(" {S}", "").Replace("{S} ", "").Replace("{S}", "");
                var got = giGet(t);
                if (got != null) return got;
            }
            return null;
        }

        public HashSet<string> UndrivenSet() => new HashSet<string>(undrivenBones);
        public HashSet<string> TorsoSet() => new HashSet<string>(torsoHeadRefBones);

        /// <summary>默认配置资产路径（管线缺失时自动创建）</summary>
        public const string DEFAULT_ASSET = "Assets/Art/PaimonPet/RetargetConfig.asset";

        public static PetRetargetConfig LoadOrCreate()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<PetRetargetConfig>(DEFAULT_ASSET);
            if (cfg != null) return cfg;
            cfg = CreateInstance<PetRetargetConfig>();
            AssetDatabase.CreateAsset(cfg, DEFAULT_ASSET);
            AssetDatabase.SaveAssets();
            return cfg;
        }
    }
}
