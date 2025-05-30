// Tests for TaskBuilder.fs
//
// Written in 2016 by Robert Peele (humbobst@gmail.com)
//
// To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
// to this software to the public domain worldwide. This software is distributed without any warranty.
//
// You should have received a copy of the CC0 Public Domain Dedication along with this software.
// If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.


// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

// Various tests for the:
// Microsoft.FSharp.Control.Async type

namespace FSharp.Core.UnitTests.Control.Tasks

open System
open System.Collections
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.FSharp.Control
open Xunit

type ITaskThing =
    abstract member Taskify : 'a option -> 'a Task

#if NETCOREAPP
type SupportBothDisposables() =
    let mutable called = false
    interface IAsyncDisposable with 
        member _.DisposeAsync() = 
            task { 
                System.Console.WriteLine "incrementing"
                called <- true }
            |> ValueTask
    interface IDisposable with 
        member _.Dispose() =  failwith "dispose"
    member x.Disposed = called
#endif

type SmokeTestsForCompilation() =

    [<Fact>]
    member _.tinyTask() =
        task {
            return 1
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.tbind() =
        task {
            let! x = Task.FromResult(1)
            return 1 + x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 2 then failwith "failed"

    [<Fact>]
    member _.tnested() =
        task {
            let! x = task { return 1 }
            return x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.tcatch0() =
        task {
            try 
               return 1
            with e -> 
               return 2
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.tcatch1() =
        task {
            try 
               let! x = Task.FromResult 1
               return x
            with e -> 
               return 2
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"


    [<Fact>]
    member _.t3() =
        let t2() =
            task {
                System.Console.WriteLine("hello")
                return 1
            }
        task {
            System.Console.WriteLine("hello")
            let! x = t2()
            System.Console.WriteLine("world")
            return 1 + x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 2 then failwith "failed"

    [<Fact>]
    member _.t3b() =
        task {
            System.Console.WriteLine("hello")
            let! x = Task.FromResult(1)
            System.Console.WriteLine("world")
            return 1 + x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 2 then failwith "failed"

    [<Fact>]
    member _.t3c() =
        task {
            System.Console.WriteLine("hello")
            do! Task.Delay(100)
            System.Console.WriteLine("world")
            return 1 
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    // This tests an exception match
    member _.t67() =
        task {
            try
                do! Task.Delay(0)
            with
            | :? ArgumentException -> 
                ()
            | _ -> 
                ()
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> () then failwith "failed"

    [<Fact>]
    // This tests compiling an incomplete exception match
    member _.t68() =
        task {
            try
                do! Task.Delay(0)
            with
            | :? ArgumentException -> 
                ()
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> () then failwith "failed"

    [<Fact>]
    member _.testCompileAsyncWhileLoop() =
        task {
            let mutable i = 0
            while i < 5 do
                i <- i + 1
                do! Task.Yield()
            return i
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 5 then failwith "failed"

    [<Fact>]
    member _.merge2tasks() =
        task {
            let! x = Task.FromResult(1)
            and! y = Task.FromResult(2)
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"    
            
    [<Fact>]
    member _.merge3tasks() =
        task {
            let! x = Task.FromResult(1)
            and! y = Task.FromResult(2)
            and! z = Task.FromResult(3)
            return x + y + z
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 6 then failwith "failed"

    [<Fact>]
    member _.mergeYieldAndTask() =
        task {
            let! _ = Task.Yield()
            and! y = Task.FromResult(1)
            return y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.mergeTaskAndYield() =
        task {
            let! x = Task.FromResult(1)
            and! _ = Task.Yield()
            return x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.merge2valueTasks() =
        task {
            let! x = ValueTask<int>(Task.FromResult(1))
            and! y = ValueTask<int>(Task.FromResult(2))
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"

    [<Fact>]
    member _.merge2valueTasksAndYield() =
        task {
            let! x = ValueTask<int>(Task.FromResult(1))
            and! y = ValueTask<int>(Task.FromResult(2))
            and! _ = Task.Yield()
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"

    [<Fact>]
    member _.mergeYieldAnd2tasks() =
        task {
            let! _ = Task.Yield()
            and! x = Task.FromResult(1)
            and! y = Task.FromResult(2)
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"

    [<Fact>]
    member _.merge2tasksAndValueTask() =
        task {
            let! x = Task.FromResult(1)
            and! y = Task.FromResult(2)
            and! z = ValueTask<int>(Task.FromResult(3))
            return x + y + z
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 6 then failwith "failed"

    [<Fact>]
    member _.merge2asyncs() =
        task {
            let! x = async { return 1 }
            and! y = async { return 2 }
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"

    [<Fact>]
    member _.merge3asyncs() =
        task {
            let! x = async { return 1 }
            and! y = async { return 2 }
            and! z = async { return 3 }
            return x + y + z
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 6 then failwith "failed"

    [<Fact>]
    member _.mergeYieldAndAsync() =
        task {
            let! _ = Task.Yield()
            and! y = async { return 1 }
            return y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.mergeAsyncAndYield() =
        task {
            let! x = async { return 1 }
            and! _ = Task.Yield()
            return x
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 1 then failwith "failed"

    [<Fact>]
    member _.mergeYieldAnd2asyncs() =
        task {
            let! _ = Task.Yield()
            and! x = async { return 1 }
            and! y = async { return 2 }
            return x + y
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 3 then failwith "failed"

    [<Fact>]
    member _.merge2asyncsAndValueTask() =
        task {
            let! x = async { return 1 }
            and! y = async { return 2 }
            and! z = ValueTask<int>(Task.FromResult(3))
            return x + y + z
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 6 then failwith "failed"

    [<Fact>]
    member _.mergeBackgroundTask() =
        backgroundTask {
            let! x = async { return 1 }
            and! y = task { return 2 }
            and! z = ValueTask<int>(Task.FromResult(3))
            return x + y + z
        }
        |> fun t -> 
            t.Wait()
            if t.Result <> 6 then failwith "failed"

exception TestException of string

[<AutoOpen>]
module Helpers = 
    let BIG = 10
    // let BIG = 10000
    let require x msg = if not x then failwith msg
    let failtest str = raise (TestException str)

[<Collection(nameof FSharp.Test.NotThreadSafeResourceCollection)>]
type Basics() = 
    [<Fact>]
    member _.testShortCircuitResult() =
        printfn "Running testShortCircuitResult..."
        let t =
            task {
                let! x = Task.FromResult(1)
                let! y = Task.FromResult(2)
                return x + y
            }
        require t.IsCompleted "didn't short-circuit already completed tasks"
        printfn "t.Result = %A" t.Result
        require (t.Result = 3) "wrong result"

    [<Fact>]
    member _.testDelay() =
        printfn "Running testDelay..."
        let mutable x = 0
        let t =
            task {
                do! Task.Delay(50)
                x <- x + 1
            }
        printfn "task created and first step run...."
        require (x = 0) "task already ran"
        printfn "waiting...."
        t.Wait()

    [<Fact>]
    member _.testNoDelay() =
        printfn "Running testNoDelay..."
        let mutable x = 0
        let t =
            task {
                x <- x + 1
                do! Task.Delay(5)
                x <- x + 1
            }
        require (x = 1) "first part didn't run yet"
        t.Wait()

    [<Fact>]
    member _.testNonBlocking() =
        printfn "Running testNonBlocking..."
        let allowContinue = new SemaphoreSlim(0)
        let finished = new ManualResetEventSlim()
        let t =
            task {
                do! allowContinue.WaitAsync()
                Thread.Sleep(100)
                finished.Set()
            }
        allowContinue.Release() |> ignore
        require (not finished.IsSet) "sleep blocked caller"
        t.Wait()

    [<Fact>]
    member _.testCatching1() =
        printfn "Running testCatching1..."
        let mutable x = 0
        let mutable y = 0
        let t =
            task {
                try
                    do! Task.Delay(0)
                    failtest "hello"
                    x <- 1
                    do! Task.Delay(100)
                with
                | TestException msg ->
                    require (msg = "hello") "message tampered"
                | _ ->
                    require false "other exn type"
                    require false "other exn type"
                y <- 1
            }
        t.Wait()
        require (y = 1) "bailed after exn"
        require (x = 0) "ran past failure"

    [<Fact>]
    member _.testCatching2() =
        printfn "Running testCatching2..."
        let mutable x = 0
        let mutable y = 0
        let t =
            task {
                try
                    do! Task.Yield() // can't skip through this
                    failtest "hello"
                    x <- 1
                    do! Task.Delay(100)
                with
                | TestException msg ->
                    require (msg = "hello") "message tampered"
                | _ ->
                    require false "other exn type"
                y <- 1
            }
        t.Wait()
        require (y = 1) "bailed after exn"
        require (x = 0) "ran past failure"

    [<Fact>]
    member _.testCatchingInApplicative() =
        printfn "Running testCatchingInApplicative..."
        let mutable x = 0
        let mutable y = 0
        let t =
            task {
                try
                    let! _ = task { 
                        do! Task.Delay(100)
                        x <- 1
                    }
                    and! _ = task { 
                        failtest "hello"
                    }
                    ()
                with
                | TestException msg ->
                    require (msg = "hello") "message tampered"
                | _ ->
                    require false "other exn type"
                y <- 1
            }
        t.Wait()
        require (y = 1) "bailed after exn"
        require (x = 1) "exit too early"

    [<Fact>]
    member _.testNestedCatching() =
        printfn "Running testNestedCatching..."
        let mutable counter = 1
        let mutable caughtInner = 0
        let mutable caughtOuter = 0
        let t1() =
            task {
                try
                    do! Task.Yield()
                    failtest "hello"
                with
                | TestException msg as exn ->
                    caughtInner <- counter
                    counter <- counter + 1
                    raise exn
            }
        let t2 =
            task {
                try
                    do! t1()
                with
                | TestException msg as exn ->
                    caughtOuter <- counter
                    raise exn
                | e ->
                    require false (sprintf "invalid msg type %s" e.Message)
            }
        try
            t2.Wait()
            require false "ran past failed task wait"
        with
        | :? AggregateException as exn ->
            require (exn.InnerExceptions.Count = 1) "more than 1 exn"
        require (caughtInner = 1) "didn't catch inner"
        require (caughtOuter = 2) "didn't catch outer"

    [<Fact>]
    member _.testWhileLoopSync() =
        printfn "Running testWhileLoopSync..."
        let t =
            task {
                let mutable i = 0
                while i < 10 do
                    i <- i + 1
                return i
            }
        //t.Wait() no wait required for sync loop
        require (t.IsCompleted) "didn't do sync while loop properly - not completed"
        require (t.Result = 10) "didn't do sync while loop properly - wrong result"

    [<Fact>]
    member _.testWhileLoopAsyncZeroIteration() =
        printfn "Running testWhileLoopAsyncZeroIteration..."
        for i in 1 .. 5 do 
            let t =
                task {
                    let mutable i = 0
                    while i < 0 do
                        i <- i + 1
                        do! Task.Yield()
                    return i
                }
            t.Wait()
            require (t.Result = 0) "didn't do while loop properly"

    [<Fact>]
    member _.testWhileLoopAsyncOneIteration() =
        printfn "Running testWhileLoopAsyncOneIteration..."
        for i in 1 .. 5 do 
            let t =
                task {
                    let mutable i = 0
                    while i < 1 do
                        i <- i + 1
                        do! Task.Yield()
                    return i
                }
            t.Wait()
            require (t.Result = 1) "didn't do while loop properly"

    [<Fact>]
    member _.testWhileLoopAsync() =
        printfn "Running testWhileLoopAsync..."
        for i in 1 .. 5 do 
            let t =
                task {
                    let mutable i = 0
                    while i < 10 do
                        i <- i + 1
                        do! Task.Yield()
                    return i
                }
            t.Wait()
            require (t.Result = 10) "didn't do while loop properly"

    [<Fact>]
    member _.testTryFinallyHappyPath() =
        printfn "Running testTryFinallyHappyPath..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let t =
                task {
                    try
                        require (not ran) "ran way early"
                        do! Task.Delay(100)
                        require (not ran) "ran kinda early"
                    finally
                        ran <- true
                }
            t.Wait()
            require ran "never ran"
    [<Fact>]
    member _.testTryFinallySadPath() =
        printfn "Running testTryFinallySadPath..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let t =
                task {
                    try
                        require (not ran) "ran way early"
                        do! Task.Delay(100)
                        require (not ran) "ran kinda early"
                        failtest "uhoh"
                    finally
                        ran <- true
                }
            try
                t.Wait()
            with
            | _ -> ()
            require ran "never ran"

    [<Fact>]
    member _.testTryFinallyCaught() =
        printfn "Running testTryFinallyCaught..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let t =
                task {
                    try
                        try
                            require (not ran) "ran way early"
                            do! Task.Delay(100)
                            require (not ran) "ran kinda early"
                            failtest "uhoh"
                        finally
                            ran <- true
                        return 1
                    with
                    | _ -> return 2
                }
            require (t.Result = 2) "wrong return"
            require ran "never ran"
    
    [<Fact>]
    member _.testUsing() =
        printfn "Running testUsing..."
        for i in 1 .. 5 do 
            let mutable disposed = false
            let t =
                task {
                    use d = { new IDisposable with member _.Dispose() = disposed <- true }
                    require (not disposed) "disposed way early"
                    do! Task.Delay(100)
                    require (not disposed) "disposed kinda early"
                }
            t.Wait()
            require disposed "never disposed B"

#if NETCOREAPP
    [<Fact>]
    member _.testUsingAsyncDisposableSync() =
        printfn "Running testUsingAsyncDisposableSync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use d = 
                        { new IAsyncDisposable with 
                            member _.DisposeAsync() = 
                                task { 
                                   System.Console.WriteLine "incrementing"
                                   disposed <- disposed + 1 }
                                |> ValueTask 
                        }
                    require (disposed = 0) "disposed way early"
                    System.Console.WriteLine "delaying"
                    do! Task.Delay(100)
                    System.Console.WriteLine "testing"
                    require (disposed = 0) "disposed kinda early"
                }
            t.Wait()
            require (disposed >= 1) "never disposed B"
            require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    member _.testUsingAsyncDisposableAsync() =
        printfn "Running testUsingAsyncDisposableAsync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use d = 
                        { new IAsyncDisposable with 
                            member _.DisposeAsync() = 
                                task { 
                                    do! Task.Delay(10)
                                    disposed <- disposed + 1 
                                }
                                |> ValueTask 
                        }
                    require (disposed = 0) "disposed way early"
                    do! Task.Delay(100)
                    require (disposed = 0) "disposed kinda early"
                }
            t.Wait()
            require (disposed >= 1) "never disposed B"
            require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    member _.testUsingAsyncDisposableExnAsync() =
        printfn "Running testUsingAsyncDisposableExnAsync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use d = 
                        { new IAsyncDisposable with 
                            member _.DisposeAsync() = 
                                task { 
                                    do! Task.Delay(10)
                                    disposed <- disposed + 1 
                                }
                                |> ValueTask 
                        }
                    require (disposed = 0) "disposed way early"
                    failtest "oops"
                    
                }
            try t.Wait()
            with | :? AggregateException -> 
                require (disposed >= 1) "never disposed B"
                require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    member _.testUsingAsyncDisposableExnSync() =
        printfn "Running testUsingAsyncDisposableExnSync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use d = 
                        { new IAsyncDisposable with 
                            member _.DisposeAsync() = 
                                task { 
                                    disposed <- disposed + 1 
                                    do! Task.Delay(10)
                                }
                                |> ValueTask 
                        }
                    require (disposed = 0) "disposed way early"
                    failtest "oops"
                    
                }
            try t.Wait()
            with | :? AggregateException -> 
                require (disposed >= 1) "never disposed B"
                require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    member _.testUsingAsyncDisposableDelayExnSync() =
        printfn "Running testUsingAsyncDisposableDelayExnSync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use d = 
                        { new IAsyncDisposable with 
                            member _.DisposeAsync() = 
                                task { 
                                    disposed <- disposed + 1 
                                    do! Task.Delay(10)
                                }
                                |> ValueTask 
                        }
                    require (disposed = 0) "disposed way early"
                    do! Task.Delay(10)
                    require (disposed = 0) "disposed kind of early"
                    failtest "oops"
                    
                }
            try t.Wait()
            with | :? AggregateException -> 
                require (disposed >= 1) "never disposed B"
                require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    // Test use! resolves
    member _.testUsingBindAsyncDisposableSync() =
        printfn "Running testUsingBindAsyncDisposableSync..."
        for i in 1 .. 5 do 
            let mutable disposed = 0
            let t =
                task {
                    use! d = 
                        task {
                         do! Task.Delay(10)
                         return
                             { new IAsyncDisposable with 
                                member _.DisposeAsync() = 
                                    task { 
                                       System.Console.WriteLine "incrementing"
                                       disposed <- disposed + 1 }
                                    |> ValueTask 
                             }
                        }
                    require (disposed = 0) "disposed way early"
                    System.Console.WriteLine "delaying"
                    do! Task.Delay(100)
                    System.Console.WriteLine "testing"
                    require (disposed = 0) "disposed kinda early"
                }
            t.Wait()
            require (disposed >= 1) "never disposed B"
            require (disposed <= 1) "too many dispose on B"

    [<Fact>]
    member _.testUsingAsyncDisposableSyncSupportingBothDisposables() =
        printfn "Running testUsingAsyncDisposableSyncSupportingBothDisposables..."
        for i in 1 .. 5 do 
            let disp = new SupportBothDisposables()
            let t =
                task {
                    use d = disp
                    require (not disp.Disposed) "disposed way early"
                    System.Console.WriteLine "delaying"
                    do! Task.Delay(100)
                    System.Console.WriteLine "testing"
                    require (not disp.Disposed) "disposed kinda early"
                }
            t.Wait()
            require disp.Disposed "never disposed B"
#endif

    [<Fact>]
    member _.testUsingFromTask() =
        printfn "Running testUsingFromTask..."
        let mutable disposedInner = false
        let mutable disposed = false
        let t =
            task {
                use! d =
                    task {
                        do! Task.Delay(50)
                        use i = { new IDisposable with member _.Dispose() = disposedInner <- true }
                        require (not disposed && not disposedInner) "disposed inner early"
                        return { new IDisposable with member _.Dispose() = disposed <- true }
                    }
                require disposedInner "did not dispose inner after task completion"
                require (not disposed) "disposed way early"
                do! Task.Delay(50)
                require (not disposed) "disposed kinda early"
            }
        t.Wait()
        require disposed "never disposed C"

    [<Fact>]
    member _.testUsingSadPath() =
        printfn "Running testUsingSadPath..."
        let mutable disposedInner = false
        let mutable disposed = false
        let t =
            task {
                try
                    use! d =
                        task {
                            do! Task.Delay(50)
                            use i = { new IDisposable with member _.Dispose() = disposedInner <- true }
                            failtest "uhoh"
                            require (not disposed && not disposedInner) "disposed inner early"
                            return { new IDisposable with member _.Dispose() = disposed <- true }
                        }
                    ()
                with
                | TestException msg ->
                    printfn "caught TestException"
                    require disposedInner "did not dispose inner after task completion"
                    require (not disposed) "disposed way early"
                    do! Task.Delay(50)
                    printfn "resumed after delay"
                    require (not disposed) "disposed kinda early"
            }
        t.Wait()
        require (not disposed) "disposed thing that never should've existed"

    [<Fact>]
    member _.testForLoopA() =
        printfn "Running testForLoopA..."
        let list = ["a"; "b"; "c"] |> Seq.ofList
        let t =
            task {
                printfn "entering loop..." 
                let mutable x = Unchecked.defaultof<_>
                let e = list.GetEnumerator()
                while e.MoveNext() do 
                    x <- e.Current
                    printfn "x = %A" x 
                    do! Task.Yield()
                    printfn "x = %A" x 
            }
        t.Wait()

    [<Fact>]
    member _.testForLoopComplex() =
        printfn "Running testForLoopComplex..."
        let mutable disposed = false
        let wrapList =
            let raw = ["a"; "b"; "c"] |> Seq.ofList
            let getEnumerator() =
                let raw = raw.GetEnumerator()
                { new IEnumerator<string> with
                    member _.MoveNext() =
                        require (not disposed) "moved next after disposal"
                        raw.MoveNext()
                    member _.Current =
                        require (not disposed) "accessed current after disposal"
                        raw.Current
                    member _.Current =
                        require (not disposed) "accessed current (boxed) after disposal"
                        box raw.Current
                    member _.Dispose() =
                        require (not disposed) "disposed twice"
                        disposed <- true
                        raw.Dispose()
                    member _.Reset() =
                        require (not disposed) "reset after disposal"
                        raw.Reset()
                }
            { new IEnumerable<string> with
                member _.GetEnumerator() : IEnumerator<string> = getEnumerator()
                member _.GetEnumerator() : IEnumerator = upcast getEnumerator()
            }
        let t =
            task {
                let mutable index = 0
                do! Task.Yield()
                printfn "entering loop..." 
                for x in wrapList do
                    printfn "x = %A, index = %d" x index
                    do! Task.Yield()
                    printfn "back from yield" 
                    do! Task.Yield()
                    printfn "back from yield" 
                    match index with
                    | 0 -> require (x = "a") "wrong first value"
                    | 1 -> require (x = "b") "wrong second value"
                    | 2 -> require (x = "c") "wrong third value"
                    | _ -> require false "iterated too far!"
                    index <- index + 1
                    printfn "yield again" 
                    do! Task.Yield()
                    printfn "yield again again" 
                    do! Task.Yield()
                    printfn "looping again..." 
                do! Task.Yield()
                return 1
            }
        t.Wait()
        require disposed "never disposed D"
        require (t.Result = 1) "wrong result"

    [<Fact>]
    member _.testForLoopSadPath() =
        printfn "Running testForLoopSadPath..."
        for i in 1 .. 5 do 
            let wrapList = ["a"; "b"; "c"]
            let t =
                task {
                        let mutable index = 0
                        do! Task.Yield()
                        for x in wrapList do
                            do! Task.Yield()
                            index <- index + 1
                        return 1
                }
            require (t.Result = 1) "wrong result"

    [<Fact>]
    member _.testForLoopSadPathComplex() =
        printfn "Running testForLoopSadPathComplex..."
        for i in 1 .. 5 do 
            let mutable disposed = false
            let wrapList =
                let raw = ["a"; "b"; "c"] |> Seq.ofList
                let getEnumerator() =
                    let raw = raw.GetEnumerator()
                    { new IEnumerator<string> with
                        member _.MoveNext() =
                            require (not disposed) "moved next after disposal"
                            raw.MoveNext()
                        member _.Current =
                            require (not disposed) "accessed current after disposal"
                            raw.Current
                        member _.Current =
                            require (not disposed) "accessed current (boxed) after disposal"
                            box raw.Current
                        member _.Dispose() =
                            require (not disposed) "disposed twice"
                            disposed <- true
                            raw.Dispose()
                        member _.Reset() =
                            require (not disposed) "reset after disposal"
                            raw.Reset()
                    }
                { new IEnumerable<string> with
                    member _.GetEnumerator() : IEnumerator<string> = getEnumerator()
                    member _.GetEnumerator() : IEnumerator = upcast getEnumerator()
                }
            let mutable caught = false
            let t =
                task {
                    try
                        let mutable index = 0
                        do! Task.Yield()
                        for x in wrapList do
                            do! Task.Yield()
                            match index with
                            | 0 -> require (x = "a") "wrong first value"
                            | _ -> failtest "uhoh"
                            index <- index + 1
                            do! Task.Yield()
                        do! Task.Yield()
                        return 1
                    with
                    | TestException "uhoh" ->
                        caught <- true
                        return 2
                }
            require (t.Result = 2) "wrong result"
            require caught "didn't catch exception"
            require disposed "never disposed A"
    
    [<Fact>]
    member _.testExceptionAttachedToTaskWithoutAwait() =
        for i in 1 .. 5 do 
            let mutable ranA = false
            let mutable ranB = false
            let t =
                task {
                    ranA <- true
                    failtest "uhoh"
                    ranB <- true
                }
            require ranA "didn't run immediately"
            require (not ranB) "ran past exception"
            require (not (isNull t.Exception)) "didn't capture exception"
            require (t.Exception.InnerExceptions.Count = 1) "captured more exceptions"
            require (t.Exception.InnerException = TestException "uhoh") "wrong exception"
            let mutable caught = false
            let mutable ranCatcher = false
            let catcher =
                task {
                    try
                        ranCatcher <- true
                        let! result = t
                        return false
                    with
                    | TestException "uhoh" ->
                        caught <- true
                        return true
                }
            require ranCatcher "didn't run"
            require catcher.Result "didn't catch"
            require caught "didn't catch"

    [<Fact>]
    member _.testExceptionAttachedToTaskWithAwait() =
        printfn "running testExceptionAttachedToTaskWithAwait"
        for i in 1 .. 5 do 
            let mutable ranA = false
            let mutable ranB = false
            let t =
                task {
                    ranA <- true
                    failtest "uhoh"
                    do! Task.Delay(100)
                    ranB <- true
                }
            require ranA "didn't run immediately"
            require (not ranB) "ran past exception"
            require (not (isNull t.Exception)) "didn't capture exception"
            require (t.Exception.InnerExceptions.Count = 1) "captured more exceptions"
            require (t.Exception.InnerException = TestException "uhoh") "wrong exception"
            let mutable caught = false
            let mutable ranCatcher = false
            let catcher =
                task {
                    try
                        ranCatcher <- true
                        let! result = t
                        return false
                    with
                    | TestException "uhoh" ->
                        caught <- true
                        return true
                }
            require ranCatcher "didn't run"
            require catcher.Result "didn't catch"
            require caught "didn't catch"
    
    [<Fact>]
    member _.testExceptionThrownInFinally() =
        printfn "running testExceptionThrownInFinally"
        for i in 1 .. 5 do
            use stepOutside = new SemaphoreSlim(0)
            use ranInitial = new ManualResetEventSlim()
            use ranNext = new ManualResetEventSlim()
            let mutable ranFinally = 0
            let t =
                task {
                    try
                        ranInitial.Set()
                        do! Task.Yield()
                        Thread.Sleep(100) // shouldn't be blocking so we should get through to requires before this finishes
                        ranNext.Set()
                    finally
                        ranFinally <- ranFinally + 1
                        failtest "finally exn!"
                }
            require ranInitial.IsSet "didn't run initial"
            require (not ranNext.IsSet) "ran next too early"
            try
                t.Wait()
                require false "shouldn't get here"
            with
            | _ -> ()
            require ranNext.IsSet "didn't run next"
            require (ranFinally = 1) "didn't run finally exactly once"

    [<Fact>]
    member _.test2ndExceptionThrownInFinally() =
        printfn "running test2ndExceptionThrownInFinally"
        for i in 1 .. 5 do 
            use ranInitial = new ManualResetEventSlim()
            use continueTask = new SemaphoreSlim(0)
            use ranNext = new ManualResetEventSlim()
            let mutable ranFinally = 0
            let t =
                task {
                    try
                        ranInitial.Set()
                        do! continueTask.WaitAsync()
                        ranNext.Set()
                        do! Task.Yield()
                        failtest "uhoh"
                    finally
                        ranFinally <- ranFinally + 1
                        failtest "2nd exn!"
                }
            ranInitial.Wait()
            continueTask.Release() |> ignore
            try
                t.Wait()
                require false "shouldn't get here"
            with
            | _ -> ()
            require ranNext.IsSet "didn't run next"
            require (ranFinally = 1) "didn't run finally exactly once"
    
    [<Fact>]
    member _.testFixedStackWhileLoop() =
        printfn "running testFixedStackWhileLoop"
        for i in 1 .. 100 do 
            let t =
                task {
                    let mutable maxDepth = Nullable()
                    let mutable i = 0
                    while i < BIG do
                        i <- i + 1
                        do! Task.Yield()
                        if i % 100 = 0 then
                            let stackDepth = StackTrace().FrameCount
                            if maxDepth.HasValue && stackDepth > maxDepth.Value then
                                failwith "Stack depth increased!"
                            maxDepth <- Nullable(stackDepth)
                    return i
                }
            t.Wait()
            require (t.Result = BIG) "didn't get to big number"

    [<Fact>]
    member _.testFixedStackForLoop() =
        for i in 1 .. 100 do 
            printfn "running testFixedStackForLoop"
            let mutable ran = false
            let t =
                task {
                    let mutable maxDepth = Nullable()
                    for i in Seq.init BIG id do
                        do! Task.Yield()
                        if i % 100 = 0 then
                            let stackDepth = StackTrace().FrameCount
                            if maxDepth.HasValue && stackDepth > maxDepth.Value then
                                failwith "Stack depth increased!"
                            maxDepth <- Nullable(stackDepth)
                    ran <- true
                    return ()
                }
            t.Wait()
            require ran "didn't run all"

    [<Fact>]
    member _.testTypeInference() =
        let t1 : string Task =
            task {
                return "hello"
            }
        let t2 =
            task {
                let! s = t1
                return s.Length
            }
        t2.Wait()

    [<Fact>]
    member _.testNoStackOverflowWithImmediateResult() =
        printfn "running testNoStackOverflowWithImmediateResult"
        let longLoop =
            task {
                let mutable n = 0
                while n < BIG do
                    n <- n + 1
                    return! Task.FromResult(())
            }
        longLoop.Wait()
    
    [<Fact>]
    member _.testNoStackOverflowWithYieldResult() =
        printfn "running testNoStackOverflowWithYieldResult"
        let longLoop =
            task {
                let mutable n = 0
                while n < BIG do
                    let! _ =
                        task {
                            do! Task.Yield()
                            let! _ = Task.FromResult(0)
                            n <- n + 1
                        }
                    n <- n + 1
            }
        longLoop.Wait()

    [<Fact>]
    member _.testSmallTailRecursion() =
        printfn "running testSmallTailRecursion"
        let rec loop n =
            task {
                if n < 100 then
                    do! Task.Yield()
                    let! _ = Task.FromResult(0)
                    return! loop (n + 1)
                else
                    return ()
            }
        let shortLoop =
            task {
                return! loop 0
            }
        shortLoop.Wait()
    
    [<Fact>]
    member _.testTryOverReturnFrom() =
        printfn "running testTryOverReturnFrom"
        let inner() =
            task {
                do! Task.Yield()
                failtest "inner"
                return 1
            }
        let t =
            task {
                try
                    do! Task.Yield()
                    return! inner()
                with
                | TestException "inner" -> return 2
            }
        require (t.Result = 2) "didn't catch"

    [<Fact>]
    member _.testTryFinallyOverReturnFromWithException() =
        printfn "running testTryFinallyOverReturnFromWithException"
        let inner() =
            task {
                do! Task.Yield()
                failtest "inner"
                return 1
            }
        let mutable m = 0
        let t =
            task {
                try
                    do! Task.Yield()
                    return! inner()
                finally
                    m <- 1
            }
        try
            t.Wait()
        with
        | :? AggregateException -> ()
        require (m = 1) "didn't run finally"
    
    [<Fact>]
    member _.testTryFinallyOverReturnFromWithoutException() =
        printfn "running testTryFinallyOverReturnFromWithoutException"
        let inner() =
            task {
                do! Task.Yield()
                return 1
            }
        let mutable m = 0
        let t =
            task {
                try
                    do! Task.Yield()
                    return! inner()
                finally
                    m <- 1
            }
        try
            t.Wait()
        with
        | :? AggregateException -> ()
        require (m = 1) "didn't run finally"

    // no need to call this, we just want to check that it compiles w/o warnings
    member _.testTrivialReturnCompiles (x : 'a) : 'a Task =
        task {
            do! Task.Yield()
            return x
        }

    // no need to call this, we just want to check that it compiles w/o warnings
    member _.testTrivialTransformedReturnCompiles (x : 'a) (f : 'a -> 'b) : 'b Task =
        task {
            do! Task.Yield()
            return f x
        }

    [<Fact>]
    member _.testAsyncsMixedWithTasks() =
        let t =
            task {
                do! Task.Delay(1)
                do! Async.Sleep(1)
                let! x =
                    async {
                        do! Async.Sleep(1)
                        return 5
                    }
                return! async { return x + 3 }
            }
        let result = t.Result
        require (result = 8) "something weird happened"

    [<Fact>]
    member _.testAsyncsMixedWithTasks_ShouldNotSwitchContext() =
        let t = task {
            let a = Thread.CurrentThread.ManagedThreadId
            let! b = async {
                return Thread.CurrentThread.ManagedThreadId
            }
            let c = Thread.CurrentThread.ManagedThreadId
            return $"Before: {a}, in async: {b}, after async: {c}"
        }
        let d = Thread.CurrentThread.ManagedThreadId
        let actual = $"{t.Result}, after task: {d}"

        require (actual = $"Before: {d}, in async: {d}, after async: {d}, after task: {d}") actual
        

    [<Fact>]
    // no need to call this, we just want to check that it compiles w/o warnings
    member _.testDefaultInferenceForReturnFrom() =
        let t = task { return Some "x" }
        task {
            let! r = t
            if r = None then
                return! failwithf "Could not find x" 
            else
                return r
        }
        |> ignore

    [<Fact>]
    // no need to call this, just check that it compiles
    member _.testCompilerInfersArgumentOfReturnFrom() =
        task {
            if true then return 1
            else return! failwith ""
        }
        |> ignore


    [<Fact; >]
    member _.testTaskUsesSyncContext() =
        printfn "Running testBackgroundTask..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let mutable posted = false
            let oldSyncContext = SynchronizationContext.Current
            let syncContext = { new SynchronizationContext()  with member _.Post(d,state) = posted <- true; d.Invoke(state) }
            try 
                SynchronizationContext.SetSynchronizationContext syncContext
                let tid = System.Threading.Thread.CurrentThread.ManagedThreadId 
                require (not (isNull SynchronizationContext.Current)) "need sync context non null on foreground thread A"
                require (SynchronizationContext.Current = syncContext) "need sync context known on foreground thread A"
                let t =
                    task {
                        let tid2 = System.Threading.Thread.CurrentThread.ManagedThreadId 
                        require (not (isNull SynchronizationContext.Current)) "need sync context non null on foreground thread B"
                        require (SynchronizationContext.Current = syncContext) "need sync context known on foreground thread B"
                        require (tid = tid2) "expected synchronous start for task B2"
                        do! Task.Yield()
                        require (not (isNull SynchronizationContext.Current)) "need sync context non null on foreground thread C"
                        require (SynchronizationContext.Current = syncContext) "need sync context known on foreground thread C"
                        ran <- true
                    }
                t.Wait()
                require ran "never ran"
                require posted "never posted"
            finally
                SynchronizationContext.SetSynchronizationContext oldSyncContext
                 
    [<Fact; >]
    member _.testBackgroundTaskEscapesSyncContext() =
        printfn "Running testBackgroundTask..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let mutable posted = false
            let oldSyncContext = SynchronizationContext.Current
            let syncContext = { new SynchronizationContext()  with member _.Post(d,state) = posted <- true; d.Invoke(state) }
            try 
                SynchronizationContext.SetSynchronizationContext syncContext
                let t =
                    backgroundTask {
                        require (System.Threading.Thread.CurrentThread.IsThreadPoolThread) "expect to be on background thread"
                        ran <- true
                    }
                t.Wait()
                require ran "never ran"
                require (not posted) "did not expect post to sync context"
            finally
                SynchronizationContext.SetSynchronizationContext oldSyncContext
                 
    [<Fact; >]
    member _.testBackgroundTaskStaysOnSameThreadIfAlreadyOnBackground() =
        printfn "Running testBackgroundTask..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let taskOuter =
                Task.Run(fun () ->
                    let tid = System.Threading.Thread.CurrentThread.ManagedThreadId 
                    // In case other thread pool activities have polluted this one, sigh
                    SynchronizationContext.SetSynchronizationContext null
                    require (System.Threading.Thread.CurrentThread.IsThreadPoolThread) "expected thread pool thread (1)"
                    let t =
                        backgroundTask {
                            require (System.Threading.Thread.CurrentThread.IsThreadPoolThread) "expected thread pool thread (2)"
                            let tid2 = System.Threading.Thread.CurrentThread.ManagedThreadId 
                            require (tid = tid2) "expected synchronous starts when already on background thread"
                            do! Task.Delay(200)
                            ran <- true
                        }
                    t.Wait()
                    require ran "never ran")
            taskOuter.Wait()
                 
    [<Fact; >]
    member _.testGenericBackgroundTasks() =
        printfn "Running testBackgroundTask..."
        for i in 1 .. 5 do 
            let mutable ran = false
            let mutable posted = false
            let oldSyncContext = SynchronizationContext.Current
            let syncContext = { new SynchronizationContext()  with member _.Post(d,state) = posted <- true; d.Invoke(state) }
            try 
                SynchronizationContext.SetSynchronizationContext syncContext
                let f (result: 'T ref) (x: 'T) =
                    backgroundTask {
                        require (System.Threading.Thread.CurrentThread.IsThreadPoolThread) "expect to be on background thread"
                        ran <- true
                        result.Value <- x
                    }
                let t = f (ref "") "hello"
                t.Wait()
                let t2 = f (ref 1) 1
                t2.Wait()
                require ran "never ran"
                require (not posted) "did not expect post to sync context"
            finally
                SynchronizationContext.SetSynchronizationContext oldSyncContext
                 

/// https://github.com/dotnet/fsharp/issues/12761
module Test12761A =

    type Dto = {
        DtoValue : string
        Key : string
    }

    type MyGenericType<'Key,'Value> = {
        Value : 'Value
        Key : 'Key
    }

    type ProblematicType<'Key, 'Value, 'Dto, 'E>( fromDto : 'Dto -> Result<MyGenericType<'Key,'Value>,'E> ) =
        let myTask = 
            backgroundTask {
                    let dto =  """{"DtoValue":"1","Key":"key1"}""" |> box |> unbox<'Dto>
                return fromDto dto |> printfn "%A"
            }
        member __.ContainsKey = fun (key: 'Key) -> true


    type MyType = MyGenericType<string,int>

    module MyType =
        let fromDto (dto: Dto)  =
            try
                {
                    Value = int dto.DtoValue
                    Key = dto.Key
                } 
                |> Ok
            with | e -> Error e


/// https://github.com/dotnet/fsharp/issues/12761
module Test12761B =
    let TestFunction<'Dto>() =
            backgroundTask {
                let dto = Unchecked.defaultof<'Dto>
                System.Console.WriteLine(dto)
            }

type Issue12184() =
    member this.TaskMethod() =
        task {
            // The overload resolution for Bind commits to 'Async<int>' since the type annotation is present.
            let! result = this.AsyncMethod(21)
            return result
        }

    member _.AsyncMethod(value: int) : Async<int> =
        async {
            return (value * 2)
        }

type Issue12184b() =
    member this.TaskMethod() =
        task {
            // The overload resolution for Bind commits to 'YieldAwaitable' since the type annotation is present.
            let! result = this.AsyncMethod(21)
            return result
        }

    member _.AsyncMethod(_value: int) : System.Runtime.CompilerServices.YieldAwaitable =
        Task.Yield()

// check this compiles 
module Issue12184c =
    let TaskMethod(t) =
        task {
            // The overload resolution for Bind commits to 'Task<_>' via overload since no type annotation is available
            //
            // This should not do an early commit to "task like" nor propagate SRTP constraints from the task-like overload for Bind.
            let! result = t
            return result
        }

#if NETCOREAPP
// check this compiles 
module Issue12184d =
    let TaskMethod(t: ValueTask) =
        task {
            // The overload resolution for Bind commits to 'ValueTask' via SRTP pattern since the type annotation is available
            let! result = t
            return result
        }

// check this compiles 
module Issue12184e =
    let TaskMethod(t: ValueTask<int>) =
        task {
            // The overload resolution for Bind commits to 'ValueTask<_>' via SRTP pattern since the type annotation is available
            let! result = t
            return result
        }
#endif

// check this compiles 
module Issue12184f =
    let TaskMethod(t: Task) =
        task {
            // The overload resolution for Bind commits to 'Task' via SRTP pattern since the type annotation is available
            let! result = t
            return result
        }