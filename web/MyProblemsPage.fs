module monoid.MyProblemsPage

open WebSharper
open WebSharper.UI
open WebSharper.UI.Html
open Context

module Server =
    open WebSharper.Sitelets
    open DataModel
    open Routes
    open WebSharper.UI.Html
    open monoid.DataModel.Problem
    open WebSharper.UI.Html
    open WebSharper.UI.Html
    open Utils

    let Content (ctx: Context<EndPoint>) (user: User.UserBasicInfo) =
        use db = ctx.Db.Connect ()
        let allSolutions = db.GetUserSolutions user.id

        let problems =
            let fillRow (problem: ProblemHeadline) = 
                let solutions = allSolutions |> List.filter (fun s -> s.problem = problem.id) 

                let problemLink = 
                    a [attr.href (ctx.Link (EndPoint.Problem problem.id)) ] [
                        text problem.title
                    ]

                let problemState, conciseness = 
                    let s =
                        solutions
                        |> Seq.where (fun s -> s.verdict = "accepted") 
                        |> Seq.map (fun s -> s, calcConcisenessScore s.conciseness)
                        |> Seq.sortBy (fun (s, v) -> v)
                        |> Seq.tryHead

                    match s with
                    | None -> 
                        if not (List.isEmpty solutions) then 
                            span [attr.style "color: red"] [text "✘"], ""
                        else
                            Doc.Empty, ""
                    | Some s -> span [attr.style "color: green"] [text "✓"], s |> snd |> string

                (new Templating.MainTemplate.MyProblemsTemplate())
                    .ProblemN(string problem.id)
                    .ProblemTitle(problemLink)
                    .AcceptedSolution(problemState)
                    .Conciseness(conciseness)
                    .Doc()

            List.map fillRow (db.GetProblems ())

        let body = 
            Templating.MainTemplate.MyProblems()
                .Username(user.fullName)
                .MyProblems(problems)
                .Doc()
        Templating.Main ctx (EndPoint.MyProblems) "Мои задачи" [body]