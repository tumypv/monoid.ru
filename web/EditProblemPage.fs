module monoid.EditProblemPage

open WebSharper
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Html


module Remoting =
    open Context
    open WebSharper.Sitelets
    open System.Security
    open DataModel.Problem

    [<Rpc>]
    let SaveProblem (problem : ProblemDescription) =
        let ctx = WebSharper.Web.Remoting.GetContext()
        async { 
            match! ctx.UserSession.GetLoggedInUser() with
            | Some u ->
                if u = "3" then
                    use db = ctx.Db.Connect ()
                    return! db.SaveProblem problem
                else
                    return raise (new SecurityException ("Not authorized"))
            | _ -> return raise (new SecurityException ("Not authenticated"))
        }

    [<Rpc>]
    let LoadProblem (id: int) =
        let ctx = WebSharper.Web.Remoting.GetContext()
        async {
            match! ctx.UserSession.GetLoggedInUser() with
            | Some u ->
                if u = "3" then
                    use db = ctx.Db.Connect ()
                    return! db.LoadProblem id
                else
                    return raise (new SecurityException ("Not authorized"))
            | _ -> return raise (new SecurityException ("Not authenticated"))
        }


[<JavaScript>]
module Client =
    open WebSharper.Forms
    open WebSharper.JavaScript
    open DataModel.Problem
    open WebSharper.UI.Html
    open WebSharper.UI.Html

    let emptyProblem = {
        id = None; 
        title = ""; 
        description = {
            problem = "<p></p>";
            inputFormat = "<p>Во входном файле INPUT.TXT сначала содержится число N (1 ≤ N ≤ 1000). Далее идут N натуральных чисел, не превосходящих 10000 -</p>";
            outputFormat = "<p>В выходной файл OUTPUT.TXT нужно вывести</p>";
            examples = []
        }
    }

    let Main (taskId: int option) =
        let notEmpty a = Validation.IsNotEmpty "Поле не заполнено" a

        let exampleModel (init: ProblemDescriptionExample) = 
            Form.Return (fun i o -> { input = i; output = o })
            <*> (Form.Yield init.input |> notEmpty)
            <*> (Form.Yield init.output |> notEmpty)

        let renderExamples i o =
            Doc.Concat [
                Doc.InputArea [attr.cols "30"; attr.rows "4"; attr.wrap "false" ] i
                Doc.InputArea [attr.cols "30"; attr.rows "4"; attr.wrap "false" ] o
            ]

        let formModel (init: ProblemDescription)  =
            let combineFn id t p i o (ex : ProblemDescriptionExample seq) = 
                {
                    id = id
                    title = t;
                    description = {
                        problem = p; 
                        inputFormat = i; 
                        outputFormat = o; 
                        examples = List.ofSeq ex
                    }
                }

            Form.Return combineFn
            <*> (Form.Yield init.id)
            <*> (Form.Yield init.title |> notEmpty)
            <*> (Form.Yield init.description.problem |> notEmpty)
            <*> (Form.Yield init.description.inputFormat |> notEmpty)
            <*> (Form.Yield init.description.outputFormat |> notEmpty)
            <*> Form.Many init.description.examples { input = "[]"; output = "[]" } exampleModel
            |> Form.WithSubmit
        
        let render 
            (id : Var<int option>) (t : Var<string>) (p : Var<string>) (i : Var<string>) (o : Var<string>)
            (examples: Form.Many.CollectionWithDefault<ProblemDescriptionExample, _, _>)
            (submitter : Submitter<Result<ProblemDescription>>) =

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
                    id.View.Map (fun v -> 
                        match v with
                        | Some id -> sprintf "Задача №%i" id
                        | None -> "Новая задача"
                    )
                    |> Doc.TextView 
                ]
                div [] [
                    div [] [
                        text "Название задачи" 
                        textView (getError t)
                    ]
                    label [] []
                    Doc.Input [attr.size "40"] t
                ]
                div [] [
                    div [] [
                        label [] [text "Усвловия"] 
                        textView (getError p)
                    ]
                    Doc.InputArea [attr.cols "80"; attr.rows "6"; attr.wrap "true"] p
                ]
                div [] [
                    div [] [
                        label [] [text "Формат входных данных"]
                        textView (getError i)
                    ]
                    Doc.InputArea [attr.cols "80"; attr.rows "4"; attr.wrap "true"] i
                ]
                div [] [
                    div [] [
                        label [] [text "Формат выходных данных"]
                        textView (getError o)
                    ]
                    Doc.InputArea [attr.cols "80"; attr.rows "4"; attr.wrap "true"] o
                ]

                h1 [] [text "Examples"]
                
                div [] [
                    examples.Render (fun ops (input: Var<string>) (output: Var<string>) -> 
                        div [] [
                            renderExamples input output
                            Doc.ButtonValidate "Вверх" [] ops.MoveUp
                            Doc.ButtonValidate "Вниз" [] ops.MoveDown
                            Doc.Button "Удалить" [] ops.Delete
                        ])
                ]

                div [] [
                    Doc.Button "Добавить пример" [] examples.Add
                    Doc.Button "Сохранить" [] submitter.Trigger
                ]
            ]

        
        let vProblem = Var.Create (emptyProblem)

        let saveForm pd = 
            async {
                let! id = Remoting.SaveProblem pd
                vProblem.Set ({ pd with id = Some id })
            } |> Async.Start

        let createProblemForm problem =
            problem
            |> formModel
            |> Form.Run saveForm
            |> Form.Render render

        let vwForm = View.Map createProblemForm vProblem.View


        async {
            match taskId with
            | Some id -> 
                let! p = Remoting.LoadProblem id
                vProblem.Set p
            | None -> ()
        } |> Async.Start
        
        div [] [
            Doc.EmbedView vwForm
        ]


module Server =
    open WebSharper.Sitelets
    open DataModel
    open Routes

    let Content (taskId: int option) (ctx: Context<EndPoint>) (user: User.UserBasicInfo) =

        let body = 
            Templating.MainTemplate.ProblemsTemplate()
                .MyContent(client <@ Client.Main taskId @>)
                .Doc()
        Templating.Main ctx (EndPoint.EditProblem taskId) "Редактор задач" [body]