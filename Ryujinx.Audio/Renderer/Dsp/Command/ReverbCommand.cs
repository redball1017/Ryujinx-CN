//
// Copyright (c) 2019-2021 Ryujinx
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
//

using Ryujinx.Audio.Renderer.Dsp.State;
using Ryujinx.Audio.Renderer.Parameter.Effect;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class ReverbCommand : ICommand
    {
        private static readonly int[] OutputEarlyIndicesTableMono = new int[10] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly int[] TargetEarlyDelayLineIndicesTableMono = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private static readonly int[] OutputIndicesTableMono = new int[4] { 0, 0, 0, 0 };
        private static readonly int[] TargetOutputFeedbackIndicesTableMono = new int[4] { 0, 1, 2, 3 };

        private static readonly int[] OutputEarlyIndicesTableStereo = new int[10] { 0, 0, 1, 1, 0, 1, 0, 0, 1, 1 };
        private static readonly int[] TargetEarlyDelayLineIndicesTableStereo = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private static readonly int[] OutputIndicesTableStereo = new int[4] { 0, 0, 1, 1 };
        private static readonly int[] TargetOutputFeedbackIndicesTableStereo = new int[4] { 2, 0, 3, 1 };

        private static readonly int[] OutputEarlyIndicesTableQuadraphonic = new int[10] { 0, 0, 1, 1, 0, 1, 2, 2, 3, 3 };
        private static readonly int[] TargetEarlyDelayLineIndicesTableQuadraphonic = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private static readonly int[] OutputIndicesTableQuadraphonic = new int[4] { 0, 1, 2, 3 };
        private static readonly int[] TargetOutputFeedbackIndicesTableQuadraphonic = new int[4] { 0, 1, 2, 3 };

        private static readonly int[] OutputEarlyIndicesTableSurround = new int[20] { 0, 5, 0, 5, 1, 5, 1, 5, 4, 5, 4, 5, 2, 5, 2, 5, 3, 5, 3, 5 };
        private static readonly int[] TargetEarlyDelayLineIndicesTableSurround = new int[20] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9 };
        private static readonly int[] OutputIndicesTableSurround = new int[Constants.ChannelCountMax] { 0, 1, 2, 3, 4, 5 };
        private static readonly int[] TargetOutputFeedbackIndicesTableSurround = new int[Constants.ChannelCountMax] { 0, 1, 2, 3, -1, 3 };

        public bool Enabled { get; set; }

        public int NodeId { get; }

        public CommandType CommandType => CommandType.Reverb;

        public ulong EstimatedProcessingTime { get; set; }

        public ReverbParameter Parameter => _parameter;
        public Memory<ReverbState> State { get; }
        public ulong WorkBuffer { get; }
        public ushort[] OutputBufferIndices { get; }
        public ushort[] InputBufferIndices { get; }
        public bool IsLongSizePreDelaySupported { get; }

        public bool IsEffectEnabled { get; }

        private ReverbParameter _parameter;

        private const int FixedPointPrecision = 14;

        public ReverbCommand(uint bufferOffset, ReverbParameter parameter, Memory<ReverbState> state, bool isEnabled, ulong workBuffer, int nodeId, bool isLongSizePreDelaySupported, bool newEffectChannelMappingSupported)
        {
            Enabled = true;
            IsEffectEnabled = isEnabled;
            NodeId = nodeId;
            _parameter = parameter;
            State = state;
            WorkBuffer = workBuffer;

            InputBufferIndices = new ushort[Constants.VoiceChannelCountMax];
            OutputBufferIndices = new ushort[Constants.VoiceChannelCountMax];

            for (int i = 0; i < Parameter.ChannelCount; i++)
            {
                InputBufferIndices[i] = (ushort)(bufferOffset + Parameter.Input[i]);
                OutputBufferIndices[i] = (ushort)(bufferOffset + Parameter.Output[i]);
            }

            IsLongSizePreDelaySupported = isLongSizePreDelaySupported;

            // NOTE: We do the opposite as Nintendo here for now to restore previous behaviour
            // TODO: Update reverb processing and remove this to use RemapLegacyChannelEffectMappingToChannelResourceMapping.
            DataSourceHelper.RemapChannelResourceMappingToLegacy(newEffectChannelMappingSupported, InputBufferIndices);
            DataSourceHelper.RemapChannelResourceMappingToLegacy(newEffectChannelMappingSupported, OutputBufferIndices);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessReverbMono(ref ReverbState state, ReadOnlySpan<IntPtr> outputBuffers, ReadOnlySpan<IntPtr> inputBuffers, uint sampleCount)
        {
            ProcessReverbGeneric(ref state,
                     outputBuffers,
                     inputBuffers,
                     sampleCount,
                     OutputEarlyIndicesTableMono,
                     TargetEarlyDelayLineIndicesTableMono,
                     TargetOutputFeedbackIndicesTableMono,
                     OutputIndicesTableMono);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessReverbStereo(ref ReverbState state, ReadOnlySpan<IntPtr> outputBuffers, ReadOnlySpan<IntPtr> inputBuffers, uint sampleCount)
        {
            ProcessReverbGeneric(ref state,
                     outputBuffers,
                     inputBuffers,
                     sampleCount,
                     OutputEarlyIndicesTableStereo,
                     TargetEarlyDelayLineIndicesTableStereo,
                     TargetOutputFeedbackIndicesTableStereo,
                     OutputIndicesTableStereo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessReverbQuadraphonic(ref ReverbState state, ReadOnlySpan<IntPtr> outputBuffers, ReadOnlySpan<IntPtr> inputBuffers, uint sampleCount)
        {
            ProcessReverbGeneric(ref state,
                     outputBuffers,
                     inputBuffers,
                     sampleCount,
                     OutputEarlyIndicesTableQuadraphonic,
                     TargetEarlyDelayLineIndicesTableQuadraphonic,
                     TargetOutputFeedbackIndicesTableQuadraphonic,
                     OutputIndicesTableQuadraphonic);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessReverbSurround(ref ReverbState state, ReadOnlySpan<IntPtr> outputBuffers, ReadOnlySpan<IntPtr> inputBuffers, uint sampleCount)
        {
            ProcessReverbGeneric(ref state,
                     outputBuffers,
                     inputBuffers,
                     sampleCount,
                     OutputEarlyIndicesTableSurround,
                     TargetEarlyDelayLineIndicesTableSurround,
                     TargetOutputFeedbackIndicesTableSurround,
                     OutputIndicesTableSurround);
        }

        private unsafe void ProcessReverbGeneric(ref ReverbState state, ReadOnlySpan<IntPtr> outputBuffers, ReadOnlySpan<IntPtr> inputBuffers, uint sampleCount, ReadOnlySpan<int> outputEarlyIndicesTable, ReadOnlySpan<int> targetEarlyDelayLineIndicesTable, ReadOnlySpan<int> targetOutputFeedbackIndicesTable, ReadOnlySpan<int> outputIndicesTable)
        {
            bool isSurround = Parameter.ChannelCount == 6;

            float reverbGain = FixedPointHelper.ToFloat(Parameter.ReverbGain, FixedPointPrecision);
            float lateGain = FixedPointHelper.ToFloat(Parameter.LateGain, FixedPointPrecision);
            float outGain = FixedPointHelper.ToFloat(Parameter.OutGain, FixedPointPrecision);
            float dryGain = FixedPointHelper.ToFloat(Parameter.DryGain, FixedPointPrecision);

            Span<float> outputValues = stackalloc float[Constants.ChannelCountMax];
            Span<float> feedbackValues = stackalloc float[4];
            Span<float> feedbackOutputValues = stackalloc float[4];
            Span<float> channelInput = stackalloc float[Parameter.ChannelCount];

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                outputValues.Fill(0);

                for (int i = 0; i < targetEarlyDelayLineIndicesTable.Length; i++)
                {
                    int earlyDelayIndex = targetEarlyDelayLineIndicesTable[i];
                    int outputIndex = outputEarlyIndicesTable[i];

                    float tapOutput = state.PreDelayLine.TapUnsafe(state.EarlyDelayTime[earlyDelayIndex], 0);

                    outputValues[outputIndex] += tapOutput * state.EarlyGain[earlyDelayIndex];
                }

                if (isSurround)
                {
                    outputValues[5] *= 0.2f;
                }

                float targetPreDelayValue = 0;

                for (int channelIndex = 0; channelIndex < Parameter.ChannelCount; channelIndex++)
                {
                    channelInput[channelIndex] = *((float*)inputBuffers[channelIndex] + sampleIndex) * 64;
                    targetPreDelayValue += channelInput[channelIndex] * reverbGain;
                }

                state.PreDelayLine.Update(targetPreDelayValue);

                float lateValue = state.PreDelayLine.Tap(state.PreDelayLineDelayTime) * lateGain;

                for (int i = 0; i < state.FdnDelayLines.Length; i++)
                {
                    feedbackOutputValues[i] = state.FdnDelayLines[i].Read() * state.HighFrequencyDecayDirectGain[i] + state.PreviousFeedbackOutput[i] * state.HighFrequencyDecayPreviousGain[i];
                    state.PreviousFeedbackOutput[i] = feedbackOutputValues[i];
                }

                feedbackValues[0] = feedbackOutputValues[2] + feedbackOutputValues[1];
                feedbackValues[1] = -feedbackOutputValues[0] - feedbackOutputValues[3];
                feedbackValues[2] = feedbackOutputValues[0] - feedbackOutputValues[3];
                feedbackValues[3] = feedbackOutputValues[1] - feedbackOutputValues[2];

                for (int i = 0; i < state.FdnDelayLines.Length; i++)
                {
                    feedbackOutputValues[i] = state.DecayDelays[i].Update(feedbackValues[i] + lateValue);
                    state.FdnDelayLines[i].Update(feedbackOutputValues[i]);
                }

                for (int i = 0; i < targetOutputFeedbackIndicesTable.Length; i++)
                {
                    int targetOutputFeedbackIndex = targetOutputFeedbackIndicesTable[i];
                    int outputIndex = outputIndicesTable[i];

                    if (targetOutputFeedbackIndex >= 0)
                    {
                        outputValues[outputIndex] += feedbackOutputValues[targetOutputFeedbackIndex];
                    }
                }

                if (isSurround)
                {
                    outputValues[4] += state.FrontCenterDelayLine.Update((feedbackOutputValues[2] - feedbackOutputValues[3]) * 0.5f);
                }

                for (int channelIndex = 0; channelIndex < Parameter.ChannelCount; channelIndex++)
                {
                    *((float*)outputBuffers[channelIndex] + sampleIndex) = (outputValues[channelIndex] * outGain + channelInput[channelIndex] * dryGain) / 64;
                }
            }
        }

        private void ProcessReverb(CommandList context, ref ReverbState state)
        {
            Debug.Assert(Parameter.IsChannelCountValid());

            if (IsEffectEnabled && Parameter.IsChannelCountValid())
            {
                Span<IntPtr> inputBuffers = stackalloc IntPtr[Parameter.ChannelCount];
                Span<IntPtr> outputBuffers = stackalloc IntPtr[Parameter.ChannelCount];

                for (int i = 0; i < Parameter.ChannelCount; i++)
                {
                    inputBuffers[i] = context.GetBufferPointer(InputBufferIndices[i]);
                    outputBuffers[i] = context.GetBufferPointer(OutputBufferIndices[i]);
                }

                switch (Parameter.ChannelCount)
                {
                    case 1:
                        ProcessReverbMono(ref state, outputBuffers, inputBuffers, context.SampleCount);
                        break;
                    case 2:
                        ProcessReverbStereo(ref state, outputBuffers, inputBuffers, context.SampleCount);
                        break;
                    case 4:
                        ProcessReverbQuadraphonic(ref state, outputBuffers, inputBuffers, context.SampleCount);
                        break;
                    case 6:
                        ProcessReverbSurround(ref state, outputBuffers, inputBuffers, context.SampleCount);
                        break;
                    default:
                        throw new NotImplementedException(Parameter.ChannelCount.ToString());
                }
            }
            else
            {
                for (int i = 0; i < Parameter.ChannelCount; i++)
                {
                    if (InputBufferIndices[i] != OutputBufferIndices[i])
                    {
                        context.CopyBuffer(OutputBufferIndices[i], InputBufferIndices[i]);
                    }
                }
            }
        }

        public void Process(CommandList context)
        {
            ref ReverbState state = ref State.Span[0];

            if (IsEffectEnabled)
            {
                if (Parameter.Status == Server.Effect.UsageState.Invalid)
                {
                    state = new ReverbState(ref _parameter, WorkBuffer, IsLongSizePreDelaySupported);
                }
                else if (Parameter.Status == Server.Effect.UsageState.New)
                {
                    state.UpdateParameter(ref _parameter);
                }
            }

            ProcessReverb(context, ref state);
        }
    }
}
