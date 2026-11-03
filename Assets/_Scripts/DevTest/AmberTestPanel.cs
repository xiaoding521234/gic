using System;
using UnityEngine;

/// <summary>
/// 安柏测试场景（Assets/Scenes/AmberTest.unity）的 Generic 分层双通道动画切换面板。
/// 身体与物理骨 TRS 曲线均由 Animation 播放。
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
    [SerializeField] public 动作条目[] 动作列表 = new 动作条目[0];

    private Vector2 scrollPos;
    private int current = -1;

    public int ActionCount => 动作列表 != null ? 动作列表.Length : 0;
    public int CurrentActionIndex => current;

    public string GetActionName(int index)
    {
        if (index < 0 || index >= ActionCount) return string.Empty;
        var entry = 动作列表[index];
        return entry != null && !string.IsNullOrEmpty(entry.显示名)
            ? entry.显示名
            : $"<空动作 {index}>";
    }

    private void Awake()
    {
        if (安柏动画组 == null) return;
        foreach (var animation in 安柏动画组)
        {
            if (animation != null)
                animation.enabled = true;
        }
    }

    private void Start()
    {
        if (ActionCount > 0) PlayAction(0);
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

        Animation player = null;
        if (安柏动画组 != null)
        {
            foreach (var animation in 安柏动画组)
            {
                if (animation == null) continue;
                player = animation;
                break;
            }
        }
        if (player == null)
        {
            Debug.LogError("安柏动作缺少 Animation，无法播放分层动画", this);
            return;
        }

        current = index;

        player.Stop();
        player.RemoveClip("Body");
        player.RemoveClip("Phys");
        player.AddClip(entry.身体动画, "Body");
        player.AddClip(entry.物理动画, "Phys");
        player["Body"].layer = 0;
        player["Phys"].layer = 1;
        player.wrapMode = entry.循环 ? WrapMode.Loop : WrapMode.Once;
        player.clip = entry.身体动画;

        bool bodyPlayed = player.Play("Body");
        bool physPlayed = player.Play("Phys");
        if (!bodyPlayed || !physPlayed)
            Debug.LogError($"安柏动作播放失败：{GetActionName(index)}（Body={bodyPlayed}，Phys={physPlayed}）", player);
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
