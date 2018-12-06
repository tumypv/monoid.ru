module Utils
open monoid
open Microsoft.Extensions.Configuration

[<WebSharper.JavaScript>]
let calcConcisenessScore (conciseness:monoid.DataModel.Problem.Conciseness) =
    conciseness.tokenCount + conciseness.literalLength / 4

[<WebSharper.JavaScript>]
let formatDate (date: System.DateTime) =
    sprintf "%02i.%02i.%02i %02i:%02i:%02i" date.Day date.Month date.Year date.Hour date.Minute date.Second

let getAppSetting (config: IConfiguration) (section: string) (key: string) =
    let section = config.GetSection(section)
    section.GetValue(key, "")