using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
using System.Web.Script.Serialization;
using ProgressGlass;
class Tests {
    static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
    static readonly DateTimeOffset T=DateTimeOffset.Parse("2026-10-06T01:00:00Z");
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
    static void Invalid(string s,string name){bool rejected=false;try{Board.Parse(s);}catch{rejected=true;}Check(rejected,name);}
    static object Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
    static void Invoke(object obj,string name,params object[] args){obj.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);}
    static string Event(int sec,string outer,object payload){return Json.Serialize(new {timestamp=T.AddSeconds(sec).ToString("o"),type=outer,payload=payload});}
    static void Apply(ActivityState state,int sec,string outer,object payload){state.Apply(Json.Deserialize<Dictionary<string,object>>(Event(sec,outer,payload)));}
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetLong(IntPtr h,int index);
    [STAThread]static int Main(string[] args){try{
        Application.EnableVisualStyles(); string dir=args[0];
        string task="{\"id\":\"a\",\"title\":\"测试\",\"status\":\"doing\"}";
        string record="{\"project\":\"测试项目\",\"current\":\"正在处理\",\"updatedAt\":\"2026-10-06T01:00:00Z\",\"tasks\":["+task+"]}";
        string metric="\"measurement\":{\"completed\":240,\"total\":1000,\"unit\":\"条\",\"source\":\"worker-count.json\",\"observedAt\":\"2026-10-06T01:00:00Z\"}";
        string measured=record.Replace("\"status\":\"doing\"","\"status\":\"doing\","+metric);
        Check(Board.Parse(record).Percent==-1,"unknown project never fabricates a percentage");
        Check(!Board.Parse(record).tasks[0].HasProgress,"unknown task is unquantified");
        Check(!Board.Parse(record.Replace("\"status\":\"doing\"","\"status\":\"doing\",\"progress\":75")).tasks[0].HasProgress,"legacy manual percentage is ignored");
        Check(!Board.Parse(record.Replace("\"status\":\"doing\"","\"status\":\"doing\",\"completedUnits\":1,\"totalUnits\":3")).tasks[0].HasProgress,"one of three milestones is not 33 percent");
        Check(Board.Parse(measured).tasks[0].Percent==24,"240 of 1000 actual units gives 24 percent");
        Check(Board.Parse(measured).Percent==-1,"task metric is never generalized into project completion");
        Check(Board.Parse(record.Replace("\"current\":",metric+",\"current\":")).Percent==24,"explicit project metric has its own scope");
        string done=record.Replace("\"status\":\"doing\"","\"status\":\"done\",\"evidence\":\"已验证\"");
        Check(Board.Parse(done).tasks[0].Percent==100 && Board.Parse(done).Percent==-1,"verified task completion does not invent project completion");
        Invalid(record.Replace("doing","done"),"done without evidence rejected");
        Invalid(measured.Replace("240","1001"),"counts over total rejected");
        Invalid(measured.Replace("1000","0"),"zero denominator rejected");
        Invalid(measured.Replace("worker-count.json",""),"measurement without source rejected");
        Invalid(measured.Replace("条",""),"measurement without unit rejected");
        Invalid(measured.Replace("doing","done"),"incomplete measured task cannot be verified complete");
        var state=new ActivityState();
        Apply(state,0,"event_msg",new{type="task_started",turn_id="t1"});
        Check(state.Snapshot(T.AddSeconds(5)).state=="activity","recent start proves recent activity only");
        Apply(state,10,"response_item",new{type="custom_tool_call",call_id="c1",name="exec"});
        Check(state.Snapshot(T.AddSeconds(12)).state=="tool_wait","pending call is waiting for return");
        Apply(state,11,"token_usage_record",new{usage=999});
        Check(state.LastEvent==T.AddSeconds(10),"billing metadata does not refresh activity clock");
        Apply(state,20,"response_item",new{type="custom_tool_call_output",call_id="c1",output="private content never used"});
        Check(state.Pending.Count==0 && state.Snapshot(T.AddSeconds(21)).state=="activity","tool return clears matching pending call");
        Check(state.Snapshot(T.AddSeconds(55)).state=="waiting","silence over 30 seconds becomes uncertain waiting");
        Check(state.Snapshot(T.AddSeconds(150)).state=="quiet","120 second silence is not declared a crash");
        Apply(state,160,"event_msg",new{type="task_complete",turn_id="other"});
        Check(state.Phase=="running","other turn completion ignored");
        Apply(state,161,"event_msg",new{type="task_complete",turn_id="t1"});
        Check(state.Snapshot(T.AddSeconds(999)).state=="idle","explicit completion stays recorded completion");
        Apply(state,162,"event_msg",new{type="task_started",turn_id="t2"});
        Apply(state,163,"event_msg",new{type="approval_requested",turn_id="t2"});
        Check(state.Snapshot(T.AddSeconds(164)).state=="approval","explicit approval request recognized");
        Apply(state,165,"event_msg",new{type="approval_resolved",turn_id="t2"});
        Apply(state,166,"event_msg",new{type="stream_error",turn_id="t2"});
        Check(state.Snapshot(T.AddSeconds(167)).state=="error","explicit stream error recognized without inventing cause");
        Apply(state,168,"event_msg",new{type="turn_aborted",turn_id="t2"});
        Check(state.Snapshot(T.AddSeconds(169)).state=="interrupted","explicit abort recognized");
        Apply(state,1,"event_msg",new{type="task_started",turn_id="old"});
        Check(state.Phase=="interrupted","out of order old event ignored");
        var missing=new ActivityState(); Apply(missing,1,"response_item",new{type="reasoning"});
        Check(missing.Snapshot(T.AddSeconds(2)).state=="unknown","reasoning without lifecycle does not prove an active turn");
        string log=Path.Combine(dir,"test-session.jsonl");
        string head=Json.Serialize(new{type="session_meta",payload=new{id="fixture"}})+"\n";
        File.WriteAllText(log,head+Event(0,"event_msg",new{type="task_started",turn_id="t"})+"\n");
        var cfg=new MonitorConfig{sessionPath=log,sessionId="fixture"}; var observer=new SessionObserver();
        Check(observer.Read(cfg,T.AddSeconds(1),false).state=="activity","observer reads bound session");
        string line=Event(2,"response_item",new{type="reasoning"});
        File.AppendAllText(log,line.Substring(0,line.Length/2));
        Check(observer.Read(cfg,T.AddSeconds(3),false).lastEvent==T.ToString("o"),"incomplete appended line is not an event");
        File.AppendAllText(log,line.Substring(line.Length/2)+"\n");
        Check(observer.Read(cfg,T.AddSeconds(4),false).lastEvent==T.AddSeconds(2).ToString("o"),"completed appended line becomes visible");
        Check(new SessionObserver().Read(new MonitorConfig{sessionPath=log,sessionId="wrong"},T,false).state=="source_error","wrong session binding rejected");
        File.WriteAllText(log,head+Event(5,"event_msg",new{type="task_complete",turn_id="t"})+"\n");
        Check(observer.Read(cfg,T.AddSeconds(6),false).state=="idle","truncated log resets reader");
        File.AppendAllText(log,"broken-json\n");
        Check(observer.Read(cfg,T.AddSeconds(7),false).state=="source_error","malformed complete record downgrades certainty");
        Check(new SessionObserver().Read(new MonitorConfig{sessionPath=log+"missing",sessionId="fixture"},T,false).state=="source_error","missing source is not misreported as idle");
        string file=Path.Combine(dir,"live-test.json"); Overlay.AtomicWrite(file,measured);
        using(var f=new Overlay(file,true)){
            var handle=f.Handle;
            Check(((Board)Field(f,"board")).tasks[0].Percent==24,"overlay loads measured progress");
            Overlay.AtomicWrite(file,measured.Replace("240","350"));Invoke(f,"ReadBoard");
            Check(((Board)Field(f,"board")).tasks[0].Percent==35,"overlay refreshes actual count changes");
            Overlay.AtomicWrite(file,"{unfinished");Invoke(f,"ReadBoard");
            Check(((Board)Field(f,"board")).tasks[0].Percent==35 && (string)Field(f,"error")!="","partial write preserves last good count");
            Overlay.AtomicWrite(file,measured);Invoke(f,"ReadBoard");Check((string)Field(f,"error")=="","valid update recovers after error");
            Invoke(f,"SetLocked",true);Check((GetLong(handle,-20).ToInt64()&0x20)!=0,"click through enabled");
            Invoke(f,"SetLocked",false);Check((GetLong(handle,-20).ToInt64()&0x20)==0,"click through reversible");
            f.Render(Path.Combine(dir,"test-render.png"));Check(File.Exists(Path.Combine(dir,"test-render.png")),"render export");
        }
        string home=Path.Combine(dir,"fixture-home"),sessions=Path.Combine(home,"sessions");Directory.CreateDirectory(sessions);
        string first=Path.Combine(sessions,"a.jsonl"),second=Path.Combine(sessions,"b.jsonl"),guardian=Path.Combine(sessions,"guardian.jsonl");
        File.WriteAllText(first,Json.Serialize(new{type="session_meta",payload=new{id="alpha",cwd="D:\\Alpha",thread_source="user",source="vscode"}})+"\n"+Event(1,"event_msg",new{type="task_started",turn_id="a"})+"\n"+Event(2,"response_item",new{type="message",role="user",content=new string('x',5*1024*1024)})+"\n"+Event(3,"response_item",new{type="reasoning"})+"\n");
        File.WriteAllText(second,Json.Serialize(new{type="session_meta",payload=new{id="beta",cwd="D:\\Beta",thread_source="user",source="vscode"}})+"\n"+Event(1,"event_msg",new{type="task_started",turn_id="b"})+"\n");
        File.WriteAllText(guardian,Json.Serialize(new{type="session_meta",payload=new{id="reviewer",cwd="D:\\Alpha",thread_source="guardian_review",source=new{subagent=new{other="guardian"}}}})+"\n"+Event(1,"event_msg",new{type="task_started"})+"\n");
        File.WriteAllText(Path.Combine(home,"session_index.jsonl"),Json.Serialize(new{id="alpha",thread_name="Alpha task"})+"\n"+Json.Serialize(new{id="beta",thread_name="Beta task"})+"\n");
        var all=new AllChatsObserver(home,dir,file);var dashboard=all.Read(T.AddSeconds(6));
        Check(dashboard.discovered==2 && dashboard.chats.Count==2 && dashboard.chats[0].title=="Alpha task","all projects found with titles; internal guardian excluded");
        Check(dashboard.chats[0].activity.phase=="running","turn start over 4 MB back remains discoverable");
        File.AppendAllText(first,Event(7,"event_msg",new{type="task_complete",turn_id="a"})+"\n");
        dashboard=all.Read(T.AddSeconds(8));Check(dashboard.chats.Count==1 && dashboard.chats[0].id=="beta","completed chat removed on next incremental read");
        File.AppendAllText(second,Event(9,"event_msg",new{type="turn_aborted",turn_id="b"})+"\n");
        Check(all.Read(T.AddSeconds(10)).chats.Count==0,"interrupted chat removed from active list");
        File.AppendAllText(first,Event(11,"event_msg",new{type="task_started",turn_id="new"})+"\n");
        dashboard=all.Read(T.AddSeconds(200));Check(dashboard.chats.Count==1 && dashboard.chats[0].activity.state=="quiet","stale open turn kept with uncertain status, never declared live computation");
        var quota=new ActivityState();Apply(quota,1,"event_msg",new{type="task_started"});
        Apply(quota,20,"event_msg",new{type="token_count",rate_limits=new{limit_id="codex",primary=new{used_percent=47,window_minutes=10080,resets_at=1791602867},secondary=(object)null}});
        Check(quota.LastEvent==T.AddSeconds(1) && quota.Usage.windows[0].Remaining==53,"real quota extracted without refreshing activity timestamp");
        Check(quota.Usage.Freshness(T.AddMinutes(10)).Contains("较早快照"),"old quota clearly labelled as snapshot");
        Check(!new UsageSnapshot{observedAt=T.ToString("o"),windows=new List<UsageWindow>{new UsageWindow{usedPercent=-5,windowDurationMins=300,resetsAt=1791602867}}}.Valid,"invalid quota is rejected instead of showing impossible remaining value");
        Check(ConnectionProbe.HttpLabel(403).Contains("403") && !ConnectionProbe.HttpLabel(403).Contains("正常"),"403 proves endpoint response but does not claim healthy generation");
        var probe=new NetworkSnapshot();ConnectionProbe.Classify(probe,200,"text/plain; charset=UTF-8","User-agent: *\nDisallow: /private",false);
        Check(probe.verifiedReachable && probe.title.Contains("通过"),"public plain-text resource confirms host reachability");
        ConnectionProbe.Classify(probe,200,"text/html","<html>Login required</html>",false);
        Check(!probe.verifiedReachable,"HTTP 200 captive portal or login HTML is not treated as successful probe");
        ConnectionProbe.Classify(probe,302,"text/html","",false);
        Check(!probe.verifiedReachable,"redirect does not prove connectivity to the requested resource");
        ConnectionProbe.Classify(probe,403,"text/html","",true);
        Check(probe.browserChallenge && !probe.verifiedReachable && probe.title.Contains("浏览器验证"),"Cloudflare challenge is identified without claiming chat authentication failure");
        ConnectionProbe.Classify(probe,403,"text/html","",false);
        Check(!probe.browserChallenge && !probe.verifiedReachable && probe.title.Contains("403"),"ordinary 403 remains visible and is not relabelled as a challenge");
        var gesture=new PetGesture();gesture.Down(new Point(10,10),new Point(100,100));gesture.Move(new Point(12,11));Check(gesture.Up(),"tap with small movement stays click");
        gesture.Down(new Point(10,10),new Point(100,100));Point moved=gesture.Move(new Point(40,30));Check(moved==new Point(130,120) && !gesture.Up(),"drag moves pet without also toggling popup");
        var screen=new Rectangle(-1920,0,1920,1080);var popupSize=new Size(440,455);var position=PetLayout.Bubble(new Rectangle(-1900,5,128,128),popupSize,screen);
        Check(screen.Contains(new Rectangle(position,popupSize)),"popup stays on negative-coordinate monitor near top edge");
        using(var cat=new Bitmap(Path.Combine(dir,"assets","mint-girl-dress.png"))){Check(cat.GetPixel(0,0).A<=8 && cat.GetPixel(cat.Width/2,cat.Height/2).A>200,"generated pet has transparent alpha and visible center; compositor removes faint background noise");}
        using(var pop=new CompanionBubble(1)) {pop.SetData(dashboard,new NetworkSnapshot{title="入口有响应 · HTTP 403",detail="生成通道状态未知"});pop.Render(Path.Combine(dir,"pet-test-render.png"));Check(pop.Height<500,"one active chat produces compact bubble");}
        Console.WriteLine("ALL "+checks+" CHECKS PASSED");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
