open System
open System.IO
open System.Diagnostics
open monoid.Database
open monoid.DataModel.Problem
open Microsoft.FSharp.Compiler.SourceCodeServices
open System.Diagnostics
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Configuration.Json

type OutputCheckResult = Accepted | PresentationError | WrongAnswer

type ExecutionResult = OutputReceived of string * int64 | MemoryLimitExceeded | TimeLimitExceeded | RuntimeError of string

type BuildSuccess = { time : TimeSpan }
type BuildError = { message : string }

type BuildResult =
    | Success of BuildSuccess 
    | Error of BuildError 
    | Timeout

let config =
    ConfigurationBuilder()
        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appsettings.json")
        .Build()

let private getAppSetting (section: string) (key: string) =
    let section = config.GetSection(section)
    section.GetValue(key, "")

let compilerPath = getAppSetting "path" "compiler"
let sandboxDir = getAppSetting "path" "sandboxdir"
let sandboxerPath = getAppSetting "path" "sandboxer"
let connectionString = config.GetConnectionString("monoid")

if compilerPath = "" || not <| File.Exists compilerPath then 
    failwith "F# compiler not found"
if sandboxDir = "" || not <| Directory.Exists sandboxDir  then 
    failwith "Sandbox dir not found"
if sandboxerPath = "" || not <| File.Exists sandboxerPath then 
    failwith "Sandboxer not found"

Directory.SetCurrentDirectory sandboxDir

let inputPath = Path.Combine (sandboxDir, "INPUT.TXT")
let outputPath = Path.Combine (sandboxDir, "OUTPUT.TXT")
let srcPath = Path.Combine (sandboxDir, "solution.fs")
let exePath = Path.Combine (sandboxDir, "solution.exe")
let checkerSrcPath = Path.Combine (sandboxDir, "checker.fs")
let checkerExePath = Path.Combine (sandboxDir, "checker.exe")
let fsharpCorePath = Path.Combine (sandboxDir, "FSharp.Core.dll")

let cleanupSandboxDir () =
    if not (Directory.Exists sandboxDir) then
        failwith (sprintf "Sandbox dir '%s' doesn't exists" sandboxDir)
    else
        let entries = Directory.GetFiles(sandboxDir, "*.*", SearchOption.TopDirectoryOnly)
        let delete (item: string) =
            if not <| String.Equals (item, fsharpCorePath, StringComparison.InvariantCultureIgnoreCase)
            then File.Delete item
        Seq.iter delete entries

let getProcessStartInfo exePath =
    let startInfo = new ProcessStartInfo (exePath)
    startInfo.CreateNoWindow <- true
    startInfo.LoadUserProfile <- false
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardError <- true
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardInput <- true
    startInfo

let buildExe srcPath outExePath =
    let startInfo = getProcessStartInfo compilerPath

    let nocopyfsharpcore =
        if File.Exists fsharpCorePath then " --nocopyfsharpcore" else ""

    startInfo.Arguments <- sprintf 
        "--nologo --out:%s --target:exe --nointerfacedata --nowin32manifest --debug- --checked+ --preferreduilang:ru-ru --simpleresolution --noframework%s %s"
        outExePath nocopyfsharpcore srcPath

    let p = Process.Start (startInfo)
    let exited = p.WaitForExit 10000

    if not exited then 
        p.Kill ()
        Timeout
    elif p.ExitCode = 0 then
        Success { time = p.ExitTime - p.StartTime }
    else
        let output = p.StandardError.ReadToEnd().Trim()
        Error { message = output }

let runRestricted (startInfo: ProcessStartInfo) (memoryLimit: int64) (timeLimit: int64) (inputPath: string) (outputPath: string) (isChecker: bool) : ExecutionResult =
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

    p.StartInfo <- startInfo
    p.Start() |> ignore
    if not isChecker then
        p.StandardInput.Write (File.ReadAllText(inputPath) + Environment.NewLine)

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
            if p.ExitCode <> 0 then
                RuntimeError (p.StandardError.ReadToEnd ())
            elif (not isChecker) && File.Exists (outputPath)
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

let basicSolutionOutputChecker (test: Test) (solutionOutput: string) =
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

let customSolutionOutputChecker (test: Test) (solutionOutput: string) (checkerExePath: string) =
    let inputData = test.input.Replace("\n", Environment.NewLine)
    File.WriteAllText (inputPath, inputData)
    File.WriteAllText (outputPath, solutionOutput)
    let startInfo = getProcessStartInfo checkerExePath
    startInfo.Arguments <- sprintf "\"%s\" \"%s\" checker" sandboxDir checkerExePath
    let testResult = runRestricted startInfo (256L * 1024L * 1024L) (3L * 1000L) inputPath outputPath true

    let r = 
        match testResult with
        | MemoryLimitExceeded ->
            failwith "memory_limit_exceeded"
        | TimeLimitExceeded ->
            failwith "time_limit_exceeded"
        | OutputReceived (o, mem) ->
            match List.ofArray (o.Split(Environment.NewLine)) with
            | "presentation_error" :: t -> PresentationError
            | "wrong_answer" :: t -> WrongAnswer
            | "accepted" :: t -> Accepted
            | _ -> failwith "unknown checker output"
        | RuntimeError s ->
            failwith "runtime_error"
    r

let calcLengthScore (source: string) : int * int =
    let sourceTok = FSharpSourceTokenizer([], Some "")

    let tokenizeLine lst state line =
        let tokenizer = sourceTok.CreateLineTokenizer line
        let rec doWork acc state = 
            match tokenizer.ScanToken(state) with
            | Some tok, nstate -> doWork (tok :: acc) nstate
            | None, nstate -> acc, nstate
        doWork lst state

    let allTokens =
        let folder (tt, s) l =
            let tokens, state = tokenizeLine tt s l
            (tokens, state)
        source.Replace("\r", "").Split "\n"
        |> Seq.fold folder ([], 0L) 
        |> fst
        |> List.rev

    let codeTokens =
        allTokens
        |> Seq.filter (fun l -> 
            l.CharClass <> FSharpTokenCharKind.Comment 
            && l.CharClass <> FSharpTokenCharKind.WhiteSpace
            && l.CharClass <> FSharpTokenCharKind.LineComment
            && l.CharClass <> FSharpTokenCharKind.String
            && l.CharClass <> FSharpTokenCharKind.Literal
        )
        |> Seq.length

    let literals = 
        allTokens
        |> Seq.filter (fun l -> l.CharClass = FSharpTokenCharKind.String || l.CharClass = FSharpTokenCharKind.Literal)
        |> Seq.sumBy(fun t -> t.FullMatchedLength)
    
    codeTokens, literals

let runSolutionInSandbox (solution: SolutionToCheck) (processTestResult: Test -> ExecutionResult -> bool) =
    let runTest (test: Test) =
        let inputData = test.input.Replace("\n", Environment.NewLine)
        File.WriteAllText (inputPath, inputData)
        if File.Exists outputPath then File.Delete outputPath
        let startInfo = getProcessStartInfo sandboxerPath
        startInfo.Arguments <- sprintf "\"%s\" \"%s\"" sandboxDir exePath
        let testResult = runRestricted startInfo (solution.memory * 1024L * 1024L) (solution.time) inputPath outputPath false
        processTestResult test testResult

    Array.forall runTest solution.tests


let check (db: DB) (solution: SolutionToCheck) =
    cleanupSandboxDir ()

    File.WriteAllText (srcPath, solution.source)
    let buildResult = buildExe srcPath exePath

    match solution.customChecker with
    | Some checker ->
        File.WriteAllText (checkerSrcPath, checker)
        match buildExe checkerSrcPath checkerExePath with
        | Success _ -> ()
        | _ -> failwith "Failed to build checker"
    | _ -> ()

    let processTestResult (test: Test) testResult =
        match testResult with
        | MemoryLimitExceeded ->
            db.SaveTestResult solution.id test.id 0 "memory_limit_exceeded" null
            false
        | TimeLimitExceeded ->
            db.SaveTestResult solution.id test.id 0 "time_limit_exceeded" null
            false
        | OutputReceived (o, mem) ->
            let checkResult =
                match solution.customChecker with
                | Some checker -> customSolutionOutputChecker test o checkerExePath
                | None -> basicSolutionOutputChecker test o
            match checkResult with
                | Accepted -> db.SaveTestResult solution.id test.id mem "passed" o; true
                | WrongAnswer -> db.SaveTestResult solution.id test.id mem "wrong_answer" o; false
                | PresentationError -> db.SaveTestResult solution.id test.id mem "presentation_error" o; false
        | RuntimeError s ->
            db.SaveTestResult solution.id test.id 0 "runtime_error" s; false

    let tokenCount, literalLength = calcLengthScore solution.source

    match buildResult with
    | Success t ->
        let passed = runSolutionInSandbox solution processTestResult
        db.CheckSolutionEnd solution.id None tokenCount literalLength
    | Error e ->
        db.CheckSolutionEnd solution.id (Some e.message) tokenCount literalLength
    | Timeout ->
        db.CheckSolutionEnd solution.id None tokenCount literalLength

let loop () =
    use db = new DB(connectionString)

    let rec mainLoop () =
        let solution = db.CheckSolutionBegin ()
        match solution with
        | Some s -> check db s
        | _ -> ()
        System.Threading.Thread.Sleep(1000)
        printf "."
        mainLoop ()
    mainLoop ()

[<EntryPoint>]
let rec main argv =
    try 
        loop() 
    with ex -> printfn "%s" ex.Message
    main argv