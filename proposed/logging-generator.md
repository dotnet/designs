# Logging Generator for strongly-typed logging messages

## Background

Currently there are two forms possible for doing logging, using `Microsoft.Extensions.Logging`:

```csharp
public class LoggingSample1
{
    private ILogger _logger;

    public LoggingSample1(ILogger logger)
    {
        _logger = logger;
    }

    public void LogMethod(string name)
    {
        _logger.LogInformation("Hello {name}", name);
    }
}
```

Here are some problems with the `LoggingSample1` sample using `LogInformation`, `LogWarning`, etc.: 
1. We can provide event ID through these APIs, but they are not required today. Which leads to bad usages in real systems that want to react or detect specific event issues being logged.
2. Parameters passed are processed before LogLevel checks; this leads to unnecessary code paths getting triggered even when logging is disabled for a log level.
3. It requires parsing of message string on every use to find templates to substitute.

Because of these problems, the more efficient approach recommended today as best practices is to use [LoggerMessage.Define](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/loggermessage?view=aspnetcore-5.0) APIs instead, illustrated below with `LoggingSample2`:

```csharp
public class LoggingSample2
{
    private ILogger _logger;

    public LoggingSample2(ILogger logger)
    {
        _logger = logger;
    }

    public void LogMethod(string name)
    {
        Log.LogName(_logger, name);
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, Exception> _logName = LoggerMessage.Define<string>(LogLevel.Information, 0, @"Hello {name}");

        public static void LogName(ILogger logger, string name)
        {
            _logName(logger, name, null!);
        }
    }
}
```

It would be better in most end-user applications to not have to repeat the generic argument types above while declaring the strongly-typed ILogger methods. Using these Action-returning static methods via `LoggerMessage.Define<T1,T2,...,Tn>`, even though results into writing more efficient logs and is recommended as best practices, it still does not necessarily enforce event ID uniqueness for each logging method. `LoggingSample2` also involves some boilerplace code that developers would prefer to reduce maintaining.

## Requirements

### Goals

Today it is difficult to declare strongly-typed code paths for ILogger messages (e.g. `LogMethod` APIs shown in both samples above) in a way that's efficient, approachable and maintainable by an entire team all at the same time. The solution we are looking for should ideally lead/guide developers into writing more declarative, less verbose, and easy to write, logging APIs with reduced boilerplate code. 

Part of the goal is to enforce best practices on library authors, so they would be required to provide event IDs, add diagnostics to warn against reusing event IDs, and lead towards writing more efficient logs (all of which discourage against using `LoggingSample1`).

### Non-Goals

Also it's been raised that `LoggerMessage.Define` approach triggers boxing of data, and we wanted to understand if there are ways to improve this with a new and improved design.

## Using a logging source generator

The first solution describes using a source generator to help reduce manual boilerplate code associated with writing efficient logs and makes structured logging more convenient to use. The source generator gets triggered with a `LoggerMessageAttribute` on partial logging methods, and is able to either autogenerate the implementation of these partial methods, or produces compile-time diagnostics hinting to proper usage of this logging approach.

### API Proposal

`LoggerMessageAttribute` As shown in the examples, is used by the developer to trigger source generation:

```csharp
namespace Microsoft.Extensions.Logging
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class LoggerMessageAttribute : Attribute
    {
        public LoggerMessageAttribute(int eventId, LogLevel level, string? message = null);
        public LoggerMessageAttribute(int eventId, string? message = null);
        public int EventId { get; }
        public string? EventName { get; set; }
        public LogLevel? Level { get; }
        public string? Message { get; }
    }
}
```

### Usage Examples: 

A log method can be a plain static method, an extension method, or an instance method (where the ILogger comes from a field on the containing type).

### Sample with Log instance method

When generating instance-mode logging methods, the field that holds the logger is now determined by looking at available fields. You get an error if there isn't a field of the right type or if there is more than one such field. This also does not look up the inheritance hierarchy, but it would be possible to add this capability in the future.

```csharp
public partial class LoggingSample3
{
    private readonly ILogger _logger;

    public LoggingSample3(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(0, LogLevel.Information, "Hello {name}")]
    public partial void LogName(string name);
}
```

Where the implementation for `LogName` would be completed by the source generator. For example, the generated code for `LoggingSample3` would look like this:

```csharp
    partial class LoggingSample3
    {
        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
        private static readonly global::System.Action<global::Microsoft.Extensions.Logging.ILogger, string, global::System.Exception?> _LogNameCallback =
            global::Microsoft.Extensions.Logging.LoggerMessage.Define<string>(global::Microsoft.Extensions.Logging.LogLevel.Information, new global::Microsoft.Extensions.Logging.EventId(0, nameof(LogName)), "Hello {name}", true);

        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
        public partial void LogName(string name)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
            {
                _LogNameCallback(_logger, name, null);
            }
        }
    }
```


### Sample with static class Log

```csharp
public static partial class Log
{
    [LoggerMessage(0, LogLevel.Information, "Hello `{name}`")]
    public static partial void LogName(this ILogger logger, string name);
}

public class LoggingSample4
{
    private ILogger _logger;

    public LoggingSample4(ILogger logger)
    {
        _logger = logger;
    }

    public void LogName(string name)
    {
        _logger.LogName(name);
    }
}
```


### Sample with exception as argument to log method

`ILogger.Log` API signature takes log level and optionally an exception per log call:

```csharp
    public partial interface ILogger
    {
        void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter);
    }
```

Therefore, as a general rule, the first instance of ILogger, LogLevel, and Exception are treated specially in the log method signature of the source generator. Subsequent instances are treated like normal arguments to the message template:

```c#
// below works
        [LoggerMessage(110, LogLevel.Debug, "M1 {ex3} {ex2}")]
        static partial void LogMethod(ILogger logger, System.Exception ex, System.Exception ex2, System.Exception ex3);

// but this warns:
        // warning SYSLIB0013: Don't include a template for ex in the logging message since it is implicitly taken care
	// DiagnosticSeverity.Warning,
        [LoggerMessage(0, LogLevel.Debug, "M1 {ex} {ex2}")]
        static partial void LogMethod(ILogger logger, System.Exception ex, System.Exception ex2);
```

### Options

The generator supports 2 global options that it recognizes:

PascalCaseArguments : YES/NO

This will convert argument names to pascal case (from city to City) within the generated code such that when the ILogger enumerates the state, the argument will be in pascal case, which can make the logs nicer to consume. This defaults to NO.

#### Sample with alternative names for name holes

```csharp
public partial class LoggingSample6
{
    private readonly ILogger _logger;

    public LoggingSample6(ILogger logger)
    {
        _logger = logger;
    }

    // possible when PascalCaseArguments is set to true:
    [LoggerMessage(10, LogLevel.Warning, "Welcome to {City} {Province}!")]
    public partial void LogMethodSupportsPascalCasingOfNames(string city, string province);

    public void TestLogging()
    {
        LogMethodSupportsPascalCasingOfNames("Vancouver", "BC");
    }
}
```

EmitDefaultMessage : YES/NO

This controls whether to generate a default message when none is supplied in the attribute. This defaults to YES.

#### Other miscellaneous logging samples using the generator

There is no constraint in the ordering at this point. So the user could define `ILogger` as the last argument for example:
```c#
        [LoggerMessage(110, LogLevel.Debug, "M1 {ex3} {ex2}")]
        static partial void LogMethod(System.Exception ex, System.Exception ex2, System.Exception ex3, ILogger logger);
```

The samples below show we could:

- `LogEmptyMessage`: generate a default empty message when none is supplied in the `LoggerMessage` attribute. 
- `LogWithCustomEventName`: retrieve event name via `LoggerMessage` attribute. 
- `LogWithDynamicLogLevel`: set log level dynamically, to allow log level to be set based on configuration input.
- `LogWithNoTemplate`: auto-generate a message as a JSON blob if you don't supply one to output parameters.

```csharp
public partial class LoggingSample5
{
    private readonly ILogger _logger;

    public LoggingSample5(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(1, LogLevel.Trace)]
    public partial void LogEmptyMessage();

    [LoggerMessage(9, LogLevel.Trace, "Fixed message", EventName = "CustomEventName")]
    public partial void LogWithCustomEventName();

    [LoggerMessage(10, "Welcome to {city} {province}!")]
    public partial void LogWithDynamicLogLevel(string city, LogLevel level, string province);

    [LoggerMessage(2, LogLevel.Trace)]
    public partial void LogWithNoTemplate(string key1, string key2);

    public void TestLogging()
    {
        LogEmptyMessage();
        LogWithCustomEventName();
        LogWithDynamicLogLevel("Vancouver", LogLevel.Warning, "BC");
        LogWithDynamicLogLevel("Vancouver", LogLevel.Information, "BC");
        LogWithNoTemplate("value2", "value2");
    }
}
```
output to `TestLogging()` using SimpleConsole:
```
trce: LoggingExample[1]
      {}
trce: LoggingExample[9]
      Fixed message
warn: LoggingExample[10]
      Welcome to Vancouver BC!
info: LoggingExample[10]
      Welcome to Vancouver BC!
trce: LoggingExample[2]
      {"key1":"value2","key2":"value2"}
```

### Diagnostics

This [gist](https://gist.github.com/maryamariyan/a1ab553bedb26b9886fbc2740ee9e954) shows 20 diagnostic messages that the generator can produce alongside use cases for each

Using SYSLIBXXXX format as the diagnostic IDs. The gist also shows diagnostic categories against each sample.

### Q & A - The source generator:

- Question: Why would we want to enforce event ID uniqueness checks if existing log APIs still allow for using default event ID values? For example with `LogInformation` we have two overloads, one taking `EventId` and the other one not. But the proposal with `LoggerMessageAttribute` only requires taking an `EventId`.

Answer: The enforced restriction added via `LoggerMessage` attribute aims at providing best practices for library authors more so than it does for app developers, who in most cases do not care about event IDs.

This is a great feedback. In order to make this approach less restrictive, it would make sense to also allow `LoggerMessageAttribute` skip taking event IDs in combination with an analyzer that generates an error to recommend best practices while allowing app developers to cut corners for productivity. When turned off, the event IDs would default to zero or implicit event IDs would get generated.

## Supporting string interpolation builder in logging

This section describes using a language feature (called string interpolation builder) to solve the problem of strongly-typing code paths for ILogger messages.

### Usage examples / API Proposal:

As opposed to writing:

```csharp
[LoggerMessage(0, LogLevel.Information, "Hello `{name}`")]
public static partial void LogName(this ILogger logger, string name, Exception exception);
```

described in the previous section, the usage would look like:

```csharp
public static void LogName(this ILogger logger, string name, Exception exception) =>
    logger.Log(LogLevel.Information, new EventId(0), $"Hello `{name}`", exception, (s, e) => s.ToString());
```

where `(s, e) => s.ToString()` is the format callback.

But then in fact, the compiler would translate the above call to:

```csharp
var receiverTemp = logger;
var builder = CustomLoggerParamsBuilder.Create(
    baseLength: 6,
    formatHoleCount: 1,
    receiverTemp, LogLevel.Information, out var builderIsValid);
_ = builderIsValid &&
    && builder.TryFormatBaseString("Hello ")
    && builder.TryFormatInterpolationHole(name, "name");

logger.Log<CustomLoggerParamsBuilder>(
    LogLevel.Information,
    new EventId(1),
    builder, // the CustomLoggerParamsBuilder as log state
    exception,
    (s, e) => s.ToString()
);
```



On C# 10 and above. `CustomLoggerParamsBuilder` would be a custom [string interpolation builder](https://github.com/333fred/csharplang/blob/2550a43b391e844faaa0a2023f66489328a41612/proposals/improved-interpolated-strings.md) defined as:

```csharp
// The builder that will actually "build" the interpolated string"
public struct CustomLoggerParamsBuilder : IReadOnlyList<KeyValuePair<string, object>>
{
    public static CustomLoggerParamsBuilder Create(int baseLength, int formatHoleCount, ILogger logger, LogLevel logLevel, out bool builderIsValid)
    {
        if (!logger.IsEnabled(logLevel))
        {
            builderIsValid = false;
            return default;
        }

        builderIsValid = true;
        return new CustomLoggerParamsBuilder(baseLength, formatHoleCount);
    }

    // returns the number of holes
    public int Count => ... // depends on the type used to store key value pairs

    private CustomLoggerParamsBuilder(int baseLength, int formatHoleCount) { }

    public bool TryFormatBaseString(string s)
    {
        // Store and format part as required
        return true;
    }

    public bool TryFormatInterpolationHole<T>(T t)
    {
        // Store and format part as required 
        // To store would need to box to T
        // Store name hole and value as KeyValuePair<string, object?>
        return true;
    }

    public KeyValuePair<string, object?> this[int index]
    {
        get => ... // depends on the type used to store key value pairs
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (int i = 0; i < Count; ++i)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
```

### Q & A - The builder approach:

- Question: Can I use a custom string interpolation builder in an async context?

```csharp
public async Task Method()
{
    var key1 = "value1";
    var key2 = "value2";
    logger.LogInformation($"Some text: {key1}, {key2}");
    // ...
    await Method2();
}
```

Answer: Yes, as long as the builder is not a ref struct, we can expose a set of `TryFormatXx` methods so that it would not fall back to a `string.Format` call.

- Question: Is duck typing supported for this string interpolated builder language feature?

Answer: Yes, as long as `TryFormatX` APIs are defined on a new builder type, then this feature would be enabled. 

### Sample-based comparison of two approaches

1. (+1 for the C# 10 Language Feature) _With the interpolated string builder, callers don't need to guard the call sites if any computation is necessary to produce the arguments to the logging. Whereas with the source generator, the user call site would need to get wrapped around `IsEnabled` checks._

For example with the source generator, the user call site would need to get wrapped around `IsEnabled` check for this specific use case to skip evaluating `Describe(..)` which might be expensive to compute:

```csharp
public static void DescribeFoundCertificates(this ILogger logger, IEnumerable<X509Certificate2> matchingCertificates)
{
    if (logger.IsEnabled(LogLevel.Trace))
    {
        DescribeFoundCertificates(logger, Describe(matchingCertificates));
    }
}

[LoggerMessage(2, LogLevel.Trace, "Certificates: `{matchingCertificates}`")]
private static partial void DescribeFoundCertificates(this ILogger logger, string matchingCertificates);
```

Whereas the same sample using the string interpolation builder allows writing:

```csharp
public static void DescribeFoundCertificates(this ILogger logger, IEnumerable<X509Certificate2> matchingCertificates) =>
    logger.Log(2, LogLevel.Trace, $"Certificates: `{Describe(matchingCertificates)}`");
```

and allow for expensive computations to be skipped when logging is not enabled.

2. (+1 for Source Generator) _The source generator provides useful error messages that don't exist using completely generic mechanisms to enforce logging best practices._

For example, the generator can provide an event ID uniqueness diagnostics check with warning severity so it can be suppressed.

The scope of uniqueness for the event IDs would be the class in which the log method is declared. It doesn't check up the inheritance hierarchy for conflicting IDs because it also wouldn't be possible cross-assembly boundaries. The diagnostic message for this condition would be a warning indicating the class name:

```csharp
    static partial class LogClass
    {
        [LoggerMessage(32, LogLevel.Debug, "M1")]
        static partial void M1(ILogger logger);
        [LoggerMessage(32, LogLevel.Debug, "M2")]
        static partial void M2(ILogger logger);
    }
```
produces diagnostic message:

```
warning SYSLIB0005: Multiple logging methods are using event id 32 in class LogClass
```
For more clarity, the documentation in the future would need to mention that the scope for event ID uniqueness is checked per class itself not at base classes.

### Challenges with string interpolation builder approach:

- Using the language feature, would require the user to supply interpolated strings as TState for `ILogger.Log` messages. Such code if compiled against C# 9 or lower, not only would cause log messages to completely lose key/value pair log structures provided from within message templates, but would make the calls very inefficient. This could be mitigated with an analyzer that warns user when they use string interpolated messages on older compiler versions.

- Our existing logging APIs are capable of recognizing name holes, or even format specifiers from message templates. Therefore, through this new language feature we would also want to be able to allow this. 

  - The above builder design is limited in its ability to get both name holes and format specifiers at once. Achieving this depends on how creative we want to get with the format string. We could use some currently-unused character to specify a _separator_ between the regular format string part and the name you want the hole to have. It would need to go through further design than presented in the proposal above to achieve this completely.

- The builder will actually "build" the interpolated string", but also needs to allocate space for keeping key value pair structure that would not know types ahead of time. This cannot be done lazily because the structure we generate could ultimately get serialized in different ways decided by the different consumers of `ILogger.Log` call. The end result of using the builder this way, could end up being less efficient than what we already have in our logging APIs, even though our current approaches already do boxing.

### Benefits with the source generator approach:

- Allows the logging structure to be preserves and enables the exact format syntax required by https://messagetemplates.org/
- Allows supplying alternative names for the holes (this may be achievable by C# 10 as well throgh a careful design via format specifiers)
- Allows to pass all of original data as-is without any complication around how it's stored prior to something being done with it other than creating a string.
- The source generator provides logging-specific diagnostics: it emits warnings for duplicate event ids, or it'll auto-generate a message if you don't supply one to output parameters as a JSON blob, etc.

### Conclusion

Both approaches discussed above have clear benefits. With the interpolated string builder, callers don't need to guard the call sites if any computation is necessary to produce the arguments to the logging, whereas the callers of source generated APIs would need `IsEnabled` checks to guard against such computations. The source generator is able to provide a proper guided experience for logging using the many diagnostics messages that it is combined with.

There are a couple of open design questions that the interpolated string builder approach would still need to address: (1) how to allow alternative names for name holes, (2) how to lazily hold onto structured log data for consumers of log APIs to themselves to materialize them in customized ways. 

The research investigation summarized in this document, has examined two different solutions to account for writing more declarative and strongly-typed logging methods. If we wanted to take advantage of the C# 10 language feature that would need to go through more design iterations. But the source generator approach is more developed at this point and is not far from how we currently write performant logging today (as initially illustrated with in `LoggingSample2`) and at the same time is improving usability and provides a proper guided experience to best practices. Due to these set of arguments it would be good to go forward with the first solution of using a source generator.

This investigation concludes there is an argument for using the source generator and at a high level the two solutions are not mutually exclusive. Logging is an important use case for the string interpolation builder language feature. Therefore it would be benefitial to continue building up a complete design for the builder as an opportunity to identify any gaps with the language feature. Once we design is complete then it can be developed and used as part of improving `Microsoft.Extensions.Logging`.

