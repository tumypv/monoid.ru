﻿open System
open System.Reflection
open System.IO
open System.Security
open System.Security.Permissions
open System.Threading
open System.Diagnostics

type Proxy () =
    inherit MarshalByRefObject()
    member this.Run (bytes : byte []) =
        let ass = Assembly.Load(bytes, null, SecurityContextSource.CurrentAppDomain)
        let t = ass.EntryPoint
        let p:obj [] = if (t.GetParameters()).Length > 0 then [|[| "" |]|] else [| |]
        (new PermissionSet(PermissionState.Unrestricted)).Assert();
        try t.Invoke(null, p) |> ignore; 0
        with ex -> Console.Error.WriteLine(if ex.InnerException <> null then ex.InnerException.ToString() else ex.ToString()); 1
        

let runInSandbox (workingDir: string) (assemblyFile: string) =
    let permissionSet =
        let inputPath = Path.Combine (workingDir, "INPUT.TXT")
        let outputPath = Path.Combine (workingDir, "OUTPUT.TXT")
        let set = new PermissionSet(PermissionState.None)
        //set.AddPermission (new CodeAccessPermission()) |> ignore
        set.AddPermission (new SecurityPermission(SecurityPermissionFlag.Execution)) |> ignore
        set.AddPermission (new FileIOPermission(FileIOPermissionAccess.Read, [| inputPath |])) |> ignore
        set.AddPermission (new FileIOPermission(FileIOPermissionAccess.Write, [| outputPath |])) |> ignore
        set

    let domain =
        let domainSetup = new AppDomainSetup()
        domainSetup.ApplicationBase <- workingDir
        let strongname = (typeof<Proxy>.Assembly.Evidence.GetHostEvidence<System.Security.Policy.StrongName>();)
        AppDomain.CreateDomain("Sandbox", null, domainSetup, permissionSet, strongname)

    let proxyType = typeof<Proxy>
    let assemblyFile0 = proxyType.Assembly.ManifestModule.FullyQualifiedName
    let handle = Activator.CreateInstanceFrom (domain, assemblyFile0, proxyType.FullName)
    let restrictedDomain = handle.Unwrap() :?> Proxy

    Directory.SetCurrentDirectory workingDir
    restrictedDomain.Run (File.ReadAllBytes(assemblyFile))

[<EntryPoint>]
let main argv =
    //let p = Process.GetCurrentProcess();
    //let tfn o =
    //    let rec watchdog () =
    //        let mem = p.PeakVirtualMemorySize64
    //        if mem > 16L * 1024L * 1024L then
    //            System.Console.Error.WriteLine("Memory limit exceeded")
    //            p.Kill()
    //        System.Threading.Thread.Sleep(5)
    //        watchdog()
    //    watchdog()

    //let t = new System.Threading.Thread(new ParameterizedThreadStart(tfn))
    //t.Start()
    
    runInSandbox argv.[0] argv.[1]
    
    //let l = Seq.init (1024 * 1024 * 16) (fun a -> a) |> List.ofSeq
    //printfn "%A" l
