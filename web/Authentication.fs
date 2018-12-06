module monoid.Authentication

open System
open System.IO
open System.Net
open Microsoft.Extensions.Configuration
open WebSharper
open WebSharper.Sitelets
open WebSharper.OAuth
open WebSharper.OAuth.OAuth2
open Context

open DataModel
open Routes
open Database

/// Perform a GET request to an OAuth2-protected JSON API.
let JsonRequest<'JsonResponse> (url: string) (token: AuthenticationToken option) = async {
    let req =
        HttpWebRequest.CreateHttp(url,
            KeepAlive = false,
            UserAgent = "monoid.ru"
        )
    Option.iter (fun (t: AuthenticationToken) -> t.AuthorizeRequest(req)) token
    let! response = req.AsyncGetResponse()
    use reader = new StreamReader(response.GetResponseStream())
    let! jsonData = reader.ReadToEndAsync() |> Async.AwaitTask
    return Json.Deserialize<'JsonResponse> jsonData
}

/// The content to serve based on a user's authentication response.
let private redirectEndpoint getUserData (ctx: Context<EndPoint>) response = async {
    match response with
    | OAuth2.Success token ->
        // All good! The user is authenticated.
        let! (userId: User.Id) = getUserData ctx token
        do! ctx.UserSession.LoginUser(string userId)
        return! Content.RedirectTemporary EndPoint.MyProblems
    | _ ->
        // This is "normal" failure: the user simply rejected the authorization.
        do! ctx.UserSession.Logout()
        return! Content.RedirectTemporary EndPoint.Home
}

module GitHub =
    open Database
    open WebSharper.OAuth.OAuth2
    open User
    open Utils

    let service config = 
        ServiceSettings.Github(
            getAppSetting config "github" "app-id",
            getAppSetting config "github" "app-secret"
        )

    /// The OAuth2 authorization provider for GitHub.
    let Provider config =
        let getUserDataAsync (ctx : Context<EndPoint>) (token: AuthenticationToken) = 
            async {
                let! u = JsonRequest<GitHubUser> "https://api.github.com/user" (Some token)
                use db = ctx.Db.Connect ()
                return! db.GetOrCreateUser (GitHubProviderUser u)
            }

        OAuth2.Provider.Setup(
            service = service config,
            redirectEndpointAction = EndPoint.OAuth OAuthProvider.GitHub,
            redirectEndpoint = redirectEndpoint getUserDataAsync
        )

module VK =
    open WebSharper.OAuth.OAuth2
    open Database
    open User
    open Utils

    type private Response = {response : VkUser list}

    let service config =
        { 
            ClientId = getAppSetting config "vk" "app-id"
            ClientSecret = getAppSetting config "vk" "app-secret"
            AuthorizationEndpoint = getAppSetting config "vk" "auth-endpoint"
            TokenEndpoint = getAppSetting config "vk" "token-endpoint"
        }

    let Provider config =
        let getUserDataAsync (ctx : Context<EndPoint>) (token: AuthenticationToken) =
            async {
                //?access_token=19be6b2b134c9337507401bc64652303891de345c2185f4f9853c0e80ae9e3dec7633d047849404b5735d&v=5.87
                let! response = JsonRequest<Response> ("https://api.vk.com/method/getProfiles?v=5.87&access_token=" + token.Token) None
                use db = ctx.Db.Connect ()
                return! db.GetOrCreateUser <| VkProviderUser response.response.Head
            }

        OAuth2.Provider.Setup(
            service = service config,
            redirectEndpointAction = EndPoint.OAuth OAuthProvider.VK,
            redirectEndpoint = redirectEndpoint getUserDataAsync
        )

/// Get the user id of the currently logged in user.
let GetLoggedInUserId (ctx: Web.Context) : Async<User.Id option> = async {
    match! ctx.UserSession.GetLoggedInUser() with
    | None -> return None
    | Some s -> return Some (int s)
}

/// Get the user data of the currently logged in user.
let GetLoggedInUserData (ctx: Web.Context) : Async<User.UserBasicInfo option> = async {
    match! GetLoggedInUserId ctx with
    | None -> return None
    | Some uid ->
        use db = ctx.Db.Connect ()
        return! db.GetUserData(uid)
}

/// The set of all redirect endpoints for our OAuth providers.
let Sitelet config =
    let github = GitHub.Provider config
    let vk = VK.Provider config

    github.RedirectEndpointSitelet
    <|>
    vk.RedirectEndpointSitelet

/// Sanity check for the purpose of demonstration:
/// if this is false, then the providers aren't configured properly,
/// so we show instructions on the home page.
let IsConfigured config =
    let github = GitHub.service config
    let vk = VK.service config

    github.ClientId <> "" &&
    github.ClientSecret <> "" &&
    vk.ClientId <> "" &&
    vk.ClientSecret <> ""