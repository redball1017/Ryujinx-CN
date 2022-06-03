﻿using Ryujinx.Common.Configuration.Hid.Controller.Motion;
using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using NotImplementedException = System.NotImplementedException;

namespace Ryujinx.Common.Configuration.Hid.Controller
{
    public class GenericControllerInputConfig<Button, Stick> : GenericInputConfigurationCommon<Button> where Button : unmanaged where Stick : unmanaged
    {
        [JsonIgnore]
        private float _deadzoneLeft;
        [JsonIgnore]
        private float _deadzoneRight;
        [JsonIgnore]
        private float _triggerThreshold;

        /// <summary>
        /// Left JoyCon Controller Stick Bindings
        /// </summary>
        public JoyconConfigControllerStick<Button, Stick> LeftJoyconStick { get; set; }

        /// <summary>
        /// Right JoyCon Controller Stick Bindings
        /// </summary>
        public JoyconConfigControllerStick<Button, Stick> RightJoyconStick { get; set; }

        /// <summary>
        /// Controller Left Analog Stick Deadzone
        /// </summary>
        public float DeadzoneLeft
        {
            get => _deadzoneLeft; set
            {
                _deadzoneLeft = MathF.Round(value, 3);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Controller Right Analog Stick Deadzone
        /// </summary>
        public float DeadzoneRight
        {
            get => _deadzoneRight; set
            {
                _deadzoneRight = MathF.Round(value, 3);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Controller Left Analog Stick Range
        /// </summary>
        public float RangeLeft { get; set; }

        /// <summary>
        /// Controller Right Analog Stick Range
        /// </summary>
        public float RangeRight { get; set; }

        /// <summary>
        /// Controller Trigger Threshold
        /// </summary>
        public float TriggerThreshold
        {
            get => _triggerThreshold; set
            {
                _triggerThreshold = MathF.Round(value, 3);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Controller Motion Settings
        /// </summary>
        public MotionConfigController Motion { get; set; }

        /// <summary>
        /// Controller Rumble Settings
        /// </summary>
        public RumbleConfigController Rumble { get; set; }
    }
}
