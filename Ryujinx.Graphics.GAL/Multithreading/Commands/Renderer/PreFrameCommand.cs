﻿namespace Ryujinx.Graphics.GAL.Multithreading.Commands.Renderer
{
    struct PreFrameCommand : IGALCommand
    {
        public CommandType CommandType => CommandType.PreFrame;

        public static void Run(ref PreFrameCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            renderer.PreFrame();
        }
    }
}
