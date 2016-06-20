#I "packages/Suave/lib/net40"
#r "packages/Suave/lib/net40/Suave.dll"
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
open System
open System.IO
open Suave
open Suave.Filters
open Suave.Operators
open FSharp.Data

type Rect = JsonProvider<"""{"x1":0.0,"y1":0.0,"x2":10.0,"y2":10.0}""">

type Message =
  | AddRect of Rect.Root
  | GetRects of AsyncReplyChannel<list<Rect.Root>>

let agent = MailboxProcessor.Start(fun inbox ->
  let rec loop rects = async {
    let! msg = inbox.Receive()
    match msg with
    | AddRect(r) -> return! loop (r::rects)
    | GetRects(repl) ->
        repl.Reply(rects)
        return! loop rects }
  loop [] )

let webRoot = Path.Combine(__SOURCE_DIRECTORY__, "web")
let clientRoot = Path.Combine(__SOURCE_DIRECTORY__, "client")

let noCache =
  Writers.setHeader "Cache-Control" "no-cache, no-store, must-revalidate"
  >=> Writers.setHeader "Pragma" "no-cache"
  >=> Writers.setHeader "Expires" "0"

let app =
  choose [
    GET >=> path "/getrects" >=> fun ctx -> async {
      let! rects = agent.PostAndAsyncReply(GetRects)
      return! ctx |> Successful.OK((JsonValue.Array [| for r in rects -> r.JsonValue |]).ToString()) }
    POST >=> path "/addrect" >=> request (fun r ->
      use ms = new StreamReader(new MemoryStream(r.rawForm))
      agent.Post(AddRect(Rect.Parse(ms.ReadToEnd())))
      Successful.OK "added")
    path "/" >=> Files.browseFile webRoot "index.html"
    path "/out/client.js" >=> noCache >=> Files.browseFile clientRoot (Path.Combine("out", "client.js"))
    path "/out/client.js.map" >=> noCache >=> Files.browseFile clientRoot (Path.Combine("out", "client.js.map"))
    pathScan "/node_modules/%s.js" (sprintf "/node_modules/%s.js" >> Files.browseFile clientRoot)
    Files.browse webRoot
  ]
