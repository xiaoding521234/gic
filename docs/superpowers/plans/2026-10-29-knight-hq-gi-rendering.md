# Knight HQ GI Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reproduce the original Genshin Knight HQ building materials in the URP battle scene and orient its entrance toward the player.

**Architecture:** Keep the verified model and material slots. Add one GI-building URP shader, per-material official inputs, a view-only day/night parameter bridge, and preserve prefab-authored orientation in `BuildingModelView`. Editor-only assertions test the real assets and model creation path; no Play Mode testing is initiated by the agent.

**Tech Stack:** Tuanjie 1.9.3, URP 14.2, HLSL, C#, AnimeStudio CLI, Unity editor readback and asset preview.

---

### Task 1: Recover and import the official material inputs

**Files:**
- Create: `Assets/Art/KnightHQ/Textures/Area_MdBuild_Column01_{Diffuse,Normal,SMBE}.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_MdBuild_{KnightHQ02_Assembly01,Wall08,Wall09,House_Roof04}_SMBE.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_Mdbuild_{Wall06,Stage01}_SMBE.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_MdBuild_Window_A_{Normal,SMBE}.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_MdBuild_{House_Roof04,Wall09}_Height.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_Mdbuild_Wall06_Height.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_Mdcity_Ground15_{SMBE,Height}.png`
- Create: `Assets/Art/KnightHQ/Textures/Area_MdGroundMix_Diffuse.png`

- [ ] **Step 1: Copy the PathID-verified official exports**

Sources are `.codely-cli/tmp/knighthq/render-assets/{10831150,11396489,13058112,15886019}/Texture2D/`. Preserve exact official filenames. Do not use `Roof05_Normal`: the original roof material references `Roof04_Normal`.

- [ ] **Step 2: Configure texture imports through `exec_editor_script`**

Diffuse/Height/SMBE: `Default`, Repeat, max 2048, no sprite conversion. Normal: `NormalMap`, Repeat. Call `SaveAndReimport()` for each and read back dimensions/type. Expected: all 17 official inputs imported, no missing reference.

### Task 2: Add failing offline assertions

**Files:**
- Create: `Assets/_Scripts/Editor/Tool/KnightHQRenderingSelfTest.cs`
- Modify: `Assets/Art/KnightHQ/KnightHQ.prefab`

- [ ] **Step 1: Set the authored default yaw and add the real-asset test**

Set the prefab root yaw to +90 degrees; the entrance is local +X and +90° maps it to -Z. Add `RunAll()` with `Check(bool condition, string message)` collecting failures and logging `KNIGHT_HQ_RENDERING_PASS` only when all pass:

```csharp
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
Check(prefab != null, "prefab exists");
var host = new GameObject("KnightHQTest");
var view = BuildingModelView.Create(host.transform, prefab, 1f);
var model = host.transform.GetChild(0);
Check(Quaternion.Angle(model.localRotation, prefab.transform.localRotation) < 0.01f,
    "Create preserves prefab-authored orientation");
Check(Vector3.Dot(model.rotation * Vector3.right, Vector3.back) > 0.999f,
    "entrance +X faces player -Z");
UnityEngine.Object.DestroyImmediate(host);
```

Assert `NightTargetForMinutes(360)==1`, `(480)==0`, `(1199)==0`, `(1200)==1`. Assert all eight materials use `GIC/Battle/GIBuilding`; Assembly has `Column01_Diffuse`, `Assembly01_SMBE`, `Column01_SMBE`; roof has `Roof04_Height`; Window has `Window_A_Normal`, `Window_A_SMBE`, `_EmissionStrength=4`. Assert `SetTint(red)` reaches cloned materials and `SetDissolveAlpha(0.5f)` gives `_Surface=1`, `_ZWrite=0`, `_BaseColor.a=0.5`.

- [ ] **Step 2: Run the test before implementation**

Expected: FAIL on current `instance.transform.rotation = Quaternion.identity`, Simple Lit materials, missing inputs and absent day/night helper. Do not alter assertions to match current behavior.

### Task 3: Implement the GI-building URP shader

**Files:**
- Create: `Assets/Shaders/GIBuilding.shader`
- Modify: `Assets/_Scripts/Battle/View/BuildingModelView.cs`

- [ ] **Step 1: Implement the material property contract**

Properties: `_BaseMap`, `_BaseColor`, `_BumpMap`, `_DetailMask`, `_SecondaryTex`, `_SecondaryBumpMap`, `_SecondaryDetailMask`, `_POMHeightMap`, `_POMHeight`, `_EnableSecondTex`, `_InvertBlend`, `_DiffuseBlendIntensity`, `_NormalBlendIntensity`, `_EnablePOM`, `_EmissionColor`, `_EmissionStrength`, `_NightEmission`, `_RGColor`, `_RGStrength`, `_RGPower`, `_ReceiveShadow`, `_SpecColor`, `_Smoothness`, `_Cull`, `_Surface`, `_SrcBlend`, `_DstBlend`, `_ZWrite`, `_Cutoff`.

Forward path uses URP `SurfaceData`/`InputData` and `UniversalFragmentBlinnPhong`; keep opaque depth. `Blend [_SrcBlend] [_DstBlend]`, `ZWrite [_ZWrite]`, `Cull [_Cull]`. Use actual shadow caster/depth passes and the same per-material tint semantics as the previous Simple Lit materials.

- [ ] **Step 2: Implement official-input blending with explicit fallbacks**

Main texture/normal is the base. Only `_EnableSecondTex=1` with non-null secondary resources enables secondary sampling. `_InvertBlend=1` reverses the secondary blend mask. Apply `_DiffuseBlendIntensity`/`_NormalBlendIntensity`. Missing resources use main texture/normal, not white or magenta. `_EnablePOM=1` samples `_POMHeightMap` in tangent view space with the per-material `_POMHeight`; missing height disables parallax. Do not enable secondary or POM on materials that did not enable them in the original data.

Use SMBE data according to the verified original channel mapping. `_UseMainMaskAlphaAsEmission` is represented by the emission-mask helper, not a blanket full-surface emission. Window emission uses original warm RGB × `_EmissionStrength` × `_NightEmission` × emission mask. `_RGColor/_RGStrength/_RGPower` only apply on Window. Other materials do not get character outline/ramp effects.

- [ ] **Step 3: Preserve state transitions**

`SetTint` multiplies final albedo using `_BaseColor`; emission is tinted consistently so freeze/corpse/hit feedback is not lost. `SetDissolveAlpha` switches to transparent blend, disables ZWrite and applies `_BaseColor.a`; visible geometry does not turn into a rectangular opaque block.

### Task 4: Wire each original material and battle clock

**Files:**
- Modify: `Assets/Art/KnightHQ/KnightHQ_{Assembly,Window,Roof05,Wall08,Wall09,CityWall09,Ground15,Stage01}.mat`
- Modify: `Assets/_Scripts/Battle/View/BuildingModelView.cs`
- Modify: `Assets/_Scripts/Battle/View/BattlePlayer.cs`
- Modify: `Assets/_Scripts/Battle/View/UnitView.cs` only if orientation propagation needs its call-site adjustment

- [ ] **Step 1: Assign per-material inputs through an idempotent editor batch**

For every material, preserve existing main diffuse and correct normal, then switch to `GIC/Battle/GIBuilding`. Assembly alone receives Column01 diffuse/normal/SMBE as secondary inputs; Ground15 receives `Area_MdGroundMix_Diffuse` and original secondary mask rules. Roof05/Wall09/CityWall09/Ground15 receive their own Height inputs. Window receives `Window_A_Normal` and SMBE, `_EmissionColor=(1,0.71353996,0.3098039,1)`, `_EmissionStrength=4`, `_NightEmission=1`, and original RG parameters. Do not enable POM or secondary textures globally.

- [ ] **Step 2: Add pure clock conversion and view updates**

```csharp
public static float NightTargetForMinutes(int minutes)
{
    int hour = (minutes / 60) % 24;
    return hour >= TimeUtility.DayStartHour && hour < TimeUtility.DayEndHour ? 0f : 1f;
}
```

`BattlePlayer` computes minutes from `_flow.BattleTimeMinutes`; LAN fallback uses `6*60 + (LatestSnapshot.turnNumber-1)*20`. Smooth the night factor toward target over 0.5 real seconds and call `BuildingModelView.SetNightEmission(factor)` on existing views. Initial value is 1 (06:00 window lights on). The clock affects only the window emission, not scene lights, AI, protocol or replay.

- [ ] **Step 3: Preserve authored prefab rotation**

Replace the reset-to-identity with the prefab's authored local rotation before parenting/scaling. The actual HQ prefab remains at +90°. Other paper-doll units and all deployment/selection logic remain unchanged.

### Task 5: Verify and visually sign off

**Files:**
- Modify: `ProjectSettings/GraphicsSettings.asset` if the new shader needs build retention
- Modify: `docs/17-代码架构指南.md` material row for KnightHQ

- [ ] **Step 1: Run the offline tests to green**

Expected: all assertions from Task 2 pass. Refresh once, then run `unity_shader.compile`; expected: zero new C# and shader errors. Confirm the new shader is retained by material reference and register Always Included Shaders only if necessary, updating `m_LengthOfAlwaysIncludedShadersInInspector` alongside the list.

- [ ] **Step 2: Read back final state**

Expected: eight correct shader/material/texture sets, second textures only where enabled, height only where enabled, Window warm emission and night factor, opaque normal state and transparent dissolve state. `KnightHQ.prefab` root yaw=+90°, created model entrance normal=-Z, unchanged ground alignment.

- [ ] **Step 3: Static preview and user acceptance**

Capture asset close-ups at the battle angle and side/top views. Compare entrance readability, roof detail, secondary decoration and window emission. Final battle appearance, day/night transition and freeze/dissolve visuals are handed to the user for inspection; no Play Mode test is initiated by the agent.

- [ ] **Step 4: Record and hand over**

Update the architecture row and decision record with implemented values and remaining optional Window reflection limitation. Do not include the unrelated TMP dynamic atlas or CODELY memory edits in a code/assets commit unless the user explicitly requests wrap-up.
