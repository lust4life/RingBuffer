#I "bin/Debug/net461"
#r "Hopac.Core"
#r "Hopac.Platform"
#r "Hopac"
#r "packages/Expecto/lib/net461/Expecto.dll"
#r "packages/Expecto.FsCheck/lib/net461/Expecto.FsCheck.dll"
#r "packages/Expecto.Hopac/lib/net461/Expecto.Hopac.dll"
#r "packages/Argu/lib/net45/Argu.dll"
#load "RingBuffer.fs"
open System.Threading
open Hopac
open Expecto
open Expecto.Logging
open Expecto.Logging.Message
open Expecto.Flip
open Hopac
open Hopac.Infixes
open Hopac.Extensions

let logger = Log.create "RB"

let putTake =
  testCaseJob "put, take" (job {
    let! rb = RingBuffer.create 4us
    let dataSource = ["a";"b";"c";"d";"e";"f";"g"]
    let latch = Latch dataSource.Length

    let mutable result = []

    let take =
      RingBuffer.take rb ^=> fun res ->
        result <- res :: result
        Latch.decrement latch
      :> Job<_>

    do! Job.foreverServer take
    do! Seq.iterJobIgnore (fun v -> RingBuffer.put rb v) (List.rev dataSource)

    do! Latch.await latch

    result |> Expect.equal "Got value" dataSource
  })

let fullBuffer =
  testCaseJob "put when full" (job {
    let! rb = RingBuffer.create 2us

    do! logger.infoWithBP (eventX "1...")
    let! firstOk = RingBuffer.tryPut rb "first"
    firstOk |> Expect.isTrue "succeeded"

    do! logger.infoWithBP (eventX "2...")
    do! RingBuffer.put rb "second"

    do! logger.infoWithBP (eventX "3...")
    let! thirdOk = RingBuffer.tryPut rb "third"
    thirdOk |> Expect.isFalse "Should not have room"

    let! b = RingBuffer.takeBatch 1us rb
    b.Length |> Expect.equal "Has one item" 1

    do! logger.infoWithBP (eventX "3 again...")
    let! thirdOk = RingBuffer.tryPut rb "third"
    thirdOk |> Expect.isTrue "Should have room now"
  })

let tryOperation =
  testCaseJob "try op in empty/full state" (job {
    let! rb = RingBuffer.create 2us

    do! logger.infoWithBP (eventX "initially empty...")
    let! res = RingBuffer.tryTake rb
    res |> Expect.isNone "Should be none, since rb empty"

    let! res = RingBuffer.tryTakeBatch 10us rb
    res |> Expect.isNone "Should be none, since rb empty"

    let! res = RingBuffer.tryTakeAll rb
    res |> Expect.isNone "Should be none, since rb empty"

    do! RingBuffer.put rb 1

    let! res = RingBuffer.tryPut rb 2
    res |> Expect.equal "Should have room" true

    do! logger.infoWithBP (eventX "first full...")

    let! res = RingBuffer.tryPut rb 3
    res |> Expect.equal "Should have no room when full" false

    let! res = RingBuffer.tryTake rb
    res |> Expect.equal "Should have 1" (Some 1)

    do! logger.infoWithBP (eventX "not full and not empty...")

    let! res = RingBuffer.tryTake rb
    res |> Expect.equal "Should have 2" (Some 2)

    do! logger.infoWithBP (eventX "second empty...")

    let! res = RingBuffer.tryTake rb
    res |> Expect.isNone "Should be none, since rb empty again"

    let! res = RingBuffer.tryTakeAll rb
    res |> Expect.isNone "Should be none, since rb empty again"

    let! res = RingBuffer.tryTakeBatch 10us rb
    res |> Expect.isNone "Should be none, since rb empty again"

    let! res = RingBuffer.tryPut rb 3
    res |> Expect.isTrue "Should have room"

    let! res = RingBuffer.tryPut rb 4
    res |> Expect.isTrue "Should have room"

    do! logger.infoWithBP (eventX "second full...")

    let! res = RingBuffer.tryPut rb 5
    res |> Expect.isFalse "Should have no room"

    let! res = RingBuffer.tryTakeAll rb
    res |> Expect.equal "Got 3, 4" (Some [|3;4|])

  })


let takeAll =
  testCaseJob "take all, batch" (job {
    let! rb = RingBuffer.create 4us

    do! RingBuffer.put rb "a"
    do! RingBuffer.put rb "b"
    do! RingBuffer.put rb "c"
    do! RingBuffer.put rb "d"
    let! res = RingBuffer.takeBatch 3us rb
    res |> Expect.equal "Got value" ([|"a";"b";"c"|])
    do! RingBuffer.put rb "e"
    do! RingBuffer.put rb "f"
    do! RingBuffer.put rb "g"

    let! res = RingBuffer.takeAll rb
    res |> Expect.equal "Got value" ([|"d";"e";"f";"g";|])
  })

let validation =
  testList "ringSizeValidate" [
    test "0 is not allowed" {
      Utils.ringSizeValidate 0us
        |> Expect.equal "(ringSizeValidate 0)" false
    }

    test "1 is allowed" {
      Utils.ringSizeValidate 1us
        |> Expect.equal "(ringSizeValidate 1)" true
    }

    test "is not power of 2" {
      Utils.ringSizeValidate 7us
        |> Expect.equal "(ringSizeValidate 7)" false
    }

    test "is power of 2" {
      Utils.ringSizeValidate 8us
        |> Expect.equal "(ringSizeValidate 8)" true
    }

    test "half the range of the index data types (uint16)" {
      Utils.ringSizeValidate (uint16 (System.Math.Pow(2.,15.)))
        |> Expect.equal "(ringSizeValidate pow 2 15)" true
    }
  ]

let tests =
  testList "RingBuffer" [
    putTake
    fullBuffer
    takeAll
    validation
    tryOperation
  ]

Tests.runTestsWithArgs defaultConfig [| "--summary"; "--debug"; "--sequenced" |] tests