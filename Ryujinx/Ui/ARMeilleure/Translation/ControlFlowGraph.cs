using ARMeilleure.IntermediateRepresentation;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ARMeilleure.Translation
{
    class ControlFlowGraph
    {
        private BasicBlock[] _postOrderBlocks;
        private int[] _postOrderMap;

        public int LocalsCount { get; private set; }
        public BasicBlock Entry { get; }
        public IntrusiveList<BasicBlock> Blocks { get; }
        public BasicBlock[] PostOrderBlocks => _postOrderBlocks;
        public int[] PostOrderMap => _postOrderMap; 

        public ControlFlowGraph(BasicBlock entry, IntrusiveList<BasicBlock> blocks, int localsCount)
        {
            Entry = entry;
            Blocks = blocks;
            LocalsCount = localsCount;

            Update();
        }

        public Operand AllocateLocal(OperandType type)
        {
            Operand result = Operand.Factory.Local(type);

            result.NumberLocal(++LocalsCount);

            return result;
        }

        public void Update()
        {
            RemoveUnreachableBlocks(Blocks);

            var visited = new HashSet<BasicBlock>();
            var blockStack = new Stack<BasicBlock>();

            Array.Resize(ref _postOrderBlocks, Blocks.Count);
            Array.Resize(ref _postOrderMap, Blocks.Count);

            visited.Add(Entry);
            blockStack.Push(Entry);

            int index = 0;

            while (blockStack.TryPop(out BasicBlock block))
            {
                bool visitedNew = false;

                for (int i = 0; i < block.SuccessorsCount; i++)
                {
                    BasicBlock succ = block.GetSuccessor(i);

                    if (visited.Add(succ))
                    {
                        blockStack.Push(block);
                        blockStack.Push(succ);

                        visitedNew = true;

                        break;
                    }
                }

                if (!visitedNew)
                {
                    PostOrderMap[block.Index] = index;

                    PostOrderBlocks[index++] = block;
                }
            }
        }

        private void RemoveUnreachableBlocks(IntrusiveList<BasicBlock> blocks)
        {
            var visited = new HashSet<BasicBlock>();
            var workQueue = new Queue<BasicBlock>();

            visited.Add(Entry);
            workQueue.Enqueue(Entry);

            while (workQueue.TryDequeue(out BasicBlock block))
            {
                Debug.Assert(block.Index != -1, "Invalid block index.");

                for (int i = 0; i < block.SuccessorsCount; i++)
                {
                    BasicBlock succ = block.GetSuccessor(i);

                    if (visited.Add(succ))
                    {
                        workQueue.Enqueue(succ);
                    }
                }
            }

            if (visited.Count < blocks.Count)
            {
                // Remove unreachable blocks and renumber.
                int index = 0;

                for (BasicBlock block = blocks.First; block != null;)
                {
                    BasicBlock nextBlock = block.ListNext;

                    if (!visited.Contains(block))
                    {
                        while (block.SuccessorsCount > 0)
                        {
                            block.RemoveSuccessor(index: block.SuccessorsCount - 1);
                        }

                        blocks.Remove(block);
                    }
                    else
                    {
                        block.Index = index++;
                    }

                    block = nextBlock;
                }
            }
        }

        public BasicBlock SplitEdge(BasicBlock predecessor, BasicBlock successor)
        {
            BasicBlock splitBlock = new BasicBlock(Blocks.Count);

            for (int i = 0; i < predecessor.SuccessorsCount; i++)
            {
                if (predecessor.GetSuccessor(i) == successor)
                {
                    predecessor.SetSuccessor(i, splitBlock);
                }
            }

            if (splitBlock.Predecessors.Count == 0)
            {
                throw new ArgumentException("Predecessor and successor are not connected.");
            }

            splitBlock.AddSuccessor(successor);

            Blocks.AddBefore(successor, splitBlock);

            return splitBlock;
        }
    }
}