[<WebSharper.JavaScript>]
module Utils
open monoid

let calcVerbosityScore (verbosity:monoid.DataModel.Problem.Verbosity) =
    verbosity.tokenCount + verbosity.literalLength / 4

let formatDate (date: System.DateTime) =
    sprintf "%02i.%02i.%02i %02i:%02i:%02i" date.Day date.Month date.Year date.Hour date.Minute date.Second
