// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Web.Script.Serialization;

namespace ProgressGlass {
    public class Measurement {
        public double completed { get; set; }
        public double total { get; set; }
        public string unit { get; set; }
        public string source { get; set; }
        public string observedAt { get; set; }
        public double Percent { get { return 100.0 * completed / total; } }
        public string CountLabel { get { return completed.ToString("0.##")+" / "+total.ToString("0.##")+" "+unit; } }
        public static void Validate(Measurement m) {
            if(m==null) return;
            if(Double.IsNaN(m.completed)||Double.IsInfinity(m.completed)||Double.IsNaN(m.total)||Double.IsInfinity(m.total)||m.total<=0||m.completed<0||m.completed>m.total) throw new FormatException("实测计数无效");
            DateTimeOffset dt;
            if(String.IsNullOrWhiteSpace(m.unit)||String.IsNullOrWhiteSpace(m.source)||!DateTimeOffset.TryParse(m.observedAt,out dt)) throw new FormatException("实测进度必须包含单位、来源和观测时间");
        }
    }
    public class MonitorConfig {
        public string sessionPath { get; set; }
        public string sessionId { get; set; }
        public string Key { get { return (sessionPath??"")+"|"+(sessionId??""); } }
    }
    public class MonitorSnapshot {
        public string state = "unbound", title = "未绑定会话记录", detail = "不能判断当前聊天是否仍在运行";
        public string sourcePath = "", sessionId = "", lastEvent = "", phase = "unknown";
        public string localNetwork = "未检测", client = "未检测", observedAt = "";
        public double silentSeconds;
        public int pendingTools;
        public bool catchingUp;
    }
    // Metadata-only reducer. Never displays prompts, reasoning text, tool inputs or output bodies.
    public class ActivityState {
        public UsageSnapshot Usage;
        public string Phase = "unknown", LastKind = "", Turn = "";
        public DateTimeOffset LastEvent = DateTimeOffset.MinValue;
        public readonly HashSet<string> Pending = new HashSet<string>();
        public static string Str(Dictionary<string,object> d,string key) { object v; return d!=null && d.TryGetValue(key,out v) && v!=null ? v.ToString() : ""; }
        static Dictionary<string,object> Dict(Dictionary<string,object> d,string key) { object v; return d!=null && d.TryGetValue(key,out v) ? v as Dictionary<string,object> : null; }
        public void Apply(Dictionary<string,object> record) {
            DateTimeOffset at; if(!DateTimeOffset.TryParse(Str(record,"timestamp"),out at)) return;
            if(at<LastEvent) return;
            var p=Dict(record,"payload"); if(p==null) return;
            string outer=Str(record,"type"), type=Str(p,"type"), kind="";
            if(outer=="event_msg" && type=="token_count") {
                var usage=UsageSnapshot.FromRecord(Dict(p,"rate_limits"),at);
                if(usage!=null && (Usage==null || usage.Time>=Usage.Time)) Usage=usage;
            }
            if(outer=="event_msg") {
                string turn=Str(p,"turn_id");
                if(type!="task_started" && Turn!="" && turn!="" && turn!=Turn) return;
                if(type=="task_started") { Phase="running"; Turn=turn; Pending.Clear(); kind="回合开始"; }
                else if(type=="task_complete" || type=="turn_aborted") {
                    if(Turn!="" && turn!="" && turn!=Turn) return;
                    Phase=type=="task_complete" ? "idle" : "interrupted"; Pending.Clear(); kind=type=="task_complete" ? "回合结束" : "回合中断";
                }
                else if(type=="error" || type=="stream_error") { Phase="error"; kind="会话记录报告错误"; }
                else if(type=="approval_requested") { Phase="approval"; kind="请求用户确认"; }
                else if(type=="approval_resolved") { Phase="running"; kind="确认已处理"; }
            }
            if(outer=="response_item") {
                if(type=="function_call" || type=="custom_tool_call") {
                    var id=Str(p,"call_id"); if(id!="") Pending.Add(id);
                    kind="工具调用";
                } else if(type=="function_call_output" || type=="custom_tool_call_output") {
                    Pending.Remove(Str(p,"call_id")); kind="工具返回";
                } else if(type=="reasoning") kind="推理记录";
                else if(type=="message" && Str(p,"role")=="assistant") kind="助手输出";
                // A recorded item proves past activity, not ongoing server computation.
                if(kind!="" && Phase=="error") Phase="running";
            }
            if(kind!="" && at>=LastEvent) { LastEvent=at; LastKind=kind; }
        }
        public MonitorSnapshot Snapshot(DateTimeOffset now) {
            var s=new MonitorSnapshot { phase=Phase, pendingTools=Pending.Count, lastEvent=LastEvent==DateTimeOffset.MinValue ? "" : LastEvent.ToString("o"), observedAt=now.ToString("o") };
            s.silentSeconds=LastEvent==DateTimeOffset.MinValue ? 0 : Math.Max(0,(now-LastEvent).TotalSeconds);
            string age=s.silentSeconds<60 ? ((int)s.silentSeconds)+" 秒" : ((int)(s.silentSeconds/60))+" 分 "+((int)s.silentSeconds%60)+" 秒";
            if(Phase=="idle") { s.state="idle"; s.title="已记录回合结束"; s.detail="最后事件："+LastKind+" · "+age+"前"; }
            else if(Phase=="interrupted") { s.state="interrupted"; s.title="已记录回合中断"; s.detail="请检查聊天是否需要继续"; }
            else if(Phase=="error") { s.state="error"; s.title="会话记录报告错误"; s.detail="具体原因请查看聊天；未自动归因为网络"; }
            else if(Phase=="approval") { s.state="approval"; s.title="等待用户确认"; s.detail="记录中出现确认请求，请查看聊天"; }
            else if(LastEvent==DateTimeOffset.MinValue) { s.state="unknown"; s.title="尚无可识别活动"; s.detail="未观察到事件，不代表程序停止"; }
            else if(Phase=="unknown") { s.state="unknown"; s.title="回合状态未确认"; s.detail="最近记录："+LastKind+" · "+age+"前"; }
            else if(s.silentSeconds>=120) { s.state="quiet"; s.title="长时间没有新事件 · "+age; s.detail="无法区分深度思考、网络等待或停滞"; }
            else if(Pending.Count>0) { s.state="tool_wait"; s.title="等待工具返回 · "+age; s.detail=Pending.Count+" 个调用尚无返回记录；不代表已卡死"; }
            else if(s.silentSeconds>=30) { s.state="waiting"; s.title="等待新事件 · "+age; s.detail="暂时无输出，无法确认服务端是否在计算"; }
            else { s.state="activity"; s.title="最近有活动 · "+LastKind; s.detail="最近事件 "+age+"前；不等于工作已完成"; }
            return s;
        }
    }
    public class SessionObserver {
        readonly bool fullHistory;
        public SessionObserver(bool fullHistory=false) { this.fullHistory=fullHistory; }
        public UsageSnapshot Usage { get { return reducer.Usage; } }
        string key="";
        long offset;
        readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
        readonly List<byte> pending=new List<byte>();
        ActivityState reducer=new ActivityState();
        string ioError="";
        bool skipFirst, initialized, parserError;
        const int Chunk=512*1024;
        public MonitorSnapshot Read(MonitorConfig config,DateTimeOffset now,bool systemSignals) {
            if(config==null || String.IsNullOrWhiteSpace(config.sessionPath) || String.IsNullOrWhiteSpace(config.sessionId)) return new MonitorSnapshot();
            if(key!=config.Key) { key=config.Key; offset=0; pending.Clear(); reducer=new ActivityState(); initialized=false; parserError=false; }
            bool behind=false;
            try {
                using(var f=new FileStream(config.sessionPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    using(var sr=new StreamReader(f,Encoding.UTF8,true,4096,true)) {
                        string head=sr.ReadLine(); var h=json.Deserialize<Dictionary<string,object>>(head??"{}");
                        object v; var p=h.TryGetValue("payload",out v) ? v as Dictionary<string,object> : null;
                        if(ActivityState.Str(h,"type")!="session_meta" || ActivityState.Str(p,"id")!=config.sessionId) throw new IOException("会话 ID 与绑定文件不符");
                    }
                    if(!initialized || f.Length<offset) {
                        reducer=new ActivityState(); pending.Clear(); parserError=false;
                        offset=fullHistory ? 0 : Math.Max(0,f.Length-4*1024*1024); skipFirst=offset>0; initialized=true;
                    }
                    f.Position=offset;
                    byte[] buffer=new byte[Chunk]; int count=f.Read(buffer,0,buffer.Length); offset+=count;
                    for(int i=0;i<count;i++) {
                        byte b=buffer[i];
                        if(b==10) {
                            if(skipFirst) skipFirst=false;
                            else if(pending.Count>0) {
                                try { reducer.Apply(json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(pending.ToArray()))); }
                                catch { parserError=true; }
                            }
                            pending.Clear();
                        } else if(!skipFirst) {
                            pending.Add(b);
                            if(pending.Count>16*1024*1024) { pending.Clear(); skipFirst=true; parserError=true; }
                        }
                    }
                    behind=f.Position<f.Length;
                }
                ioError="";
            } catch(Exception ex) { ioError=ex is FileNotFoundException ? "绑定的记录文件不存在" : ex is UnauthorizedAccessException ? "没有权限读取会话记录" : "会话记录读取失败或绑定不符"; }
            var s=reducer.Snapshot(now); s.sourcePath=config.sessionPath; s.sessionId=config.sessionId; s.catchingUp=behind;
            if(ioError!="" || parserError) { s.state="source_error"; s.title="观测源异常"; s.detail=ioError!="" ? ioError : "部分记录无法解析，当前状态不可靠"; }
            else if(behind) { s.state="loading"; s.title="正在读取会话记录"; s.detail="追上最新事件后再判断活动状态"; }
            if(systemSignals) {
                try { s.localNetwork=NetworkInterface.GetIsNetworkAvailable() ? "本地网卡已连接" : "本地网络接口断开"; } catch { s.localNetwork="本地网络未知"; }
                try {
                    var processes=Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("Codex")).ToArray();
                    s.client=processes.Length>0 ? "客户端进程存在" : "未发现客户端进程";
                    foreach(var p in processes) p.Dispose();
                } catch { s.client="客户端状态未知"; }
            }
            return s;
        }
    }
}
