using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>
/// 安柏测试场景（Assets/Scenes/AmberTest.unity）的动画切换面板：
/// 左侧列表点击或 ↑↓ 键切换动画，多目标同步播放（本体对比用）。
/// 双通道播放：肌肉曲线（humanoid clip，m_Legacy=0）走 Animator+Avatar 重定向；
/// 物理骨 TRS 曲线（legacy）走 Animation。两类 clip 同名并存，Play 时同时触发。
/// 列表只显示测试白名单（默认 13 条真肌肉动画，其余 140 条 motion-only 覆盖层身体无动作）。
/// </summary>
public class AmberTestPanel : MonoBehaviour
{
    [Header("安柏测试面板")]
    [SerializeField] private Animation[] 安柏动画组 = new Animation[0];
    [Tooltip("可选：Animator 通道（未填则自动取安柏动画组所在物体上的）")]
    [SerializeField] private Animator 安柏动画机 = null;
    [Tooltip("测试动作列表：留空=用内置 13 条真肌肉动作；填入则按所填顺序覆盖")]
    [SerializeField] private string[] 测试动作列表 = new string[0];

    // 安柏 blk 源内 14 条真肌肉动画（docs/14 §56）中剔除 DiveMoveAddU（未建肌肉 clip，身体不动），
    // 顺序=推荐测试顺序：弓箭攻击 → 展示（大幅全身）→ 元素战技 → 下落攻击 → 组队配置
    private static readonly string[] 内置测试名单 =
    {
        "Ani_Avatar_Girl_Bow_Ambor_Attack_05",
        "Ani_Avatar_Girl_Bow_Ambor_Show_01",
        "Ani_Avatar_Girl_Bow_Ambor_Show_02",
        "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short",
        "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short_AS",
        "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Middle_01",
        "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Middle_02",
        "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Long_01",
        "Ani_Avatar_Girl_Bow_Ambor_FallingAttack_BS_01",
        "Ani_Avatar_Girl_Bow_Ambor_FallingAttack_BS_02",
        "Ani_Avatar_Girl_Bow_Ambor_TeamConfig_01_AS",
        "Ani_Avatar_Girl_Bow_Ambor_TeamConfig_01_BS",
        "Ani_Avatar_Girl_Bow_Ambor_TeamConfig_01_Loop",
    };

    private readonly List<string> clipNames = new List<string>();
    private readonly Dictionary<string, AnimationClip> animatorClips = new Dictionary<string, AnimationClip>();
    private Vector2 scrollPos;
    private int current = -1;
    private Animator animator;
    private readonly Dictionary<Animation, Dictionary<string, AnimationClip>> legacyClips = new Dictionary<Animation, Dictionary<string, AnimationClip>>();

    private void Awake()
    {
        animator = 安柏动画机;
        if (animator == null && 安柏动画组.Length > 0 && 安柏动画组[0] != null)
            animator = 安柏动画组[0].GetComponentInParent<Animator>();
        // 肌肉通道依赖 Animator.enabled：disabled 时自建 PlayableGraph 照常 IsPlaying=true、
        // 但人形肌肉求值静默不跑（不写任何骨骼）——取证实证 arm/spine delta=0 而 hair 在动
        if (animator != null && !animator.enabled) animator.enabled = true;
    }

    private void Start()
    {
        // 收集 Animator 上的肌肉 clip（Playables 播放池）
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c != null && !animatorClips.ContainsKey(c.name))
                    animatorClips[c.name] = c;
        }

        foreach (var a in 安柏动画组)
        {
            if (a == null) continue;
            var map = new Dictionary<string, AnimationClip>();
            foreach (AnimationState s in a)
            {
                var clip = a.GetClip(s.name);
                if (clip == null) continue;
                if (!map.ContainsKey(s.name)) map[s.name] = clip;
            }
            legacyClips[a] = map;
        }

        // 列表只收测试白名单，顺序=推荐测试顺序（不按字母排序）
        var list = 测试动作列表 != null && 测试动作列表.Length > 0 ? 测试动作列表 : 内置测试名单;
        foreach (var name in list)
        {
            if (clipNames.Contains(name)) continue;
            bool legacyHas = false;
            foreach (var kv in legacyClips)
                if (kv.Value.ContainsKey(name)) { legacyHas = true; break; }
            if (legacyHas || animatorClips.ContainsKey(name))
                clipNames.Add(name);
        }

        // 初始动画：Attack_05（真肌肉动画，Play 一进来即验证身体动作）
        int initial = clipNames.IndexOf("Ani_Avatar_Girl_Bow_Ambor_Attack_05");
        if (initial >= 0) Play(initial);
    }

    private void Update()
    {
        if (clipNames.Count == 0) return;
        if (Input.GetKeyDown(KeyCode.UpArrow)) Play(current - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) Play(current + 1);
    }

    private void Play(int index)
    {
        if (index < 0 || index >= clipNames.Count) return;
        current = index;
        string name = clipNames[index];

        // Animator 通道：肌肉 clip 走 Playable 直接单播（免建控制器）
        if (animator != null && animatorClips.TryGetValue(name, out var mclip))
        {
            // 首次需清掉 Animator 自带控制器图（防止默认状态与单播图互相覆盖）；
            // 被销毁后 animator.playableGraph 返回无效句柄，直接 Destroy 会抛
            // "The PlayableGraph is null"（点击切换时连抛），一律 IsValid 守卫。
            var prev = animator.playableGraph;
            if (prev.IsValid()) prev.Destroy();
            if (_graph.IsValid()) _graph.Destroy();
            var graph = PlayableGraph.Create("AmberTestPanel");
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, mclip);
            playable.SetApplyFootIK(false);
            output.SetSourcePlayable(playable);
            graph.Play();
            _graph = graph;
        }

        // Animation 通道：物理骨 TRS legacy clip
        foreach (var kv in legacyClips)
        {
            var a = kv.Key;
            if (a == null) continue;
            if (!kv.Value.TryGetValue(name, out var lclip)) continue;
            a.Stop();
            a.clip = lclip;
            a.Play();
        }
    }

    private PlayableGraph _graph;

    private void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }

    private void OnGUI()
    {
        if (clipNames.Count == 0) return;
        float height = Screen.height - 32f;
        GUILayout.BeginArea(new Rect(16f, 16f, 320f, height), GUI.skin.box);
        GUILayout.Label("动画 ×" + clipNames.Count + "（↑↓ 切换）");
        scrollPos = GUILayout.BeginScrollView(scrollPos);
        for (int i = 0; i < clipNames.Count; i++)
        {
            GUI.enabled = i != current;
            if (GUILayout.Button(clipNames[i]))
                Play(i);
        }
        GUI.enabled = true;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
