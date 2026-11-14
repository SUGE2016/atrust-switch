using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ATrustSwitch {
    public static class OnDemand {
        public static readonly string StateFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ATrustSwitch");
        static readonly string[] TaskNames={"aTrustDaemonGuarder","eaio_watchdog_daily"};
        static readonly string[] TaskFiles={Policy.Root+@"\aTrustAgent\aTrustServRepair.exe",Diagnostics.EaioRoot+@"\eaio_watchdog.exe"};
        static readonly string[] ServiceNames={"aTrustService","eaio_service"};
        static readonly string[] ServiceFiles={Policy.Agent,Diagnostics.EaioRoot+@"\eaio_service.exe"};
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h,int flags,StringBuilder path,ref int size);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr h,uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h,uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct FailureActions { public uint Reset; public IntPtr Reboot,Command; public uint Count; public IntPtr Actions; }
        [StructLayout(LayoutKind.Sequential)] struct ServiceAction { public uint Type,Delay; }
        [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref FailureActions actions);
        [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
        public static bool ManagedFile(string path) {
            if(String.IsNullOrEmpty(path) || !path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return false;
            try {
                if(!String.Equals(Path.GetFullPath(path),path,StringComparison.OrdinalIgnoreCase)) return false;
                return Diagnostics.Within(path,Policy.Root) || Diagnostics.Within(path,Diagnostics.EaioRoot);
            } catch { return false; }
        }
        static void VerifyFile(string path) {
            if(!ManagedFile(path) || !File.Exists(path)) throw new Failure(14,"程序路径未通过核验："+path);
            FileSystemInfo item=new FileInfo(path);
            while(item!=null) {
                if((item.Attributes&FileAttributes.ReparsePoint)!=0) throw new Failure(14,"安装路径含重定向。");
                var dir=item as DirectoryInfo; item=dir==null?((FileInfo)item).Directory:dir.Parent;
            }
        }
        static void PrepareFolder() {
            if(Directory.Exists(StateFolder)) {
                if((File.GetAttributes(StateFolder)&FileAttributes.ReparsePoint)!=0) throw new Failure(14,"备份目录含重定向。");
            } else Directory.CreateDirectory(StateFolder);
            var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            Directory.SetAccessControl(StateFolder,acl);
        }
        static dynamic Scheduler() {
            dynamic scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); scheduler.Connect(); return scheduler;
        }
        static dynamic TaskAt(dynamic scheduler,int index) {
            dynamic task;
            try { task=scheduler.GetFolder("\\").GetTask(TaskNames[index]); }
            catch(COMException e) { if((uint)e.ErrorCode==0x80070002 || (uint)e.ErrorCode==0x8004130F) return null; throw; }
            dynamic actions=task.Definition.Actions;
            if(actions.Count!=1 || actions.Item(1).Type!=0) throw new Failure(14,"保活任务动作异常："+TaskNames[index]);
            string path=((string)actions.Item(1).Path).Trim().Trim('"').Replace('/','\\');
            string arguments=((string)actions.Item(1).Arguments ?? "").Trim();
            if(!String.Equals(path,TaskFiles[index],StringComparison.OrdinalIgnoreCase) || arguments!=(index==0?"--start-aTrustService":"")) throw new Failure(14,"保活任务路径或参数已变化："+TaskNames[index]);
            VerifyFile(path); return task;
        }
        static ManagementObject ServiceAt(int index) {
            var svc=new ManagementObject("Win32_Service.Name='"+ServiceNames[index]+"'"); svc.Get();
            string path=Convert.ToString(svc["PathName"]).Trim().Trim('"').Replace('/','\\');
            if(!String.Equals(path,ServiceFiles[index],StringComparison.OrdinalIgnoreCase)) { svc.Dispose(); throw new Failure(14,"服务路径不匹配。"); }
            VerifyFile(path); return svc;
        }
        static void Backup() {
            PrepareFolder();
            string file=Path.Combine(StateFolder,"original-state.txt");
            if(File.Exists(file)) return;
            var text=new StringBuilder(DateTime.Now.ToString("o")+"\r\n");
            dynamic scheduler=Scheduler();
            for(int i=0;i<TaskNames.Length;i++) {
                dynamic task=TaskAt(scheduler,i);
                text.AppendLine("Task "+TaskNames[i]+" Enabled="+(task==null?"Absent":Convert.ToString(task.Enabled)));
                if(task!=null) File.WriteAllText(Path.Combine(StateFolder,TaskNames[i]+".xml"),(string)task.Xml,Encoding.Unicode);
            }
            for(int i=0;i<ServiceNames.Length;i++) using(var svc=ServiceAt(i)) text.AppendLine("Service "+ServiceNames[i]+" StartMode="+svc["StartMode"]+" State="+svc["State"]);
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) text.AppendLine("HKCU aTrustTray="+(key==null?"":Convert.ToString(key.GetValue("aTrustTray"))));
            File.WriteAllText(file,text.ToString(),Encoding.UTF8);
        }
        static void DisableTasks() {
            dynamic scheduler=Scheduler();
            for(int i=0;i<TaskNames.Length;i++) {
                dynamic task=TaskAt(scheduler,i); if(task==null) continue;
                task.Enabled=false;
                try { task.Stop(0); } catch(COMException e) { if((uint)e.ErrorCode!=0x8004130B) throw; }
                if(task.Enabled) throw new Failure(15,"保活任务未关闭："+TaskNames[i]);
                Log.Write("保活任务已关闭 "+TaskNames[i]);
            }
        }
        public static void DisableUserAutostart() {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) {
                if(key==null) return;
                string value=Convert.ToString(key.GetValue("aTrustTray")); if(value.Length==0) return;
                string expected="\""+Policy.Client+"\" -- -s autostart";
                if(!String.Equals(value,expected,StringComparison.OrdinalIgnoreCase)) throw new Failure(14,"登录启动项与本机已核实的配置不匹配。");
                Directory.CreateDirectory(Log.Folder);
                string backup=Path.Combine(Log.Folder,"original-autostart.txt");
                if(!File.Exists(backup)) File.WriteAllText(backup,value,Encoding.UTF8);
                key.DeleteValue("aTrustTray",false);
                Log.Write("已关闭当前用户的 aTrustTray 登录启动项");
            }
        }
        static void ManualServices() {
            for(int i=0;i<ServiceNames.Length;i++) using(var svc=ServiceAt(i)) {
                uint code=Convert.ToUInt32(svc.InvokeMethod("ChangeStartMode",new object[]{"Manual"}));
                if(code!=0) throw new Failure(code==2?11:15,"设置手动启动失败："+ServiceNames[i]+" / "+code);
                svc.Get(); if(Convert.ToString(svc["StartMode"])!="Manual") throw new Failure(15,"手动启动未生效。");
            }
        }
        static void DisableRecovery() {
            string backup=Path.Combine(StateFolder,"original-recovery.txt");
            if(!File.Exists(backup)) {
                var saved=new StringBuilder();
                foreach(string name in ServiceNames) using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name)) {
                    byte[] data=key.GetValue("FailureActions") as byte[];
                    saved.AppendLine(name+"="+(data==null?"":Convert.ToBase64String(data)));
                }
                File.WriteAllText(backup,saved.ToString(),Encoding.UTF8);
            }
            IntPtr manager=OpenSCManager(null,null,1);
            if(manager==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                foreach(string name in ServiceNames) {
                    IntPtr service=OpenService(manager,name,2);
                    if(service==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    IntPtr action=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ServiceAction)));
                    try {
                        Marshal.StructureToPtr(new ServiceAction { Type=0,Delay=0 },action,false);
                        var config=new FailureActions { Reset=0,Count=1,Actions=action };
                        if(!ChangeServiceConfig2(service,2,ref config)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        Log.Write("已关闭服务失败重启 "+name);
                    } finally { Marshal.FreeHGlobal(action); CloseServiceHandle(service); }
                }
            } finally { CloseServiceHandle(manager); }
        }
        static void SetRunning(int index,bool running) {
            using(var svc=ServiceAt(index)) {
                string state=Convert.ToString(svc["State"]);
                if(state==(running?"Running":"Stopped")) return;
                uint code=Convert.ToUInt32(svc.InvokeMethod(running?"StartService":"StopService",null));
                if(!running && code==5) {
                    // This service does not accept STOP. Its recovery and scheduled guard
                    // have already been disabled; verify and terminate this exact image.
                    Log.Write(ServiceNames[index]+" 不接受 STOP；结束经路径核实的服务进程");
                    Kill(new Proc { Id=Convert.ToInt32(svc["ProcessId"]),Name=ServiceNames[index],Path=ServiceFiles[index] });
                } else if(code!=0 && code!=(running?10:6)) throw new Failure(code==2?11:15,(running?"启动":"停止")+ServiceNames[index]+" 失败 / "+code);
            }
            using(var controller=new ServiceController(ServiceNames[index])) controller.WaitForStatus(running?ServiceControllerStatus.Running:ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));
            Log.Write(ServiceNames[index]+(running?" 已启动":" 已停止"));
        }
        static List<Proc> Processes() {
            var list=new List<Proc>();
            using(var search=new ManagementObjectSearcher("SELECT Name,ProcessId,ExecutablePath FROM Win32_Process")) using(var rows=search.Get())
                foreach(ManagementObject row in rows) using(row) {
                    string path=Convert.ToString(row["ExecutablePath"]),name=Convert.ToString(row["Name"]);
                    int id=Convert.ToInt32(row["ProcessId"]);
                    bool candidate=name.StartsWith("eaio_",StringComparison.OrdinalIgnoreCase) || name=="aTrustAgent.exe" || name=="aTrustTray.exe" || name=="aTrustXtunnel.exe" || name=="aTrustServRepair.exe";
                    if(path.Length==0 && candidate) {
                        // WMI may retain a row briefly after the service's child exits.
                        IntPtr h=OpenProcess(0x1000|0x100000,false,id);
                        if(h==IntPtr.Zero) { if(Marshal.GetLastWin32Error()==87) continue; throw new Failure(13,"无法读取相关进程路径："+name+" PID="+id); }
                        try {
                            if(WaitForSingleObject(h,0)==0) continue;
                            int size=32768; var buffer=new StringBuilder(size);
                            if(!QueryFullProcessImageName(h,0,buffer,ref size)) { if(WaitForSingleObject(h,0)==0) continue; throw new Failure(13,"无法核实进程路径："+name+" PID="+id); }
                            path=buffer.ToString();
                        } finally { CloseHandle(h); }
                    }
                    if(ManagedFile(path)) list.Add(new Proc { Id=id,Name=name,Path=path });
                }
            return list;
        }
        static void Kill(Proc process) {
            IntPtr h=OpenProcess(0x1000|1|0x100000,false,process.Id);
            if(h==IntPtr.Zero) { int code=Marshal.GetLastWin32Error(); if(code==87) return; throw new Win32Exception(code); }
            try {
                if(WaitForSingleObject(h,0)==0) return;
                int size=32768; var path=new StringBuilder(size);
                if(!QueryFullProcessImageName(h,0,path,ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                string file=path.ToString();
                if(!String.Equals(file,process.Path,StringComparison.OrdinalIgnoreCase)) throw new Failure(14,"进程路径已变化。");
                VerifyFile(file);
                if(!TerminateProcess(h,0) && WaitForSingleObject(h,0)!=0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if(WaitForSingleObject(h,5000)!=0) throw new Failure(12,"等待相关进程退出超时。");
                Log.Write("完整退出 PID="+process.Id+" 路径="+file);
            } finally { CloseHandle(h); }
        }
        static void VerifyOff() {
            for(int i=0;i<ServiceNames.Length;i++) using(var svc=ServiceAt(i)) {
                if(Convert.ToString(svc["State"])!="Stopped" || Convert.ToString(svc["StartMode"])!="Manual") throw new Failure(15,"服务退出或按需配置未保持："+ServiceNames[i]);
            }
            dynamic scheduler=Scheduler();
            for(int i=0;i<TaskNames.Length;i++) { dynamic task=TaskAt(scheduler,i); if(task!=null && task.Enabled) throw new Failure(15,"保活任务重新启用："+TaskNames[i]); }
            if(Processes().Count!=0) throw new Failure(15,"仍有相关进程运行。");
        }
        static void Stop() {
            DisableTasks();
            DisableRecovery(); ManualServices();
            SetRunning(1,false); SetRunning(0,false);
            for(int i=0;i<12;i++) {
                List<Proc> list;
                try { list=Processes(); } catch(Failure e) { if(e.Code!=13 || i==11) throw; Thread.Sleep(500); continue; }
                if(list.Count==0) break;
                foreach(var process in list) Kill(process);
                Thread.Sleep(500);
            }
            DisableTasks(); ManualServices(); DisableUserAutostart();
            VerifyOff(); Thread.Sleep(1500); VerifyOff();
            File.WriteAllText(Path.Combine(StateFolder,"last-exit.txt"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" 两项服务已停止并设为手动；两项保活任务已禁用；安装目录内无运行进程。",Encoding.UTF8);
        }
        static void Start() {
            DisableTasks(); DisableRecovery(); ManualServices();
            SetRunning(1,true); SetRunning(0,true);
            DisableTasks(); DisableUserAutostart();
        }
        public static void Run(string action) {
            if(action=="verify") { VerifyOff(); Log.Write("退出状态复核通过"); return; }
            if(action!="start" && action!="stop" && action!="manual" && action!="cycle") throw new Failure(1,"未知按需操作。");
            Backup();
            if(action=="stop") Stop();
            else if(action=="start") Start();
            else if(action=="manual") { DisableTasks(); DisableRecovery(); ManualServices(); DisableUserAutostart(); }
            else if(action=="cycle") {
                Stop(); Start(); Thread.Sleep(5000); Stop();
                Log.Write("真实启停循环完成；开始 6 分钟退出后复核");
                for(int i=0;i<12;i++) { Thread.Sleep(30000); VerifyOff(); Log.Write("退出后复核通过 "+((i+1)*30)+" 秒"); }
            }
            else throw new Failure(1,"未知按需操作。");
        }
    }
}
