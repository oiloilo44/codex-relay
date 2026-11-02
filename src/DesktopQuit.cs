using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodexRelay {
    internal interface IQuitWindow {
        bool IsTarget(IntPtr window,int processId);
        bool Activate(IntPtr window);
        bool IsForeground(IntPtr window);
        bool KeysReleased();
        bool SendQuit();
    }
    internal static class DesktopQuit {
        internal static async Task<bool> RequestAsync(IQuitWindow native,IntPtr window,int processId,CancellationToken ct,Action<string,string> log,Func<int,CancellationToken,Task> delay=null) {
            delay=delay??Task.Delay;
            Action<string,string> record=log??((kind,detail)=>{});
            var diagnostic=native as QuitWindow;
            record("QUIT_TARGET",diagnostic==null?"pid="+processId:diagnostic.Describe(window));
            // Let the Relay confirmation dialog finish closing before moving focus.
            await delay(250,ct);
            for(int attempt=0;attempt<20;attempt++) {
                ct.ThrowIfCancellationRequested();
                if(window==IntPtr.Zero || !native.IsTarget(window,processId)){record("QUIT_TARGET_UNAVAILABLE","pid="+processId);return false;}
                if(!native.KeysReleased()){if(attempt==0)record("QUIT_WAIT_KEYS",diagnostic==null?"keys held":diagnostic.Describe(window));await delay(100,ct);continue;}
                bool activated=native.Activate(window);
                record("QUIT_ACTIVATE","attempt="+(attempt+1)+"; accepted="+activated+(diagnostic==null?"":"; "+diagnostic.Describe(window)));
                await delay(150,ct);
                if(!native.IsTarget(window,processId)){record("QUIT_TARGET_CHANGED","pid="+processId);return false;}
                if(!native.IsForeground(window)||!native.KeysReleased())continue;
                ct.ThrowIfCancellationRequested();
                bool sent=native.SendQuit();
                record(sent?"QUIT_INPUT_SENT":"QUIT_INPUT_FAILED",diagnostic==null?"sent="+sent:"input_count="+diagnostic.LastInputCount+"/4; win32_error="+diagnostic.LastInputError+"; "+diagnostic.Describe(window));
                // Input is never repeated after SendInput: a late response may still arrive.
                return sent;
            }
            record("QUIT_FOCUS_NOT_READY",diagnostic==null?"foreground or held keys":diagnostic.Describe(window));return false;
        }
        // Ctrl+Q is the app's File > Quit command. WM_CLOSE only closes a window.
        internal static bool Request(IQuitWindow native,IntPtr window,int processId) {
            if(window==IntPtr.Zero || !native.IsTarget(window,processId) || !native.KeysReleased()) return false;
            if(!native.Activate(window)) return false;
            // Never send a global shortcut when activation failed or focus changed.
            if(!native.IsTarget(window,processId) || !native.IsForeground(window) || !native.KeysReleased()) return false;
            return native.SendQuit();
        }
    }
    internal sealed class QuitWindow : IQuitWindow {
        readonly Func<bool> stillVerified;
        public QuitWindow(Func<bool> validate=null){stillVerified=validate;}
        internal uint LastInputCount;
        internal int LastInputError;
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint Type; public INPUTDATA Data; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTDATA {
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
            [FieldOffset(0)] public MOUSEINPUT Mouse;
        }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
        public bool IsTarget(IntPtr window,int processId) {
            uint owner; return IsWindow(window)&&IsWindowVisible(window)&&IsWindowEnabled(window)&&GetWindowThreadProcessId(window,out owner)!=0&&owner==(uint)processId&&(stillVerified==null||stillVerified());
        }
        public bool Activate(IntPtr window) { if(IsIconic(window))ShowWindowAsync(window,9); return SetForegroundWindow(window); }
        public bool IsForeground(IntPtr window) { return GetForegroundWindow()==window; }
        public bool KeysReleased() {
            foreach(int key in new[]{0x10,0x11,0x12,0x5B,0x5C,0x51,0x0D,0x20,0x01,0x02}) if((GetAsyncKeyState(key)&0x8000)!=0)return false;
            return true;
        }
        internal string Describe(IntPtr window) {
            uint owner,foregroundOwner;GetWindowThreadProcessId(window,out owner);var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out foregroundOwner);
            return "target_hwnd=0x"+window.ToInt64().ToString("X")+"; target_pid="+owner+"; foreground_hwnd=0x"+foreground.ToInt64().ToString("X")+"; foreground_pid="+foregroundOwner+"; visible="+IsWindowVisible(window)+"; enabled="+IsWindowEnabled(window)+"; minimized="+IsIconic(window)+"; keys_released="+KeysReleased();
        }
        internal static int State(IntPtr window,int processId) {
            uint owner;if(!IsWindow(window)||GetWindowThreadProcessId(window,out owner)==0||owner!=(uint)processId||!IsWindowVisible(window))return 0;
            return IsWindowEnabled(window)?1:2;
        }
        static INPUT Key(ushort key,bool up) { return new INPUT{Type=1,Data=new INPUTDATA{Keyboard=new KEYBDINPUT{Key=key,Flags=up?2u:0u}}}; }
        public bool SendQuit() {
            var keys=new[]{Key(0x11,false),Key(0x51,false),Key(0x51,true),Key(0x11,true)};
            uint sent=SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(INPUT)));
            LastInputCount=sent;LastInputError=sent==keys.Length?0:Marshal.GetLastWin32Error();
            if(sent>0&&sent<keys.Length) SendInput(2,new[]{Key(0x51,true),Key(0x11,true)},Marshal.SizeOf(typeof(INPUT)));
            return sent==keys.Length;
        }
    }
}
