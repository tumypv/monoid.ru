[<WebSharper.JavaScript>]
module Utils
open monoid

let calcConcisenessScore (conciseness:monoid.DataModel.Problem.Conciseness) =
    conciseness.tokenCount + conciseness.literalLength / 4

let formatDate (date: System.DateTime) =
    sprintf "%02i.%02i.%02i %02i:%02i:%02i" date.Day date.Month date.Year date.Hour date.Minute date.Second
