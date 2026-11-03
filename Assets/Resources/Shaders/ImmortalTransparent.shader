// Полупрозрачный шейдер для дыма, вспышек и индикаторов.
Shader "Immortal/Transparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
        _Pulse ("Pulse", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Pulse;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float3 wpos : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                // Повторяющийся «сбой» в дыму: полосы, бегущие по высоте.
                c.a *= 1.0 + _Pulse * 0.25 * sin(i.wpos.y * 3.0 + _Time.y * 2.0 + i.wpos.x * 0.2);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
