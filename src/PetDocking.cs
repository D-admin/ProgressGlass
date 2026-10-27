// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
namespace ProgressGlass {
    public static class PetDocking {
        public static string Normalize(string side){return side=="bottom"||side=="left"||side=="right"?side:"none";}
        public static Rectangle Visible(Size size,string side){
            int w=(int)Math.Round(size.Width*.68),h=(int)Math.Round(size.Height*.68);
            if(side=="bottom")return new Rectangle(0,0,size.Width,h);
            if(side=="left")return new Rectangle(size.Width-w,0,w,size.Height);
            if(side=="right")return new Rectangle(0,0,w,size.Height);
            return new Rectangle(Point.Empty,size);
        }
        public static Point Place(Point desired,Size size,Rectangle surface,string side){
            var visible=Visible(size,side);var p=PetLayout.Clamp(desired,size,surface);
            if(side=="bottom")p.Y=surface.Bottom-visible.Height;
            if(side=="left")p.X=surface.Left-visible.Left;
            if(side=="right")p.X=surface.Right-visible.Width;
            return p;
        }
        public static string Snap(Rectangle pet,Rectangle surface,int threshold){
            int bottom=Math.Abs(pet.Bottom-surface.Bottom),left=Math.Abs(pet.Left-surface.Left),right=Math.Abs(pet.Right-surface.Right);
            int min=Math.Min(bottom,Math.Min(left,right));if(min>threshold)return "none";
            return min==bottom?"bottom":min==left?"left":"right";
        }
    }
    internal static class DockWindows {
        [StructLayout(LayoutKind.Sequential)] struct WinRect{public int left,top,right,bottom;}
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out WinRect r);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int max);
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out WinRect r,int size);
        public static uint Owner(IntPtr h){uint id;GetWindowThreadProcessId(h,out id);return id;}
        public static bool Bounds(IntPtr h,uint owner,out Rectangle rect){
            rect=Rectangle.Empty;if(h==IntPtr.Zero||!IsWindowVisible(h)||IsIconic(h)||Owner(h)!=owner)return false;
            WinRect r;if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf(typeof(WinRect)))!=0 && !GetWindowRect(h,out r))return false;
            rect=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);return rect.Width>=160&&rect.Height>=100;
        }
        public static IntPtr Candidate(int ownProcess){
            IntPtr h=GetForegroundWindow();uint owner=Owner(h);if(owner==ownProcess)return IntPtr.Zero;
            var name=new StringBuilder(128);GetClassName(h,name,128);
            if(name.ToString()=="Progman"||name.ToString()=="WorkerW"||name.ToString().StartsWith("Shell_"))return IntPtr.Zero;
            Rectangle r;return Bounds(h,owner,out r)?h:IntPtr.Zero;
        }
    }
}
