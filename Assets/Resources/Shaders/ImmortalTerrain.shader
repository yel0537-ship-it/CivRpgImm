// Местность: массив текстур по типам (слой = индекс местности из terrain.json, передаётся в uv2.x),
// цвет вершин несёт туман войны, разброс оттенка и оттенок эпохи. Работает в URP и встроенном конвейере.
Shader "Immortal/Terrain"
{
    Properties
    {
        _TerrainTex ("Terrain layers", 2DArray) = "" {}
        _Tiling ("Tiling (repeats per world unit)", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_TerrainTex);
            float _Tiling;
            float4 _ImmHeroPos;
            float _ImmVision;
            float _ImmFogDark;
            fixed4 _ImmSeason;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; float layer : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.layer = v.uv2.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 tex = UNITY_SAMPLE_TEX2DARRAY(_TerrainTex, float3(i.wpos.xz * _Tiling, i.layer)).rgb;
                float3 L = normalize(float3(0.35, 0.85, -0.4));
                float ndl = saturate(dot(normalize(i.wn), L));
                float3 season = _ImmSeason.a > 0 ? _ImmSeason.rgb : float3(1, 1, 1);
                float3 c = tex * i.color.rgb * season * (0.45 + 0.65 * ndl);
                float d = distance(i.wpos.xz, _ImmHeroPos.xz);
                c *= 1.0 - smoothstep(_ImmVision * 0.85, _ImmVision * 1.05, d) * _ImmFogDark;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
