// Простой освещённый шейдер с цветами вершин и туманом войны.
// Без LightMode-тегов: рендерится и во встроенном конвейере, и в URP (проход SRPDefaultUnlit).
Shader "Immortal/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Emission ("Emission", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            float _Emission;
            float4 _ImmHeroPos;
            float _ImmVision;
            float _ImmFogDark;
            fixed4 _ImmSeason;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 L = normalize(float3(0.35, 0.85, -0.4));
                float ndl = saturate(dot(normalize(i.wn), L));
                float3 season = _ImmSeason.a > 0 ? lerp(float3(1, 1, 1), _ImmSeason.rgb, 0.6) : float3(1, 1, 1);
                float3 c = i.color.rgb * season * (0.42 + 0.68 * ndl) + i.color.rgb * _Emission;
                float d = distance(i.wpos.xz, _ImmHeroPos.xz);
                float outside = smoothstep(_ImmVision * 0.85, _ImmVision * 1.05, d);
                c *= 1.0 - outside * _ImmFogDark;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
