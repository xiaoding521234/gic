# 安柏完整模型与官方动作重做实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `AmberTest` 中交付完整安柏模型与首批可画面验收的原生官方动作。

**Architecture:** 从 GI 源 JSON 和修复版 DBACL 解压数据重建 Tuanjie 原生 Humanoid 肌肉 clip；Animator 播放身体层，legacy Animation 播放角色物理层。测试面板用序列化显式配对取代同名猜测，新资产与旧资产隔离。

**Tech Stack:** Tuanjie 1.9.3 / Unity 2022.3、C#、Playables、Legacy Animation、Node.js、AnimeStudio/AnimHarness、Game View MP4。

---

## 文件地图

- 新建临时工具 `.codely-cli/tmp/ambor/prepare_rebuilt_inputs.cjs`：把源 JSON、harness 二进制解压结果转换为 v11 构建输入；不入 git。
- 修改临时工具 `.codely-cli/tmp/ambor/build_muscle_clips_v11.cjs`：按 `m_IndexArray` 清 Root/Motion，不使用固定列号；不入 git。
- 新建 `Assets/_Scripts/Editor/Tool/AmberAnimationSelfTest.cs`：资产和场景离线断言。
- 新建 `Assets/Art/AmberTMR/MuscleClips/Rebuilt/*.anim`：首批 8 条重建身体动画。
- 修改 `Assets/_Scripts/DevTest/AmberTestPanel.cs`：显式动作配对、播放与测试驱动 API。
- 修改 `Assets/Scenes/AmberTest.unity`：配置首批动作条目。
- 修改 `Assets/Art/AmberTMR/README.md`、`docs/17-代码架构指南.md`、`docs/14-技术陷阱与Bug修复记录.md`：记录最终真实接线与陷阱。

### Task 1：先建立会失败的资产自检，再恢复导出输入

**Files:**
- Create: `Assets/_Scripts/Editor/Tool/AmberAnimationSelfTest.cs`
- Create: `.codely-cli/tmp/ambor/prepare_rebuilt_inputs.cjs`

- [ ] **Step 1：新建离线自检**

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor
{
    public static class AmberAnimationSelfTest
    {
        private const string BodyRoot = "Assets/Art/AmberTMR/MuscleClips/Rebuilt/";
        private static readonly List<string> Failures = new List<string>();
        private static readonly string[] Required =
        {
            "Ani_Avatar_Girl_Standby",
            "Ani_Avatar_Girl_Bow_WeaponStandby",
            "Ani_Avatar_Girl_RunBS",
            "Ani_Avatar_Girl_SprintCycle+Mirror",
            "Ani_Avatar_Girl_FlyNormal",
            "Ani_Avatar_Girl_Bow_Ambor_Attack_05",
            "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short",
            "Ani_Avatar_Girl_Bow_Ambor_Show_01"
        };

        [MenuItem("Tools/TG/安柏动画资产自检")]
        public static bool RunAssets()
        {
            Failures.Clear();
            CheckAssets();
            return Report("AMBER_ANIMATION_ASSETS");
        }

        [MenuItem("Tools/TG/安柏动画全量自检")]
        public static bool RunAll()
        {
            Failures.Clear();
            CheckAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/AmberTest.unity");
            var panel = Object.FindObjectOfType<AmberTestPanel>();
            Check(panel != null, "AmberTestPanel 存在");
            if (panel != null)
            {
                var list = new SerializedObject(panel).FindProperty("动作列表");
                Check(list != null && list.isArray && list.arraySize == Required.Length,
                    $"显式动作条目={Required.Length}");
                if (list != null && list.isArray)
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var entry = list.GetArrayElementAtIndex(i);
                        var body = entry.FindPropertyRelative("身体动画")?.objectReferenceValue as AnimationClip;
                        var physics = entry.FindPropertyRelative("物理动画")?.objectReferenceValue as AnimationClip;
                        string display = entry.FindPropertyRelative("显示名")?.stringValue;
                        Check(body != null && body.humanMotion, $"身体动画有效：{display}");
                        Check(physics != null && physics.legacy, $"物理动画为 legacy：{display}");
                    }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/AmberTMR/AmberTMR.prefab");
            Check(prefab != null && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 5,
                "完整模型仍为 5 个蒙皮部件");
            return Report("AMBER_ANIMATION_ALL");
        }

        private static void CheckAssets()
        {
            foreach (var name in Required)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyRoot + name + ".anim");
                Check(clip != null, $"重建身体 clip 存在：{name}");
                if (clip != null)
                    Check(clip.humanMotion && clip.length > 0.01f,
                        $"重建身体 clip 为有效 Humanoid：{name}");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) Failures.Add(message);
        }

        private static bool Report(string marker)
        {
            if (Failures.Count == 0)
            {
                Debug.Log(marker + "_PASS");
                return true;
            }
            foreach (var failure in Failures) Debug.LogError(marker + "_ASSERT " + failure);
            Debug.LogError(marker + $"_FAIL={Failures.Count}");
            return false;
        }
    }
}
#endif
```

- [ ] **Step 2：编译并运行自检，确认红阶段**

运行 `unity_editor.refresh`，再执行菜单 `Tools/TG/安柏动画资产自检`。

预期：编译无 C# 错；自检 FAIL，首批 `Rebuilt/*.anim` 尚不存在。不得把此阶段的缺失资产失败改成跳过。

- [ ] **Step 3：用 harness 重新解压首批 8 条**

```powershell
$bin = ".codely-cli\tmp\AnimeStudio-new\AnimeStudio-net10-02994d5c30a43d0d5c5124557ec361ccbbad24c0\bin"
$exe = Join-Path $bin "AnimHarness.exe"
$rig = "D:\Game\Genshin Impact\Genshin Impact Game\YuanShen_Data\StreamingAssets\AssetBundles\blocks\00\02050112.blk"
$anim = "D:\Game\Genshin Impact\Genshin Impact Game\YuanShen_Data\StreamingAssets\AssetBundles\blocks\00\04161624.blk"
$clips = @(
  "Ani_Avatar_Girl_Standby",
  "Ani_Avatar_Girl_Bow_WeaponStandby",
  "Ani_Avatar_Girl_RunBS",
  "Ani_Avatar_Girl_SprintCycle+Mirror",
  "Ani_Avatar_Girl_FlyNormal",
  "Ani_Avatar_Girl_Bow_Ambor_Attack_05",
  "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short",
  "Ani_Avatar_Girl_Bow_Ambor_Show_01"
)
foreach ($name in $clips) {
  $out = ".codely-cli\tmp\ambor\dumps\$name"
  & $exe $bin $out "Avatar_Girl_Bow_Ambor" $name dump $rig $anim
  if ($LASTEXITCODE -ne 0) { throw "dump failed: $name" }
}
```

预期：每条出现 `DUMP_OK`，目录含 `acl_values.f32`、`acl_times.f32`、`muscle_dump.json`。

- [ ] **Step 4：写入输入准备脚本**

```javascript
const fs = require('fs');
const path = require('path');
const ROOT = 'D:/Tuanjie_editor/gic/.codely-cli/tmp/ambor';
const SURVEY = 'D:/Tuanjie_editor/gic/.codely-cli/tmp/amber-shared-survey/AnimationClip';
const CLIPS = [
  'Ani_Avatar_Girl_Standby',
  'Ani_Avatar_Girl_Bow_WeaponStandby',
  'Ani_Avatar_Girl_RunBS',
  'Ani_Avatar_Girl_SprintCycle+Mirror',
  'Ani_Avatar_Girl_FlyNormal',
  'Ani_Avatar_Girl_Bow_Ambor_Attack_05',
  'Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short',
  'Ani_Avatar_Girl_Bow_Ambor_Show_01'
];
const srcDir = path.join(ROOT, 'srcjson/AnimationClip');
const decDir = path.join(ROOT, 'decoded');
fs.mkdirSync(srcDir, { recursive: true });
fs.mkdirSync(decDir, { recursive: true });
for (const name of CLIPS) {
  const source = path.join(SURVEY, name + '.json');
  if (!fs.existsSync(source)) throw new Error('missing source JSON: ' + name);
  fs.copyFileSync(source, path.join(srcDir, name + '.json'));
  const dump = path.join(ROOT, 'dumps', name);
  const times = fs.readFileSync(path.join(dump, 'acl_times.f32'));
  const values = fs.readFileSync(path.join(dump, 'acl_values.f32'));
  const frames = times.length / 4;
  const stride = values.length / times.length;
  if (!Number.isInteger(frames) || !Number.isInteger(stride) || frames < 2 || stride < 1)
    throw new Error('bad ACL dimensions: ' + name);
  fs.writeFileSync(path.join(decDir, name + '.json'), JSON.stringify({
    stride, frames, values_b64: values.toString('base64')
  }));
  console.log(`PREPARED ${name} frames=${frames} stride=${stride}`);
}
```

运行：`node .codely-cli\tmp\ambor\prepare_rebuilt_inputs.cjs`
预期：输出 8 行 `PREPARED`，无缺文件或维度错误。

### Task 2：生成原生 Humanoid 动画并让资产自检转绿

**Files:**
- Modify: `.codely-cli/tmp/ambor/build_muscle_clips_v11.cjs`
- Create: `Assets/Art/AmberTMR/MuscleClips/Rebuilt/*.anim`

- [ ] **Step 1：按真实通道映射清 Root/Motion**

在 `build_muscle_clips_v11.cjs` 的 `const ia = mc.m_IndexArray || [];` 后、计算 `m_ValueArrayDelta` 前插入：

```javascript
for (let ch = 0; ch < Math.min(14, ia.length); ch++) {
  const col = ia[ch];
  if (col < 0 || col >= stride) continue;
  const identity = (ch === 3 || ch === 6 || ch === 10 || ch === 13) ? 1 : 0;
  for (let f = 0; f < frames; f++) values[f * stride + col] = identity;
}
```

把六个 `m_StartX/m_StopX/...` 的输出改为保留 scale、清 translation 和 rotation：

```javascript
for (const k of ['m_StartX', 'm_StopX', 'm_LeftFootStartX', 'm_RightFootStartX', 'm_MotionStartX', 'm_MotionStopX']) {
  L.push('    ' + k + ':');
  L.push('      t: {x: 0, y: 0, z: 0}');
  L.push('      q: {x: 0, y: 0, z: 0, w: 1}');
  L.push('      s: ' + vec3(mc[k].s));
}
```

把 `m_AverageSpeed` 改为 `    m_AverageSpeed: {x: 0, y: 0, z: 0}`。禁止再运行固定列号 410–423 的 `fix_v11_rootzero2.cjs`。

在文件顶部增加循环白名单，并把 `m_MuscleClip.m_LoopTime` 与 `m_AnimationClipSettings.m_LoopTime` 两处的 `boolF(mc.m_LoopTime)` 改为 `LOOP_IN_PLACE.has(name) ? 1 : boolF(mc.m_LoopTime)`：

```javascript
const LOOP_IN_PLACE = new Set([
  'Ani_Avatar_Girl_Standby',
  'Ani_Avatar_Girl_Bow_WeaponStandby',
  'Ani_Avatar_Girl_RunBS',
  'Ani_Avatar_Girl_SprintCycle+Mirror',
  'Ani_Avatar_Girl_FlyNormal'
]);
```

- [ ] **Step 2：重建并复制到隔离目录**

```powershell
node ".codely-cli\tmp\ambor\build_muscle_clips_v11.cjs"
New-Item -ItemType Directory -Force "Assets\Art\AmberTMR\MuscleClips\Rebuilt" | Out-Null
Copy-Item ".codely-cli\tmp\ambor\rebuilt11\*.anim" "Assets\Art\AmberTMR\MuscleClips\Rebuilt\" -Force
```

预期：构建日志 `built 8 clips (v11)`；`Rebuilt` 只有首批 8 条，不覆盖旧 `MuscleClips`。

- [ ] **Step 3：逐条核对首末帧和 ValueArrayDelta**

新建 `.codely-cli/tmp/ambor/verify_rebuilt_v11.cjs`：

```javascript
const fs = require('fs');
const path = require('path');
const dir = 'D:/Tuanjie_editor/gic/.codely-cli/tmp/ambor/rebuilt11';
let checked = 0;
for (const file of fs.readdirSync(dir).filter(f => f.endsWith('.anim'))) {
  const text = fs.readFileSync(path.join(dir, file), 'utf8');
  const num = key => Number(text.match(new RegExp(key + ': ([^\\n]+)'))[1]);
  const frames = num('m_FrameCount');
  const stride = num('m_CurveCount');
  const ia = JSON.parse(text.match(/m_IndexArray: (\[[^\n]+\])/)[1]);
  const sampleText = text.match(/m_SampleArray: \[([^\n]+)\]/)[1];
  const values = sampleText.split(',').map(Number);
  if (values.length !== frames * stride || values.some(v => !Number.isFinite(v)))
    throw new Error('bad sample array: ' + file);
  const deltas = [...text.matchAll(/- m_Start: ([^\n]+)\n\s+m_Stop: ([^\n]+)/g)]
    .map(m => ({ start: Number(m[1]), stop: Number(m[2]) }));
  for (let channel = 0; channel < ia.length; channel++) {
    const col = ia[channel];
    if (col < 0) continue;
    const first = values[col];
    const last = values[(frames - 1) * stride + col];
    const delta = deltas[channel];
    if (!delta || Math.abs(delta.start - first) > 1e-5 || Math.abs(delta.stop - last) > 1e-5)
      throw new Error(`VAD mismatch: ${file} channel=${channel}`);
  }
  checked++;
}
console.log(`VAD_PASS clips=${checked}`);
```

运行：`node .codely-cli\tmp\ambor\verify_rebuilt_v11.cjs`
预期：`VAD_PASS clips=8`，且样本无 NaN/Infinity。

- [ ] **Step 4：导入并运行资产自检**

运行 `unity_editor.refresh`，再执行 `Tools/TG/安柏动画资产自检`。

预期：`AMBER_ANIMATION_ASSETS_PASS`；8 条均 `humanMotion=true`、时长非零、无导入错误。

- [ ] **Step 5：提交本阶段资产与自检**

提交信息写入 `.codely-cli/tmp/amber-task2.txt`：

```text
feat: 重建安柏首批原生身体动画

- 从官方肌肉数据生成八条 Humanoid 动作并与旧资产隔离
- 增加编辑器资产自检，防止物理层冒充完整身体动画
```

仅暂存 `AmberAnimationSelfTest.cs`、对应 `.meta`、`MuscleClips/Rebuilt/` 后执行 `git commit -F .codely-cli/tmp/amber-task2.txt`。

### Task 3：重做显式双通道面板并配置场景

**Files:**
- Modify: `Assets/_Scripts/DevTest/AmberTestPanel.cs`
- Modify: `Assets/Scenes/AmberTest.unity`

- [ ] **Step 1：用显式动作条目替换同名推断**

将 `AmberTestPanel.cs` 改为以下完整实现：

```csharp
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

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
    [SerializeField] private Animator 安柏动画机;
    [SerializeField] public 动作条目[] 动作列表 = new 动作条目[0];

    private Animator animator;
    private PlayableGraph graph;
    private Vector2 scrollPos;
    private int current = -1;

    public int ActionCount => 动作列表?.Length ?? 0;
    public int CurrentActionIndex => current;
    public string GetActionName(int index) =>
        index >= 0 && index < ActionCount ? 动作列表[index].显示名 : string.Empty;

    private void Awake()
    {
        animator = 安柏动画机;
        if (animator == null && 安柏动画组.Length > 0 && 安柏动画组[0] != null)
            animator = 安柏动画组[0].GetComponentInParent<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
        }
    }

    private void Start() => PlayAction(0);

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow)) PlayAction(current - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) PlayAction(current + 1);
    }

    public void PlayAction(int index)
    {
        if (index < 0 || index >= ActionCount) return;
        var entry = 动作列表[index];
        if (entry == null || entry.身体动画 == null || entry.物理动画 == null)
        {
            Debug.LogError($"安柏动作条目缺引用：index={index}");
            return;
        }
        current = index;
        if (animator != null)
        {
            var previous = animator.playableGraph;
            if (previous.IsValid()) previous.Destroy();
            if (graph.IsValid()) graph.Destroy();
            graph = PlayableGraph.Create("AmberTestPanel." + entry.显示名);
            var output = AnimationPlayableOutput.Create(graph, "Body", animator);
            var playable = AnimationClipPlayable.Create(graph, entry.身体动画);
            playable.SetApplyFootIK(false);
            playable.SetTime(0);
            output.SetSourcePlayable(playable);
            graph.Play();
        }
        foreach (var player in 安柏动画组)
        {
            if (player == null) continue;
            player.Stop();
            player.clip = entry.物理动画;
            player.wrapMode = entry.循环 ? WrapMode.Loop : WrapMode.Once;
            player.Play();
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    private void OnGUI()
    {
        if (ActionCount == 0) return;
        GUILayout.BeginArea(new Rect(16f, 16f, 320f, Screen.height - 32f), GUI.skin.box);
        GUILayout.Label($"安柏官方动作 ×{ActionCount}（↑↓ 切换）");
        scrollPos = GUILayout.BeginScrollView(scrollPos);
        for (int i = 0; i < ActionCount; i++)
        {
            GUI.enabled = i != current;
            if (GUILayout.Button(动作列表[i].显示名)) PlayAction(i);
        }
        GUI.enabled = true;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
```

- [ ] **Step 2：编译后配置首批 8 个场景条目**

先运行 `unity_editor.refresh`，再用 `exec_editor_script` 打开 `AmberTest.unity` 并执行：

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AmberTest.unity");
var panel = UnityEngine.Object.FindObjectOfType<AmberTestPanel>();
var entries = new[]
{
    ("常态待机", "Ani_Avatar_Girl_Standby", "Ani_Avatar_Girl_Bow_Ambor_Standby", true),
    ("弓箭待机", "Ani_Avatar_Girl_Bow_WeaponStandby", "Ani_Avatar_Girl_Bow_Ambor_WeaponStandby", true),
    ("跑步", "Ani_Avatar_Girl_RunBS", "Ani_Avatar_Girl_Bow_Ambor_RunBS", true),
    ("冲刺", "Ani_Avatar_Girl_SprintCycle+Mirror", "Ani_Avatar_Girl_Bow_Ambor_RunBS", true),
    ("飞行", "Ani_Avatar_Girl_FlyNormal", "Ani_Avatar_Girl_Bow_Ambor_FlyNormal", true),
    ("弓箭攻击", "Ani_Avatar_Girl_Bow_Ambor_Attack_05", "Ani_Avatar_Girl_Bow_Ambor_Attack_05", false),
    ("元素战技", "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short", "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short", false),
    ("展示", "Ani_Avatar_Girl_Bow_Ambor_Show_01", "Ani_Avatar_Girl_Bow_Ambor_Show_01", false)
};
var so = new UnityEditor.SerializedObject(panel);
var list = so.FindProperty("动作列表");
list.arraySize = entries.Length;
for (int i = 0; i < entries.Length; i++)
{
    var item = list.GetArrayElementAtIndex(i);
    item.FindPropertyRelative("显示名").stringValue = entries[i].Item1;
    item.FindPropertyRelative("身体动画").objectReferenceValue =
        UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>(
            "Assets/Art/AmberTMR/MuscleClips/Rebuilt/" + entries[i].Item2 + ".anim");
    item.FindPropertyRelative("物理动画").objectReferenceValue =
        UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>(
            "Assets/Art/AmberTMR/Clips/" + entries[i].Item3 + ".anim");
    item.FindPropertyRelative("循环").boolValue = entries[i].Item4;
}
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
```

- [ ] **Step 3：运行全量离线自检**

执行 `Tools/TG/安柏动画全量自检`。

预期：`AMBER_ANIMATION_ALL_PASS`，8 条引用完整、身体层 Humanoid、物理层 legacy、模型仍为 5 个蒙皮部件。

- [ ] **Step 4：提交面板与场景**

提交信息写入 `.codely-cli/tmp/amber-task3.txt`：

```text
feat: 安柏测试面板改为显式双通道动作配对

- 支持共享身体层与角色物理层跨命名组合
- 配置待机、跑冲刺、飞行、攻击、战技和展示首批条目
```

仅暂存 `AmberTestPanel.cs`、`AmberTest.unity` 后执行 `git commit -F .codely-cli/tmp/amber-task3.txt`。

### Task 4：逐条录制并自检实际动态画面

**Files:**
- Verify: `Assets/Scenes/AmberTest.unity`
- Create: `screenshots/amber-*.mp4`

- [ ] **Step 1：建立一致的预览场景**

用 `exec_editor_script` 打开 `Assets/Scenes/AmberTest.unity`。运行 `unity_editor.refresh`，确认无新增错误；不得先改相机、灯光或模型来掩盖动作问题。

- [ ] **Step 2：逐动作触发并录制**

每次用 `exec_runtime_script`，`record_game_view` 设置 `durationSeconds=5`（展示动作 8 秒）、`fps=15`、`encodeFps=2`、`scale=0.75`。脚本按索引触发：

```csharp
using System.Threading.Tasks;
using UnityEngine;

async Task<string> RunAsync()
{
    var panel = Object.FindObjectOfType<AmberTestPanel>();
    if (panel == null) return "FAIL: AmberTestPanel missing";
    panel.PlayAction(ACTION_INDEX);
    await Task.Delay(4500);
    return $"PLAYED {panel.GetActionName(ACTION_INDEX)}";
}
return RunAsync();
```

将 `ACTION_INDEX` 依次替换为 0–7。每个 MP4 必须用 `analyze_video` 检查：身体/四肢是否实际运动、是否拉伸、比例是否稳定、手脚姿态、头发裙摆、循环接缝、明显穿模。展示与攻击同时检查动作是否完整而非只有第一帧。

- [ ] **Step 3：只返修未通过项**

若某条失败，先确定是身体层、物理层还是配对问题。修改后重新运行 `unity_editor.refresh`、全量离线自检，并只重录失败动作及相邻的待机对照；不得把失败项从列表删除后宣称完成。

- [ ] **Step 4：提交通过后的画面验证修正**

若返修改动资产或代码，提交信息写入 `.codely-cli/tmp/amber-task4.txt`：

```text
fix: 修正安柏首批动作的画面验收问题

- 按实机视频修正身体层、物理层或配对缺陷
- 保持完整模型、材质与既有双通道接线不回归
```

只暂存实际改动文件后执行 `git commit -F .codely-cli/tmp/amber-task4.txt`；无返修则不创建空提交。

### Task 5：记录真实终态与交付

**Files:**
- Modify: `Assets/Art/AmberTMR/README.md`
- Modify: `docs/17-代码架构指南.md`
- Modify: `docs/14-技术陷阱与Bug修复记录.md`

- [ ] **Step 1：更新资产说明**

在 `Assets/Art/AmberTMR/README.md` 增加 `MuscleClips/Rebuilt/` 的来源、8 条清单、显式配对关系和菜单自检入口；保留旧资产历史，不宣称 154 条全部完成。

- [ ] **Step 2：更新架构与陷阱记录**

在 `docs/17` 的 AmberTMR 行补“Rebuilt 原生肌肉首批+显式双通道面板”；在 `docs/14` 新增一节，记录：共享身体层与角色物理层可跨命名、固定 Root 列号不可跨 clip 复用、画面验证才暴露旧问题。

- [ ] **Step 3：最终核验**

再次执行 `unity_editor.refresh`、`Tools/TG/安柏动画全量自检`；检查 `git status`，排除 `zh-cn SDF.asset`、`CODELY.md`、Packages 两文件、`docs/00-概述.md`、`docs/active/48` 和其他非本批改动。

- [ ] **Step 4：提交文档并交付目检清单**

提交信息写入 `.codely-cli/tmp/amber-task5.txt`：

```text
docs: 记录安柏原生动作重接与画面验收结果

- 登记首批重建资产、显式配对和自检入口
- 沉淀跨命名配对与 Root 通道映射陷阱
```

仅暂存上述三个文档后执行 `git commit -F .codely-cli/tmp/amber-task5.txt`。交付时附每条动作自检结果、对应 MP4 和用户最终目检清单；不自动 push。
