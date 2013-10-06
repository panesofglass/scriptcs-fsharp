﻿namespace ScriptCs.FSharp

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Runtime.ExceptionServices
open Microsoft.FSharp.Compiler.Interactive.Shell
open Common.Logging
open ExtCore
open ScriptCs
open ScriptCs.Contracts
open ScriptCs.Hosting

type Result =
    | Success of String
    | Error of string
    | Incomplete

type FSharpEngine(host: IScriptHost) = 
    let stdin = new StreamReader(Stream.Null)
 
    let stdoutStream = new CompilerOutputStream()
    let stdout = StreamWriter.Synchronized(new StreamWriter(stdoutStream, AutoFlush=true))
 
    let stderrStream = new CompilerOutputStream()
    let stderr = StreamWriter.Synchronized(new StreamWriter(stderrStream, AutoFlush=true))
     
    let commonOptions = [| "fsi.exe"; "--nologo"; "--readline-";|]
    let session = FsiEvaluationSession(commonOptions, stdin, stdout, stderr)

    let (>>=) (d1:#IDisposable) (d2:#IDisposable) = 
        { new IDisposable with
            member x.Dispose() =
                d1.Dispose()
                d2.Dispose() }

    member x.Execute(code) = 
        try
            session.EvalInteraction(code)
            if code.EndsWith ";;" then
                let error = stderrStream.Read()
                if error.Length > 0 then Error(error) else
                Success(stdoutStream.Read())
            else Incomplete
        with ex -> Error ex.Message

    member x.AddReference(ref) =
        session.EvalInteraction(sprintf "#r @\"%s\"" ref)

    member x.SilentAddReference(ref) = 
        x.AddReference(ref)
        stdoutStream.Read() |> ignore

    member x.ImportNamespace(namespace') =
        session.EvalInteraction(sprintf "open %s" namespace')

    member x.SilentImportNamespace(namespace') =
        x.ImportNamespace(namespace')
        stdoutStream.Read() |> ignore

    interface IDisposable with
        member x.Dispose() =
            (stdin >>= stdoutStream >>= stdout >>= stderrStream >>= stderr).Dispose()             
                             
type FSharpScriptEngine(scriptHostFactory: IScriptHostFactory, logger: ILog) =
    let [<Literal>] sessionKey = "F# Session"
    
    interface IScriptEngine with
        member val BaseDirectory = null with get, set

        member val FileName = null with get, set

        member x.Execute(code, args, references, namespaces, scriptPackSession) =
            let distinctReferences = references.Union(scriptPackSession.References).Distinct()
            let sessionState = 
                match scriptPackSession.State.TryGetValue sessionKey with
                | false, _ ->
                    let host = scriptHostFactory.CreateScriptHost(ScriptPackManager(scriptPackSession.Contexts), args)
                    logger.Debug("Creating session")
                    let session = new FSharpEngine(host)
                      
                    distinctReferences
                    |> Seq.iter (fun ref ->
                        logger.DebugFormat("Adding reference to {0}", ref)
                        session.SilentAddReference ref)
                      
                    namespaces.Union(scriptPackSession.Namespaces).Distinct() 
                    |> Seq.iter (fun ns ->
                        logger.DebugFormat("Importing namespace {0}", ns)
                        session.SilentImportNamespace ns)
                      
                    let sessionState = SessionState<_>(References = distinctReferences, Session = session)
                    scriptPackSession.State.Add(sessionKey, sessionState)
                    sessionState 
                | true, res ->
                    logger.Debug("Reusing existing session") 
                    let sessionState = res :?> SessionState<FSharpEngine>
                                    
                    let newReferences =
                        match sessionState.References with
                        | null -> distinctReferences
                        | refs when Seq.isEmpty refs -> distinctReferences
                        | refs ->  distinctReferences.Except refs
                    newReferences
                    |> Seq.iter (fun ref ->
                        logger.DebugFormat("Adding reference to {0}", ref)
                        sessionState.Session.AddReference ref)
                    sessionState

            match sessionState.Session.Execute(code) with
            | Success result ->
                let cleaned = 
                    result.Split([|"\r"; "\n";|], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.filter (fun str -> not(str = "> "))
                    |> String.concat "\r\n"
                ScriptResult(ReturnValue = cleaned)
            | Error e -> ScriptResult(CompileExceptionInfo = ExceptionDispatchInfo.Capture(exn e))
            | Incomplete -> ScriptResult()
