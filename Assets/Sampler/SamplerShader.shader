Shader "Unlit/SamplerShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            /*#include "Assets/ShaderDebugger/debugger.cginc"*/

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            float4 _Color;
            float4x4 _CVCT_WorldToLightLocalMatrix;
            float4 _CVCT_GridResolution;
            sampler3D _CVCT_LightTex3d;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
                return o;
            }

            float light_at_point(v2f i)
            {
                float3 norm = mul((float3x3)_CVCT_WorldToLightLocalMatrix, i.worldNormal);
                if (norm.z >= 0)
                    return 0;

                /* the scale in this matrix is such that the 0th cascade corresponds to
                   the box from (-1,-1,-1) to (+1,+1,+1). */
                float3 pos = mul(_CVCT_WorldToLightLocalMatrix, i.worldPos).xyz;
                
                float3 central_pos = abs(pos);
                float central_distance = max(central_pos.x, max(central_pos.y, central_pos.z));
                /* central_distance => cascade:
                          <= 1            0
                          <= 2            1
                          <= 4            2
                          <= 8            3
                 */
                float cascade = ceil(log2(max(1.0, central_distance)));

                pos *= pow(0.5, cascade);       /* (-1,-1,-1) to (+1,+1,+1) in the right cascade */
                pos = pos * 0.5 + 0.5;            /* (0,0,0) to (1,1,1) */
                pos.y += cascade;                    /* (0,c,0) to (1,1+c,1) */
                pos.y *= _CVCT_GridResolution.y;       /* rescale along y */

                float light = pos.y < 1 ? tex3D(_CVCT_LightTex3d, pos).r : 1;

                /*uint root = DebugFragment(i.vertex);
                DbgValue4(root, float4(pos, light));*/

                return light * (-normalize(norm).z);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 col = _Color;
                col *= light_at_point(i);
                return col;
            }
            ENDCG
        }
    }
}
