# Improve Activity API usability (Part 3)

The improvements up to [part 2](accepted/2020/diagnostics/activity-improvements-2.md) have made things substantially more usable for the Activity API. This is a small proposed improvement to introduce a sensible default activity name.

## ActivitySource.StartActivity

We currently require an activity `name` to be provided as the only required argument in `ActivitySource.StartActivity` overloads. For scenarios where the caller wants to match the activity with the calling method name, providing that as a default value would make calling code less repetitive.

## ***API  Proposal***

```c#
    public Activity? StartActivity([CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
            => StartActivity(name, kind, default, null, null, null, default);
```

In addition, when the only thing the caller wants to customize is the `ActivityKind`, a new overload would also be useful:

## ***API  Proposal***

```c#
    public Activity? StartActivity(ActivityKind kind, [CallerMemberName] string name = "")
            => StartActivity(name, kind, default, null, null, null, default);
```

A proposed PR is in place at https://github.com/dotnet/runtime/pull/39761 (sent that too quickly, I think).
