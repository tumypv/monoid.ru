module monoid.ProblemPage

open WebSharper
open WebSharper.UI
open WebSharper.UI.Html
open Database

module Remoting =
    open Context
    open WebSharper.Sitelets
    open System.Security
    open DataModel.Problem

    [<Rpc>]
    let SubmitSolution (problem: int) (source: string) =
        let ctx = WebSharper.Web.Remoting.GetContext()
        async { 
            match! ctx.UserSession.GetLoggedInUser() with
            | Some u ->
                use db = ctx.Db.Connect ()
                return db.SubmitSolution (int u) problem source
            | _ -> return raise (new SecurityException ("Not authenticated"))
        }

    [<Rpc>]
    let CheckSolutionState (id: int) =
        let ctx = WebSharper.Web.Remoting.GetContext()
        async {
            use db = ctx.Db.Connect ()
            let state = db.GetSolutionState(id)
            return state
        }

    [<Rpc>]
    let GetSolutions (id: int) =
        let ctx = WebSharper.Web.Remoting.GetContext()
        async {
            match! ctx.UserSession.GetLoggedInUser() with
            | Some u ->
                use db = ctx.Db.Connect ()
                let r = db.GetUserSolutionsForProblem (int u) id
                return r
            | _ -> return raise (new SecurityException ("Not authenticated"))
        }

[<JavaScript>]
module Client =
    open WebSharper.Forms
    open WebSharper.JavaScript
    open DataModel.Problem
    open WebSharper.UI.Html
    open WebSharper.UI
    open WebSharper.UI.Client
    open monoid.DataModel
    open Routes

    let router = WebSharper.Sitelets.InferRouter.Router.Infer<EndPoint>()

    let vwSolutions = Var.Create (div [] [text "Empty"])

    let updateSolutions (taskId: int) =
        async {
            let! solutions = Remoting.GetSolutions taskId
            Console.Log solutions
            let doc = 
                let makeRow s = 
                    tr [] [
                        td [] [a [attr.href (EndPoint.Solution s.solution |> router.Link)] [text (Utils.formatDate s.submitDate)]]
                        td [] [text s.verdict]
                        td [] [
                            text (
                                match s.failedTest with
                                | Some n -> string n
                                | _ -> ""
                            )
                        ]
                        td [] [text (Utils.calcVerbosityScore s.verbosity |> string)]
                    ]

                table [attr.``class`` "check_results"] (
                    tr [] [
                        th [] [text "Время отправки"]
                        th [] [text "Результат проверки"]
                        th [] [text "№ теста"]
                        th [] [text "Многословность"]
                    ] :: (
                        solutions 
                        |> List.sortBy (fun s -> s.submitDate)
                        |> List.map makeRow
                    )
                )
            vwSolutions.Update (fun old -> doc)
        } |> Async.Start

    let Main (problemId: int) =

        updateSolutions problemId

        let formModel (init: string) =
            let combineFn (source: string) = 
                source

            Form.Return combineFn
            <*> (Form.Yield init)
            |> Form.WithSubmit
        
        let submitDisabled = Var.Create false

        let render (source : Var<string>) (submitter : Submitter<Result<string>>) =
            let getError (v : Var<string>) =
                let v = submitter.View.Through v
                v.Map (function
                    | Failure ll ->
                        match ll with
                        | h :: t -> h.Text
                        | [] -> ""
                    | _ -> 
                        ""
                )

            div [] [
                div [] [
                    label [] [text "Исходный код решения:"]
                    textView (getError source)
                ]
                Doc.InputArea [attr.cols "80"; attr.rows "4"; attr.wrap "true"] source
                Doc.Button 
                    "Отправить"
                    [Attr.DynamicPred "disabled" submitDisabled.View (Var.Create null).View] 
                    submitter.Trigger
            ]
        
        let vProblem = Var.Create ("")

        let saveForm source =
            submitDisabled.Set true
            let rec waitForChecker solutionId lastState =
                async {
                    do! Async.Sleep 2000
                    let! state = Remoting.CheckSolutionState solutionId
                    if state <> lastState then 
                        updateSolutions problemId
                    if state <> Solution.State.Checked then 
                        do! waitForChecker solutionId state
                    else
                        submitDisabled.Set false
                }

            async {
                let! solutionId = Remoting.SubmitSolution problemId source
                do updateSolutions problemId
                do! waitForChecker solutionId Solution.State.Submitted
            } |> Async.Start

        let createProblemForm problem =
            problem
            |> formModel
            |> Form.Run saveForm
            |> Form.Render render

        let vwForm = View.Map createProblemForm vProblem.View

        Doc.EmbedView vwForm

module Server =
    open WebSharper.Sitelets
    open DataModel
    open Routes
    open Context
    open WebSharper.UI.Html
    open DataModel.Problem

    let Content (taskId : int) (ctx: Context<EndPoint>) (user: User.UserBasicInfo) =
        use db = ctx.Db.Connect ()
        let problem = db.LoadProblem taskId |> Async.RunSynchronously
        let desc = problem.description
        
        let body = 
            let constructExample (i: int) (e: ProblemDescriptionExample) = 
                Templating.MainTemplate.ExampleTemplate()
                    .ExampleN(string i)
                    .ExampleInput(e.input)
                    .ExampleOutput(e.output)
                    .Doc()
            
            Templating.MainTemplate.ProblemTemplate()
                .ProblemId(string taskId)
                .Title(problem.title)
                .Description(Doc.Verbatim desc.problem)
                .InputFormat(Doc.Verbatim desc.inputFormat)
                .OutputFormat(Doc.Verbatim desc.outputFormat)
                .ProblemExample(List.mapi constructExample desc.examples)
                .PreviousSolutions(client <@ WebSharper.UI.Client.Doc.EmbedView Client.vwSolutions.View @>)
                .MySolution(client <@ Client.Main taskId @>)
                .Doc()

        Templating.Main ctx (EndPoint.MyProblems) "Мои задачи" [body]