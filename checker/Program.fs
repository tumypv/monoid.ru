open System
open System.IO
open System.Diagnostics
open monoid.Database
open monoid.DataModel.Problem

let compilerPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsc.exe"

let getProcessStartInfo exePath =
    let startInfo = new ProcessStartInfo (exePath)
    do startInfo.CreateNoWindow <- true
    do startInfo.LoadUserProfile <- false
    do startInfo.UseShellExecute <- false
    do startInfo.RedirectStandardError <- true
    do startInfo.RedirectStandardOutput <- true
    do startInfo.RedirectStandardInput <- true
    startInfo

type OutputCheckResult = Accepted | PresentationError | WrongAnswer

let checkSolutionOutput (test: Test) (solutionOutput: string) =
    let tokenize (s: string) = 
        let sep = [|' '; '\r'; '\n'|]
        s.Split(sep, StringSplitOptions.RemoveEmptyEntries)
    let testTokens = tokenize test.output
    let outputTokens = tokenize solutionOutput
    if testTokens.Length <> outputTokens.Length then
        PresentationError
    elif Seq.exists2 (<>) outputTokens testTokens then
        WrongAnswer
    else
        Accepted

type ExecutionResult = OutputReceived of string * int64 | MemoryLimitExceeded | TimeLimitExceeded | RuntimeError of string

let runRestricted (startInfo: ProcessStartInfo) (memoryLimit: int64) (timeLimit: int64) (inputPath: string) (outputPath: string) : ExecutionResult =
    use p = new Process()

    let kill () = 
        if not p.HasExited then 
            try p.Kill() 
            with | :? System.ComponentModel.Win32Exception -> ()

    let getMem () = 
        if not p.HasExited then
            p.Refresh()
            try Some(p.PeakWorkingSet64)
            with | :? System.InvalidOperationException -> None
        else
            None

    do p.StartInfo <- startInfo
    do p.Start() |> ignore
    do p.StandardInput.Write (File.ReadAllText(inputPath) + Environment.NewLine)

    let sw = System.Diagnostics.Stopwatch.StartNew()
    
    let rec loop lastMem =
        System.Threading.Thread.Sleep 1
        printf "*"
        let mem = Option.defaultValue lastMem (getMem ())
        let time = sw.ElapsedMilliseconds

        if mem > memoryLimit then
            kill ()
            MemoryLimitExceeded
        elif p.HasExited then
            if p.ExitCode > 0 then
                RuntimeError (p.StandardError.ReadToEnd ())
            elif File.Exists (outputPath)
                then OutputReceived (File.ReadAllText(outputPath), mem)
            else
                let output = p.StandardOutput.ReadToEnd ()
                OutputReceived (output, mem)
        elif time >= timeLimit then
            kill ()
            TimeLimitExceeded
        else
            loop mem
    let res = loop 0L
    res

let runInSandbox (sandboxDir: string) (exePath: string) (solution: SolutionToCheck) (processTestResult: Test -> ExecutionResult -> bool) =
    let sandboxerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sandboxer.exe")
    let inputPath = Path.Combine (sandboxDir, "INPUT.TXT")
    let outputPath = Path.Combine (sandboxDir, "OUTPUT.TXT")
    let runTest (test: Test) =
        let inputData = test.input.Replace("\n", Environment.NewLine)
        do File.WriteAllText (inputPath, inputData)
        let startInfo = getProcessStartInfo sandboxerPath
        do startInfo.Arguments <- sprintf "\"%s\" \"%s\"" sandboxDir exePath
        let testResult = runRestricted startInfo (solution.memory * 1024L * 1024L) (solution.time) inputPath outputPath 
        do if (File.Exists outputPath) then File.Delete outputPath
        do File.Delete inputPath
        processTestResult test testResult

    Array.forall runTest solution.tests

type BuildSuccess = { time : TimeSpan }
type BuildError = { message : string }

type BuildResult =
    | Success of BuildSuccess 
    | Error of BuildError 
    | Timeout

let buildExe srcPath outExePath =
    let startInfo = getProcessStartInfo compilerPath
    startInfo.Arguments <- sprintf 
        "--nologo --out:%s --target:exe --nointerfacedata --nowin32manifest --debug- --checked+ --preferreduilang:ru-ru --simpleresolution --noframework %s" 
        outExePath srcPath

    let p = Process.Start (startInfo)
    let exited = p.WaitForExit 10000

    if not exited then 
        do p.Kill ()
        Timeout
    elif p.ExitCode = 0 then
        Success { time = p.ExitTime - p.StartTime }
    else
        let output = p.StandardError.ReadToEnd().Trim()
        Error { message = output }

let cleanupSandboxDir sandboxDir =
    if not (Directory.Exists sandboxDir) then
        failwith (sprintf "Sandbox dir '%s' doesn't exists" sandboxDir)
    else
        let entries = Directory.EnumerateFileSystemEntries(sandboxDir, "*.*", SearchOption.TopDirectoryOnly)
        let delete (item: string) = 
            if File.Exists item then File.Delete item
            elif Directory.Exists item then Directory.Delete (item, true)
        Seq.iter delete entries

let check (db: DB) (solution: SolutionToCheck) =
    let sandboxDir = Path.Combine (AppDomain.CurrentDomain.BaseDirectory, "sandbox")
    do cleanupSandboxDir sandboxDir
    
    let srcPath = Path.Combine (sandboxDir, "solution.fs")
    do File.WriteAllText (srcPath, solution.source)
    let exePath = Path.Combine (sandboxDir, "solution.exe")
    let buildResult = buildExe srcPath exePath

    let processTestResult (test: Test) testResult =
        match testResult with
        | MemoryLimitExceeded ->
            db.SaveTestResult solution.id test.id 0 "memory_limit_exceeded" null
            false
        | TimeLimitExceeded ->
            db.SaveTestResult solution.id test.id 0 "time_limit_exceeded" null
            false
        | OutputReceived (o, mem) ->
            match checkSolutionOutput test o with
            | Accepted -> db.SaveTestResult solution.id test.id mem "passed" o; true
            | WrongAnswer -> db.SaveTestResult solution.id test.id mem "wrong_answer" o; false
            | PresentationError -> db.SaveTestResult solution.id test.id mem "presentation_error" o; false
        | RuntimeError s ->
            db.SaveTestResult solution.id test.id 0 "runtime_error" s; false

    match buildResult with
    | Success t ->
        let passed = runInSandbox sandboxDir exePath solution processTestResult
        db.CheckSolutionEnd solution.id None
    | Error e ->
        db.CheckSolutionEnd solution.id (Some e.message)
    | Timeout ->
        db.CheckSolutionEnd solution.id None

let loop () =
    use db = new DB "host=<your_db_host>;port=3306;user id=<your_checker_user>;password=<your_checker_password>;database=fsharp;"

    let rec mainLoop () =
        let solution = db.CheckSolutionBegin ()
        match solution with
        | Some s -> check db s
        | _ -> None
        System.Threading.Thread.Sleep(1000)
        printf "."
        mainLoop ()
    mainLoop ()

[<EntryPoint>]
let rec main argv =
    try
        loop()
    with 
    | :? System.Exception -> main argv