# Enabling Batch Events for ObservableCollection

**Owner** [Immo Landwerth](https://github.com/terrajobst) | [Eirik Tsarpalis](https://github.com/eiriktsarpalis)

The .NET platform has infrastructure to enable *view models* that is, an object
model that UI can be bound to such that when the data changes the UI refreshes
automatically.

One of the core building blocks for this is `INotifyCollectionChanged`, with
it's canonical implementation of `ObservableCollection<T>`.

While the shape of `INotifyCollectionChanged.CollectionChanged` always supported
expressing batched notifications, `ObservableCollection<T>` only raises
notifications for individual items (except for the `Reset` event, which tells UI
to update everything).

In fact, `ObservableCollection<T>` has no APIs like `AddRange` which could allow
bulk operations. In the past we tried to add those but then we learned that some
of the .NET platform pieces took a dependency on the fact that
`ObservableCollection<T>` raises events for individual items and crash when the
event args represent more than one item, specifically WPF.

From a UI standpoint, bulk operations are desirable because they allow for UI
controls to be more performant when the data changes. Other UI frameworks that
support view models, such as Avalonia, Uno, and MAUI have expressed interest in
seeing this change. In fact, the [tracking issue][issue] is eight years old and
has over four hundred comments, with several folks expressing frustration that
this issue hasn't been addressed yet.

The goal of this document is to explore ways to address this request.

## Requirements

### Goals

* Enable bulk operations for `Add`, `Remove`, `Insert`, and `Replace` that
  result in a single notification for view models.

### Non-Goals

* Support bulk notifications for discontinuous ranges in the collection

## Stakeholders and Reviewers

* Platform components
    - Libraries team
    - WinForms team
    - WPF team
    - WinUI team
    - UWP team
    - MAUI team
* External UI platforms
    - Avalonia
    - Uno

## Design

### API Changes

We don't need to make any changes to `INotifyCollectionChanged` as
`NotifyCollectionChangedEventArgs` can already represent ranges:

```C#
public partial class NotifyCollectionChangedEventArgs : EventArgs
{
    // Constructors omitted
    public NotifyCollectionChangedAction Action { get; }
    public IList? NewItems { get; }
    public IList? OldItems { get; }
    public int NewStartingIndex { get; }
    public int OldStartingIndex { get; }
}
```

> [!NOTE]
> Please note that the API shape only supports batched notifications if the
> affected range is contiguous, for example, inserting multiple items or
> removing a contiguous range. It doesn't, for example, support removing
> multiple discontiguous items.

`ObservableCollection<T>` needs to expose new APIs for performing bulk
operations that will result in bulk notifications:

```C#
namespace System.Collections.ObjectModel;

public class ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> collection);
    public void InsertRange(int index, IEnumerable<T> collection);
    public void RemoveRange(int index, int count);
    public void ReplaceRange(int index, int count, IEnumerable<T> collection);

    protected virtual void InsertItemsRange(int index, IEnumerable<T> collection);
    protected virtual void RemoveItemsRange(int index, int count);
    protected virtual void ReplaceItemsRange(int index, int count, IEnumerable<T> collection);
}
```

## How it breaks consumers

`ObservableCollection<T>` exists since .NET Framework 3.0, which was shipped in
2006, 18 years ago. While the eventing structure in principle supports raising
events for more than one item, the collection was never capable of raising such
events. As a result, consumers of `ObservableCollection<T>`, specifically WPF,
have baked in assumptions that these can never happen. As a result, when tried
to make these changes a ton of code in WPF broke, i.e. it crashed with
exceptions.

## Options to avoid breaking consumers

The most likely candidate for accommodating consumers is this:

1. Global switch to turn off bulk updates
2. Per instance switch to turn off bulk updates

We could decide to ship (1) first and see whether that's sufficient and add (2)
if deemed necessary.

Below are a list of all options that were identified, with their 

* **Do nothing**. We could just do nothing and simply let customers report bugs
  for controls that don't support bulk notifications. Strictly speaking, this
  isn't a behavioral change as existing code won't start raising bulk events
  unless the user is calling the new methods on `ObservableCollection<T>`. The
  guidance would be "use bulk operations if the data bound UI supports it,
  otherwise don't". We would treat the lack of WPF support as a feature request
  and let it run its course.
  - Pro: Simplest API, easiest to implement, unblocks the ecosystem.
  - Con: Violates our goal to not ship functionality we know won't work with
    core in-box functionality (specifically WPF).

* **Global switch**. We could add an `AppContext` switch which disables bulk
  updates process-wide.
  - Pro: Easy to implement, reduced complexity for the API.
  - Con: limiting if some UI components support bulk updates and some don't.

* **Per instance switch**. We could have a property on `ObservableCollection<T>`
  that turns off bulk notifications.
  - Pro: Allows some part of the view model to use bulk notifications when the
    UI supports it while turning it off for the parts that don't.
  - Con: Still limiting when the same collection is bound to multiple UI
    components and some support bulk notifications and some don't. Also
    complicates the core API.

* **Event adaptor**. We could provide an implementation of
  `INotifyCollectionChanged` that wraps an existing `INotifyCollectionChanged`
  and translates bulk operations to individual events. Authors of view models
  could expose two properties, one with the actual collection and another one
  that returns the wrapper. In data binding, they use the property with bulk
  notifications if the UI supports it and the other if it doesn't.
  - Pro: Core API is simple while providing an affordance to the user to solve
    the problem without requiring changes from the UI framework.
  - Con: More complex state, might not play well with WPF's `CollectionView`.
    > [!NOTE]
    > Doesn't seem feasible. Handlers of the event likely assume the collection
    > was just changed to include the one modification. Just translating the
    > event would mean they get a series of events but the handler sees the
    > final collection in all invocations.

* **Event enumerator**. We could expose a method on
  `NotifyCollectionChangedEventArgs` that returns an
  `IEnumerable<NotifyCollectionChangedEventArgs>`. For single item events it
  returns itself, otherwise it creates a new event arg per item. This requires
  UI controls to make code changes if they don't support bulk notifications, but
  the change is fairly targeted.
  - Pro: Simple API
  - Con: Still breaks consumers, just provides an easier path to adapt to the
    new behavior without having to fully support bulk notifications.
    > [!NOTE]
    > Doesn't seem feasible. Handlers of the event likely assume the collection
    > was just changed to include the one modification. Just translating the
    > event would mean they get a series of events but the handler sees the
    > final collection in all invocations.

* **Handling it at the CollectionView level**. WPF seems to have an indirection
  between the data-bound UI controls and the observable collections via the
  collection view. Maybe we could expose a property on `CollectionView` that
  controls whether the collection view will translate bulk events to individual
  events.
  - Pro: Simpler API for the base, with an opt-out switch at the level of WPF.
  - Con: Needs more investigation to determine feasibility, would only be helpful for WPF

* **Separate API**. We could add a new interface
  `IBatchedNotifyCollectionChanged` interface that `ObservableCollection<T>`
  also implements. The interface adds new even `BulkCollectionChanged`.
  - Pro: Consumers opt-into bulk events by subscribing to a different event.
    This allows a single instance of `ObservableCollection<T>` to be bound to
    multiple UI elements and leverage bulk updates when it's supported while
    falling back when it's not. This would also allow change of semantics to
    allow for non-contiguous updates, for instance.
  - Con: Complicates that API; requires all consumers to explicitly opt-in, even
    if happen to already support bulk notifications. Would also likely require
    adding a corresponding interface to WinUI that .NET can project to. Will
    likely take longer to be supported by UI frameworks.
    > [!NOTE]
    > For the same reason event adapters aren't viable just having a new
    > interface is probably not sufficient. Rather, we'd need a different
    > implementation like `BulkObservableCollection<T>`. However, for WinUI
    > we likely also still need an interface too.

[issue]: https://github.com/dotnet/runtime/issues/18087