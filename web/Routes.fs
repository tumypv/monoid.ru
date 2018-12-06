module monoid.Routes

open WebSharper
open WebSharper.Sitelets.InferRouter

type OAuthProvider = VK | GitHub | Guest

type EndPoint =
    | [<EndPoint "GET /">] Home
    | [<EndPoint "GET /problem">] Problem of int
    | [<EndPoint "GET /solution">] Solution of int
    | [<EndPoint "GET /myproblems">] MyProblems
    | [<EndPoint "GET /editproblem">] EditProblem of int option
    | [<EndPoint "GET /halloffame">] HallOfFame
    | [<EndPoint "GET /oauth">] OAuth of provider: OAuthProvider
    | [<EndPoint "GET /logout">] Logout
    | [<EndPoint "GET /guestlogin">] GuestLogin
    