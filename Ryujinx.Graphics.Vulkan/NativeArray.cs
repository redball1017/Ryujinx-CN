﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Vulkan
{
    unsafe class NativeArray<T> : IDisposable where T : unmanaged
    {
        public T* Pointer { get; private set; }
        public int Length { get; }

        public ref T this[int index]
        {
            get => ref Pointer[Checked(index)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Checked(int index)
        {
            if ((uint)index >= (uint)Length)
            {
                throw new IndexOutOfRangeException();
            }

            return index;
        }

        public NativeArray(int length)
        {
            Pointer = (T*)Marshal.AllocHGlobal(checked(length * Unsafe.SizeOf<T>()));
            Length = length;
        }

        public Span<T> ToSpan()
        {
            return new Span<T>(Pointer, Length);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal((IntPtr)Pointer);
            Pointer = null;
        }
    }
}
