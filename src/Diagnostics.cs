using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Management;
using System.Text;

namespace ATrustSwitch {
    public sealed class RelatedState {
        public string EaioService="未安装",Driver="未安装",ModeWarning="",ExitProof="";
        public int EaioAgents,OtherProcesses,Unverified;
        public readonly List<string> Details=new List<string>();
        public string Error="";
    }
    public static class Diagnostics {
        public const string EaioRoot=@"C:\Program Files\sangfor\EAIO";
        public static bool Within(string path,string root) {
            if(String.IsNullOrEmpty(path)) return false;
            try { return Path.GetFullPath(path.Replace('/','\\')).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public static string DescribeChange(DateTime time,string before,string after,string actor) {
            bool manual=before.IndexOf("按需",StringComparison.OrdinalIgnoreCase)>=0 || before.IndexOf("手动",StringComparison.OrdinalIgnoreCase)>=0 || before.IndexOf("demand",StringComparison.OrdinalIgnoreCase)>=0 || before.IndexOf("manual",StringComparison.OrdinalIgnoreCase)>=0;
            bool automatic=after.IndexOf("自动",StringComparison.OrdinalIgnoreCase)>=0 || after.IndexOf("auto",StringComparison.OrdinalIgnoreCase)>=0;
            if(!manual || !automatic) return "";
            return time.ToString("MM-dd HH:mm")+" "+(actor=="S-1-5-18"?"SYSTEM":"其他账户")+" 将手动改为自动";
        }
        public static RelatedState Read() {
            var result=new RelatedState();
            try {
                string proof=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ATrustSwitch","last-exit.txt");
                if(File.Exists(proof)) { result.ExitProof=File.ReadAllText(proof); result.Details.Add("最近退出核验："+result.ExitProof); }
            } catch { }
            try {
                using(var search=new ManagementObjectSearcher("SELECT Name,State,PathName,ProcessId FROM Win32_Service WHERE Name='eaio_service'"))
                using(var rows=search.Get()) foreach(ManagementObject row in rows) using(row) {
                    string path=Convert.ToString(row["PathName"]).Trim().Trim('"').Replace('/','\\');
                    if(!String.Equals(path,EaioRoot+@"\eaio_service.exe",StringComparison.OrdinalIgnoreCase)) { result.EaioService="路径待核实"; result.Unverified++; }
                    else result.EaioService=Convert.ToString(row["State"])=="Running"?"运行中":"已停止";
                    result.Details.Add("服务：eaio_service · "+result.EaioService+" · PID "+row["ProcessId"]+"\r\n服务配置路径："+path);
                }
            } catch(Exception e) { result.EaioService="未知"; result.Error+="EAIO 服务读取失败；"; Log.Write("相关服务读取失败 "+e.Message); }
            try {
                using(var search=new ManagementObjectSearcher("SELECT Name,ProcessId,ExecutablePath FROM Win32_Process"))
                using(var rows=search.Get()) foreach(ManagementObject row in rows) using(row) {
                    string name=Convert.ToString(row["Name"]),path=Convert.ToString(row["ExecutablePath"]);
                    bool atrust=Within(path,Policy.Root),eaio=Within(path,EaioRoot);
                    bool candidate=name.StartsWith("eaio_",StringComparison.OrdinalIgnoreCase) || name.Equals("aTrustDefenser.exe",StringComparison.OrdinalIgnoreCase);
                    if(!atrust && !eaio && !(candidate && path.Length==0)) continue;
                    if(Policy.Allowed(path)) continue; // Already displayed in the connection card.
                    bool unknown=path.Length==0;
                    if(name.Equals("eaio_agent.exe",StringComparison.OrdinalIgnoreCase)) result.EaioAgents++;
                    else if(!name.Equals("eaio_service.exe",StringComparison.OrdinalIgnoreCase)) result.OtherProcesses++;
                    if(unknown) result.Unverified++;
                    result.Details.Add("进程："+name+" · PID "+row["ProcessId"]+"\r\n"+(unknown?"路径不可读，按名称列出待核实":path));
                }
            } catch(Exception e) { result.Error+="进程列表读取失败；"; Log.Write("相关进程读取失败 "+e.Message); }
            try {
                using(var search=new ManagementObjectSearcher("SELECT Name,State,PathName FROM Win32_SystemDriver WHERE Name='aTrustNetflt_1' OR Name='aTrustXtun' OR Name='SfRemoveCallback'"))
                using(var rows=search.Get()) foreach(ManagementObject row in rows) using(row) {
                    string name=Convert.ToString(row["Name"]),state=Convert.ToString(row["State"])=="Running"?"运行中":"已停止";
                    if(name=="aTrustNetflt_1") result.Driver=state;
                    result.Details.Add("驱动："+name+" · "+state+"\r\n"+row["PathName"]);
                }
            } catch(Exception e) { result.Driver="未知"; result.Error+="驱动列表读取失败；"; Log.Write("相关驱动读取失败 "+e.Message); }
            try {
                var query=new EventLogQuery("System",PathType.LogName,"*[System[Provider[@Name='Service Control Manager'] and EventID=7040] and EventData[Data[@Name='param4']='aTrustService']]");
                query.ReverseDirection=true;
                using(var reader=new EventLogReader(query)) using(var entry=reader.ReadEvent()) {
                    if(entry!=null && entry.Properties.Count>=4) {
                        string before=Convert.ToString(entry.Properties[1].Value),after=Convert.ToString(entry.Properties[2].Value);
                        string actor=entry.UserId==null?"未知":entry.UserId.Value;
                        DateTime time=entry.TimeCreated.GetValueOrDefault();
                        result.ModeWarning=DescribeChange(time,before,after,actor);
                        result.Details.Add("最近启动方式变更："+time.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n"+before+" → "+after+"\r\n执行账户："+actor+"\r\n事件 7040 记录的是服务控制管理器，未提供调用程序身份。");
                    }
                }
            } catch(Exception e) { result.Error+="启动方式变更记录不可读；"; Log.Write("启动方式历史读取失败 "+e.Message); }
            return result;
        }
    }
}
