﻿using Ryujinx.Common;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Controller.Motion;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

using CemuHookClient = Ryujinx.Input.Motion.CemuHook.Client;
using ConfigControllerType = Ryujinx.Common.Configuration.Hid.ControllerType;

namespace Ryujinx.Input.HLE
{
    public class NpadController : IDisposable
    {
        private class HLEButtonMappingEntry
        {
            public readonly GamepadButtonInputId DriverInputId;
            public readonly ControllerKeys HLEInput;

            public HLEButtonMappingEntry(GamepadButtonInputId driverInputId, ControllerKeys hleInput)
            {
                DriverInputId = driverInputId;
                HLEInput = hleInput;
            }
        }

        private static readonly HLEButtonMappingEntry[] _hleButtonMapping = new HLEButtonMappingEntry[]
        {
            new HLEButtonMappingEntry(GamepadButtonInputId.A, ControllerKeys.A),
            new HLEButtonMappingEntry(GamepadButtonInputId.B, ControllerKeys.B),
            new HLEButtonMappingEntry(GamepadButtonInputId.X, ControllerKeys.X),
            new HLEButtonMappingEntry(GamepadButtonInputId.Y, ControllerKeys.Y),
            new HLEButtonMappingEntry(GamepadButtonInputId.LeftStick, ControllerKeys.LStick),
            new HLEButtonMappingEntry(GamepadButtonInputId.RightStick, ControllerKeys.RStick),
            new HLEButtonMappingEntry(GamepadButtonInputId.LeftShoulder, ControllerKeys.L),
            new HLEButtonMappingEntry(GamepadButtonInputId.RightShoulder, ControllerKeys.R),
            new HLEButtonMappingEntry(GamepadButtonInputId.LeftTrigger, ControllerKeys.Zl),
            new HLEButtonMappingEntry(GamepadButtonInputId.RightTrigger, ControllerKeys.Zr),
            new HLEButtonMappingEntry(GamepadButtonInputId.DpadUp, ControllerKeys.DpadUp),
            new HLEButtonMappingEntry(GamepadButtonInputId.DpadDown, ControllerKeys.DpadDown),
            new HLEButtonMappingEntry(GamepadButtonInputId.DpadLeft, ControllerKeys.DpadLeft),
            new HLEButtonMappingEntry(GamepadButtonInputId.DpadRight, ControllerKeys.DpadRight),
            new HLEButtonMappingEntry(GamepadButtonInputId.Minus, ControllerKeys.Minus),
            new HLEButtonMappingEntry(GamepadButtonInputId.Plus, ControllerKeys.Plus),

            new HLEButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger0, ControllerKeys.SlLeft),
            new HLEButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger0, ControllerKeys.SrLeft),
            new HLEButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger1, ControllerKeys.SlRight),
            new HLEButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger1, ControllerKeys.SrRight),
        };

        private class HLEKeyboardMappingEntry
        {
            public readonly Key TargetKey;
            public readonly byte Target;

            public HLEKeyboardMappingEntry(Key targetKey, byte target)
            {
                TargetKey = targetKey;
                Target = target;
            }
        }

        private static readonly HLEKeyboardMappingEntry[] KeyMapping = new HLEKeyboardMappingEntry[]
        {
            new HLEKeyboardMappingEntry(Key.A, 0x4),
            new HLEKeyboardMappingEntry(Key.B, 0x5),
            new HLEKeyboardMappingEntry(Key.C, 0x6),
            new HLEKeyboardMappingEntry(Key.D, 0x7),
            new HLEKeyboardMappingEntry(Key.E, 0x8),
            new HLEKeyboardMappingEntry(Key.F, 0x9),
            new HLEKeyboardMappingEntry(Key.G, 0xA),
            new HLEKeyboardMappingEntry(Key.H, 0xB),
            new HLEKeyboardMappingEntry(Key.I, 0xC),
            new HLEKeyboardMappingEntry(Key.J, 0xD),
            new HLEKeyboardMappingEntry(Key.K, 0xE),
            new HLEKeyboardMappingEntry(Key.L, 0xF),
            new HLEKeyboardMappingEntry(Key.M, 0x10),
            new HLEKeyboardMappingEntry(Key.N, 0x11),
            new HLEKeyboardMappingEntry(Key.O, 0x12),
            new HLEKeyboardMappingEntry(Key.P, 0x13),
            new HLEKeyboardMappingEntry(Key.Q, 0x14),
            new HLEKeyboardMappingEntry(Key.R, 0x15),
            new HLEKeyboardMappingEntry(Key.S, 0x16),
            new HLEKeyboardMappingEntry(Key.T, 0x17),
            new HLEKeyboardMappingEntry(Key.U, 0x18),
            new HLEKeyboardMappingEntry(Key.V, 0x19),
            new HLEKeyboardMappingEntry(Key.W, 0x1A),
            new HLEKeyboardMappingEntry(Key.X, 0x1B),
            new HLEKeyboardMappingEntry(Key.Y, 0x1C),
            new HLEKeyboardMappingEntry(Key.Z, 0x1D),

            new HLEKeyboardMappingEntry(Key.Number1, 0x1E),
            new HLEKeyboardMappingEntry(Key.Number2, 0x1F),
            new HLEKeyboardMappingEntry(Key.Number3, 0x20),
            new HLEKeyboardMappingEntry(Key.Number4, 0x21),
            new HLEKeyboardMappingEntry(Key.Number5, 0x22),
            new HLEKeyboardMappingEntry(Key.Number6, 0x23),
            new HLEKeyboardMappingEntry(Key.Number7, 0x24),
            new HLEKeyboardMappingEntry(Key.Number8, 0x25),
            new HLEKeyboardMappingEntry(Key.Number9, 0x26),
            new HLEKeyboardMappingEntry(Key.Number0, 0x27),

            new HLEKeyboardMappingEntry(Key.Enter,        0x28),
            new HLEKeyboardMappingEntry(Key.Escape,       0x29),
            new HLEKeyboardMappingEntry(Key.BackSpace,    0x2A),
            new HLEKeyboardMappingEntry(Key.Tab,          0x2B),
            new HLEKeyboardMappingEntry(Key.Space,        0x2C),
            new HLEKeyboardMappingEntry(Key.Minus,        0x2D),
            new HLEKeyboardMappingEntry(Key.Plus,         0x2E),
            new HLEKeyboardMappingEntry(Key.BracketLeft,  0x2F),
            new HLEKeyboardMappingEntry(Key.BracketRight, 0x30),
            new HLEKeyboardMappingEntry(Key.BackSlash,    0x31),
            new HLEKeyboardMappingEntry(Key.Tilde,        0x32),
            new HLEKeyboardMappingEntry(Key.Semicolon,    0x33),
            new HLEKeyboardMappingEntry(Key.Quote,        0x34),
            new HLEKeyboardMappingEntry(Key.Grave,        0x35),
            new HLEKeyboardMappingEntry(Key.Comma,        0x36),
            new HLEKeyboardMappingEntry(Key.Period,       0x37),
            new HLEKeyboardMappingEntry(Key.Slash,        0x38),
            new HLEKeyboardMappingEntry(Key.CapsLock,     0x39),

            new HLEKeyboardMappingEntry(Key.F1,  0x3a),
            new HLEKeyboardMappingEntry(Key.F2,  0x3b),
            new HLEKeyboardMappingEntry(Key.F3,  0x3c),
            new HLEKeyboardMappingEntry(Key.F4,  0x3d),
            new HLEKeyboardMappingEntry(Key.F5,  0x3e),
            new HLEKeyboardMappingEntry(Key.F6,  0x3f),
            new HLEKeyboardMappingEntry(Key.F7,  0x40),
            new HLEKeyboardMappingEntry(Key.F8,  0x41),
            new HLEKeyboardMappingEntry(Key.F9,  0x42),
            new HLEKeyboardMappingEntry(Key.F10, 0x43),
            new HLEKeyboardMappingEntry(Key.F11, 0x44),
            new HLEKeyboardMappingEntry(Key.F12, 0x45),

            new HLEKeyboardMappingEntry(Key.PrintScreen, 0x46),
            new HLEKeyboardMappingEntry(Key.ScrollLock,  0x47),
            new HLEKeyboardMappingEntry(Key.Pause,       0x48),
            new HLEKeyboardMappingEntry(Key.Insert,      0x49),
            new HLEKeyboardMappingEntry(Key.Home,        0x4A),
            new HLEKeyboardMappingEntry(Key.PageUp,      0x4B),
            new HLEKeyboardMappingEntry(Key.Delete,      0x4C),
            new HLEKeyboardMappingEntry(Key.End,         0x4D),
            new HLEKeyboardMappingEntry(Key.PageDown,    0x4E),
            new HLEKeyboardMappingEntry(Key.Right,       0x4F),
            new HLEKeyboardMappingEntry(Key.Left,        0x50),
            new HLEKeyboardMappingEntry(Key.Down,        0x51),
            new HLEKeyboardMappingEntry(Key.Up,          0x52),

            new HLEKeyboardMappingEntry(Key.NumLock,        0x53),
            new HLEKeyboardMappingEntry(Key.KeypadDivide,   0x54),
            new HLEKeyboardMappingEntry(Key.KeypadMultiply, 0x55),
            new HLEKeyboardMappingEntry(Key.KeypadSubtract, 0x56),
            new HLEKeyboardMappingEntry(Key.KeypadAdd,      0x57),
            new HLEKeyboardMappingEntry(Key.KeypadEnter,    0x58),
            new HLEKeyboardMappingEntry(Key.Keypad1,        0x59),
            new HLEKeyboardMappingEntry(Key.Keypad2,        0x5A),
            new HLEKeyboardMappingEntry(Key.Keypad3,        0x5B),
            new HLEKeyboardMappingEntry(Key.Keypad4,        0x5C),
            new HLEKeyboardMappingEntry(Key.Keypad5,        0x5D),
            new HLEKeyboardMappingEntry(Key.Keypad6,        0x5E),
            new HLEKeyboardMappingEntry(Key.Keypad7,        0x5F),
            new HLEKeyboardMappingEntry(Key.Keypad8,        0x60),
            new HLEKeyboardMappingEntry(Key.Keypad9,        0x61),
            new HLEKeyboardMappingEntry(Key.Keypad0,        0x62),
            new HLEKeyboardMappingEntry(Key.KeypadDecimal,  0x63),

            new HLEKeyboardMappingEntry(Key.F13, 0x68),
            new HLEKeyboardMappingEntry(Key.F14, 0x69),
            new HLEKeyboardMappingEntry(Key.F15, 0x6A),
            new HLEKeyboardMappingEntry(Key.F16, 0x6B),
            new HLEKeyboardMappingEntry(Key.F17, 0x6C),
            new HLEKeyboardMappingEntry(Key.F18, 0x6D),
            new HLEKeyboardMappingEntry(Key.F19, 0x6E),
            new HLEKeyboardMappingEntry(Key.F20, 0x6F),
            new HLEKeyboardMappingEntry(Key.F21, 0x70),
            new HLEKeyboardMappingEntry(Key.F22, 0x71),
            new HLEKeyboardMappingEntry(Key.F23, 0x72),
            new HLEKeyboardMappingEntry(Key.F24, 0x73),

            new HLEKeyboardMappingEntry(Key.ControlLeft,  0xE0),
            new HLEKeyboardMappingEntry(Key.ShiftLeft,    0xE1),
            new HLEKeyboardMappingEntry(Key.AltLeft,      0xE2),
            new HLEKeyboardMappingEntry(Key.WinLeft,      0xE3),
            new HLEKeyboardMappingEntry(Key.ControlRight, 0xE4),
            new HLEKeyboardMappingEntry(Key.ShiftRight,   0xE5),
            new HLEKeyboardMappingEntry(Key.AltRight,     0xE6),
            new HLEKeyboardMappingEntry(Key.WinRight,     0xE7),
        };

        private static readonly HLEKeyboardMappingEntry[] KeyModifierMapping = new HLEKeyboardMappingEntry[]
        {
            new HLEKeyboardMappingEntry(Key.ControlLeft,  0),
            new HLEKeyboardMappingEntry(Key.ShiftLeft,    1),
            new HLEKeyboardMappingEntry(Key.AltLeft,      2),
            new HLEKeyboardMappingEntry(Key.WinLeft,      3),
            new HLEKeyboardMappingEntry(Key.ControlRight, 4),
            new HLEKeyboardMappingEntry(Key.ShiftRight,   5),
            new HLEKeyboardMappingEntry(Key.AltRight,     6),
            new HLEKeyboardMappingEntry(Key.WinRight,     7),
            new HLEKeyboardMappingEntry(Key.CapsLock,     8),
            new HLEKeyboardMappingEntry(Key.ScrollLock,   9),
            new HLEKeyboardMappingEntry(Key.NumLock,      10),
        };

        private bool _isValid;
        private string _id;

        private MotionInput _leftMotionInput;
        private MotionInput _rightMotionInput;

        private IGamepad _gamepad;
        private InputConfig _config;

        public IGamepadDriver GamepadDriver { get; private set; }
        public GamepadStateSnapshot State { get; private set; }

        public string Id => _id;

        private CemuHookClient _cemuHookClient;

        public NpadController(CemuHookClient cemuHookClient)
        {
            State = default;
            _id = null;
            _isValid = false;
            _cemuHookClient = cemuHookClient;
        }

        public bool UpdateDriverConfiguration(IGamepadDriver gamepadDriver, InputConfig config)
        {
            GamepadDriver = gamepadDriver;

            _gamepad?.Dispose();

            _id = config.Id;
            _gamepad = GamepadDriver.GetGamepad(_id);
            _isValid = _gamepad != null;

            UpdateUserConfiguration(config);

            return _isValid;
        }

        public void UpdateUserConfiguration(InputConfig config)
        {
            if (config is StandardControllerInputConfig controllerConfig)
            {
                bool needsMotionInputUpdate = _config == null || (_config is StandardControllerInputConfig oldControllerConfig &&
                                                                (oldControllerConfig.Motion.EnableMotion != controllerConfig.Motion.EnableMotion) &&
                                                                (oldControllerConfig.Motion.MotionBackend != controllerConfig.Motion.MotionBackend));

                if (needsMotionInputUpdate)
                {
                    UpdateMotionInput(controllerConfig.Motion);
                }
            }
            else
            {
                // Non-controller doesn't have motions.
                _leftMotionInput = null;
            }

            _config = config;

            if (_isValid)
            {
                _gamepad.SetConfiguration(config);
            }
        }

        private void UpdateMotionInput(MotionConfigController motionConfig)
        {
            if (motionConfig.MotionBackend != MotionInputBackendType.CemuHook)
            {
                _leftMotionInput = new MotionInput();
             }
            else
            {
                _leftMotionInput = null;
            }
        }

        public void Update()
        {
            if (_isValid && GamepadDriver != null)
            {
                State = _gamepad.GetMappedStateSnapshot();

                if (_config is StandardControllerInputConfig controllerConfig && controllerConfig.Motion.EnableMotion)
                {
                    if (controllerConfig.Motion.MotionBackend == MotionInputBackendType.GamepadDriver)
                    {
                        if (_gamepad.Features.HasFlag(GamepadFeaturesFlag.Motion))
                        {
                            Vector3 accelerometer = _gamepad.GetMotionData(MotionInputId.Accelerometer);
                            Vector3 gyroscope = _gamepad.GetMotionData(MotionInputId.Gyroscope);

                            accelerometer = new Vector3(accelerometer.X, -accelerometer.Z, accelerometer.Y);
                            gyroscope = new Vector3(gyroscope.X, -gyroscope.Z, gyroscope.Y);

                            _leftMotionInput.Update(accelerometer, gyroscope, (ulong)PerformanceCounter.ElapsedNanoseconds / 1000, controllerConfig.Motion.Sensitivity, (float)controllerConfig.Motion.GyroDeadzone);

                            if (controllerConfig.ControllerType == ConfigControllerType.JoyconPair)
                            {
                                _rightMotionInput = _leftMotionInput;
                            }
                        }
                    }
                    else if (controllerConfig.Motion.MotionBackend == MotionInputBackendType.CemuHook && controllerConfig.Motion is CemuHookMotionConfigController cemuControllerConfig)
                    {
                        int clientId = (int)controllerConfig.PlayerIndex;

                        // First of all ensure we are registered
                        _cemuHookClient.RegisterClient(clientId, cemuControllerConfig.DsuServerHost, cemuControllerConfig.DsuServerPort);

                        // Then request and retrieve the data
                        _cemuHookClient.RequestData(clientId, cemuControllerConfig.Slot);
                        _cemuHookClient.TryGetData(clientId, cemuControllerConfig.Slot, out _leftMotionInput);

                        if (controllerConfig.ControllerType == ConfigControllerType.JoyconPair)
                        {
                            if (!cemuControllerConfig.MirrorInput)
                            {
                                _cemuHookClient.RequestData(clientId, cemuControllerConfig.AltSlot);
                                _cemuHookClient.TryGetData(clientId, cemuControllerConfig.AltSlot, out _rightMotionInput);
                            }
                            else
                            {
                                _rightMotionInput = _leftMotionInput;
                            }
                        }
                    }
                }
            }
            else
            {
                // Reset states
                State = default;
                _leftMotionInput = null;
            }
        }

        public GamepadInput GetHLEInputState()
        {
            GamepadInput state = new GamepadInput();

            // First update all buttons
            foreach (HLEButtonMappingEntry entry in _hleButtonMapping)
            {
                if (State.IsPressed(entry.DriverInputId))
                {
                    state.Buttons |= entry.HLEInput;
                }
            }

            if (_gamepad is IKeyboard)
            {
                (float leftAxisX, float leftAxisY) = State.GetStick(StickInputId.Left);
                (float rightAxisX, float rightAxisY) = State.GetStick(StickInputId.Right);

                state.LStick = new JoystickPosition
                {
                    Dx = ClampAxis(leftAxisX),
                    Dy = ClampAxis(leftAxisY)
                };

                state.RStick = new JoystickPosition
                {
                    Dx = ClampAxis(rightAxisX),
                    Dy = ClampAxis(rightAxisY)
                };
            }
            else if (_config is StandardControllerInputConfig controllerConfig)
            {
                (float leftAxisX, float leftAxisY) = State.GetStick(StickInputId.Left);
                (float rightAxisX, float rightAxisY) = State.GetStick(StickInputId.Right);

                state.LStick = ClampToCircle(ApplyDeadzone(leftAxisX, leftAxisY, controllerConfig.DeadzoneLeft), controllerConfig.RangeLeft);
                state.RStick = ClampToCircle(ApplyDeadzone(rightAxisX, rightAxisY, controllerConfig.DeadzoneRight), controllerConfig.RangeRight);
            }

            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JoystickPosition ApplyDeadzone(float x, float y, float deadzone)
        {
            float magnitudeClamped = Math.Min(MathF.Sqrt(x * x + y * y), 1f);
            
            if (magnitudeClamped <= deadzone)
            {
                return new JoystickPosition() {Dx = 0, Dy = 0};
            }
            
            return new JoystickPosition()
            {
                Dx = ClampAxis((x / magnitudeClamped) * ((magnitudeClamped - deadzone) / (1 - deadzone))),
                Dy = ClampAxis((y / magnitudeClamped) * ((magnitudeClamped - deadzone) / (1 - deadzone)))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short ClampAxis(float value)
        {
            if (Math.Sign(value) < 0)
            {
                return (short)Math.Max(value * -short.MinValue, short.MinValue);
            }

            return (short)Math.Min(value * short.MaxValue, short.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JoystickPosition ClampToCircle(JoystickPosition position, float range)
        {
            Vector2 point = new Vector2(position.Dx, position.Dy) * range;

            if (point.Length() > short.MaxValue)
            {
                point = point / point.Length() * short.MaxValue;
            }

            return new JoystickPosition
            {
                Dx = (int)point.X,
                Dy = (int)point.Y
            };
        }

        public SixAxisInput GetHLEMotionState(bool isJoyconRightPair = false)
        {
            float[] orientationForHLE = new float[9];
            Vector3 gyroscope;
            Vector3 accelerometer;
            Vector3 rotation;

            MotionInput motionInput = _leftMotionInput;

            if (isJoyconRightPair)
            {
                if (_rightMotionInput == null)
                {
                    return default;
                }

                motionInput = _rightMotionInput;
            }

            if (motionInput != null)
            {
                gyroscope = Truncate(motionInput.Gyroscrope * 0.0027f, 3);
                accelerometer = Truncate(motionInput.Accelerometer, 3);
                rotation = Truncate(motionInput.Rotation * 0.0027f, 3);

                Matrix4x4 orientation = motionInput.GetOrientation();

                orientationForHLE[0] = Math.Clamp(orientation.M11, -1f, 1f);
                orientationForHLE[1] = Math.Clamp(orientation.M12, -1f, 1f);
                orientationForHLE[2] = Math.Clamp(orientation.M13, -1f, 1f);
                orientationForHLE[3] = Math.Clamp(orientation.M21, -1f, 1f);
                orientationForHLE[4] = Math.Clamp(orientation.M22, -1f, 1f);
                orientationForHLE[5] = Math.Clamp(orientation.M23, -1f, 1f);
                orientationForHLE[6] = Math.Clamp(orientation.M31, -1f, 1f);
                orientationForHLE[7] = Math.Clamp(orientation.M32, -1f, 1f);
                orientationForHLE[8] = Math.Clamp(orientation.M33, -1f, 1f);
            }
            else
            {
                gyroscope = new Vector3();
                accelerometer = new Vector3();
                rotation = new Vector3();
            }

            return new SixAxisInput()
            {
                Accelerometer = accelerometer,
                Gyroscope     = gyroscope,
                Rotation      = rotation,
                Orientation   = orientationForHLE
            };
        }

        private static Vector3 Truncate(Vector3 value, int decimals)
        {
            float power = MathF.Pow(10, decimals);

            value.X = float.IsNegative(value.X) ? MathF.Ceiling(value.X * power) / power : MathF.Floor(value.X * power) / power;
            value.Y = float.IsNegative(value.Y) ? MathF.Ceiling(value.Y * power) / power : MathF.Floor(value.Y * power) / power;
            value.Z = float.IsNegative(value.Z) ? MathF.Ceiling(value.Z * power) / power : MathF.Floor(value.Z * power) / power;

            return value;
        }

        public KeyboardInput? GetHLEKeyboardInput()
        {
            if (_gamepad is IKeyboard keyboard)
            {
                KeyboardStateSnapshot keyboardState = keyboard.GetKeyboardStateSnapshot();

                KeyboardInput hidKeyboard = new KeyboardInput
                {
                    Modifier = 0,
                    Keys = new ulong[0x4]
                };

                foreach (HLEKeyboardMappingEntry entry in KeyMapping)
                {
                    ulong value = keyboardState.IsPressed(entry.TargetKey) ? 1UL : 0UL;

                    hidKeyboard.Keys[entry.Target / 0x40] |= (value << (entry.Target % 0x40));
                }

                foreach (HLEKeyboardMappingEntry entry in KeyModifierMapping)
                {
                    int value = keyboardState.IsPressed(entry.TargetKey) ? 1 : 0;

                    hidKeyboard.Modifier |= value << entry.Target;
                }

                return hidKeyboard;
            }

            return null;
        }


        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gamepad?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void UpdateRumble(ConcurrentQueue<(VibrationValue, VibrationValue)> queue)
        {
            if (queue.TryDequeue(out (VibrationValue, VibrationValue) dualVibrationValue))
            {
                if (_config is StandardControllerInputConfig controllerConfig && controllerConfig.Rumble.EnableRumble)
                {
                    VibrationValue leftVibrationValue = dualVibrationValue.Item1;
                    VibrationValue rightVibrationValue = dualVibrationValue.Item2;

                    float low = Math.Min(1f, (float)((rightVibrationValue.AmplitudeLow * 0.85 + rightVibrationValue.AmplitudeHigh * 0.15) * controllerConfig.Rumble.StrongRumble));
                    float high = Math.Min(1f, (float)((leftVibrationValue.AmplitudeLow * 0.15 + leftVibrationValue.AmplitudeHigh * 0.85) * controllerConfig.Rumble.WeakRumble));

                    _gamepad.Rumble(low, high, uint.MaxValue);

                    Logger.Debug?.Print(LogClass.Hid, $"Effect for {controllerConfig.PlayerIndex} " +
                        $"L.low.amp={leftVibrationValue.AmplitudeLow}, " +
                        $"L.high.amp={leftVibrationValue.AmplitudeHigh}, " +
                        $"R.low.amp={rightVibrationValue.AmplitudeLow}, " +
                        $"R.high.amp={rightVibrationValue.AmplitudeHigh} " +
                        $"--> ({low}, {high})");
                }
            }
        }
    }
}
