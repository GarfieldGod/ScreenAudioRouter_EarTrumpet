using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EarTrumpet.Interop;
using EarTrumpet.Interop.Helpers;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.WindowsAudio;
using System.IO;
using System.Drawing;

namespace EarTrumpet.ScreenRouter
{
    public class DragInfo
    {
        public IntPtr Hwnd { get; set; } = IntPtr.Zero;
        public string StartScreen { get; set; } = null;

        public void Reset()
        {
            Hwnd = IntPtr.Zero;
            StartScreen = null;
        }

        public bool Valid()
        {
            return Hwnd != IntPtr.Zero && !string.IsNullOrEmpty(StartScreen);
        }
    }

    public class ScreenListener
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] char[] lpFilename, uint nSize);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private const string EXE_SUFFIX = ".exe";
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        readonly private IAudioDeviceManager _auidoDeviceManager;

        private MouseHook _leftButtonMouseHook;
        private DragInfo _dragInfo { get; set; }
        private AppSettings _settings;

        public ScreenListener(IAudioDeviceManager auidoDeviceManager, AppSettings settings)
        {
            if (auidoDeviceManager == null) return;

            _auidoDeviceManager = auidoDeviceManager;
            _dragInfo = new DragInfo();
            _leftButtonMouseHook = null;

            _settings = settings;
            _settings.EnableScreenAudioRoutingChanged += OnEnableScreenAudioRoutingChanged;
        }

        public void OnEnableScreenAudioRoutingChanged(object sender, bool enableScreenAudioRouting) {
            if (enableScreenAudioRouting)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (_auidoDeviceManager == null) return;

            if (_leftButtonMouseHook == null)
            {
                _leftButtonMouseHook = new MouseHook();
                _leftButtonMouseHook.MouseLeftButtonDown += OnMouseLeftDown;
                _leftButtonMouseHook.MouseLeftButtonUp += OnMouseLeftUp;
            }

            try
            {
                _leftButtonMouseHook.SetHook();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set mouse hook: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_leftButtonMouseHook != null)
            {
                _leftButtonMouseHook.MouseLeftButtonDown -= OnMouseLeftDown;
                _leftButtonMouseHook.MouseLeftButtonUp -= OnMouseLeftUp;

                _leftButtonMouseHook.UnHook();
                _leftButtonMouseHook = null;
            }
        }

        private void OnMouseLeftDown(object sender, MouseEventArgs e)
        {
            POINT point = new POINT(e.X, e.Y);
            IntPtr hwnd = WindowFromPoint(point);

            string startScreen = GetScreenNameByHwnd(hwnd, true);

            if (hwnd != IntPtr.Zero && IsWindow(hwnd) && startScreen != null)
            {
                _dragInfo.Hwnd = hwnd;
                _dragInfo.StartScreen = startScreen;
            }
            else
            {
                _dragInfo.Reset();
            }
        }

        private void OnMouseLeftUp(object sender, MouseEventArgs e)
        {
            if (_dragInfo.Valid())
            {
                IntPtr hwnd = _dragInfo.Hwnd;
                string startScreen = _dragInfo.StartScreen;

                ThreadPool.QueueUserWorkItem(state =>
                {
                    AsyncHandleAudioSwitch(hwnd, startScreen);
                });

                _dragInfo.Reset();
            }
        }

        private void AsyncHandleAudioSwitch(IntPtr hwnd, string startScreen)
        {
            try
            {
                string endScreen = GetScreenNameByHwnd(hwnd, true);

                if (endScreen != null && !string.Equals(startScreen, endScreen, StringComparison.OrdinalIgnoreCase))
                {
                    if (_settings.ScreenAudioDeviceMap.TryGetValue(endScreen, out string targetDeviceName) && !string.IsNullOrEmpty(targetDeviceName))
                    {
                        var targetDevice = 
                            _auidoDeviceManager.Devices.FirstOrDefault(d => string.Equals(d.DisplayName, targetDeviceName, StringComparison.OrdinalIgnoreCase)) ??
                            _auidoDeviceManager.Devices.FirstOrDefault(d => (d.DisplayName ?? "").IndexOf(targetDeviceName, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (targetDevice == null)
                        {
                            Console.WriteLine($"Can not find target device: {targetDeviceName}");
                            return;
                        }

                        var windowsAudio = (IAudioDeviceManagerWindowsAudio)_auidoDeviceManager;

                        string AppName = GetAppNameByHwnd(hwnd);
                        var processes = Process.GetProcessesByName(AppName);
                        foreach (var process in processes)
                        {
                            try
                            {
                                string deviceId = windowsAudio.GetDefaultEndPoint(process.Id);
                                if (deviceId != targetDevice.Id) {
                                    Console.WriteLine($"Try switch {AppName} to {targetDevice}");
                                    windowsAudio.SetDefaultEndPoint(targetDevice.Id, process.Id);
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"Failed to switch {AppName} PID: {process?.Id} error: {ex}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Automatic switch audio device failed: {ex.Message}");
            }
        }

        public static string GetScreenNameByHwnd(IntPtr hwnd, bool windowMidAsPos = true)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return null;

            GetWindowRect(hwnd, out RECT windowRect);

            int windowX, windowY;
            if (windowMidAsPos)
            {
                int width = Math.Abs(windowRect.Right - windowRect.Left);
                int height = Math.Abs(windowRect.Bottom - windowRect.Top);
                windowX = windowRect.Left + width / 2;
                windowY = windowRect.Top + height / 2;
            }
            else
            {
                windowX = windowRect.Left;
                windowY = windowRect.Top;
            }

            return GetScreenNameByPos(windowX, windowY);
        }

        public static string GetScreenNameByPos(int x, int y)
        {
            var targetScreen = Screen.AllScreens.FirstOrDefault(
                monitor => monitor.Bounds.Contains(new Point(x, y)));

            return targetScreen?.DeviceName;
        }

        public static string GetAppNameByHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                Console.WriteLine("Invalid windows handle");
                return null;
            }

            IntPtr processHandle = IntPtr.Zero;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);

                processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
                if (processHandle == IntPtr.Zero)
                {
                    Console.WriteLine("Can't open process handle");
                    return null;
                }

                char[] buffer = new char[1024];
                var result = GetModuleFileNameEx(processHandle, IntPtr.Zero, buffer, (uint)buffer.Length);
                if (result == 0)
                {
                    Console.WriteLine($"GetModuleFileNameEx failed for PID: {processId}");
                    return null;
                }

                string exeFullPath = new string(buffer).TrimEnd('\0');
                string exeName = Path.GetFileName(exeFullPath);
                if (exeName.EndsWith(EXE_SUFFIX, StringComparison.OrdinalIgnoreCase))
                {
                    exeName = exeName.Substring(0, exeName.Length - EXE_SUFFIX.Length);
                }
                return exeName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get exe name failed: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
        }
    }
}
