module ChatApp.Tests.SampleTests

open Xunit
open FsUnit.Xunit

[<Fact>]
let ``Sample test to verify setup`` () =
    1 + 1 |> should equal 2

[<Fact>]
let ``FsUnit syntax works correctly`` () =
    "Hello" |> should not' (equal "World")
