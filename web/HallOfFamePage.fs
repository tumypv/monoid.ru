module monoid.HallOfFamePage

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
    open DataModel.User
    open WebSharper.UI.Html
    open WebSharper.UI.Client
    open Utils

    let Content (ctx: Context<EndPoint>) (user: User.UserBasicInfo) =
        use db = ctx.Db.Connect ()
        let hallOfFameRaw = db.GetHallOfFame ()
        let hallOfFame = 
            hallOfFameRaw
            |> Seq.groupBy (fun r -> r.problem, r.user)
            |> Map.ofSeq
        
        let hallOfFameBest =
            hallOfFameRaw
            |> Seq.groupBy (fun r -> r.problem)
            |> Seq.map (fun (k, v) -> 
                let mm = v |> Seq.minBy (fun r -> calcVerbosityScore r.verbosity)
                let vv = calcVerbosityScore mm.verbosity
                mm.problem, (mm.user, vv)
            )
            |> Map.ofSeq
    
        let users = db.getUsers ()

        let makeRow (p: ProblemHeadline) = 
            let best = hallOfFameBest.TryFind p.id
            let makeCell (u: UserBasicInfo) =
                let isBest = best |> Option.map (fun (user, _) -> user = u.id)
                let attr =
                    if Option.defaultValue false isBest then
                        [attr.style "background-color:yellow;"]
                    else
                        []
                td attr [text (if hallOfFame.ContainsKey (p.id, u.id) then "+" else "")]
            tr [] (
                th [attr.``class`` "row-header"] [text p.title] 
                :: td [] [text (best |> Option.map (fun (u, v) -> string v) |> Option.defaultValue "")]
                :: List.map makeCell users)


        let rows = 
            let head = 
                tr [] (
                    th [attr.width "150px" ] [] 
                    :: th [attr.width "50px" ] []
                    :: List.map (fun (u: UserBasicInfo) -> th [attr.``class`` "rotate"] [div[] [span [] [text u.fullName]]]) users
                )
            head :: List.map makeRow (db.GetProblems ())

        let body =
            div [] [
                table [attr.``class`` "hall"] rows
                p [attr.style "text-align:center;font-size:14px;"] [text "Жёлтым выделены самые краткие решения."]
            ]
        
        Templating.Main ctx (EndPoint.HallOfFame) "Зал славы" [body]