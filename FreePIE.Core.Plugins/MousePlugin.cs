﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using FreePIE.Core.Contracts;
using SlimDX.DirectInput;

namespace FreePIE.Core.Plugins
{

    [LuaGlobalType(Type = typeof(MouseGlobal))]
    public class MousePlugin : Plugin
    {

        // These are copies of DirectX constants
        const int INPUT_MOUSE = 0;
        const int INPUT_KEYBOARD = 1;
        const int INPUT_HARDWARE = 2;
        const uint XBUTTON1 = 0x0001;
        const uint XBUTTON2 = 0x0002;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_XDOWN = 0x0080;
        const uint MOUSEEVENTF_XUP = 0x0100;
        const uint MOUSEEVENTF_WHEEL = 0x0800;
        const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(4)]
            public MOUSEINPUT mi;
            [FieldOffset(4)]
            public KEYBDINPUT ki;
            [FieldOffset(4)]
            public HARDWAREINPUT hi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);


        // Mouse position state variables
        int X = 0;
        int Y = 0;
        int DeltaXOut = 0;
        int DeltaYOut = 0;

        DirectInput DirectInputInstance = new DirectInput();
        Mouse MouseDevice;
        MouseState CurrentMouseState;

        //-----------------------------------------------------------------------
        public override object CreateGlobal()
        {
            return new MouseGlobal(this);
        }

        //-----------------------------------------------------------------------
        public override System.Action Start()
        {

            Process process = Process.GetCurrentProcess();
            IntPtr handle = process.MainWindowHandle;
            if (handle == null)
                return null;
            // TODO: I should create a hidden window if the main window is unavailable (tray icon)

            MouseDevice = new Mouse(DirectInputInstance);
            if (MouseDevice == null)
                return null;  // fail
            // TODO: Investigate why I should use the newer RawInput model versus DirectInput
            MouseDevice.SetCooperativeLevel(handle, CooperativeLevel.Background | CooperativeLevel.Nonexclusive);
            MouseDevice.Properties.AxisMode = DeviceAxisMode.Relative;   // Get delta values

            try
            {
                MouseDevice.Acquire();
            }
            catch (Exception err)
            {
                //System.Console.WriteLine(err);
                return null; // fail
            }

            OnStarted(this, new EventArgs());
            return null;
        }

        //-----------------------------------------------------------------------
        public override void Stop()
        {
            if (MouseDevice != null)
            {
                MouseDevice.Unacquire();
                MouseDevice.Dispose();
                MouseDevice = null;
            }

            if (DirectInputInstance != null)
            {
                DirectInputInstance.Dispose();
                DirectInputInstance = null;
            }
        }

        public override string FriendlyName
        {
            get { return "Mouse"; }
        }

        MOUSEINPUT MouseInput(int x, int y, uint data, uint t, uint flag)
        {
            MOUSEINPUT mi = new MOUSEINPUT();
            mi.dx = x;
            mi.dy = y;
            mi.mouseData = data;
            mi.time = t;
            mi.dwFlags = flag;
            return mi;
        }

        public override void DoBeforeNextExecute()
        {

            // If a mouse command was given in the script, issue it all at once right here
            if ((DeltaXOut != 0) || (DeltaYOut != 0))
            {

                INPUT[] input = new INPUT[1];
                input[0].type = INPUT_MOUSE;
                input[0].mi = MouseInput(DeltaXOut, DeltaYOut, 0, 0, MOUSEEVENTF_MOVE);

                SendInput(1, input, Marshal.SizeOf(input[0].GetType()));

                // Reset the mouse values
                DeltaXOut = 0;
                DeltaYOut = 0;
            }
            else if ((X != 0) || (Y != 0))
            {
                INPUT[] input = new INPUT[1];
                input[0].type = INPUT_MOUSE;
                input[0].mi = MouseInput(X, Y, 0, 0, MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE);

                SendInput(1, input, Marshal.SizeOf(input[0].GetType()));

                // Reset the mouse values
                X = 0;
                Y = 0;
            }

            CurrentMouseState = null;  // flush the mouse state
        }

        //-----------------------------------------------------------------------
        public int DeltaX
        {
            set
            {
                DeltaXOut = value;
            }

            get
            {
                // Retrieve the mouse state only once per iteration to avoid getting
                // zeros on subsequent calls
                if (CurrentMouseState == null)
                    CurrentMouseState = MouseDevice.GetCurrentState();

                return CurrentMouseState.X;
            }
        }

        //-----------------------------------------------------------------------
        public int DeltaY
        {
            set
            {
                DeltaYOut = value;
            }

            get
            {
                // Retrieve the mouse state only once per iteration to avoid getting
                // zeros on subsequent calls
                if (CurrentMouseState == null)
                    CurrentMouseState = MouseDevice.GetCurrentState();

                return CurrentMouseState.Y;
            }
        }

        //-----------------------------------------------------------------------
        public bool IsButtonPressed(int index)
        {

            // Retrieve the mouse state only once per iteration to avoid getting
            // zeros on subsequent calls
            if (CurrentMouseState == null)
                CurrentMouseState = MouseDevice.GetCurrentState();

            return CurrentMouseState.IsPressed(index);
        }
    }

    //==========================================================================
    [LuaGlobal(Name = "mouse")]
    public class MouseGlobal : UpdateblePluginGlobal
    {

        private readonly MousePlugin Mouse;

        //-----------------------------------------------------------------------
        public MouseGlobal(MousePlugin plugin)
            : base(plugin)
        {
            Mouse = plugin;
        }

        //-----------------------------------------------------------------------
        public void setDeltaX(double x)
        {
            Mouse.DeltaX = (int)Math.Round(x);
        }

        //-----------------------------------------------------------------------
        public void setDeltaY(double y)
        {
            Mouse.DeltaY = (int)Math.Round(y);
        }

        //-----------------------------------------------------------------------
        public double getDeltaX()
        {
            return Mouse.DeltaX;
        }

        //-----------------------------------------------------------------------
        public double getDeltaY()
        {
            return Mouse.DeltaY;
        }

        //-----------------------------------------------------------------------
        public bool getLeftButton()
        {
            return Mouse.IsButtonPressed(0);
        }

        //-----------------------------------------------------------------------
        public bool getMiddleButton()
        {
            return Mouse.IsButtonPressed(2);
        }

        //-----------------------------------------------------------------------
        public bool getRightButton()
        {
            return Mouse.IsButtonPressed(1);
        }
    }
}
