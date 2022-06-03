﻿using OpenTK;
using OpenTK.Graphics.OpenGL;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Graphics.OpenGL;
using Ryujinx.Input.HLE;
using System;

using static SDL2.SDL;

namespace Ryujinx.Headless.SDL2.OpenGL
{
    class OpenGLWindow : WindowBase
    {
        private static void SetupOpenGLAttributes(bool sharedContext, GraphicsDebugLevel debugLevel)
        {
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_COMPATIBILITY);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_FLAGS, debugLevel != GraphicsDebugLevel.None ? (int)SDL_GLcontext.SDL_GL_CONTEXT_DEBUG_FLAG : 0);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, sharedContext ? 1 : 0);

            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_ACCELERATED_VISUAL, 1);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_RED_SIZE, 8);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_GREEN_SIZE, 8);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_BLUE_SIZE, 8);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_ALPHA_SIZE, 8);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_DEPTH_SIZE, 16);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_STENCIL_SIZE, 0);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_STEREO, 0);
        }

        private class OpenToolkitBindingsContext : IBindingsContext
        {
            public IntPtr GetProcAddress(string procName)
            {
                return SDL_GL_GetProcAddress(procName);
            }
        }

        private class SDL2OpenGLContext : IOpenGLContext
        {
            private IntPtr _context;
            private IntPtr _window;
            private bool _shouldDisposeWindow;

            public SDL2OpenGLContext(IntPtr context, IntPtr window, bool shouldDisposeWindow = true)
            {
                _context = context;
                _window = window;
                _shouldDisposeWindow = shouldDisposeWindow;
            }

            public static SDL2OpenGLContext CreateBackgroundContext(SDL2OpenGLContext sharedContext)
            {
                sharedContext.MakeCurrent();

                // Ensure we share our contexts.
                SetupOpenGLAttributes(true, GraphicsDebugLevel.None);
                IntPtr windowHandle = SDL_CreateWindow("Ryujinx background context window", 0, 0, 1, 1, SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
                IntPtr context = SDL_GL_CreateContext(windowHandle);

                GL.LoadBindings(new OpenToolkitBindingsContext());

                SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 0);

                SDL_GL_MakeCurrent(windowHandle, IntPtr.Zero);

                return new SDL2OpenGLContext(context, windowHandle);
            }

            public void MakeCurrent()
            {
                if (SDL_GL_GetCurrentContext() == _context || SDL_GL_GetCurrentWindow() == _window)
                {
                    return;
                }

                int res = SDL_GL_MakeCurrent(_window, _context);

                if (res != 0)
                {
                    string errorMessage = $"SDL_GL_CreateContext failed with error \"{SDL_GetError()}\"";

                    Logger.Error?.Print(LogClass.Application, errorMessage);

                    throw new Exception(errorMessage);
                }
            }

            public void Dispose()
            {
                SDL_GL_DeleteContext(_context);

                if (_shouldDisposeWindow)
                {
                    SDL_DestroyWindow(_window);
                }
            }
        }

        private GraphicsDebugLevel _glLogLevel;
        private SDL2OpenGLContext _openGLContext;

        public OpenGLWindow(InputManager inputManager, GraphicsDebugLevel glLogLevel, AspectRatio aspectRatio, bool enableMouse) : base(inputManager, glLogLevel, aspectRatio, enableMouse)
        {
            _glLogLevel = glLogLevel;
        }

        protected override string GetGpuVendorName()
        {
            return ((Renderer)Renderer).GpuVendor;
        }

        public override SDL_WindowFlags GetWindowFlags() => SDL_WindowFlags.SDL_WINDOW_OPENGL;

        protected override void InitializeRenderer()
        {
            // Ensure to not share this context with other contexts before this point.
            SetupOpenGLAttributes(false, _glLogLevel);
            IntPtr context = SDL_GL_CreateContext(WindowHandle);
            SDL_GL_SetSwapInterval(1);

            if (context == IntPtr.Zero)
            {
                string errorMessage = $"SDL_GL_CreateContext failed with error \"{SDL_GetError()}\"";

                Logger.Error?.Print(LogClass.Application, errorMessage);

                throw new Exception(errorMessage);
            }

            // NOTE: The window handle needs to be disposed by the thread that created it and is handled separately.
            _openGLContext = new SDL2OpenGLContext(context, WindowHandle, false);

            // First take exclusivity on the OpenGL context.
            ((Renderer)Renderer).InitializeBackgroundContext(SDL2OpenGLContext.CreateBackgroundContext(_openGLContext));

            _openGLContext.MakeCurrent();

            GL.ClearColor(0, 0, 0, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            SwapBuffers(0);

            Renderer?.Window.SetSize(DefaultWidth, DefaultHeight);
            MouseDriver.SetClientSize(DefaultWidth, DefaultHeight);
        }

        protected override void FinalizeRenderer()
        {
            // Try to bind the OpenGL context before calling the gpu disposal.
            _openGLContext.MakeCurrent();

            Device.DisposeGpu();

            // Unbind context and destroy everything
            SDL_GL_MakeCurrent(WindowHandle, IntPtr.Zero);
            _openGLContext.Dispose();
        }

        protected override void SwapBuffers(object image)
        {
            if ((int)image != 0)
            {
                // The game's framebruffer is already bound, so blit it to the window's backbuffer
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);

                GL.Clear(ClearBufferMask.ColorBufferBit);
                GL.ClearColor(0, 0, 0, 1);

                GL.BlitFramebuffer(0,
                    0,
                    Width,
                    Height,
                    0,
                    0,
                    Width,
                    Height,
                    ClearBufferMask.ColorBufferBit,
                    BlitFramebufferFilter.Linear);
            }

            SDL_GL_SwapWindow(WindowHandle);
        }
    }
}
