// ============================================
// ToonOutlineFeature.cs
// 用途: URP Renderer Feature —— 把描边壳（反壳 Pass）从普通不透明队列里
//       拆出来，统一在"所有不透明物体渲染完成后"绘制。
// 原理: 之前描边 Pass 挂在 SRPDefaultUnlit，和物体表面一起按距离混排。
//       壳可能先于别的物体表面绘制，而壳悬在开放几何（帐篷布/屋顶布）
//       表面前方 → 表面后画时深度测试失败 → 整面被描边色覆盖。
//       挪到 AfterRenderingOpaques 后，所有表面必然已盖完 Stencil 章，
//       描边 Pass 的 Comp NotEqual [_StencilRef] 测试永远有效。
// 使用: 选中 URP Renderer Data 资产（PC_Renderer / Mobile_Renderer），
//       Add Renderer Feature → ToonOutlineFeature。
// 依赖: Character_Toon_Opaque.shader 的 Outline Pass（LightMode = ToonOutline）
// 兼容: Unity 6000.3 / URP 17 —— 同时实现 RenderGraph 与兼容模式两条路径，
//       项目无论开不开 RenderGraph 都能工作
// 作者: Yang
// ============================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule; // RenderGraph 类型（RenderGraph/RendererListHandle 等）
using UnityEngine.Rendering.Universal;

public class ToonOutlineFeature : ScriptableRendererFeature
{
    class ToonOutlinePass : ScriptableRenderPass
    {
        static readonly ShaderTagId OutlineTag = new ShaderTagId("ToonOutline");
        FilteringSettings filtering;

        // RenderGraph 模式下在渲染函数间传递的绘制数据
        private class PassData
        {
            internal RendererListHandle rendererList;
        }

        public ToonOutlinePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            // 只在不透明队列里找带 ToonOutline Pass 的渲染器
            filtering = new FilteringSettings(RenderQueueRange.opaque);
        }

        // ---------- 路径 A: RenderGraph（Unity 6 默认）----------
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            // 组装"画哪些物体、用哪个 Pass"的设置（不透明排序规则）
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                OutlineTag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

            RendererListParams rendererListParams = new RendererListParams(
                renderingData.cullResults, drawingSettings, filtering);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Toon Outline", out PassData passData, new ProfilingSampler("ToonOutlinePass")))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                builder.UseRendererList(passData.rendererList);

                // 画到相机颜色缓冲；深度可读可写——描边壳必须写入深度，
                // 否则后渲染的天空盒会把对着天空的描边像素整个盖掉
                builder.SetRenderAttachment(resourceData.cameraColor, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.cameraDepth, AccessFlags.Read | AccessFlags.Write);
                // 描边 shader 需要访问全局光照数据等
                builder.UseAllGlobalTextures(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

        // ---------- 路径 B: 兼容模式（RenderGraph 关闭时）----------
        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Toon Outline");
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                OutlineTag, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);

            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filtering);
        }
    }

    ToonOutlinePass outlinePass;

    public override void Create()
    {
        outlinePass = new ToonOutlinePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(outlinePass);
    }
}
