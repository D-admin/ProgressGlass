// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
namespace ProgressGlass {
    public sealed class PetPerchSprites : IDisposable {
        readonly Bitmap sheet;readonly Rectangle[] crops=new Rectangle[6];
        Bitmap canvas;
        public PetPerchSprites(string path){
            sheet=new Bitmap(path);
            // Generated rows can have unequal gutters; find the transparent gap instead of
            // cutting the duck at the exact mathematical halfway point.
            int split=sheet.Height/2,best=Int32.MaxValue;
            for(int y=sheet.Height*4/10;y<sheet.Height*6/10;y++){
                int count=0;for(int x=0;x<sheet.Width;x++)if(sheet.GetPixel(x,y).A>40)count++;
                if(count<best || (count==best&&Math.Abs(y-sheet.Height/2)<Math.Abs(split-sheet.Height/2))){best=count;split=y;}
            }
            for(int i=0;i<6;i++){
                int x0=sheet.Width*(i%3)/3,x1=sheet.Width*(i%3+1)/3,y0=i<3?0:split,y1=i<3?split:sheet.Height;
                int l=x1,r=x0,t=y1,b=y0;
                for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)if(sheet.GetPixel(x,y).A>40){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
                if(l>=r||t>=b)throw new InvalidDataException("依附动画帧缺少角色");crops[i]=Rectangle.FromLTRB(l,t,r+1,b+1);
            }
        }
        public Bitmap Render(Size size,string side,int expression,double elapsed,bool gentle){
            if(canvas==null||canvas.Size!=size){if(canvas!=null)canvas.Dispose();canvas=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);}
            int face=expression==1||expression==2||expression==9?1:expression==6||expression==7||expression==8?2:0;
            int index=side=="bottom"?face:face+3;Rectangle crop=crops[index],visible=PetDocking.Visible(size,side);
            using(var g=Graphics.FromImage(canvas)){
                g.Clear(Color.Transparent);g.SetClip(visible);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                float factor=Math.Min((visible.Width-4f)/crop.Width,(visible.Height-4f)/crop.Height);
                float w=crop.Width*factor,h=crop.Height*factor;
                float x=side=="bottom"?(size.Width-w)/2:visible.Right-w-1;
                float y=side=="bottom"?visible.Bottom-h-1:(size.Height-h)/2;
                if(side=="left"){g.TranslateTransform(size.Width,0);g.ScaleTransform(-1,1);x=PetDocking.Visible(size,"right").Right-w-1;}
                // Keep hands at the ledge while the upper body breathes very gently.
                if(!gentle){float breath=(float)(1+.004*Math.Sin(elapsed/920));float anchor=side=="bottom"?visible.Bottom-1:size.Height/2f;g.TranslateTransform(0,anchor);g.ScaleTransform(1,breath);g.TranslateTransform(0,-anchor);}
                g.DrawImage(sheet,new RectangleF(x,y,w,h),crop,GraphicsUnit.Pixel);
            }
            return canvas;
        }
        public void Dispose(){if(canvas!=null)canvas.Dispose();sheet.Dispose();}
        public static void Preview(string root,string output){
            using(var art=new PetPerchSprites(Path.Combine(root,"assets","mint-girl-perch.png")))
            using(var bitmap=new Bitmap(800,380))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",11)){
                g.Clear(Color.FromArgb(228,236,240));
                using(var bg=new SolidBrush(Color.FromArgb(248,250,252)))g.FillRectangle(bg,180,36,440,236);
                using(var pen=new Pen(Color.FromArgb(130,152,167)))g.DrawRectangle(pen,180,36,440,236);
                using(var bar=new SolidBrush(Color.FromArgb(35,47,61)))g.FillRectangle(bar,0,340,800,40);
                var size=new Size(128,128);var screen=new Rectangle(0,0,800,340);var window=new Rectangle(180,36,440,236);
                var p=PetDocking.Place(new Point(650,0),size,screen,"bottom");g.DrawImageUnscaled(art.Render(size,"bottom",0,0,true),p);
                p=PetDocking.Place(new Point(0,80),size,screen,"left");g.DrawImageUnscaled(art.Render(size,"left",6,0,true),p);
                p=PetDocking.Place(new Point(0,100),size,screen,"right");g.DrawImageUnscaled(art.Render(size,"right",0,0,true),p);
                p=PetDocking.Place(new Point(400,0),size,window,"bottom");g.DrawImageUnscaled(art.Render(size,"bottom",6,0,true),p);
                g.DrawString("窗口底边",font,Brushes.DimGray,215,235);g.DrawString("任务栏",font,Brushes.White,24,349);
                g.DrawString("屏幕左边",font,Brushes.DimGray,22,224);g.DrawString("屏幕右边",font,Brushes.DimGray,687,242);
                bitmap.Save(output,ImageFormat.Png);
            }
        }
    }
}
