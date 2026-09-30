// 神碑看得到的部分寫進 stencil（不畫顏色）。
// 萬年龜的剪影材質（OccludedSilhouette）只在這些像素、而且萬年龜在神碑後面時才畫，
// 所以萬年龜自己的殼擋住腳、或站在神碑前面時都不會出現剪影。
Shader "ARBase/SteleOcclusionMask"
{
    SubShader
    {
        // 排在所有不透明物件（含碑面的 Cutout）之後，深度已經寫好
        Tags { "Queue" = "Geometry+501" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
