namespace Ryujinx.Graphics.Gpu.Shader
{
    /// <summary>
    /// State used by the <see cref="GpuAccessor"/>.
    /// </summary>
    struct GpuChannelComputeState
    {
        // New fields should be added to the end of the struct to keep disk shader cache compatibility.

        /// <summary>
        /// Local group size X of the compute shader.
        /// </summary>
        public readonly int LocalSizeX;

        /// <summary>
        /// Local group size Y of the compute shader.
        /// </summary>
        public readonly int LocalSizeY;

        /// <summary>
        /// Local group size Z of the compute shader.
        /// </summary>
        public readonly int LocalSizeZ;

        /// <summary>
        /// Local memory size of the compute shader.
        /// </summary>
        public readonly int LocalMemorySize;

        /// <summary>
        /// Shared memory size of the compute shader.
        /// </summary>
        public readonly int SharedMemorySize;

        /// <summary>
        /// Creates a new GPU compute state.
        /// </summary>
        /// <param name="localSizeX">Local group size X of the compute shader</param>
        /// <param name="localSizeY">Local group size Y of the compute shader</param>
        /// <param name="localSizeZ">Local group size Z of the compute shader</param>
        /// <param name="localMemorySize">Local memory size of the compute shader</param>
        /// <param name="sharedMemorySize">Shared memory size of the compute shader</param>
        public GpuChannelComputeState(
            int localSizeX,
            int localSizeY,
            int localSizeZ,
            int localMemorySize,
            int sharedMemorySize)
        {
            LocalSizeX = localSizeX;
            LocalSizeY = localSizeY;
            LocalSizeZ = localSizeZ;
            LocalMemorySize = localMemorySize;
            SharedMemorySize = sharedMemorySize;
        }
    }
}