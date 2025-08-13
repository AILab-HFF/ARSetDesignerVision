#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        private GsRenderPass _renderPass;
        private static readonly string GaussianSplatRTName = "_GaussianSplatRT";
        private static readonly string GaussianSplatDepthRTName = "_GaussianSplatDepthRT";
        private static readonly string GaussianSplatRTArrayName = "_GaussianSplatRTArray";
        private static readonly string RenderProfilerTag = "GaussianSplatRenderGraph:RenderPass";
        private static readonly string CompositeProfilerTag = "GaussianSplatRenderGraph:ComposePass";
        private static readonly ProfilingSampler SRenderProfilingSampler = new(RenderProfilerTag);
        private static readonly ProfilingSampler SCompositeProfilingSampler = new(CompositeProfilerTag);
        private static readonly int SGaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);
        private static readonly int SGaussianSplatArrayRT = Shader.PropertyToID(GaussianSplatRTArrayName);
        private static readonly int SGaussianSplatDepthRT = Shader.PropertyToID(GaussianSplatDepthRTName);

        public static Material compositeMaterial;

        private bool _mHasCamera;

        private class GsRenderPass : ScriptableRenderPass
        {
            private class PassData
            {
                internal UniversalCameraData CameraData;
                internal Camera Camera;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle GaussianSplatRT;
                internal TextureHandle GaussianSplatDepthRT;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                CreateSingleSplatPass(renderGraph, frameData);
            }

            private void CreateSingleSplatPass(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass<PassData>(RenderProfilerTag, out var passData);
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                passData.CameraData = cameraData;
                passData.Camera = cameraData.camera; // Always use current URP camera

                // Color RT
                var rtDesc = cameraData.cameraTargetDescriptor;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                rtDesc.depthBufferBits = 0;
                passData.GaussianSplatRT =
                    UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtDesc, GaussianSplatRTName, true);

                // Depth RT (float32 for precision)
                var depthDesc = rtDesc;
                depthDesc.graphicsFormat = GraphicsFormat.R32_SFloat;
                var depthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, "_GaussianSplatDepthRT", true);
                passData.GaussianSplatDepthRT = depthHandle;


                passData.SourceDepth = resourceData.activeDepthTexture;
                passData.SourceTexture = resourceData.activeColorTexture;

                builder.UseTexture(passData.GaussianSplatRT, AccessFlags.ReadWrite);
                builder.UseTexture(passData.GaussianSplatDepthRT, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SourceTexture, AccessFlags.Write);
                builder.UseTexture(passData.SourceDepth, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    ExecuteRenderPass(data, context));
            }

            static void ExecuteRenderPass(PassData data, UnsafeGraphContext context)
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                using var renderProfile = new ProfilingScope(cmd, SRenderProfilingSampler);

                // MRT binding for both color + depth RT
                RenderTargetIdentifier[] mrt = new RenderTargetIdentifier[]
                {
                    data.GaussianSplatRT,
                    data.GaussianSplatDepthRT
                };

                if (data.CameraData.xr.enabled)
                {
                    // Bind all slices in XR
                    cmd.SetRenderTarget(mrt, data.SourceDepth, 0, CubemapFace.Unknown, -1);
                }
                else
                {
                    cmd.SetRenderTarget(mrt, data.SourceDepth);
                }

                if (data.CameraData.xr.supportsFoveatedRendering)
                    cmd.SetFoveatedRenderingMode(FoveatedRenderingMode.Enabled);

                compositeMaterial =
                    GaussianSplatRenderSystem.instance.SortAndRenderSplats(data.Camera, cmd);

                if (data.CameraData.xr.supportsFoveatedRendering)
                    cmd.SetFoveatedRenderingMode(FoveatedRenderingMode.Disabled);

                using var _ = new ProfilingScope(cmd, SCompositeProfilingSampler);

                // Enable stereo keyword if needed
                if (data.CameraData.xr.enabled &&
                    XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced)
                {
                    compositeMaterial.EnableKeyword("UNITY_SINGLE_PASS_STEREO");
                }
                else
                {
                    compositeMaterial.DisableKeyword("UNITY_SINGLE_PASS_STEREO");
                }

                cmd.SetRenderTarget(data.SourceTexture, data.SourceDepth, 0, CubemapFace.Unknown, -1);
                compositeMaterial.SetTexture(SGaussianSplatRT, data.GaussianSplatRT);
                compositeMaterial.SetTexture(SGaussianSplatArrayRT, data.GaussianSplatRT);
                compositeMaterial.SetTexture(SGaussianSplatDepthRT, data.GaussianSplatDepthRT);

                cmd.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                cmd.DrawProcedural(Matrix4x4.identity, compositeMaterial, 0, MeshTopology.Triangles, 6, 1);
                cmd.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
            }
        }

        public override void Create()
        {
            _renderPass = new GsRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            _mHasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            _mHasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!_mHasCamera)
                return;
            renderer.EnqueuePass(_renderPass);
        }

        protected override void Dispose(bool disposing)
        {
            _renderPass = null;
        }
    }
}
