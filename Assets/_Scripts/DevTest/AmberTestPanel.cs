using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 安柏测试场景（Assets/Scenes/AmberTest.unity）的动画切换面板：
/// 左侧列表点击或 ↑↓ 键切换动画，多目标同步播放（本体/皮肤对比用）。
/// </summary>
public class AmberTestPanel : MonoBehaviour
{
    [Header("安柏测试面板")]
    [SerializeField] private Animation[] 安柏动画组 = new Animation[0];

    private readonly List<string> clipNames = new List<string>();
    private Vector2 scrollPos;
    private int current = -1;

    private void Start()
    {
        foreach (var a in 安柏动画组)
        {
            if (a == null) continue;
            foreach (AnimationState s in a)
                if (!clipNames.Contains(s.name))
                    clipNames.Add(s.name);
        }
        clipNames.Sort(System.StringComparer.Ordinal);
    }

    private void Update()
    {
        if (clipNames.Count == 0 || 安柏动画组.Length == 0) return;
        if (Input.GetKeyDown(KeyCode.UpArrow)) Play(current - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) Play(current + 1);
    }

    private void Play(int index)
    {
        if (index < 0 || index >= clipNames.Count) return;
        current = index;
        string name = clipNames[index];
        foreach (var a in 安柏动画组)
        {
            if (a == null) continue;
            a.Stop();
            a.Play(name);
        }
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
