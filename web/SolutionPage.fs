module monoid.SolutionPage

open WebSharper
open WebSharper.UI
open WebSharper.UI.Html
open Database

module Server =
    open WebSharper.Sitelets
    open DataModel
    open Routes
    open Context

    let Content (solutionId : int) (ctx: Context<EndPoint>) (user: User.UserBasicInfo) =
        use db = ctx.Db.Connect ()
        let solution = db.GetSolution solutionId
        if solution.summary.user <> user.id then
            raise (new System.Exception("Not authorized"))

        let body = 
            Templating.MainTemplate.SolutionTemplate()
                .MySolution(solution.source)
                .Error(Option.defaultValue "" solution.summary.error)
                .Doc()

        Templating.Main ctx (EndPoint.Solution solutionId) "Решение" [body]