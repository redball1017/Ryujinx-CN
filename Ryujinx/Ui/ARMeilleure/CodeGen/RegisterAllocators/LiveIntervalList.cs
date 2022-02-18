﻿using System;

namespace ARMeilleure.CodeGen.RegisterAllocators
{
    unsafe struct LiveIntervalList
    {
        private LiveInterval* _items;
        private int _count;
        private int _capacity;

        public int Count => _count;
        public Span<LiveInterval> Span => new(_items, _count);

        public void Add(LiveInterval interval)
        {
            if (_count + 1 > _capacity)
            {
                var oldSpan = Span;

                _capacity = Math.Max(4, _capacity * 2);
                _items = Allocators.References.Allocate<LiveInterval>((uint)_capacity);

                var newSpan = Span;

                oldSpan.CopyTo(newSpan);
            }

            int position = interval.GetStart();
            int i = _count - 1;

            while (i >= 0 && _items[i].GetStart() > position)
            {
                _items[i + 1] = _items[i--];
            }

            _items[i + 1] = interval;
            _count++;
        }
    }
}