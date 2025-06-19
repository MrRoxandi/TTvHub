﻿using System.Runtime.InteropServices;
using static TTvHub.Core.BackEnds.HardwareWrapper.NativeInputs;

namespace TTvHub.Core.BackEnds.HardwareWrapper
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern short GetKeyState(ushort virtualKeyCode);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BlockInput(bool fBlockIt);
        [DllImport("user32.dll")]
        public static extern nint GetMessageExtraInfo();
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }
}
