// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
namespace ProgressGlass {
    public sealed class PetSprites : IDisposable {
        public const int PoseCount=12;
        readonly Bitmap sheet;
        readonly Rectangle[] cells=new Rectangle[PoseCount];
        readonly PointF[] feet=new PointF[PoseCount];
        readonly int sourceHeight;
        Bitmap[] cache;Bitmap canvas;Size size;
        public static readonly string[] Names={"微笑","半眨眼","闭眼","看左边","看右边","惊讶","眨单眼","开心笑","招手","笑着挥手","好奇","哈欠"};
        public PetSprites(string path) {
            sheet=new Bitmap(path);int maxHeight=0;
            for(int i=0;i<PoseCount;i++) {
                int col=i%3,row=i/3;
                cells[i]=Rectangle.FromLTRB(sheet.Width*col/3,sheet.Height*row/4,sheet.Width*(col+1)/3,sheet.Height*(row+1)/4);
                var cell=cells[i];int top=cell.Bottom,bottom=cell.Top,left=cell.Right,right=cell.Left;
                for(int y=cell.Top;y<cell.Bottom;y++)for(int x=cell.Left;x<cell.Right;x++)if(sheet.GetPixel(x,y).A>40){top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
                if(bottom<=top)throw new InvalidDataException("动画帧缺少可见角色: "+i);
                // Anchor at the shoes so expressions do not move the character across the desktop.
                for(int y=bottom-(bottom-top)/14;y<=bottom;y++)for(int x=cell.Left;x<cell.Right;x++)if(sheet.GetPixel(x,y).A>40){left=Math.Min(left,x);right=Math.Max(right,x);}
                feet[i]=new PointF((left+right)/2f-cell.Left,bottom-cell.Top);maxHeight=Math.Max(maxHeight,bottom-top+1);
            }
            sourceHeight=maxHeight;
        }
        void ClearCache(){if(cache!=null){foreach(var f in cache)if(f!=null)f.Dispose();cache=null;}if(canvas!=null){canvas.Dispose();canvas=null;}}
        public Bitmap Frame(Size desired,int pose) {
            if(cache==null || size!=desired) {
                ClearCache();size=desired;cache=new Bitmap[PoseCount];float factor=(size.Height*.89f)/sourceHeight;
                for(int i=0;i<PoseCount;i++) {
                    var bitmap=new Bitmap(size.Width*2,size.Height*2,PixelFormat.Format32bppPArgb);var cell=cells[i];
                    using(var g=Graphics.FromImage(bitmap)) {
                        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        var dest=new RectangleF((size.Width/2f-feet[i].X*factor)*2,(size.Height-4-feet[i].Y*factor)*2,cell.Width*factor*2,cell.Height*factor*2);
                        g.DrawImage(sheet,dest,cell,GraphicsUnit.Pixel);
                    }
                    for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).A<=8)bitmap.SetPixel(x,y,Color.Transparent);
                    cache[i]=bitmap;
                }
                canvas=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
            }
            return cache[Math.Max(0,Math.Min(PoseCount-1,pose))];
        }
        // Reused output surface: draw synchronously; ownership stays with PetSprites.
        public Bitmap Render(Size desired,PetMotion motion) {
            var source=Frame(desired,motion.pose);
            using(var g=Graphics.FromImage(canvas)) {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                float anchor=desired.Height-4;
                g.TranslateTransform(desired.Width/2f,anchor-motion.lift*desired.Height/128f);
                g.RotateTransform(motion.tilt);g.ScaleTransform(motion.scaleX,motion.scaleY);g.TranslateTransform(-desired.Width/2f,-anchor);
                g.DrawImage(source,new Rectangle(0,0,desired.Width,desired.Height));g.ResetTransform();
                if(motion.sparkle>0) {
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    using(var pen=new Pen(Color.FromArgb((int)(180*motion.sparkle),255,191,207),Math.Max(1,desired.Width/100f))) {
                        float r=desired.Width*.025f*motion.sparkle;
                        foreach(var p in new[]{new PointF(desired.Width*.14f,desired.Height*.25f),new PointF(desired.Width*.86f,desired.Height*.17f)}) {
                            g.DrawLine(pen,p.X-r,p.Y,p.X+r,p.Y);g.DrawLine(pen,p.X,p.Y-r,p.X,p.Y+r);
                        }
                    }
                }
            }
            return canvas;
        }
        public void Dispose(){ClearCache();sheet.Dispose();}
        public static void RenderPreview(string root,string output) {
            using(var sprites=new PetSprites(Path.Combine(root,"assets","mint-girl-expressions.png")))
            using(var bitmap=new Bitmap(760,560))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",10)) {
                g.Clear(Color.FromArgb(25,35,46));
                for(int i=0;i<PoseCount;i++){
                    int x=20+(i%4)*186,y=8+(i/4)*182;
                    g.DrawImageUnscaled(sprites.Render(new Size(160,160),new PetMotion{pose=i}),x,y);g.DrawString(Names[i],font,Brushes.White,x+48,y+158);
                }
                bitmap.Save(output,ImageFormat.Png);
            }
        }
        public static void RenderAnimation(string root,string directory) {
            Directory.CreateDirectory(directory);
            using(var sprites=new PetSprites(Path.Combine(root,"assets","mint-girl-expressions.png"))) {
                var animation=new PetAnimation(7);
                for(int i=0;i<210;i++) {
                    double now=i*40;if(i==25)animation.Greet(now);if(i==80)animation.Click(now);if(i==167)animation.Land(now);
                    var motion=animation.Sample(now,i<25?-.85:i<65?.85:0,i<145,i>=145 && i<167,false);
                    sprites.Render(new Size(160,160),motion).Save(Path.Combine(directory,i.ToString("D3")+".png"),ImageFormat.Png);
                }
            }
        }
    }
}
