// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
namespace ProgressGlass {
    // Decorative local animation, independent of session state and completion metrics.
    public sealed class PetMotion {
        public int pose;
        public string action="idle";
        public float tilt,lift,scaleX=1,scaleY=1,sparkle;
    }
    public sealed class PetAnimation {
        readonly Random random;
        double lastTime=-1,nextBlink,blinkAt=-10000,nextIdle,actionAt=-10000,duration,lean;
        bool doubleBlink;string action="idle";int gaze;
        public PetAnimation() : this(Environment.TickCount) {}
        public PetAnimation(int seed) {random=new Random(seed);nextBlink=2200+random.Next(1400);nextIdle=7000+random.Next(4000);}
        void Begin(string name,double now,double length) {action=name;actionAt=now;duration=length;}
        public void Greet(double now) {if(action!="delight" || now-actionAt>=duration)Begin("greeting",now,1600);}
        public void Click(double now) {Begin("delight",now,1900);}
        public void Land(double now) {Begin("settle",now,700);}
        public PetMotion Sample(double now,double cursorX,bool nearby,bool dragging,bool gentle) {
            now=Math.Max(0,now);double dt=lastTime<0?0:Math.Max(0,Math.Min(100,now-lastTime));lastTime=now;
            double target=nearby?Math.Max(-1,Math.Min(1,cursorX)):0;
            lean+=(target-lean)*(1-Math.Exp(-dt/180));
            if(!nearby)gaze=0;else if(target<-.35)gaze=3;else if(target>.35)gaze=4;else if(Math.Abs(target)<.18)gaze=0;
            if(now>=nextBlink){blinkAt=now;doubleBlink=random.Next(5)==0;nextBlink=now+2800+random.Next(3400);}
            if(action!="idle" && now-actionAt>=duration){action="idle";nextIdle=now+8500+random.Next(8000);}
            if(action=="idle" && now>=nextIdle && !nearby && !dragging && !gentle){
                int c=random.Next(4);Begin(c==0?"curious":c==1?"yawn":c==2?"wink":"happy",now,c==1?2200:1700);
            }
            var m=new PetMotion{pose=gaze,action=action};double age=now-actionAt;
            if(action=="greeting")m.pose=age<420?8:age<640?9:age<1020?8:age<1260?9:0;
            else if(action=="delight")m.pose=age<430?6:age<1080?7:age<1610?8:0;
            else if(action=="curious")m.pose=10;
            else if(action=="yawn")m.pose=age<300||age>1900?1:11;
            else if(action=="wink")m.pose=age<950?6:0;
            else if(action=="happy")m.pose=age<1100?7:0;
            else if(action=="settle")m.pose=age<260?5:0;
            if(m.pose==0 || m.pose==3 || m.pose==4){
                double b=now-blinkAt;if(doubleBlink && b>=310)b-=310;
                if(b>=0 && b<65)m.pose=1;else if(b>=65 && b<155)m.pose=2;else if(b>=155 && b<225)m.pose=1;
            }
            if(!gentle){
                double breath=Math.Sin(now/920);
                m.scaleY=(float)(1+breath*.008);m.scaleX=(float)(1-breath*.0035);
                m.tilt=(float)(Math.Sin(now/1550)*.8+lean*1.35);m.lift=(float)(.55+.55*Math.Sin(now/1150));
                if(action=="greeting")m.tilt+=(float)(Math.Sin(age/160)*1.1*Math.Sin(Math.PI*Math.Min(1,age/duration)));
                if(action=="delight"){
                    m.lift+=(float)(5.2*Math.Abs(Math.Sin(Math.PI*age/610))*Math.Max(0,1-age/1800));
                    m.sparkle=(float)Math.Max(0,Math.Sin(Math.PI*age/1900));
                }
                if(action=="curious")m.tilt+=(float)(1.1*Math.Sin(Math.PI*age/duration));
                if(action=="settle"){double spring=Math.Sin(age/65)*Math.Exp(-age/180);m.scaleY-=(float)(.035*spring);m.scaleX+=(float)(.018*spring);}
            }
            if(dragging){m.pose=5;m.action="dragging";m.lift=0;m.sparkle=0;if(!gentle)m.tilt=(float)(lean*3);}
            return m;
        }
    }
}
