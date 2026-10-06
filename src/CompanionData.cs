// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Web.Script.Serialization;

namespace ProgressGlass {
    public class UsageWindow {
        public double usedPercent { get; set; }
        public int windowDurationMins { get; set; }
        public long resetsAt { get; set; }
        public double Remaining { get { return Math.Max(0,100-usedPercent); } }
        public DateTimeOffset Reset { get { return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(resetsAt); } }
        public string Name { get { return windowDurationMins==10080 ? "每周额度" : windowDurationMins==300 ? "5 小时额度" : windowDurationMins>=1440 ? (windowDurationMins/1440)+" 天额度" : windowDurationMins+" 分钟额度"; } }
    }
    public class UsageSnapshot {
        public string observedAt { get; set; }
        public string source { get; set; }
        public List<UsageWindow> windows { get; set; }
        public DateTimeOffset Time { get { DateTimeOffset t; return DateTimeOffset.TryParse(observedAt,out t) ? t : DateTimeOffset.MinValue; } }
        public bool Valid { get { return Time!=DateTimeOffset.MinValue && windows!=null && windows.Count>0 && windows.All(w=>w!=null && !Double.IsNaN(w.usedPercent) && w.usedPercent>=0 && w.usedPercent<=100 && w.windowDurationMins>0 && w.resetsAt>0 && w.resetsAt<253402300799); } }
        public string Freshness(DateTimeOffset now) { return (now-Time).TotalMinutes>5 ? "较早快照 · "+Time.ToLocalTime().ToString("MM-dd HH:mm") : "记录于 "+Time.ToLocalTime().ToString("HH:mm:ss"); }
        public static UsageSnapshot FromRecord(Dictionary<string,object> limits,DateTimeOffset at) {
            if(limits==null || ActivityState.Str(limits,"limit_id")!="codex") return null;
            var result=new UsageSnapshot { observedAt=at.ToString("o"),source="本地 Codex 额度记录",windows=new List<UsageWindow>() };
            foreach(string key in new[]{"primary","secondary"}) {
                object value; if(!limits.TryGetValue(key,out value)) continue;
                var d=value as Dictionary<string,object>; if(d==null) continue;
                double used; int minutes; long reset;
                if(!Double.TryParse(ActivityState.Str(d,"used_percent"),out used) || !Int32.TryParse(ActivityState.Str(d,"window_minutes"),out minutes) || !Int64.TryParse(ActivityState.Str(d,"resets_at"),out reset)) continue;
                result.windows.Add(new UsageWindow{usedPercent=used,windowDurationMins=minutes,resetsAt=reset});
            }
            return result.Valid ? result : null;
        }
    }
    public class ChatActivity {
        public string id, title, project, cwd;
        public MonitorSnapshot activity;
        public string current="";
        public Measurement measurement;
    }
    public class DashboardSnapshot {
        public List<ChatActivity> chats=new List<ChatActivity>();
        public UsageSnapshot usage;
        public string observedAt="", error="";
        public int discovered, loading, uncertain;
    }
    public class AllChatsObserver {
        class Entry { public string id, cwd; public SessionObserver observer=new SessionObserver(true); }
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
        readonly string home, root, boardPath;
        Dictionary<string,string> titles=new Dictionary<string,string>();
        DateTimeOffset discoveredAt=DateTimeOffset.MinValue;
        string discoveryError="";
        public AllChatsObserver(string codexHome,string appRoot,string file) { home=codexHome; root=appRoot; boardPath=file; }
        public static bool Include(MonitorSnapshot s) { return !s.catchingUp && s.state!="source_error" && (s.phase=="running" || s.phase=="approval" || s.phase=="error"); }
        public static bool UserSession(Dictionary<string,object> p) {
            object source;
            return ActivityState.Str(p,"thread_source")!="guardian_review" && !(p.TryGetValue("source",out source) && source is Dictionary<string,object>);
        }
        void Discover(DateTimeOffset now) {
            if((now-discoveredAt).TotalSeconds<10) return;
            discoveredAt=now;
            try {
                string folder=Path.Combine(home,"sessions");
                if(!Directory.Exists(folder)) throw new DirectoryNotFoundException();
                var paths=new HashSet<string>(Directory.GetFiles(folder,"*.jsonl",SearchOption.AllDirectories),StringComparer.OrdinalIgnoreCase);
                foreach(string removed in entries.Keys.Where(p=>!paths.Contains(p)).ToArray()) entries.Remove(removed);
                foreach(string path in paths) {
                    if(entries.ContainsKey(path)) continue;
                    using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var reader=new StreamReader(f)) {
                        var head=json.Deserialize<Dictionary<string,object>>(reader.ReadLine()??"{}"); object obj;
                        var p=head.TryGetValue("payload",out obj) ? obj as Dictionary<string,object> : null;
                        if(p==null || !UserSession(p) || ActivityState.Str(head,"type")!="session_meta") continue;
                        var id=ActivityState.Str(p,"id"); if(id=="") continue;
                        entries[path]=new Entry{id=id,cwd=ActivityState.Str(p,"cwd")};
                    }
                }
                string index=Path.Combine(home,"session_index.jsonl");
                if(File.Exists(index)) {
                    var next=new Dictionary<string,string>();
                    using(var f=new FileStream(index,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var reader=new StreamReader(f)) { string line; while((line=reader.ReadLine())!=null) {
                        try {var d=json.Deserialize<Dictionary<string,object>>(line); string id=ActivityState.Str(d,"id"), name=ActivityState.Str(d,"thread_name");if(id!="" && name!="") next[id]=name;} catch { }
                    }}
                    titles=next;
                }
                discoveryError="";
            } catch { discoveryError="会话目录读取不完整，列表可能缺项"; }
        }
        public DashboardSnapshot Read(DateTimeOffset now) {
            Discover(now);
            var result=new DashboardSnapshot { observedAt=now.ToString("o"),discovered=entries.Count,error=discoveryError };
            try { var u=json.Deserialize<UsageSnapshot>(File.ReadAllText(Path.Combine(root,"usage-snapshot.json")));if(u!=null && u.Valid) result.usage=u; } catch { }
            Board board=null;
            try { board=Board.Parse(File.ReadAllText(boardPath)); } catch { }
            foreach(var pair in entries) {
                var e=pair.Value; MonitorSnapshot s=null;
                // Catch up in a worker thread with bounded work; never block the UI.
                for(int i=0;i<16;i++) { s=e.observer.Read(new MonitorConfig{sessionId=e.id,sessionPath=pair.Key},now,false);if(!s.catchingUp) break; }
                if(s.catchingUp) result.loading++;
                if(s.state=="source_error" || s.phase=="unknown") result.uncertain++;
                var u=e.observer.Usage;
                if(u!=null && (result.usage==null || u.Time>result.usage.Time)) result.usage=u;
                if(!Include(s)) continue;
                string title; if(!titles.TryGetValue(e.id,out title)) title="聊天 · "+e.id.Substring(Math.Max(0,e.id.Length-8));
                var chat=new ChatActivity{id=e.id,title=title,cwd=e.cwd,project=String.IsNullOrEmpty(e.cwd)?"未归属项目":Path.GetFileName(e.cwd.TrimEnd('\\','/')),activity=s};
                if(board!=null && board.monitor!=null && board.monitor.sessionId==e.id) { chat.current=board.current;chat.measurement=board.measurement; }
                result.chats.Add(chat);
            }
            result.chats=result.chats.OrderBy(c=>c.cwd).ThenByDescending(c=>c.activity.lastEvent).ToList();
            return result;
        }
    }
    public class NetworkSnapshot {
        public string title="连接检测中", detail="", observedAt="";
        public bool localConnected;
        public int? httpStatus;
        public string probeUrl=ConnectionProbe.ProbeUrl, method="GET";
        public bool verifiedReachable, browserChallenge;
    }
    public static class ConnectionProbe {
        // The homepage challenges unauthenticated automated clients. This public resource
        // checks the same host without treating a browser verification page as a network outage.
        public const string ProbeUrl="https://chatgpt.com/robots.txt";
        public static string HttpLabel(int code) { return code==200 ? "公开资源已响应 · HTTP 200" : "探测响应待确认 · HTTP "+code; }
        public static void Classify(NetworkSnapshot s,int code,string contentType,string body,bool challenge) {
            s.httpStatus=code;s.browserChallenge=challenge;s.verifiedReachable=false;
            if(challenge) {s.title="网站要求浏览器验证";s.detail="自动连接探测受限；不代表聊天请求被拒绝";return;}
            if(code==200 && (contentType??"").StartsWith("text/plain",StringComparison.OrdinalIgnoreCase) && (body??"").IndexOf("User-agent:",StringComparison.OrdinalIgnoreCase)>=0) {
                s.verifiedReachable=true;s.title="ChatGPT 连接探测通过";s.detail="公开资源 HTTP 200 · 不检测生成请求";return;
            }
            s.title=HttpLabel(code);
            s.detail=code==200 ? "响应内容不符合公开资源格式，暂不判定连通" : "该探测未通过；聊天连接需单独确认";
        }
        public static NetworkSnapshot Read() {
            var s=new NetworkSnapshot { observedAt=DateTimeOffset.Now.ToString("o") };
            try {
                s.localConnected=NetworkInterface.GetIsNetworkAvailable();
                if(!s.localConnected) {s.title="本地网络接口断开";s.detail="未检测到可用网卡";return s;}
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                var r=(HttpWebRequest)WebRequest.Create(ProbeUrl);
                r.Method="GET";r.Timeout=6000;r.ReadWriteTimeout=6000;r.AllowAutoRedirect=false;r.UserAgent="ProgressGlass/0.4.3";
                HttpWebResponse response;
                try {response=(HttpWebResponse)r.GetResponse();}
                catch(WebException ex) {response=ex.Response as HttpWebResponse;if(response==null)throw;}
                using(response) {
                    s.httpStatus=(int)response.StatusCode;
                    string body="";if(s.httpStatus==200)using(var reader=new StreamReader(response.GetResponseStream())) {char[] buffer=new char[8192];int count=reader.Read(buffer,0,buffer.Length);body=new string(buffer,0,count);}
                    Classify(s,(int)response.StatusCode,response.ContentType,body,String.Equals(response.Headers["cf-mitigated"],"challenge",StringComparison.OrdinalIgnoreCase));
                }
            } catch(WebException ex) {s.title=ex.Status==WebExceptionStatus.Timeout ? "连接探测超时" : "连接探测未成功";s.detail="本地网卡已连接；不能据此断定聊天已断线";}
            catch {s.title="连接探测暂不可用";s.detail="尚未取得可验证的公开资源响应";}
            return s;
        }
    }
}
