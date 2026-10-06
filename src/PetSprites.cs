// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ProgressGlass {
    // Four generated poses. Motion is decorative and never represents task completion.
    public sealed class PetSprites : IDisposable {
        readonly Bitmap sheet;
        readonly Rectangle[] cells=new Rectangle[4];
        readonly PointF[] feet=new PointF[4];
        readonly int sourceHeight;
        Bitmap[,] cache;
        Size size;
        public PetSprites(string path) {
            sheet=new Bitmap(path);int maxHeight=0;
            for(int i=0;i<4;i++) {
                int col=i%2,row=i/2;
                cells[i]=Rectangle.FromLTRB(sheet.Width*col/2,sheet.Height*row/2,sheet.Width*(col+1)/2,sheet.Height*(row+1)/2);
                var cell=cells[i];int top=cell.Bottom,bottom=cell.Top,left=cell.Right,right=cell.Left;
                for(int y=cell.Top;y<cell.Bottom;y++)for(int x=cell.Left;x<cell.Right;x++)if(sheet.GetPixel(x,y).A>40){top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
                if(bottom<=top)throw new InvalidDataException("动画帧缺少可见角色");
                // Anchor at the shoes, keeping generated cells from wobbling sideways.
                for(int y=bottom-(bottom-top)/12;y<=bottom;y++)for(int x=cell.Left;x<cell.Right;x++)if(sheet.GetPixel(x,y).A>40){left=Math.Min(left,x);right=Math.Max(right,x);}
                feet[i]=new PointF((left+right)/2f-cell.Left,bottom-cell.Top);
                maxHeight=Math.Max(maxHeight,bottom-top+1);
            }
            sourceHeight=maxHeight;
        }
        public static int Pose(double elapsedMs,bool greeting) {
            if(greeting)return 2+((int)(elapsedMs/260)%2);
            double t=elapsedMs%8000;
            if(t>=2200 && t<2450)return 1;
            if(t>=5000 && t<6300)return 2+((int)((t-5000)/260)%2);
            return 0;
        }
        public static int Sway(double elapsedMs) {return (int)Math.Round(1+Math.Sin(elapsedMs/750));}
        void ClearCache(){if(cache!=null){foreach(var frame in cache)if(frame!=null)frame.Dispose();cache=null;}}
        public Bitmap Frame(Size desired,int pose,int sway) {
            if(cache==null || size!=desired) {
                ClearCache();size=desired;cache=new Bitmap[4,3];
                float factor=(size.Height-8f)/sourceHeight;
                for(int i=0;i<4;i++)for(int shift=0;shift<3;shift++) {
                    var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);var cell=cells[i];
                    using(var g=Graphics.FromImage(bitmap)) {
                        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        var dest=new RectangleF(size.Width/2f-feet[i].X*factor,size.Height-5-feet[i].Y*factor-shift,cell.Width*factor,cell.Height*factor);
                        g.DrawImage(sheet,dest,cell,GraphicsUnit.Pixel);
                    }
                    for(int y=0;y<size.Height;y++)for(int x=0;x<size.Width;x++)if(bitmap.GetPixel(x,y).A<=8)bitmap.SetPixel(x,y,Color.Transparent);
                    cache[i,shift]=bitmap;
                }
            }
            return cache[Math.Max(0,Math.Min(3,pose)),Math.Max(0,Math.Min(2,sway))];
        }
        public void Dispose(){ClearCache();sheet.Dispose();}
        public static void RenderPreview(string root,string output) {
            using(var sprites=new PetSprites(Path.Combine(root,"assets","mint-girl-sprites.png")))
            using(var bitmap=new Bitmap(640,292))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",10)) {
                g.Clear(Color.FromArgb(25,35,46));
                string[] labels={"日常","眨眼","挥手 1","挥手 2"};
                for(int i=0;i<4;i++){g.DrawImageUnscaled(sprites.Frame(new Size(128,128),i,0),16+i*156,12);g.DrawString(labels[i],font,Brushes.White,52+i*156,148);}
                g.DrawString("托盘头像 · 实际像素尺寸",font,Brushes.White,20,181);
                int x=24;foreach(int n in new[]{16,20,24,32,40,48,64}){using(var icon=new Icon(Path.Combine(root,"assets","mint-girl.ico"),n,n)){g.DrawIcon(icon,new Rectangle(x,211,n,n));}x+=n+25;}
                bitmap.Save(output,ImageFormat.Png);
            }
        }
    }
}
