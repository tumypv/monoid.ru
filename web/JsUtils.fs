[<WebSharper.JavaScript>]
module JsUtils
    let formatDate (date: System.DateTime) =
        sprintf "%02i.%02i.%02i %02i:%02i:%02i" date.Day date.Month date.Year date.Hour date.Minute date.Second