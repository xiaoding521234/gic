using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>
/// 安柏测试场景（Assets/Scenes/AmberTest.unity）的显式双通道动画切换面板。
/// 身体肌肉曲线由 Animator + PlayableGraph 播放，物理骨 TRS 曲线由 Animation 播放。
/// </summary>
public class AmberTestPanel : MonoBehaviour
{
    [Serializable]
    public sealed class 动作条目
    {
        public string 显示名;
        public AnimationClip 身体动画;
        public AnimationClip 物理动画;
        public bool 循环 = true;
    }

    [Header("安柏测试面板")]
    [SerializeField] private Animation[] 安柏动画组 = new Animation[0];
    [Tooltip("可选：Animator 通道（未填则自动取安柏动画组所在物体上的）")]
    [SerializeField] private Animator 安柏动画机 = null;
    [SerializeField] public 动作条目[] 动作列表 = new 动作条目[0];

    private Vector2 scrollPos;
    private int current = -1;
    private Animator animator;
    private PlayableGraph graph;

    public int ActionCount => 动作列表 != null ? 动作列表.Length : 0;
    public int CurrentActionIndex => current;

    public string GetActionName(int index)
    {
        return index >= 0 && index < ActionCount ? 动作列表[index].显示名 : string.Empty;
    }

    private void Awake()
    {
        animator = 安柏动画机;
        if (animator == null && 安柏动画组 != null && 安柏动画组.Length > 0 && 安柏动画组[0] != null)
            animator = 安柏动画组[0].GetComponentInParent<Animator>();

        // 自建图的人形肌肉求值依赖 Animator.enabled。
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
        }
    }

    private void Start()
    {
        PlayAction(0);
    }

    private void Update()
    {
        if (ActionCount == 0) return;
        if (Input.GetKeyDown(KeyCode.UpArrow))
            PlayAction((current - 1 + ActionCount) % ActionCount);
        else if (Input.GetKeyDown(KeyCode.DownArrow))
            PlayAction((current + 1) % ActionCount);
    }

    public void PlayAction(int index)
    {
        if (index < 0 || index >= ActionCount)
        {
            Debug.LogError($"安柏动作索引越界：{index}，总数：{ActionCount}", this);
            return;
        }

        var entry = 动作列表[index];
        if (entry == null || entry.身体动画 == null || entry.物理动画 == null)
        {
            Debug.LogError($"安柏动作条目缺少身体或物理动画：索引 {index}", this);
            return;
        }
        if (animator == null)
        {
            Debug.LogError("安柏动作缺少 Animator，无法播放身体动画", this);
            return;
        }

        current = index;

        // 先销毁 Animator 原控制器图和本面板旧图，避免多个图源相互覆盖。
        var prev = animator.playableGraph;
        if (prev.IsValid()) prev.Destroy();
        if (graph.IsValid()) graph.Destroy();

        graph = PlayableGraph.Create("AmberTestPanel");
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimationClipPlayable.Create(graph, entry.身体动画);
        playable.SetApplyFootIK(false);
        playable.SetTime(0);
        output.SetSourcePlayable(playable);
        graph.Play();

        if (安柏动画组 == null) return;
        foreach (var animation in 安柏动画组)
        {
            if (animation == null) continue;
            animation.Stop();
            animation.clip = entry.物理动画;
            animation.wrapMode = entry.循环 ? WrapMode.Loop : WrapMode.Once;
            animation.Play();
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    private void OnGUI()
    {
        if (ActionCount == 0) return;
        float height = Screen.height - 32f;
        GUILayout.BeginArea(new Rect(16f, 16f, 320f, height), GUI.skin.box);
        GUILayout.Label("动画 ×" + ActionCount + "（↑↓ 切换）");
        scrollPos = GUILayout.BeginScrollView(scrollPos);
        for (int i = 0; i < ActionCount; i++)
        {
            GUI.enabled = i != current;
            if (GUILayout.Button(GetActionName(i)))
                PlayAction(i);
        }
        GUI.enabled = true;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
