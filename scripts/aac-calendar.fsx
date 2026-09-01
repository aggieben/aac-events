#r "nuget: FSharp.SystemTextJson, 1.4.36"

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson

type Start = { localDate: string; localTime: string option }
type Dates = { start: Start }
type TmEvent = { name: string; url: string; dates: Dates }
type Embedded = { events: TmEvent[] }

type DiscoveryResponse = {
  [<JsonPropertyName("_embedded")>]
  _embedded: Embedded
}

let apiKey =
    match Environment.GetEnvironmentVariable("TM_API_KEY") with
    | null | "" -> failwith "TM_API_KEY is not set"
    | value -> value

let venueId = "KovZpZAJ67eA"
let url =
    $"https://app.ticketmaster.com/discovery/v2/events?apikey={apiKey}&venueId={venueId}&size=100&sort=date,asc"

let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
let http = new HttpClient()
http.DefaultRequestHeaders.UserAgent.ParseAdd("aac-ics-publisher/1.0")
let json = http.GetStringAsync(url).Result
let payload = JsonSerializer.Deserialize<DiscoveryResponse>(json, options)

let events =
    match payload with
    | null -> [||]
    | p when isNull (box p.Embedded) || isNull p.Embedded.events -> [||]
    | p ->
        p.Embedded.events
        |> Array.filter (fun e ->
            not (isNull e)
            && not (String.IsNullOrWhiteSpace e.dates.start.localDate))

let esc (s: string) =
    (s |> Option.ofObj |> Option.defaultValue "")
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\n", "\\n")

let fold (line: string) =
    let sb = StringBuilder()
    let rec loop (rest: string) =
        if rest.Length <= 75 then
            sb.Append(rest) |> ignore
        else
            sb.Append(rest.Substring(0, 75)).Append("\r\n ").Append("") |> ignore
            loop rest[75..]
    loop line
    sb.ToString()

let icsDate (localDate: string) =
    DateTime.Parse(localDate).ToString("yyyyMMdd")

let icsDateTime (localDate: string) (localTime: string) =
    let dt = DateTime.Parse($"{localDate}T{localTime}")
    dt.ToString("yyyyMMdd'T'HHmmss")

let nextDay (localDate: string) =
    DateTime.Parse(localDate).AddDays(1.0).ToString("yyyyMMdd")

let plusHours (localDate: string) (localTime: string) hours =
    DateTime.Parse($"{localDate}T{localTime}").AddHours(hours).ToString("yyyyMMdd'T'HHmmss")

let stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")

let vevents =
    [ for e in events do
        let start = e.dates.start
        let uid =
            match e.url with
            | null | "" -> $"{e.name}-{start.localDate}@americanairlinescenter.com"
            | u -> $"{u.GetHashCode():X}@americanairlinescenter.com"

        yield "BEGIN:VEVENT"
        yield $"UID:{uid}"
        yield $"DTSTAMP:{stamp}"

        if String.IsNullOrWhiteSpace start.localTime then
            yield $"DTSTART;VALUE=DATE:{icsDate start.localDate}"
            yield $"DTEND;VALUE=DATE:{nextDay start.localDate}"
        else
            yield $"DTSTART;TZID=America/Chicago:{icsDateTime start.localDate start.localTime}"
            yield $"DTEND;TZID=America/Chicago:{plusHours start.localDate start.localTime 2.0}"

        yield fold $"SUMMARY:{esc e.name}"
        if not (String.IsNullOrWhiteSpace e.url) then
            yield fold $"URL:{e.url}"
        yield "LOCATION:American Airlines Center\\, 2500 Victory Avenue\\, Dallas\\, TX 75219"
        yield "END:VEVENT" ]

let ics =
    [ "BEGIN:VCALENDAR"
      "VERSION:2.0"
      "PRODID:-//aac-ics-publisher//EN"
      "CALSCALE:GREGORIAN"
      "METHOD:PUBLISH"
      "X-WR-CALNAME:American Airlines Center"
      "X-WR-TIMEZONE:America/Chicago" ]
    @ vevents
    @ [ "END:VCALENDAR" ]

Directory.CreateDirectory("public") |> ignore
File.WriteAllText("public/calendar.ics", String.Join("\r\n", ics) + "\r\n")
printfn "wrote %d events" events.Length
