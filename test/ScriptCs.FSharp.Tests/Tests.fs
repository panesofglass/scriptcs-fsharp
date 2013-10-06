namespace ScriptCs.FSharp.Tests

open Common.Logging
open Moq
open Ploeh.AutoFixture
open Ploeh.AutoFixture.AutoMoq
open Ploeh.AutoFixture.Xunit
open Xunit.Extensions
open Swensen.Unquote.Assertions
open ScriptCs
open ScriptCs.Contracts
open ScriptCs.FSharp

type ScriptCsAutoDataAttribute() =
    inherit AutoDataAttribute(Fixture().Customize(new AutoMoqCustomization()))

module ``FSharpScriptEngine Tests`` =

    module ``The Constructor`` =
        [<Theory; ScriptCsAutoData>]
        let ``should lazily construct a session``() =
            let scriptHostFactory = let x = Mock<IScriptHostFactory>() in x.Object
            let log = let x = Mock<ILog>() in x.Object
            let engine = FSharpScriptEngine(scriptHostFactory, log)
            test <@ engine.Session = None @>

    module ``The Execute Method`` =
        [<Theory; ScriptCsAutoData>]
        let ``should create ScriptHost with contexts``([<Frozen>] scriptHostFactory : Mock<IScriptHostFactory>,
                                                       [<Frozen>] scriptPack : Mock<IScriptPack>,
                                                       scriptPackSession : ScriptPackSession,
                                                       [<NoAutoProperties>] engine : FSharpScriptEngine) =
            // Arrange
            let code = "let a = 0"

            scriptHostFactory
                .Setup(fun (f: IScriptHostFactory) -> f.CreateScriptHost(It.IsAny<IScriptPackManager>(), It.IsAny<string[]>()))
                .Returns<IScriptPackManager, string[]>(fun p q -> new ScriptHost(p, q) :> IScriptHost) |> ignore

            scriptPack.Setup(fun (p: IScriptPack) -> p.Initialize(It.IsAny<IScriptPackSession>())) |> ignore
            scriptPack.Setup(fun (p: IScriptPack) -> p.GetContext()).Returns(Unchecked.defaultof<IScriptPackContext>) |> ignore
            
            // Act
            (engine :> IScriptEngine).Execute(code, [||], Seq.empty, Seq.empty, scriptPackSession) |> ignore

            // Assert
            scriptHostFactory.Verify(fun (f: IScriptHostFactory) -> f.CreateScriptHost(It.IsAny<IScriptPackManager>(), It.IsAny<string[]>())) |> ignore


//        let count =
//            engine.Session.GetReferences()
//            |> Seq.filter (fun r -> r.EndsWith("ScriptCs.Core.dll"))
//            |> Seq.length
//        test <@ count = 1 @>
