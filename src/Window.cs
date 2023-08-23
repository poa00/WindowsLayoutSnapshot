using System;
using System.Runtime.InteropServices;

namespace WindowsLayoutSnapshot
{
    // Open source
    // Imported by: Adam Smith
    // Imported on: 8/9/2012
    // Imported from: http://www.codeproject.com/Articles/2286/Window-Hiding-with-C
    // License: CPOL (liberal)
    // Modifications: cleanup

    public class Window
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWDEFAULT = 10;

        private bool _visible = true;
        private bool _wasMax;

        public Window(string title, IntPtr hWnd, string process)
        {
            this.Title = title;
            this.HWnd = hWnd;
            this.Process = process;
        }

        public IntPtr HWnd { get; }

        public string Title { get; }

        public string Process { get; }

        public bool Visible
        {
            get => _visible;
            set
            {
                //show the window
                if (value)
                {
                    if (_wasMax)
                    {
                        if (ShowWindowAsync(HWnd, SW_SHOWMAXIMIZED))
                            _visible = true;
                    }
                    else
                    {
                        if (ShowWindowAsync(HWnd, SW_SHOWNORMAL))
                            _visible = true;
                    }
                }
                else
                {
                    _wasMax = IsZoomed(HWnd);
                    if (ShowWindowAsync(HWnd, SW_HIDE))
                        _visible = false;
                }
            }
        }

        public void Activate()
        {
            if (HWnd == GetForegroundWindow()) return;

            var threadId1 = GetWindowThreadProcessId(GetForegroundWindow(),
                IntPtr.Zero);
            var threadId2 = GetWindowThreadProcessId(HWnd, IntPtr.Zero);

            if (threadId1 != threadId2)
            {
                AttachThreadInput(threadId1, threadId2, 1);
                SetForegroundWindow(HWnd);
                AttachThreadInput(threadId1, threadId2, 0);
            }
            else
            {
                SetForegroundWindow(HWnd);
            }

            ShowWindowAsync(HWnd, IsIconic(HWnd) ? SW_RESTORE : SW_SHOWNORMAL);
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        private static extern IntPtr AttachThreadInput(IntPtr idAttach, IntPtr idAttachTo, int fAttach);
    }
}