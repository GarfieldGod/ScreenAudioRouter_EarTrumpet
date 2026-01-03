using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace EarTrumpet.Interop.Helpers
{
    public class MouseHook
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseLLHookStruct
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public int dwExtraInfo;
        }

        public delegate int MouseWheelHandler(object sender, MouseEventArgs e);
        public delegate void MouseLeftButtonDownHandler(object sender, MouseEventArgs e);
        public delegate void MouseLeftButtonUpHandler(object sender, MouseEventArgs e);

        public event MouseWheelHandler MouseWheelEvent;
        public event MouseLeftButtonDownHandler MouseLeftButtonDown;
        public event MouseLeftButtonUpHandler MouseLeftButtonUp;

        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WH_MOUSE_LL = 14;
        private User32.HookProc _hProc;
        private int _hHook;
        private bool _hookIsSet = false;

        public void SetHook()
        {
            if (!_hookIsSet)
            {
                _hProc = new User32.HookProc(MouseHookProc);
                _hHook = User32.SetWindowsHookEx(WH_MOUSE_LL, _hProc, IntPtr.Zero, 0);
                _hookIsSet = true;
            }
        }

        public void UnHook()
        {
            if (_hookIsSet)
            {
                User32.UnhookWindowsHookEx(_hHook);
                _hookIsSet = false;
            }
        }

        private int MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return User32.CallNextHookEx(_hHook, nCode, wParam, lParam);
            }
            MouseLLHookStruct MyMouseHookStruct = (MouseLLHookStruct)Marshal.PtrToStructure(lParam, typeof(MouseLLHookStruct));
            MouseEventArgs mouseArgs = new MouseEventArgs(MouseButtons.Left, 1, MyMouseHookStruct.pt.x, MyMouseHookStruct.pt.y, MyMouseHookStruct.mouseData >> 16);

            if ((int)wParam == WM_LBUTTONDOWN && MouseLeftButtonDown != null)
            {
                MouseLeftButtonDown(this, mouseArgs);
            }

            if ((int)wParam == WM_LBUTTONUP && MouseLeftButtonUp != null)
            {
                MouseLeftButtonUp(this, mouseArgs);
            }

            if ((int)wParam == WM_MOUSEWHEEL && MouseWheelEvent != null)
            {
                int result = MouseWheelEvent(this, mouseArgs);
                if (result != 0)
                {
                    return result;
                }
            }

            return User32.CallNextHookEx(_hHook, nCode, wParam, lParam);
        }
    }
}
