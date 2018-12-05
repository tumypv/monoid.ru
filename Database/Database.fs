module monoid.Database

open System
open System.Data

open monoid.DataModel
open Problem
open User
open MySql.Data.MySqlClient
open WebSharper

type CommandBuilder = { commandType: CommandType; commandText: string; commandParams: (string * obj) list }

type DB(connString: string) =
    let db =
        let connString = connString
        let conn = new MySql.Data.MySqlClient.MySqlConnection(connString)
        do conn.Open()
        conn
    
    let createProcCommand (procName: string) =
        let cmd = db.CreateCommand()
        cmd.CommandType <- CommandType.StoredProcedure
        cmd.CommandText <- procName
        cmd

    let createSqlCommand (query: string) =
        let cmd = db.CreateCommand()
        cmd.CommandType <- CommandType.Text
        cmd.CommandText <- query
        cmd

    let buildProcCommand (procName: string) =
        { commandType = CommandType.StoredProcedure; commandText = procName; commandParams = [] }

    let buildSqlCommand (sql: string) =
        { commandType = CommandType.Text; commandText = sql; commandParams = [] }
    
    let addParam (name: string) (value: obj) (cmd: CommandBuilder) = { 
        cmd with 
            commandParams = 
                (name, if value = null then DBNull.Value :> obj else value) 
                :: cmd.commandParams 
    }

    let addParamTruncated (name: string) (value: string) (truncateToLength: int) (cmd: CommandBuilder) = {
        cmd with
            commandParams = 
                ("@" + name, if value = null then DBNull.Value :> obj else value.Substring(0, min value.Length truncateToLength) :> obj)
                :: cmd.commandParams 
    }

    let getCommand (builder: CommandBuilder): MySqlCommand =
        let cmd = db.CreateCommand()        
        cmd.CommandType <- builder.commandType
        cmd.CommandText <- builder.commandText
        List.iter
            (fun (name, value) -> cmd.Parameters.AddWithValue(name, value) |> ignore)
            builder.commandParams
        cmd

    let execScalar (builder: CommandBuilder) = 
        use cmd = getCommand builder
        cmd.ExecuteScalar()

    let execNonquery (builder: CommandBuilder) = 
        use cmd = getCommand builder
        cmd.ExecuteNonQuery()

    let expect (expected: 'T) (message: string) (actual: 'T)  = 
        if expected <> actual then failwith message

    let execReader (makeRow: MySqlDataReader -> 'Row) (builder: CommandBuilder) : 'Row list =
        use cmd = getCommand builder
        use dr = cmd.ExecuteReader()
        let rec inorder () =
            seq {
                if dr.Read () then
                    yield makeRow dr
                    yield! inorder ()
            }
        List.ofSeq (inorder ())

    member this.createNewProblem (title: string) (content: string) =
        buildProcCommand "createProblem"
        |> addParamTruncated "title" title 100
        |> addParam "content" content
        |> execScalar
        |> unbox<uint64> 
        |> int

    member this.updateProblem (id: int) (title: string) (content: string) =
        buildProcCommand "updateProblem"
        |> addParam "id" id
        |> addParamTruncated "title" title 100
        |> addParam "content" content
        |> execNonquery

    member this.loadProblem (id: int) =
        buildSqlCommand "select title, content from problem where id = @id;"
        |> addParam "id" id
        |> execReader (fun dr -> dr.GetString(0), dr.GetString(1))
        |> List.exactlyOne

    member this.GetProblems () =
        buildProcCommand "get_problems"
        |> execScalar
        |> unbox string
        |> fun s -> Json.Deserialize<ProblemHeadline list> s

    member this.GetUserSolutions (userId: int) =
        buildProcCommand "get_user_solutions"
        |> addParam "user_id" userId
        |> execScalar
        |> unbox string
        |> fun s -> 
            if not (String.IsNullOrWhiteSpace s)
                then Json.Deserialize<SolutionSummary list> s
                else []

    member this.GetOrCreateUser(user: ProviderUser) : Async<User.Id> = async {
        return
            match user with
            | GitHubProviderUser u -> 
                buildProcCommand "createGitHubUser"
                |> addParam "login" u.login
                |> addParam "name" u.name
            | VkProviderUser u ->
                buildProcCommand "createVkUser"
                |> addParam "id" u.id
                |> addParam "@first_name" u.first_name
                |> addParam "@last_name" u.last_name
            |> execScalar
            |> unbox<int>
    }

    member this.GetUserData(userId: User.Id) : Async<User.UserBasicInfo option> = async {
        return
            buildSqlCommand "select full_name from user where id = @id;"
            |> addParam "id" userId
            |> execReader (fun dr -> { id = userId; fullName = dr.GetString(0)})
            |> List.tryHead
    }

    member this.SaveProblem (problem : Problem.ProblemDescription) = async {
        let content = WebSharper.Json.Serialize problem.description
        match problem.id with
        | Some u -> return this.updateProblem u problem.title content
        | None -> return this.createNewProblem problem.title content
    }

    member this.SubmitSolution (userId: int) (problemId: int) (source: string) =
        buildProcCommand "submit_solution"
        |> addParam "user" userId
        |> addParam "problem" problemId
        |> addParam "source" source
        |> execScalar
        |> unbox<uint64> 
        |> int

    member this.LoadProblem (id: int) = async {
        let title, json = this.loadProblem id
        let problem = WebSharper.Json.Deserialize json
        return { id = Some id; title = title; description = problem }
    }

    member this.CheckSolutionBegin() : SolutionToCheck option =
        buildProcCommand "check_solution_begin"
        |> execScalar
        |> unbox string
        |> fun s -> 
            if String.IsNullOrWhiteSpace(s) then None
            else Some(WebSharper.Json.Deserialize s)

    member this.SaveTestResult solutionId testId memory result output =
        buildProcCommand "save_test_result"
        |> addParam "solution" solutionId
        |> addParam "test" testId
        |> addParam "memory" memory
        |> addParam "result" result
        |> addParamTruncated "output" output 250
        |> execNonquery
        |> expect 1 "SaveTestResult failed"

    member this.CheckSolutionEnd solutionId (compilationError: string option) (tokenCount: int) (literalLength: int) =
        buildProcCommand "check_solution_end"
        |> addParam "solution" solutionId
        |> addParamTruncated "error" (Option.defaultValue null compilationError) 250
        |> addParam "token_count" tokenCount
        |> addParam "literal_length" literalLength
        |> execNonquery
        |> expect 1 "CheckSolutionEnd failed"

    member this.GetHallOfFame () : HallOfFameRaw list =
        buildProcCommand "get_hall_of_fame"
        |> execScalar
        |> unbox string
        |> Json.Deserialize

    member this.getUsers () : UserBasicInfo list =
        buildProcCommand "get_users"
        |> execScalar
        |> unbox string
        |> Json.Deserialize
    
    member this.GetSolution (solutionId: int) : Solution =
        buildProcCommand "get_solution"
        |> addParam "solution_id" solutionId
        |> execScalar
        |> unbox string
        |> Json.Deserialize

    member this.GetSolutionState (solutionId: int) : Solution.State =
        buildProcCommand "get_solution_state"
        |> addParam "solution_id" solutionId
        |> execScalar
        |> unbox string
        |> function
            | "submitted" -> Solution.State.Submitted
            | "checking" -> Solution.State.Checking
            | "checked" -> Solution.State.Checked
            | _ -> raise (new Exception())

    member this.GetUserSolutionsForProblem (userId: int) (problemId: int) =
        buildProcCommand "get_user_solutions_for_problem"
        |> addParam "user_id" userId
        |> addParam "problem_id" problemId
        |> execScalar
        |> unbox string
        |> fun s -> 
            if not (String.IsNullOrWhiteSpace s)
                then Json.Deserialize<SolutionSummary list> s
                else []

    interface IDisposable with
        member this.Dispose() = db.Dispose ()

 (* solution_summary
   select 
    `s`.`id` AS `id`,
    `s`.`user` AS `user`,
    `s`.`problem` AS `problem`,
    `s`.`server_time` AS `submitDate`,
    coalesce(`s`.`compilation_error`, if(`tr`.`result` = 'runtime_error', `tr`.`output`, NULL)) AS `error`,
    coalesce(if((`s`.`state` = 'checked'),NULL,`s`.`state`),if(isnull(`s`.`compilation_error`),NULL,'compilation_error'),`tr`.`result`,'accepted') AS `verdict`,
    `t`.`n` AS `failedTest`,
    (select max(`tr`.`memory`) from `test_result` `tr` where ((`tr`.`result` = 'passed') and (`tr`.`solution` = `s`.`id`))) AS `memory`,
    s.token_count, s.literal_length
from 
    `solution` `s` 
    left join `test_result` `tr` on `s`.`id` = `tr`.`solution` and `tr`.`result` <> 'passed'
    left join `test` `t` on `tr`.`test` = `t`.`id`
*)