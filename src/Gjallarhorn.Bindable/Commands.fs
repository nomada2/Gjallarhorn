﻿namespace Gjallarhorn.Bindable

open Gjallarhorn
open Gjallarhorn.Internal

open System
open System.Windows.Input

/// Simple Command implementation for ICommand and INotifyCommand
type BasicCommand (execute : obj -> unit, canExecute : obj -> bool) =
    let canExecuteChanged = new Event<EventHandler, EventArgs>()

    member this.RaiseCanExecuteChanged() =
        canExecuteChanged.Trigger(this, EventArgs.Empty)

    interface ICommand with
        [<CLIEvent>]
        member __.CanExecuteChanged = canExecuteChanged.Publish

        member __.CanExecute(param : obj) =
            canExecute(param)

        member __.Execute(param : obj) =
            execute(param)
    
/// Command type which uses an IView<bool> to track whether it can execute, and implements IView<'a> with the command parameter each time the command updates
/// Note that this will signal for each execution, whether or not the value has changed.
type ParameterCommand<'a> (initialValue : 'a, allowExecute : IView<bool>) as self =
    let canExecuteChanged = new Event<EventHandler, EventArgs>()

    let disposeTracker = new CompositeDisposable()
    let mutable value = initialValue

    let dependencies = Dependencies.create [| |] self

    do
        allowExecute
        |> View.subscribe (fun _ -> self.RaiseCanExecuteChanged())    
        |> disposeTracker.Add

    member this.RaiseCanExecuteChanged() =
        canExecuteChanged.Trigger(this, EventArgs.Empty)

    /// Used to process the command itself
    abstract member HandleExecute : obj -> unit
    default this.HandleExecute(param : obj) =
        let v = Utilities.downcastAndCreateOption param
        match v with
        | Some newVal ->
            value <- newVal
            dependencies.Signal this
        | None ->
            ()

    interface ITrackingCommand<'a> 
    
    interface IObservable<'a> with
        member __.Subscribe obs = 
            dependencies.Add obs
            { 
                new IDisposable with
                    member __.Dispose() = dependencies.Remove obs
            }

    interface ITracksDependents with
        member __.Track dep = dependencies.Add dep
        member __.Untrack dep = dependencies.Remove dep

    interface IDependent with
        member __.RequestRefresh _ = ()
        member __.HasDependencies = dependencies.HasDependencies

    interface IView<'a> with
        member __.Value with get() = value

    interface ICommand with
        [<CLIEvent>]
        member __.CanExecuteChanged = canExecuteChanged.Publish

        member __.CanExecute(_ : obj) =
            allowExecute.Value

        member this.Execute(param : obj) =
            this.HandleExecute(param)

    interface IDisposable with
        member __.Dispose() = disposeTracker.Dispose()

/// Reports whether a command is executed, including the timestamp of the most recent execution
type CommandState =
    /// The command is in an unexecuted state
    | Unexecuted
    /// The command has been executed at a specific time
    | Executed of timestamp : DateTime

/// Command type which uses an IView<bool> to track whether it can execute, and implements IView<CommandState>, where each execute passes DateTime.UtcNow on execution
type ViewCommand (allowExecute : IView<bool>) =
    inherit ParameterCommand<CommandState>(Unexecuted, allowExecute)

    override __.HandleExecute _ =
        base.HandleExecute(Executed(DateTime.Now))

/// Reports whether a paramterized command is executed, including the timestamp of the most recent execution
type CommandParameterState<'a> =
    /// The command is in an unexecuted state
    | Unexecuted
    /// The command has been executed at a specific time with a specific argument
    | Executed of timestamp : DateTime * parameter : 'a    

/// Command type which uses an IView<bool> to track whether it can execute, and implements IView<DateTime>, where each execute passes DateTime.UtcNow on execution
type ViewParameterCommand<'a> (allowExecute : IView<bool>) =
    inherit ParameterCommand<CommandParameterState<'a>>(Unexecuted, allowExecute)

    override __.HandleExecute p =
        base.HandleExecute(Executed(DateTime.Now, p))

/// Core module for creating and using ICommand implementations
module Command =        
    /// Create a command with an optional enabling source, provided as an IView<bool>
    let create enabledSource =
        (new ViewCommand(enabledSource)) :> ITrackingCommand<CommandState>

    /// Create a command which is always enabled
    let createEnabled () =
        create (View.constant true)

    /// Create a subscription to the changes of a command which calls the provided function upon each change
    let subscribe (f : DateTime -> unit) (provider : ITrackingCommand<CommandState>) = 
        let f state =
            match state with
            | CommandState.Executed(time) -> f(time)
            | _ -> ()
        View.subscribe f provider