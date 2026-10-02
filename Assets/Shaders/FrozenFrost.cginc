// 冻结霜化共享数学（2026-10-02 拍板「像真的结冰」shader 实现——原神级技术路线）
// 消费方：FrozenSprite.shader（静态/序列帧立牌）+ ChromaKeyVideo.shader（视频立牌）。
// 五层视觉（初版信源=CSDN《Unity Shader 冰冻效果实现：原理、代码与优化》；
// 2026-10-02 二版对齐原神实机截图像素测量：冻结=强去饱和(饱和度中位~0.20)+整体提亮(明度中位~0.76)
// +低饱和灰蓝白霜化(机体均值 RGB≈(145,164,175))+底部冰雾带(淡蓝 (180,211,242) 色相~210°)）：
//   ①霜色重映射——原色强去饱和+提亮（奶白霜感），再与「暗部灰蓝→亮部霜白」色板混合；
//   ①b白霜斑——中频噪声成片结白（雾凇质感，霜层不均匀）；
//   ②边缘霜光（2D 菲涅尔等价）——冰折射率高、边缘发白发亮，且「边缘实、中间透」；
//   ②b轮廓外延冰缘——冰壳超出身体轮廓的低透边；②c冰雾——脚部蓝白雾气噪声漂移；
//   ③晶体闪烁——高频 hash 晶胞内少数锐脉冲白点（冰面反光）。
// 冻结遮罩（Freeze Mask）：世界系从脚往头梯度 + 值噪声扰动边界（信源同款 (g+amount-1)/softness+noise*amp，
// 边界外扩 15% 保证 amount=1 时全身覆盖）+ 缓慢向上流动（冰霜蔓延感）。
// 2D 立牌显式舍弃：折射（需抓屏+法线，sprite 无意义）与顶点膨胀（无体积）。
// 防过曝纪律：一律 lerp 混合，边缘/闪烁控制幅度，勿 += 叠爆（信源踩坑总结）。

#ifndef GIC_FROZEN_FROST_INCLUDED
#define GIC_FROZEN_FROST_INCLUDED

// —— uniforms（Properties 由各消费 shader 声明同名属性；值由 UnitView 逐帧驱动/材质默认承载）——
float _FrozenAmount;          // 0~1 结冰进度（冻结遮罩唯一驱动，UnitView 协程推进）
float _FreezeFootY;            // 立牌脚部世界 Y（梯度下锚——世界系免除 atlas UV/图集子矩形歧义）
float _FreezeTopY;            // 立牌头顶世界 Y（梯度上锚）
half4 _FrostDeepColor;        // 暗部霜色（低饱和灰蓝——原神实测机体暗部 (0.39,0.46,0.49) 量级）
half4 _FrostBrightColor;      // 霜白（亮部映射/边缘霜光/闪烁色）
half _FrostDesat;              // 去饱和强度（原神冻结核心特征=低饱和奶白霜）
half _FrostLift;               // 霜化提亮（明度整体上抬）
half _FrostPatchStrength;      // 白霜斑强度（雾凇成片结白）
half4 _MistColor;             // 冰雾色（实测淡蓝 (0.71,0.83,0.95)）
half _MistAlpha;               // 冰雾浓度
half _MistHeight;              // 冰雾高度（占身高比例，从脚部起算）
half _FrostAlpha;              // 冰体不透明度（冻结区内部收向半透——透出底面=冰的通透感）
half _FrostFringeAlpha;        // 轮廓外延冰缘不透明度（冰壳超出身体轮廓的低透边）

float FrostHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// 值噪声（平滑插值）：冻结边界扰动——笔直边界读作「刷渐变」，噪声打破才像冰霜蔓延
float FrostNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    fp = fp * fp * (3.0 - 2.0 * fp);
    float a = FrostHash(ip);
    float b = FrostHash(ip + float2(1.0, 0.0));
    float c = FrostHash(ip + float2(0.0, 1.0));
    float d = FrostHash(ip + float2(1.0, 1.0));
    return lerp(lerp(a, b, fp.x), lerp(c, d, fp.x), fp.y);
}

// 冻结遮罩：0=原样 1=完全冻。世界 Y 归一化到 [0.115, 0.885]（外扩 15%，amount=1 时脚部也满）
float FrostMask(float worldY, float2 uv)
{
    float range = max(0.0001, _FreezeTopY - _FreezeFootY);
    float g = saturate((worldY - _FreezeFootY + range * 0.15) / (range * 1.3));
    float n = FrostNoise(uv * 9.0 + float2(0.0, _Time.y * 0.045));
    return saturate((g + _FrozenAmount - 1.0) / 0.12 + n * 0.28 - 0.05);
}

// 边缘霜光（2D 菲涅尔等价）：alpha 边缘带（抗锯齿轮廓）→ 边缘亮白；内部实心区恒 0
float FrostRim(half alpha)
{
    return 1.0 - smoothstep(0.35, 0.95, alpha);
}

// 晶体闪烁：18 晶胞/hash 网格（2026-10-02 用户拍板「太小了，加大3倍」56→18=晶胞 3.1 倍→晶点同步 3 倍），
// ~7% 晶胞以 sin^24 锐脉冲各自相位闪烁
float FrostSparkle(float2 uv)
{
    float2 sp = uv * 18.0;
    float2 cell = floor(sp);
    float rn = FrostHash(cell + 17.0);
    float tw = rn * 6.2831 + _Time.y * 2.4;
    float glint = pow(max(0.0, sin(tw)), 24.0);
    if (glint <= 0.001) return 0.0; // 非脉冲期零成本
    // 晶点=格内随机圆心的径向衰减小圆点——首版整格矩形亮块（floor 网格无内衰减）不像冰晶反光
    //（2026-10-02 用户报障「白色晶点是矩形」返修）；随机心收在 0.3~0.7 带+半径约 1/4 格=不跨格截断
    float2 center = float2(FrostHash(cell + 3.7), FrostHash(cell + 9.1)) * 0.4 + 0.3;
    float dot = saturate(1.0 - length(frac(sp) - center) * 4.0);
    return glint * step(0.93, frac(rn * 7.13)) * dot;
}

// 霜化主出口（inout col 已含 tint）：baseAlpha=去 tint 前的形状 alpha（边缘判定基准）；
// neighborAlpha=±4 texel 四邻域的形状 alpha 最大值（轮廓外延冰缘用）——sprite 路径=tex 采样 alpha、
// 视频路径=四邻域各自抠色求形状（形状定义两条路径不同，由消费方算好传入）
void FrostApply(inout half4 col, float worldY, float2 uv, half baseAlpha, half neighborAlpha)
{
    float mask = FrostMask(worldY, uv);
    if (mask <= 0.003) return; // 非冻结像素零后续成本

    // ① 霜色重映射（2026-10-02 原神实机测量定版）：先对原色强去饱和+提亮（奶白霜感=
    // 原神冻结核心特征），再与「暗部灰蓝→亮部霜白」色板五五混合——纯色板整幅替换=信源
    // 「蓝色塑料袋」坑，纯原色提亮又丢冰蓝倾向，五五混合兼保角色体积感与霜化色相；
    // 色板映射的明度加 gamma 上抬——原神冻结=霜层覆盖表面整体发白，线性映射下深色服装
    //（凯亚深蓝衣 lum~0.15）仍读深灰蓝，gamma 后暗部也结白霜
    half lum = dot(col.rgb, half3(0.299, 0.587, 0.114));
    half3 milky = lerp(col.rgb, half3(lum, lum, lum), _FrostDesat);
    milky = milky * (1.0 + _FrostLift) + _FrostLift * 0.25;
    half lumG = pow(saturate(lum * 1.5), 0.65);
    half3 frosted = lerp(_FrostDeepColor.rgb, _FrostBrightColor.rgb, lumG);
    col.rgb = lerp(col.rgb, lerp(milky, frosted, 0.5), mask * 0.85);

    // ①b 白霜斑：中频噪声成片结白（雾凇质感——实机霜层不均匀、局部堆积更白），缓慢流动
    float pn = FrostNoise(uv * 22.0 + float2(_Time.y * 0.02, -_Time.y * 0.03));
    half patch = smoothstep(0.55, 0.8, pn) * mask;
    col.rgb = lerp(col.rgb, _FrostBrightColor.rgb, patch * _FrostPatchStrength);

    // ② 冰体半透明（2026-10-02 报障「应半透明、当前全不透明」返修）：冻结区内部按原 alpha 比例
    // 收向 _FrostAlpha——底面透出=冰的通透；边缘带反向拉实（信源「边缘实中间透」口径）
    half rim = FrostRim(baseAlpha);
    col.rgb = lerp(col.rgb, _FrostBrightColor.rgb, saturate(rim * 1.6) * mask);
    half interior = saturate(1.0 - rim);
    col.a = lerp(col.a, _FrostAlpha * col.a, mask * interior);
    col.a = max(col.a, rim * 0.95 * mask);

    // ②b 轮廓外延冰缘：轮廓外 ~4 texel 带由邻域形状 alpha 生成低透冰缘（冰壳超出身体轮廓，
    // 「延伸出的部分」）；噪声调制防机械环
    half fringe = saturate(neighborAlpha - baseAlpha);
    fringe *= 0.55 + 0.45 * FrostNoise(uv * 7.0);
    col.a = max(col.a, fringe * _FrostFringeAlpha * mask);
    col.rgb = lerp(col.rgb, _FrostBrightColor.rgb, fringe * mask);

    // ②c 冰雾（实机=冻结体底部缠绕的蓝白色雾气，色相~210°）：脚部低带、高度二次衰减、
    // 噪声漂移流动；乘 mask 随蔓延进度一起出现（脚部先冻→冰雾先起，与蔓延同向）；
    // 雾带下锚点外放低 1/4 雾高（脚部以下也有薄雾）；alpha 通道同步叠加——轮廓外缘也可见=缭绕感
    //（噪声零值带天然破形，不会读出 sprite 矩形边）
    float mrange = max(0.0001, _FreezeTopY - _FreezeFootY);
    float mh = saturate((worldY - _FreezeFootY + mrange * _MistHeight * 0.25) / (mrange * _MistHeight * 1.25));
    float mist = (1.0 - mh) * (1.0 - mh);
    float mnoise = FrostNoise(float2(uv.x * 6.0 + _Time.y * 0.25, uv.y * 3.0 - _Time.y * 0.12));
    mist *= (0.3 + 0.7 * mnoise) * mask;
    mist = saturate(mist);
    col.rgb = lerp(col.rgb, _MistColor.rgb, mist * _MistAlpha);
    col.a = max(col.a, mist * _MistAlpha * 0.85);

    // ③ 晶体闪烁：锐脉冲白点叠加
    col.rgb += _FrostBrightColor.rgb * FrostSparkle(uv) * mask * 0.55;
}

#endif // GIC_FROZEN_FROST_INCLUDED
