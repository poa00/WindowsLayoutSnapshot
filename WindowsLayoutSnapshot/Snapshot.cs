using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static WindowsLayoutSnapshot.Native;

namespace WindowsLayoutSnapshot
{
    internal class Snapshot
    {
        private readonly Dictionary<IntPtr, WinInfo> _infos = new Dictionary<IntPtr, WinInfo>();
        private List<IntPtr> _windowsBackToTop = new List<IntPtr>();

        private Snapshot(bool userInitiated)
        {
#if DEBUG
            Debug.WriteLine("*** NEW SNAPSHOT ***");
#endif
            EnumWindows(EvalWindow, 0);

            TimeTaken = DateTime.UtcNow;
            UserInitiated = userInitiated;

            var pixels = new List<long>();
            foreach (var screen in Screen.AllScreens)
                pixels.Add(screen.Bounds.Width * screen.Bounds.Height);
            MonitorPixelCounts = pixels.ToArray();
            NumMonitors = pixels.Count;
        }

        internal DateTime TimeTaken { get; }
        internal bool UserInitiated { get; }
        internal long[] MonitorPixelCounts { get; }
        internal int NumMonitors { get; }

        internal TimeSpan Age => DateTime.UtcNow.Subtract(TimeTaken);

        internal static Snapshot TakeSnapshot(bool userInitiated)
        {
            return new Snapshot(userInitiated);
        }

        private bool EvalWindow(int hwndInt, int lParam)
        {
            var hwnd = new IntPtr(hwndInt);

            if (!IsAltTabWindow(hwnd))
                return true;

            // EnumWindows returns windows in Z order from back to front
            _windowsBackToTop.Add(hwnd);

            var win = GetWindowInfo(hwnd);
            _infos.Add(hwnd, win);

#if DEBUG
            // For debugging purpose, output window title with handle
            int textLength = 256;
            System.Text.StringBuilder outText = new System.Text.StringBuilder(textLength + 1);
            int a = GetWindowText(hwnd, outText, outText.Capacity);
            Debug.WriteLine(hwnd + " " + win.position + " " + outText);
#endif

            return true;
        }

        public string GetDisplayString()
        {
            var dt = TimeTaken.ToLocalTime();
            return dt.ToString("M") + ", " + dt.ToString("T");
        }

        internal void RestoreAndPreserveMenu(object sender, EventArgs e)
        {
            // ignore extra params
            // We save and restore the current foreground window because it's our tray menu
            // I couldn't find a way to get this handle straight from the tray menu's properties;
            //   the ContextMenuStrip.Handle isn't the right one, so I'm using win32
            // More info RE the restore is below, where we do it
            var currentForegroundWindow = GetForegroundWindow();

            try
            {
                Restore(sender, e);
            }
            finally
            {
                // A combination of SetForegroundWindow + SetWindowPos (via set_Visible) seems to be needed
                // This was determined by trying a bunch of stuff
                // This prevents the tray menu from closing, and makes sure it's still on top
                SetForegroundWindow(currentForegroundWindow);
                TrayIconForm.Cms.Visible = true;
            }
        }

        internal void Restore(object sender, EventArgs e)
        {
            // ignore extra params
            // first, restore the window rectangles and normal/maximized/minimized states
            foreach (var placement in _infos)
            {
                // this might error out if the window no longer exists
                var win = placement.Value;

                // make sure window will be inside a monitor
                var newpos = GetRectInsideNearestMonitor(win);

                if (!SetWindowPos(placement.Key, 0, newpos.Left, newpos.Top, newpos.Width, newpos.Height,
                    0x0004 /*NOZORDER*/))
                    Debug.WriteLine("Can't move window " + placement.Key + ": " + GetLastError());
            }

            // now update the z-orders
            _windowsBackToTop = _windowsBackToTop.FindAll(IsWindowVisible);
            var positionStructure = BeginDeferWindowPos(_windowsBackToTop.Count);
            for (var i = 0; i < _windowsBackToTop.Count; i++)
                positionStructure = DeferWindowPos(positionStructure, _windowsBackToTop[i],
                    i == 0 ? IntPtr.Zero : _windowsBackToTop[i - 1],
                    0, 0, 0, 0,
                    DeferWindowPosCommands.SWP_NOMOVE | DeferWindowPosCommands.SWP_NOSIZE |
                    DeferWindowPosCommands.SWP_NOACTIVATE);
            EndDeferWindowPos(positionStructure);
        }

        private static Rectangle GetRectInsideNearestMonitor(WinInfo win)
        {
            var real = win.Position;
            var rect = win.Visible;
            var monitorRect = Screen.GetWorkingArea(rect); // use workspace coordinates
            var y = new Rectangle(
                Math.Max(monitorRect.Left, Math.Min(monitorRect.Right - rect.Width, rect.Left)),
                Math.Max(monitorRect.Top, Math.Min(monitorRect.Bottom - rect.Height, rect.Top)),
                Math.Min(monitorRect.Width, rect.Width),
                Math.Min(monitorRect.Height, rect.Height)
            );
            if (rect != real) // support different real and visible position
                y = new Rectangle(
                    y.Left - rect.Left + real.Left,
                    y.Top - rect.Top + real.Top,
                    y.Width - rect.Width + real.Width,
                    y.Height - rect.Height + real.Height
                );
#if DEBUG
            if (y != real)
                Debug.WriteLine("Moving " + real + "→" + y + " in monitor " + monitorRect);
#endif
            return y;
        }

        private static bool IsAltTabWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd))
                return false;

            var extendedStyles = GetWindowLongPtr(hwnd, -20); // GWL_EXSTYLE
            if ((extendedStyles.ToInt64() & WS_EX_APPWINDOW) > 0)
                return true;
            if ((extendedStyles.ToInt64() & WS_EX_TOOLWINDOW) > 0)
                return false;

            var hwndTry = GetAncestor(hwnd, GetAncestor_Flags.GetRootOwner);
            var hwndWalk = IntPtr.Zero;
            while (hwndTry != hwndWalk)
            {
                hwndWalk = hwndTry;
                hwndTry = GetLastActivePopup(hwndWalk);
                if (IsWindowVisible(hwndTry))
                    break;
            }

            return hwndWalk == hwnd;
        }

        private static WinInfo GetWindowInfo(IntPtr hwnd)
        {
            var win = new WinInfo();
            RECT pos;
            if (!GetWindowRect(hwnd, out pos))
                throw new Exception("Error getting window rectangle");
            win.Position = win.Visible = pos.ToRectangle();
            if (Environment.OSVersion.Version.Major >= 6)
                if (DwmGetWindowAttribute(hwnd, 9 /*DwmwaExtendedFrameBounds*/, out pos,
                        Marshal.SizeOf(typeof(RECT))) == 0)
                    win.Visible = pos.ToRectangle();
            return win;
        }

        private class WinInfo
        {
            public Rectangle Position; // real window border, we use this to move it
            public Rectangle Visible; // visible window borders, we use this to force inside a screen
        }
    }
}