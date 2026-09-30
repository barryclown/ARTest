// 萬年龜走到神碑後面時，透過神碑畫出半透明剪影（邊緣較亮），玩家才知道後面有龜、點得到。
// 只畫在 SteleOcclusionMask 標記過的像素（stencil = 1），畫過一次就改成 2，重疊的部位不會越疊越亮。
Shader "ARBase/OccludedSilhouette"
{
    Properties
    {
        _XrayColor ("Color", Color) = (0.55, 0.92, 1, 0.75)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest Greater
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp Equal
                Pass IncrSat
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _XrayColor;
            half _RimPower;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = UnityWorldSpaceViewDir(mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half rim = pow(1 - saturate(dot(normalize(i.normal), normalize(i.viewDir))), _RimPower);
                fixed4 c = _XrayColor;
                c.rgb *= lerp(0.8, 1.35, rim);
                c.a *= lerp(0.5, 1, rim);
                return c;
            }
            ENDCG
        }
    }
}
