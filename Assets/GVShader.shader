Shader "Hidden/CVCT/GVShader" {
    /*
       Geometry Volume shader
       ======================

       Output: a RWStructuredBuffer<int> storing RGB values in (G << 20 | R << 10 | B)
       (This order is chosen so that the atomic max operations tend to prefer colors that
       contribute a lot of luminosity.)
     */
    Properties
    {
    }

    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            ZTest off
            ZWrite off
            Cull off
            ColorMask 0

            CGPROGRAM
            #pragma target 5.0
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            //#pragma multi_compile   _ ORIENTATION_2 ORIENTATION_3

            struct appdata
            {
                float4 vertex : POSITION;
                //float3 normal : NORMAL;
                //float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                //float2 uv_MainTex : TEXCOORD0;
                //float normal_z : TEXCOORD1;
            };

            RWStructuredBuffer<int> _CVCT_gv : register(u1);
            int _CVCT_GridResolutionI;
            
            /* these come from the properties of the replaced shader */
            //float4 _Color;
            //sampler2D _MainTex;
            //float4 _MainTex_ST;


            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                //o.uv_MainTex = TRANSFORM_TEX(v.texcoord, _MainTex);
                //o.normal_z = mul((float3x3)UNITY_MATRIX_MV, v.normal).z;
                return o;
            }

            void frag(v2f i)
            {
                //clip(i.normal_z);

                float3 xyz = i.vertex.xyz;
#if defined(UNITY_REVERSED_Z)
                xyz.z = 1 - xyz.z;
#endif
                xyz.z *= _CVCT_GridResolutionI * 0.99999;   /* makes sure 0 <= pos.z < GridResolution */

                int3 pos = int3(xyz);

//#ifdef ORIENTATION_2
//                pos = pos.yzx;
//#endif
//#ifdef ORIENTATION_3
//                pos = pos.zxy;
//#endif
                /*float3 col0 = tex2D(_MainTex, i.uv_MainTex).rgb * _Color;
                uint3 col1 = min(uint3(col0 * 256), 1023);
                int color = (col1.g << 20) + (col1.r << 10) + col1.b;

                int index = dot(pos, int3(1, _CVCT_GridResolution, _CVCT_GridResolution * _CVCT_GridResolution));
                InterlockedMax(_CVCT_gv[index], color);*/

                int index = dot(pos, int3(1, _CVCT_GridResolutionI, _CVCT_GridResolutionI * _CVCT_GridResolutionI));
                _CVCT_gv[index] = 1;
            }
            ENDCG
        }
    }
}