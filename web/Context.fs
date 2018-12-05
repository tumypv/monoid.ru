module Context

open monoid.Database
open monoid.DataModel
open FSharp.Data.Sql
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open FSharp.Data.Sql.Transactions
open WebSharper
open WebSharper.AspNetCore

open monoid.DataModel.User
open WebSharper.JavaScript
open WebSharper.Sitelets.Http
open System.Data
open Problem
open System

type Context(config: IConfiguration, logger: ILogger<Context>) =
    member this.Connect () : DB = 
        let connString = config.GetSection("ConnectionStrings").["monoid"]
        new DB (connString)

type Web.Context with
    /// Get a new database context.
    member this.Db : Context =
        this.HttpContext().RequestServices.GetRequiredService<Context>()