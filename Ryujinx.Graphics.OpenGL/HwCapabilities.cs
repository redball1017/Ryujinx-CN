using OpenTK.Graphics.OpenGL;
using System;

namespace Ryujinx.Graphics.OpenGL
{
    static class HwCapabilities
    {
        private static readonly Lazy<bool> _supportsAlphaToCoverageDitherControl = new Lazy<bool>(() => HasExtension("GL_NV_alpha_to_coverage_dither_control"));
        private static readonly Lazy<bool> _supportsAstcCompression              = new Lazy<bool>(() => HasExtension("GL_KHR_texture_compression_astc_ldr"));
        private static readonly Lazy<bool> _supportsDrawTexture                  = new Lazy<bool>(() => HasExtension("GL_NV_draw_texture"));
        private static readonly Lazy<bool> _supportsFragmentShaderInterlock      = new Lazy<bool>(() => HasExtension("GL_ARB_fragment_shader_interlock"));
        private static readonly Lazy<bool> _supportsFragmentShaderOrdering       = new Lazy<bool>(() => HasExtension("GL_INTEL_fragment_shader_ordering"));
        private static readonly Lazy<bool> _supportsImageLoadFormatted           = new Lazy<bool>(() => HasExtension("GL_EXT_shader_image_load_formatted"));
        private static readonly Lazy<bool> _supportsIndirectParameters           = new Lazy<bool>(() => HasExtension("GL_ARB_indirect_parameters"));
        private static readonly Lazy<bool> _supportsParallelShaderCompile        = new Lazy<bool>(() => HasExtension("GL_ARB_parallel_shader_compile"));
        private static readonly Lazy<bool> _supportsPolygonOffsetClamp           = new Lazy<bool>(() => HasExtension("GL_EXT_polygon_offset_clamp"));
        private static readonly Lazy<bool> _supportsQuads                        = new Lazy<bool>(SupportsQuadsCheck);
        private static readonly Lazy<bool> _supportsSeamlessCubemapPerTexture    = new Lazy<bool>(() => HasExtension("GL_ARB_seamless_cubemap_per_texture"));
        private static readonly Lazy<bool> _supportsShaderBallot                 = new Lazy<bool>(() => HasExtension("GL_ARB_shader_ballot"));
        private static readonly Lazy<bool> _supportsTextureShadowLod             = new Lazy<bool>(() => HasExtension("GL_EXT_texture_shadow_lod"));
        private static readonly Lazy<bool> _supportsViewportSwizzle              = new Lazy<bool>(() => HasExtension("GL_NV_viewport_swizzle"));

        private static readonly Lazy<int> _maximumComputeSharedMemorySize = new Lazy<int>(() => GetLimit(All.MaxComputeSharedMemorySize));
        private static readonly Lazy<int> _storageBufferOffsetAlignment   = new Lazy<int>(() => GetLimit(All.ShaderStorageBufferOffsetAlignment));

        public enum GpuVendor
        {
            Unknown,
            AmdWindows,
            AmdUnix,
            IntelWindows,
            IntelUnix,
            Nvidia
        }

        private static readonly Lazy<GpuVendor> _gpuVendor = new Lazy<GpuVendor>(GetGpuVendor);

        private static bool _isAMD   => _gpuVendor.Value == GpuVendor.AmdWindows || _gpuVendor.Value == GpuVendor.AmdUnix;
        private static bool _isIntel => _gpuVendor.Value == GpuVendor.IntelWindows || _gpuVendor.Value == GpuVendor.IntelUnix;

        public static GpuVendor Vendor => _gpuVendor.Value;

        private static Lazy<float> _maxSupportedAnisotropy = new Lazy<float>(GL.GetFloat((GetPName)All.MaxTextureMaxAnisotropy));

        public static bool UsePersistentBufferForFlush       => _gpuVendor.Value == GpuVendor.AmdWindows || _gpuVendor.Value == GpuVendor.Nvidia;

        public static bool SupportsAlphaToCoverageDitherControl => _supportsAlphaToCoverageDitherControl.Value;
        public static bool SupportsAstcCompression              => _supportsAstcCompression.Value;
        public static bool SupportsDrawTexture                  => _supportsDrawTexture.Value;
        public static bool SupportsFragmentShaderInterlock      => _supportsFragmentShaderInterlock.Value;
        public static bool SupportsFragmentShaderOrdering       => _supportsFragmentShaderOrdering.Value;
        public static bool SupportsImageLoadFormatted           => _supportsImageLoadFormatted.Value;
        public static bool SupportsIndirectParameters           => _supportsIndirectParameters.Value;
        public static bool SupportsParallelShaderCompile        => _supportsParallelShaderCompile.Value;
        public static bool SupportsPolygonOffsetClamp           => _supportsPolygonOffsetClamp.Value;
        public static bool SupportsQuads                        => _supportsQuads.Value;
        public static bool SupportsSeamlessCubemapPerTexture    => _supportsSeamlessCubemapPerTexture.Value;
        public static bool SupportsShaderBallot                 => _supportsShaderBallot.Value;
        public static bool SupportsTextureShadowLod             => _supportsTextureShadowLod.Value;
        public static bool SupportsViewportSwizzle              => _supportsViewportSwizzle.Value;

        public static bool SupportsMismatchingViewFormat    => _gpuVendor.Value != GpuVendor.AmdWindows && _gpuVendor.Value != GpuVendor.IntelWindows;
        public static bool SupportsNonConstantTextureOffset => _gpuVendor.Value == GpuVendor.Nvidia;
        public static bool RequiresSyncFlush                => _gpuVendor.Value == GpuVendor.AmdWindows || _isIntel;

        public static int MaximumComputeSharedMemorySize => _maximumComputeSharedMemorySize.Value;
        public static int StorageBufferOffsetAlignment   => _storageBufferOffsetAlignment.Value;

        public static float MaximumSupportedAnisotropy => _maxSupportedAnisotropy.Value;

        private static bool HasExtension(string name)
        {
            int numExtensions = GL.GetInteger(GetPName.NumExtensions);

            for (int extension = 0; extension < numExtensions; extension++)
            {
                if (GL.GetString(StringNameIndexed.Extensions, extension) == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetLimit(All name)
        {
            return GL.GetInteger((GetPName)name);
        }

        private static GpuVendor GetGpuVendor()
        {
            string vendor = GL.GetString(StringName.Vendor).ToLower();

            if (vendor == "nvidia corporation")
            {
                return GpuVendor.Nvidia;
            }
            else if (vendor == "intel")
            {
                string renderer = GL.GetString(StringName.Renderer).ToLower();

                return renderer.Contains("mesa") ? GpuVendor.IntelUnix : GpuVendor.IntelWindows;
            }
            else if (vendor == "ati technologies inc." || vendor == "advanced micro devices, inc.")
            {
                return GpuVendor.AmdWindows;
            }
            else if (vendor == "amd" || vendor == "x.org")
            {
                return GpuVendor.AmdUnix;
            }
            else
            {
                return GpuVendor.Unknown;
            }
        }

        private static bool SupportsQuadsCheck()
        {
            GL.GetError(); // Clear any existing error.
            GL.Begin(PrimitiveType.Quads);
            GL.End();

            return GL.GetError() == ErrorCode.NoError;
        }
    }
}