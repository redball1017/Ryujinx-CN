namespace Ryujinx.Graphics.Shader.CodeGen.Glsl
{
    static class DefaultNames
    {
        public const string LocalNamePrefix = "temp";

        public const string SamplerNamePrefix = "tex";
        public const string ImageNamePrefix   = "img";

        public const string PerPatchAttributePrefix = "patch_attr_";
        public const string IAttributePrefix = "in_attr";
        public const string OAttributePrefix = "out_attr";

        public const string StorageNamePrefix = "s";

        public const string DataName = "data";

        public const string SupportBlockName = "support_block";
        public const string SupportBlockAlphaTestName = "s_alpha_test";
        public const string SupportBlockIsBgraName = "s_is_bgra";
        public const string SupportBlockViewportInverse = "s_viewport_inverse";
        public const string SupportBlockFragmentScaleCount = "s_frag_scale_count";
        public const string SupportBlockRenderScaleName = "s_render_scale";

        public const string BlockSuffix = "block";

        public const string UniformNamePrefix = "c";
        public const string UniformNameSuffix = "data";

        public const string LocalMemoryName  = "local_mem";
        public const string SharedMemoryName = "shared_mem";

        public const string ArgumentNamePrefix = "a";

        public const string UndefinedName = "undef";
    }
}