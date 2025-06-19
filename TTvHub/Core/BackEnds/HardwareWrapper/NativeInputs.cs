﻿using System.Runtime.InteropServices;

namespace TTvHub.Core.BackEnds.HardwareWrapper
{
    internal static class NativeInputs
    {
        internal struct Input
        {
            public uint Type;
            public MouseKeybdHardwareInput Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct MouseKeybdHardwareInput
        {
            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public KeybdInput Keyboard;

            [FieldOffset(0)]
            public HardwareInput Hardware;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeybdInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HardwareInput
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [Flags]
        internal enum KeyboardFlag : uint // UInt32
        {
            ExtendedKey = 0x0001,
            KeyUp = 0x0002,
            Unicode = 0x0004,
            ScanCode = 0x0008,
        }
        
        [Flags]
        internal enum MouseFlag : uint // UInt32
        {
            Move = 0x0001,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            XDown = 0x0080,
            XUp = 0x0100,
            VerticalWheel = 0x0800,
            HorizontalWheel = 0x1000,
            VirtualDesk = 0x4000,
            Absolute = 0x8000,
        }

        internal enum XButton : uint
        {
            XButton1 = 0x0001,
            XButton2 = 0x0002,
        }
        
        [Flags]
        internal enum InputType : uint // UInt32
        {
            Mouse = 0,
            Keyboard = 1,
            Hardware = 2,
        }

        internal enum KeyCode : byte
        {
            /// --- Numbers line ---
            D0 = 0x30, D1 = 0x31, D2 = 0x32,
            D3 = 0x33, D4 = 0x34, D5 = 0x35,
            D6 = 0x36, D7 = 0x37, D8 = 0x38,
            D9 = 0x39,
            // --- Alphabet ---
            A = 0x41, B = 0x42, C = 0x43,
            D = 0x44, E = 0x45, F = 0x46,
            G = 0x47, H = 0x48, I = 0x49,
            J = 0x4A, K = 0x4B, L = 0x4C,
            M = 0x4D, N = 0x4E, O = 0x4F,
            P = 0x50, Q = 0x51, R = 0x52,
            S = 0x53, T = 0x54, U = 0x55,
            V = 0x56, W = 0x57, X = 0x58,
            Y = 0x59, Z = 0x5A,
            // --- Numpad ---
            NumLock = 0x90, NumPad0 = 0x60,
            NumPad1 = 0x61, NumPad2 = 0x62,
            NumPad3 = 0x63, NumPad4 = 0x64,
            NumPad5 = 0x65, NumPad6 = 0x66,
            NumPad7 = 0x67, NumPad8 = 0x68,
            NumPad9 = 0x69, Multiply = 0x6A,
            Separator = 0x6C, Add = 0x6B,
            Subtract = 0x6D, Decimal = 0x6E,
            Divide = 0x6F,
            // --- Function keys ---
            F1 = 0x70, F2 = 0x71, F3 = 0x72,
            F4 = 0x73, F5 = 0x74, F6 = 0x75,
            F7 = 0x76, F8 = 0x77, F9 = 0x78,
            F10 = 0x79, F11 = 0x7A, F12 = 0x7B,
            F13 = 0x7C, F14 = 0x7D, F15 = 0x7E,
            F16 = 0x7F, F17 = 0x80, F18 = 0x81,
            F19 = 0x82, F20 = 0x83, F21 = 0x84,
            F22 = 0x85, F23 = 0x86, F24 = 0x87,

            // --- Shift keys ---
            Shift = 0x10, RShiftKey = 0xA1, LShiftKey = 0xA0,

            // --- Alt keys ---
            Alt = 0x12, LAlt = 0xA4, RAlt = 0xA5,

            // --- Control keys ---
            Control = 0x11, LControl = 0xA2, RControl = 0xA3,

            // --- Arrow keys ---
            Up = 0x26, Right = 0x27, Left = 0x25, Down = 0x28,

            // --- Extra buttons ---
            LWin = 0x5B, RWin = 0x5C,
            Backspace = 0x08, Tab = 0x09,
            LineFeed = 0x0A, Clear = 0x0C,
            Enter = 0x0D, Pause = 0x13,
            CapsLock = 0x14, Escape = 0x1B,
            Space = 0x20, PageUp = 0x21,
            PageDown = 0x22, End = 0x23,
            Home = 0x24, PrintScreen = 0x2C,
            Insert = 0x2D, Delete = 0x2E,
            Scroll = 0x91, Sleep = 0x5F,

        }

        internal enum MouseButton
        {
            LeftButton,
            MiddleButton,
            RightButton
        }


    }
}
