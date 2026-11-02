using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexRelay {
    internal static class OverlayZOrder {
        delegate bool EnumCallback(IntPtr window,IntPtr state);
        [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumCallback callback,IntPtr state);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
        internal static bool IsTopMost(IntPtr window){return (GetWindowLongPtr(window,-20).ToInt64()&8)!=0;}
        static bool OnDesktop(IntPtr window){int cloaked;return DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked==0;}
        internal static bool CoveredByNormalWindow(IntPtr window) {
            Rect bounds;if(!GetWindowRect(window,out bounds))return false;
            bool found=false,covered=false;
            // A native TOPMOST bit can remain set while the actual Z order is wrong.
            // Inspect the order as well; leave other topmost tools and system popups alone.
            EnumWindows((other,state)=>{
                if(other==window){found=true;return false;}
                if(covered||!IsWindowVisible(other)||IsIconic(other)||IsTopMost(other))return true;
                Rect area;
                if(GetWindowRect(other,out area)&&area.Left<bounds.Right&&area.Right>bounds.Left&&area.Top<bounds.Bottom&&area.Bottom>bounds.Top&&OnDesktop(other))covered=true;
                return true;
            },IntPtr.Zero);
            return found&&covered;
        }
        internal static bool Repair(Form overlay) {
            if(overlay.IsDisposed||!overlay.IsHandleCreated||!overlay.Visible)return false;
            return RepairWindow(overlay.Handle);
        }
        internal static bool RepairWindow(IntPtr window) {
            if(!IsWindowVisible(window)||IsIconic(window)||!OnDesktop(window))return false;
            if(IsTopMost(window)&&!CoveredByNormalWindow(window))return false;
            // Preserve focus, size, position and the hidden taskbar owner's Z order.
            // Do not toggle TopMost off/on or show an overlay the user has hidden.
            return SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0200);
        }
    }
}
