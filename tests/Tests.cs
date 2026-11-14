using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Reflection;
using ATrustSwitch;

sealed class Fake : ISystem {
    public Snapshot State = new Snapshot { Service="Stopped",Mode="Auto" };
    public readonly List<string> Calls = new List<string>();
    public bool FailStop, IgnoreStop, IgnoreStart, Respawn, IgnoreMode;
    public Snapshot Read() { Calls.Add("read"); return new Snapshot { Service=State.Service,Mode=State.Mode,Unknown=State.Unknown,Processes=new List<Proc>(State.Processes) }; }
    public void Start() { Calls.Add("start"); if (!IgnoreStart) State.Service="Running"; }
    public void Stop() { Calls.Add("stop"); if (FailStop) throw new UnauthorizedAccessException(); if (!IgnoreStop) State.Service="Stopped"; }
    public void Kill(Proc p) { Calls.Add("kill"); State.Processes.Remove(p); }
    public void Mode(string mode) { Calls.Add("mode"); if (!IgnoreMode) State.Mode=mode=="Automatic" ? "Auto" : mode; }
    public void Pause(int ms) { Calls.Add("pause"); if (Respawn) Add(); }
    public void Add() { State.Processes.Add(new Proc { Id=123,Name="aTrustTray",Path=Policy.Client }); }
}
static class Tests {
    static int count;
    static void Check(bool success,string title) { if (!success) throw new Exception("FAIL: " + title); Console.WriteLine("PASS: " + title); count++; }
    static void Fails(Action run,int code,string title) { try { run(); } catch (Exception e) { Check(Policy.ErrorCode(e)==code,title); return; } throw new Exception("FAIL: expected error " + title); }
    static int Main(string[] args) {
        if (args.Length==1 && args[0]=="--decoy") { Thread.Sleep(60000); return 0; }
        try {
            Check(Policy.Allowed(Policy.Agent),"installed agent allowed");
            Check(Policy.Allowed(Policy.Client.ToUpperInvariant()),"Windows path case insensitive");
            Check(!Policy.Allowed(@"C:\Temp\aTrustTray.exe"),"same name outside install rejected");
            Check(!Policy.Allowed(Policy.Root + @"-fake\aTrustTray\aTrustTray.exe"),"prefix lookalike rejected");
            Check(!Policy.Allowed(Policy.Root + @"\other\aTrustTray.exe"),"unapproved subdirectory rejected");
            Check(!Policy.Allowed(Policy.Client + ":stream"),"alternate stream rejected");
            Check(!Policy.Allowed(null) && !Policy.Allowed("aTrustTray.exe"),"unknown and relative path rejected");
            Check(!Policy.Allowed(Policy.Root + @"\aTrustTray\..\aTrustTray\aTrustTray.exe"),"unnormalized path rejected");
            Check(!Policy.ValidAction("disable") && !Policy.ValidAction("delete"),"unsupported actions rejected");
            Check(OnDemand.ManagedFile(Policy.Agent),"managed exit includes installed aTrust image");
            Check(OnDemand.ManagedFile(Diagnostics.EaioRoot+@"\eaio_service.exe"),"managed exit includes installed EAIO image");
            Check(!OnDemand.ManagedFile(Diagnostics.EaioRoot+@"-fake\eaio_service.exe"),"managed exit rejects lookalike install directory");
            Check(!OnDemand.ManagedFile(Diagnostics.EaioRoot+@"\..\other.exe"),"managed exit rejects traversal path");
            Check(!OnDemand.ManagedFile(@"C:\Windows\System32\svchost.exe"),"managed exit excludes Windows services");
            Check(!OnDemand.ManagedFile(@"C:\Program Files\Tailscale\tailscaled.exe"),"managed exit excludes other network tools");
            Check(Policy.ErrorCode(new Win32Exception(1223))==10,"UAC cancellation classified");
            Check(Policy.ErrorCode(new Win32Exception(5))==11,"access denied classified");
            Check(Policy.ErrorCode(new InvalidOperationException("wrapper",new Win32Exception(5)))==11,"nested service denial classified");
            var f=new Fake(); Operations.Run(f,"start"); Check(f.State.Service=="Running" && f.State.Mode=="Auto","start preserves startup mode");
            f=new Fake(); f.State.Service="Running"; Operations.Run(f,"start"); Check(!f.Calls.Contains("start"),"already running start is idempotent");
            f=new Fake(); f.State.Mode="Disabled"; Fails(()=>Operations.Run(f,"start"),11,"disabled service stays disabled"); Check(!f.Calls.Contains("start") && !f.Calls.Contains("mode"),"disabled policy preserved");
            f=new Fake(); f.IgnoreStart=true; Fails(()=>Operations.Run(f,"start"),15,"start requires running verification");
            f=new Fake(); Operations.Run(f,"stop"); Check(f.State.Exited && !f.Calls.Contains("kill") && !f.Calls.Contains("stop"),"already exited stop is idempotent");
            f=new Fake(); f.State.Service="Running"; f.Add(); f.Add(); Operations.Run(f,"stop"); Check(f.State.Exited && f.Calls.IndexOf("stop")<f.Calls.IndexOf("kill"),"stop before killing all residual processes");
            f=new Fake(); f.State.Service="Running"; f.FailStop=true; f.Add(); Fails(()=>Operations.Run(f,"stop"),11,"stop failure propagated"); Check(!f.Calls.Contains("kill"),"failed stop never kills residuals");
            f=new Fake(); f.State.Service="Running"; f.IgnoreStop=true; f.Add(); Fails(()=>Operations.Run(f,"stop"),15,"stopped state must be verified"); Check(!f.Calls.Contains("kill"),"unverified stop never kills");
            f=new Fake(); f.State.Unknown=1; Fails(()=>Operations.Run(f,"stop"),13,"unknown path prevents false complete result");
            f=new Fake(); f.Respawn=true; f.Add(); Fails(()=>Operations.Run(f,"stop"),15,"respawning process reports failure");
            f=new Fake(); Operations.Run(f,"manual"); Check(f.State.Mode=="Manual" && f.State.Service=="Stopped","explicit manual setting preserves runtime state");
            Operations.Run(f,"auto"); Check(f.State.Mode=="Auto","explicit automatic setting verified");
            f=new Fake(); f.IgnoreMode=true; Fails(()=>Operations.Run(f,"manual"),15,"startup setting requires verification");
            Fails(()=>Operations.Run(new Fake(),"delete"),1,"invalid action rejected before system mutation");
            Fails(()=>OnDemand.Run("delete"),1,"on-demand invalid action rejected before backup or mutation");
            var unknown=new Snapshot { Service="Stopped" }; unknown.AddUnknown("aTrustAgent");
            Check(unknown.UnknownCount("aTrustAgent")==1 && unknown.UnknownCount("aTrustTray")==0 && !unknown.Exited,"unreadable process cannot appear absent or fully exited");
            if(args.Length==0) { Console.WriteLine("TOTAL " + count + " passed; no installed client or admin rights required."); return 0; }
            if(args.Length!=1 || args[0]!="--live") throw new Exception("Usage: Tests.exe [--live]");
            Policy.VerifyFile(Policy.Agent); Policy.VerifyFile(Policy.Client);
            Snapshot actual=new WindowsSystem().Read();
            Check(!String.IsNullOrEmpty(actual.Service) && (actual.Mode=="Auto" || actual.Mode=="Manual" || actual.Mode=="Disabled"),"live service status readable without assuming an idle machine");
            string decoyPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"aTrustTray.exe");
            using (Process decoy=Process.Start(new ProcessStartInfo(decoyPath,"--decoy") { UseShellExecute=false,CreateNoWindow=true })) {
                try {
                    Thread.Sleep(300);
                    Check(!new WindowsSystem().Read().Processes.Exists(p=>p.Id==decoy.Id),"live unrelated same-name process excluded from status");
                    Fails(()=>new WindowsSystem().Kill(new Proc { Id=decoy.Id,Name="aTrustTray",Path=Policy.Client }),14,"live handle path recheck rejects unrelated process");
                    Check(!decoy.HasExited,"live unrelated same-name process remains running");
                    var kill=typeof(OnDemand).GetMethod("Kill",BindingFlags.NonPublic|BindingFlags.Static);
                    Fails(()=>kill.Invoke(null,new object[]{new Proc { Id=decoy.Id,Name="aTrustTray",Path=decoyPath }}),14,"managed exit validates live handle and rejects out-of-scope image");
                    Check(!decoy.HasExited,"managed exit preserves unrelated same-name process");
                } finally { if (!decoy.HasExited) { decoy.Kill(); decoy.WaitForExit(); } }
            }
            Console.WriteLine("TOTAL " + count + " passed; live checks are read-only."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
