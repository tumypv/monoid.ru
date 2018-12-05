namespace monoid

open System
open WebSharper
open WebSharper.AspNetCore
open WebSharper.Sitelets
open WebSharper.OAuth
open WebSharper.UI
open WebSharper.UI.Html
open WebSharper.UI.Server
open Microsoft.Extensions.Configuration
open monoid.DataModel
open monoid.Routes
open monoid.Authentication

type SecuredPage<'a> = Context<EndPoint> -> User.UserBasicInfo -> Async<Content<'a>>

type Site(config: IConfiguration) =
    inherit SiteletService<EndPoint>()

    let NotLoggedInErrorMessage (ctx: Context<EndPoint>) =
        let gitProvider = Authentication.GitHub.Provider config
        let vkProvider = Authentication.VK.Provider config

        Templating.MainTemplate.LoginTemplate()
            .GitHubLoginUrl(gitProvider.GetAuthorizationRequestUrl(ctx))
            .VkLoginUrl(vkProvider.GetAuthorizationRequestUrl(ctx))
            .Doc()

    let HomePage ctx =
        Templating.Main ctx EndPoint.Home "Monoid.ru" [
            Templating.MainTemplate.HomeTemplate()
                .Doc()
        ]

    let LoginPage (ctx: Context<EndPoint>) =
        let body = NotLoggedInErrorMessage ctx
        Templating.Main ctx EndPoint.MyProblems "Вход" [body]

    let LogoutPage (ctx: Context<EndPoint>) = async {
        do! ctx.UserSession.Logout()
        return! Content.RedirectTemporary EndPoint.Home
    }

    // Force using TLS 1.2 for HTTPS requests, because GitHub's API requires it.
    do System.Net.ServicePointManager.SecurityProtocol <- System.Net.SecurityProtocolType.Tls12
   
    let securedPage (ctx: Context<EndPoint>) (page: SecuredPage<'a>) = async {
        match! Authentication.GetLoggedInUserData ctx with
        | Some user -> return! page ctx user
        | None -> return! LoginPage ctx
    }

    override val Sitelet =
        Sitelet config
        <|>
        Application.MultiPage (fun (ctx: Context<EndPoint>) endpoint -> async {
            match endpoint with
            | EndPoint.Home -> return! HomePage ctx
            | EndPoint.Problem taskId -> return! securedPage ctx (ProblemPage.Server.Content taskId)
            | EndPoint.MyProblems -> return! securedPage ctx MyProblemsPage.Server.Content
            | EndPoint.EditProblem taskId -> return! securedPage ctx (EditProblemPage.Server.Content taskId)
            | EndPoint.HallOfFame -> return! securedPage ctx (HallOfFamePage.Server.Content)
            | EndPoint.Solution solutionId  -> return! securedPage ctx (SolutionPage.Server.Content solutionId)
            | EndPoint.Logout -> return! LogoutPage ctx
            // This is already handled by Auth.Sitelet above:
            | EndPoint.OAuth _ -> return! Content.ServerError
        })
