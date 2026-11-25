// A mosaic over part of an avatar, like the pixelation a broadcaster puts
// over a face, with a choice of where it applies: everywhere, only in
// mirrors, only in photos, in either, or only in a mirror seen in a photo.
//
// The mesh is a rounded volume around the head or the whole body. Inside
// its outline on screen, the avatar's own pixels are redrawn in square
// blocks, each taking the colour at its block's centre. The room showing
// past the avatar is left as it is.
//
// "The avatar's own pixels" are the ones whose stencil has the bit _Stencil
// set, which the avatar's materials set as they draw (ToonMaterial.MarkStencil
// in net.wirenook.assets does that for lilToon). The stencil remembers that
// the avatar was drawn at a pixel, not that it is still what shows there:
//
// - Something drawn over the avatar afterwards, nearer than this volume,
//   hides the mosaic, because the volume is depth tested like anything else.
// - Something drawn over the avatar afterwards from inside the volume (a
//   hand reaching in, anything transparent) is pixelated with it.
// - Anything else that sets the same bit counts as the avatar wherever it
//   shows inside the outline, at any distance behind.
// - On an avatar that sets the bit nowhere, nothing is drawn at all. With
//   _Stencil at 0 the test always passes and everything inside the outline
//   is pixelated, the room included, as a censor bar would.
//
// A block's centre can lie off the avatar. Its pixels then take the colour
// of whatever is behind, so the blocks along the avatar's edge look bitten
// into and thin parts mostly disappear.
//
// Blocks are a fixed size in metres at the volume's centre, so they shrink
// with distance like anything else, and the grid is anchored to that centre
// so it travels with the avatar instead of the avatar swimming through it.
//
// A viewer standing inside the volume sees nothing of it (its near faces are
// behind them), so the wearer's own first-person view is never pixelated.
//
// This is a look, not a privacy measure: a viewer who blocks this avatar's
// shaders, or its animations, sees the avatar plainly.
Shader "Wirenook/Censor"
{
    Properties
    {
        _Show ("Show", Range(0, 1)) = 0
        _Mode ("When (0 always, 1 mirrors, 2 photos, 3 mirrors or photos, 4 mirrors in photos)", Float) = 0
        _Block ("Block size (m)", Float) = 0.035
        [IntRange] _Stencil ("Only where this stencil bit is set (0: everywhere)", Range(0, 255)) = 128
    }

    SubShader
    {
        Tags
        {
            // After everything an avatar is normally made of, so its
            // stencil marks are all in place, and before the screen-space
            // overlays.
            "Queue" = "Overlay-100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            "VRCFallback" = "Hidden"
        }

        // One copy of the screen per camera, shared by every censor in view.
        GrabPass { "_WirenookCensorGrab" }

        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        // Read only: the one bit is compared and nothing is written.
        Stencil
        {
            Ref [_Stencil]
            ReadMask [_Stencil]
            Comp Equal
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_WirenookCensorGrab);
            float _Show;
            float _Mode;
            float _Block;

            // Set by VRChat: non-zero when rendering a mirror, or a handheld
            // camera or screenshot.
            float _VRChatMirrorMode;
            float _VRChatCameraMode;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grab : TEXCOORD0;
                // The same for every vertex: where the volume's centre is on
                // screen, and the size of one block there.
                float4 grid : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.grab = ComputeGrabScreenPos(o.pos);

                float4 centre = UnityObjectToClipPos(float4(0, 0, 0, 1));
                float depth = max(centre.w, 0.1);
                float4 centreGrab = ComputeGrabScreenPos(float4(centre.xyz, depth));
                float2 halfView = 1.0 / abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11));
                float2 block = _Block / (2.0 * depth * halfView);
                #if defined(UNITY_SINGLE_PASS_STEREO)
                // Both eyes side by side in one image: each has half its width.
                block.x *= unity_StereoScaleOffset[unity_StereoEyeIndex].x;
                #endif
                o.grid = float4(centreGrab.xy / centreGrab.w, block);

                bool mirror = _VRChatMirrorMode > 0.5;
                bool photo = _VRChatCameraMode > 0.5;
                bool applies = _Mode < 0.5 ? true
                    : _Mode < 1.5 ? mirror
                    : _Mode < 2.5 ? photo
                    : _Mode < 3.5 ? (mirror || photo)
                    : (mirror && photo);
                // Collapsing every vertex to one point draws nothing.
                if (!applies || _Show <= 0.001) o.pos = float4(0, 0, 0, 1);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.grab.xy / i.grab.w;
                float2 centre = i.grid.xy;
                float2 block = max(i.grid.zw, 0.0005);
                float2 snapped = centre + (floor((uv - centre) / block) + 0.5) * block;
                fixed4 colour = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_WirenookCensorGrab, saturate(snapped));
                colour.a = _Show;
                return colour;
            }
            ENDCG
        }
    }
}
