// AnalogFX placeholder - PROGRAMMER ART, NOT FINAL.
//
// Purpose: prove the render pipeline and the parameter contract, nothing else. It is meant to look
// crude so nobody mistakes it for a design decision or ships it by accident.
//
// ARTIST C: replace this file (or point the material at a different shader / Shader Graph). The one
// thing you must preserve is the five global uniform names below. They are set from C# by
// AnalogFXController via Shader.SetGlobalFloat, so they are NOT declared in the Properties block -
// a material property of the same name would shadow the global and break the contract.
//
//   _AnalogFX_Enabled    0 = pass through untouched, 1 = apply. The kill switch. Honour it.
//   _AnalogFX_Scanline   0..1 horizontal CRT banding
//   _AnalogFX_Grain      0..1 noise
//   _AnalogFX_Chromatic  0..1 RGB channel separation
//   _AnalogFX_Glitch     0..1 blocky row tearing
//
// Every parameter must be a no-op at 0 and must not blow out at 1.
Shader "JamKit/AnalogFX Placeholder"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "AnalogFXPlaceholder"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // URP 17 / Unity 6: Blit.hlsl lives in the CORE package, not universal.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // The contract. Global uniforms, set from AnalogFXController.
            float _AnalogFX_Enabled;
            float _AnalogFX_Scanline;
            float _AnalogFX_Grain;
            float _AnalogFX_Chromatic;
            float _AnalogFX_Glitch;

            // COMFORT BUDGET. Anything animated is quantised to a low update rate on purpose.
            // Re-randomising noise every frame reads as harsh flicker: it causes eye strain and is
            // a photosensitivity risk in a public build. Keep these low. A replacement shader
            // should respect the same budget.
            #define ANALOGFX_GRAIN_HZ    10.0   // grain re-rolls 10x/sec, not 60
            #define ANALOGFX_GLITCH_HZ    5.0   // tearing re-rolls 5x/sec
            #define ANALOGFX_SCANLINES  200.0   // fixed band count, NOT tied to pixel height

            // Cheap hash. Good enough for placeholder grain; no texture, no loop, WebGL-safe.
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half3 SampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // Kill switch: exact pass-through, no arithmetic on the source colour.
                if (_AnalogFX_Enabled < 0.5)
                    return half4(SampleSource(uv), 1.0);

                // GLITCH: displace chunky horizontal rows, re-rolled a few times a second.
                // Only the worst ~8% of rows tear, so it reads as an occasional signal fault
                // rather than constant motion.
                float row = floor(uv.y * 24.0);
                float roll = Hash21(float2(row, floor(_Time.y * ANALOGFX_GLITCH_HZ)));
                float tear = max(roll - 0.92, 0.0) * 12.5;                // 0..1 on the worst rows
                uv.x = frac(uv.x + tear * _AnalogFX_Glitch * 0.15);

                // CHROMATIC: split R and B horizontally. Deliberately linear and ugly.
                float split = _AnalogFX_Chromatic * 0.012;
                half3 col;
                col.r = SampleSource(float2(uv.x + split, uv.y)).r;
                col.g = SampleSource(uv).g;
                col.b = SampleSource(float2(uv.x - split, uv.y)).b;

                // SCANLINE: a fixed count of soft bands. Deriving this from _ScreenParams.y gave a
                // 1-pixel comb that aliased and shimmered under any camera motion; a fixed count
                // is stable at every resolution and much easier on the eyes.
                float band = 0.5 + 0.5 * sin(uv.y * ANALOGFX_SCANLINES * 6.2831853);
                col *= 1.0 - _AnalogFX_Scanline * 0.30 * band;

                // GRAIN: chunky (2x2 pixel) noise stepping at ANALOGFX_GRAIN_HZ, not per frame.
                float2 grainCell = floor(uv * _ScreenParams.xy * 0.5);
                float n = Hash21(grainCell + floor(_Time.y * ANALOGFX_GRAIN_HZ) * 37.0) - 0.5;
                col += n * _AnalogFX_Grain * 0.18;

                // A small brightness lift on torn rows. Kept low: bright flashes are the worst
                // offender for eye strain.
                col += tear * _AnalogFX_Glitch * 0.08;

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
