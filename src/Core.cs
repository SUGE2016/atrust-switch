using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace ATrustSwitch {
    public sealed class Failure : Exception {
        public readonly int Code;
        public Failure(int code, string message) : base(message) { Code = code; }
    }
    public static class Policy {
        public const string Root = @"C:\Program Files (x86)\Sangfor\aTrust";
        public const string Agent = Root + @"\aTrustAgent\aTrustAgent.exe";
        public const string Client = Root + @"\aTrustTray\aTrustTray.exe";
        public static readonly string[] Paths = { Agent, Client,
            Root + @"\aTrustAgent\xtunnel-32\aTrustXtunnel.exe",
            Root + @"\aTrustAgent\xtunnel-64\aTrustXtunnel.exe" };
        public static bool Allowed(string path) {
            if (String.IsNullOrEmpty(path)) return false;
            // Exact paths only; no prefix matching, relative paths or alternate directories.
            foreach (string allowed in Paths) if (String.Equals(path, allowed, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public static void VerifyFile(string path) {
            if (!Allowed(path) || !File.Exists(path)) throw new Failure(14, "安装文件缺失或路径不匹配。");
            FileSystemInfo item = new FileInfo(path);
            while (item != null) {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new Failure(14, "安装路径含重定向，已停止操作。");
                var dir = item as DirectoryInfo;
                item = dir == null ? ((FileInfo)item).Directory : dir.Parent;
            }
        }
        public static bool ValidAction(string action) { return action == "start" || action == "stop" || action == "manual" || action == "auto"; }
        public static string Message(int code) {
            switch (code) {
                case 0: return "操作完成。";
                case 10: return "已取消管理员授权，未执行操作。";
                case 11: return "权限被拒绝，请检查管理员权限或企业策略。";
                case 12: return "等待超时，请刷新状态后查看日志。";
                case 13: return "有进程无法核实路径，无法确认完整退出。";
                case 14: return "aTrust 安装路径与预期不符或文件缺失。";
                case 15: return "操作后的状态未达到预期，请查看日志。";
                case 16: return "另一项 aTrust 操作正在进行，请稍后重试。";
                default: return "操作未完成，请查看日志中的错误详情。";
            }
        }
        public static int ErrorCode(Exception e) {
            var f = e as Failure; if (f != null) return f.Code;
            var w = e as Win32Exception;
            if (w != null && w.NativeErrorCode == 1223) return 10;
            if ((w != null && w.NativeErrorCode == 5) || e is UnauthorizedAccessException) return 11;
            if (e is System.ServiceProcess.TimeoutException || e is System.TimeoutException) return 12;
            if (e.InnerException != null) return ErrorCode(e.InnerException);
            return 1;
        }
    }
    public static class Log {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATrustSwitch");
        public static string FilePath { get { return Path.Combine(Folder, "manager-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"); } }
        public static void Write(string value) {
            try { Directory.CreateDirectory(Folder); File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + Process.GetCurrentProcess().Id + "] " + value + Environment.NewLine, Encoding.UTF8); }
            catch { /* Logging must never interrupt service state verification. */ }
        }
    }
    public sealed class Proc {
        public int Id; public string Name; public string Path;
    }
    public sealed class Snapshot {
        public string Service; public string Mode; public List<Proc> Processes = new List<Proc>(); public int Unknown;
        public Dictionary<string,int> UnknownCounts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        public void AddUnknown(string name) { Unknown++; if(!UnknownCounts.ContainsKey(name)) UnknownCounts[name]=0; UnknownCounts[name]++; }
        public int UnknownCount(string name) { return UnknownCounts.ContainsKey(name)?UnknownCounts[name]:0; }
        public int Count(string name) { return Processes.FindAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count; }
        public bool Exited { get { return Service == "Stopped" && Processes.Count == 0 && Unknown == 0; } }
    }
    public interface ISystem {
        Snapshot Read(); void Start(); void Stop(); void Kill(Proc p); void Mode(string mode); void Pause(int ms);
    }
    public static class Operations {
        public static void Run(ISystem system, string action) {
            if (!Policy.ValidAction(action)) throw new Failure(1, "未知操作。");
            Snapshot before = system.Read();
            if (action == "start") {
                if (before.Mode == "Disabled") throw new Failure(11, "服务已禁用；请由管理员确认启动策略。");
                if (before.Service != "Running") system.Start();
                if (system.Read().Service != "Running") throw new Failure(15, "服务未启动。");
            } else if (action == "stop") {
                if (before.Service != "Stopped") system.Stop();
                if (system.Read().Service != "Stopped") throw new Failure(15, "服务未停止，未处理残留进程。");
                int stable = 0;
                for (int attempt = 0; attempt < 12; attempt++) {
                    Snapshot now = system.Read();
                    if (now.Service != "Stopped") throw new Failure(15, "服务重新运行，请检查企业管理策略。");
                    if (now.Unknown > 0) throw new Failure(13, "同名进程路径无法读取。");
                    if (now.Exited) { stable++; if (stable >= 3) return; }
                    else { stable = 0; foreach (Proc p in now.Processes) system.Kill(p); }
                    system.Pause(500);
                }
                throw new Failure(15, "残留进程未退出或被重新启动。");
            } else {
                string mode = action == "auto" ? "Automatic" : "Manual";
                system.Mode(mode);
                if (system.Read().Mode != (action == "auto" ? "Auto" : "Manual")) throw new Failure(15, "启动方式未生效。");
            }
        }
    }
    public sealed class WindowsSystem : ISystem {
        const uint Query = 0x1000, Terminate = 1, Synchronize = 0x100000;
        [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr h,out uint exitCode);
        [StructLayout(LayoutKind.Sequential)] struct ServiceStatus { public uint Type,State,Accepted,Win32Exit,SpecificExit,CheckPoint,WaitHint; }
        [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool ControlService(IntPtr service,uint control,out ServiceStatus status);
        [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
        static void StopOnlyTargetService() {
            IntPtr manager=OpenSCManager(null,null,1);
            if (manager==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr service=OpenService(manager,"aTrustService",0x20);
                if (service==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try {
                    ServiceStatus status;
                    // ControlService fails if running dependents exist; it never recursively stops them.
                    if (!ControlService(service,1,out status)) { int code=Marshal.GetLastWin32Error(); if (code!=1062) throw new Win32Exception(code); }
                } finally { CloseServiceHandle(service); }
            } finally { CloseServiceHandle(manager); }
        }
        static string ImagePath(IntPtr h) {
            int size = 32768; var path = new StringBuilder(size);
            if (!QueryFullProcessImageName(h, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
        static ManagementObject Service() {
            var service = new ManagementObject("Win32_Service.Name='aTrustService'");
            service.Get();
            string path = Convert.ToString(service["PathName"]).Trim();
            if (path.StartsWith("\"") && path.EndsWith("\"")) path = path.Substring(1, path.Length - 2);
            if (!String.Equals(path, Policy.Agent, StringComparison.OrdinalIgnoreCase)) { service.Dispose(); throw new Failure(14, "aTrustService 的程序路径不匹配。"); }
            Policy.VerifyFile(Policy.Agent);
            return service;
        }
        public Snapshot Read() {
            var result = new Snapshot();
            using (var svc = Service()) { result.Service = Convert.ToString(svc["State"]); result.Mode = Convert.ToString(svc["StartMode"]); }
            foreach (string name in new[] { "aTrustAgent", "aTrustXtunnel", "aTrustTray" }) {
                foreach (Process process in Process.GetProcessesByName(name)) using (process) {
                    IntPtr h = OpenProcess(Query, false, process.Id);
                    if (h == IntPtr.Zero) { if (Marshal.GetLastWin32Error() != 87) result.AddUnknown(name); continue; }
                    try {
                        string path = ImagePath(h);
                        if (Policy.Allowed(path)) result.Processes.Add(new Proc { Id = process.Id, Name = name, Path = path });
                    } catch (Win32Exception) { uint code; if (!GetExitCodeProcess(h,out code) || code==259) result.AddUnknown(name); }
                    finally { CloseHandle(h); }
                }
            }
            return result;
        }
        public void Start() {
            using (var verified = Service()) using (var svc = new ServiceController("aTrustService")) {
                if (svc.Status == ServiceControllerStatus.StopPending) svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                svc.Refresh();
                if (svc.Status == ServiceControllerStatus.Stopped) svc.Start();
                else if (svc.Status == ServiceControllerStatus.Paused) svc.Continue();
                svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
            }
        }
        public void Stop() {
            using (var verified = Service()) using (var svc = new ServiceController("aTrustService")) {
                if (svc.Status == ServiceControllerStatus.StartPending) svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                svc.Refresh();
                if (svc.Status != ServiceControllerStatus.Stopped && svc.Status != ServiceControllerStatus.StopPending) StopOnlyTargetService();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
            }
        }
        public void Mode(string mode) {
            using (var svc = Service()) {
                uint result = Convert.ToUInt32(svc.InvokeMethod("ChangeStartMode", new object[] { mode }));
                if (result != 0) throw new Failure(result == 2 ? 11 : 1, "ChangeStartMode 返回 " + result);
            }
        }
        public void Kill(Proc p) {
            // Verify the image and terminate through the SAME handle, preventing PID reuse races.
            IntPtr h = OpenProcess(Query | Terminate | Synchronize, false, p.Id);
            if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); if (e == 87) return; throw new Win32Exception(e); }
            try {
                if (WaitForSingleObject(h, 0) == 0) return;
                string path = ImagePath(h);
                if (!Policy.Allowed(path) || !String.Equals(path, p.Path, StringComparison.OrdinalIgnoreCase)) throw new Failure(14, "进程路径已变化，停止处理 PID " + p.Id);
                Policy.VerifyFile(path);
                Log.Write("结束残留 PID=" + p.Id + " 路径=" + path);
                if (!TerminateProcess(h, 0)) { int error = Marshal.GetLastWin32Error(); if (WaitForSingleObject(h, 0) != 0) throw new Win32Exception(error); }
                if (WaitForSingleObject(h, 5000) != 0) throw new Failure(12, "等待进程退出超时。");
            } finally { CloseHandle(h); }
        }
        public void Pause(int ms) { Thread.Sleep(ms); }
    }
    public static class Worker {
        public static int Run(string action) {
            if (!Policy.ValidAction(action) && action!="cycle" && action!="verify") return 1;
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 11;
            try {
                using (var gate = new Mutex(false, @"Global\ATrustSwitch.Operation")) {
                    bool held = false;
                    try {
                        try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) return 16;
                        Log.Write("开始 " + action);
                        if(action=="auto") Operations.Run(new WindowsSystem(),action);
                        else OnDemand.Run(action);
                        Log.Write("核验完成 " + action); return 0;
                    } finally { if (held) gate.ReleaseMutex(); }
                }
            } catch (Exception e) { Log.Write(action + " 失败 " + e); return Policy.ErrorCode(e); }
        }
    }
}
