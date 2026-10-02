// 冻结立牌 sprite shader（2026-10-02 拍板「像真的结冰」shader 实现路线，霜化数学=FrozenFrost.cginc）
// 立牌冻结态专用：UnitView 冻结时把 SpriteRenderer.sharedMaterial 换成本材质（全单位共享单材质，
// 零实例化），解冻回到原 sprite 默认材质；_FrozenAmount/_FreezeFootY/_FreezeTopY 经
// MaterialPropertyBlock 逐帧写入（renderer.color 走 mesh 顶点色互不冲突）。
// 与 Sprites/Default 同构的透明混合/顶点色消费；Cull Off——立牌 SetFacing 负 localScale.x 翻面
// 绕向翻转会被默认 Cull Back 剔成隐形（ChromaKeyVideo 同理）。
// 必须登记 GraphicsSettings→Always Included Shaders（运行时 Shader.Find 防构建剥离，docs/14 §63③）。
Shader "GIC/Battle/FrozenSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FrozenAmount ("结冰进度", Range(0, 1)) = 0
        _FreezeFootY ("脚部世界Y", Float) = 0
        _FreezeTopY ("头顶世界Y", Float) = 1
        _FrostDeepColor ("暗部霜色", Color) = (0.58, 0.68, 0.76, 1)
        _FrostBrightColor ("霜白", Color) = (0.85, 0.95, 1.00, 1)
        _FrostDesat ("去饱和强度", Range(0, 1)) = 0.8
        _FrostLift ("霜化提亮", Range(0, 0.5)) = 0.3
        _FrostPatchStrength ("白霜斑强度", Range(0, 1)) = 0.55
        _MistColor ("冰雾色", Color) = (0.71, 0.83, 0.95, 1)
        _MistAlpha ("冰雾浓度", Range(0, 1)) = 0.3
        _MistHeight ("冰雾高度占比", Range(0.05, 1)) = 0.3
        _FrostAlpha ("冰体不透明度", Range(0.05, 1)) = 0.78
        _FrostFringeAlpha ("轮廓外冰缘不透明度", Range(0, 1)) = 0.38
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FrozenFrost.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // 轮廓外延冰缘的四邻域采样步长

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR; // SpriteRenderer 把 renderer.color 烘进 mesh 顶点色
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz; // 霜化遮罩=世界系梯度
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                half texAlpha = c.a;           // 形状 alpha（边缘霜光基准，tint 前取值）
                c *= i.color;                  // tint（尸体灰/受击红/常态白——冻结配色由霜化接管）

                // 轮廓外延冰缘的邻域形状 alpha（±4 texel 十字四采样取 max——sprite 路径形状=tex alpha）
                float2 o4 = _MainTex_TexelSize.xy * 4.0;
                half neighborAlpha = max(
                    max(tex2D(_MainTex, i.uv + float2(o4.x, 0.0)).a, tex2D(_MainTex, i.uv - float2(o4.x, 0.0)).a),
                    max(tex2D(_MainTex, i.uv + float2(0.0, o4.y)).a, tex2D(_MainTex, i.uv - float2(0.0, o4.y)).a));

                FrostApply(c, i.worldPos.y, i.uv, texAlpha, neighborAlpha);
                return c;
            }
            ENDCG
        }
    }
}
