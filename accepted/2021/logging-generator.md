# Compile-time source generation for strongly-typed logging messages

**Dev** [Maryam Ariyan](https://github.com/maryamariyan) | [Martin Taillefer](https://github.com/geeknoid)

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

## Goal

Today it is difficult/verbose to declare strongly-typed code paths for ILogger messages (e.g. `LogMethod` APIs shown in both samples above) in a way that's efficient, approachable and maintainable by an entire team all at the same time. The solution we are looking for should ideally lead/guide developers into writing more declarative, less verbose, and easy to write, logging APIs with reduced boilerplate code. 

Part of the goal is to also enable diagnostics, to enforce best practices on library authors, so they would be required to provide event IDs, warn them against reusing event IDs, and lead towards writing more efficient logs (all of which discourage against using `LoggingSample1`).

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
        public LoggerMessageAttribute();
        public int EventId { get; set; }
        public string? EventName { get; set; }
        public LogLevel Level { get; set; } = LogLevel.None;
        public string Message { get; set; } = "";
    }
}
```

When using `LoggerMessageAttribute`, the source generator would be making sure both `Level` and `Message` are provided by the user. For library authors it makes sense to have `EventId` required as well. Or, the event IDs could potentially default to zero or implicit event IDs would get generated. This way the diagnostics for `EventId` uniqueness checks still recommend best practices while allowing app developers to cut corners for productivity.

### Usage Examples: 

A log method can be a plain static method, an extension method, or an instance method (where the ILogger comes from a field on the containing type).

#### Sample with Log instance method

When generating instance-mode logging methods, the field that holds the logger is now determined by looking at available fields. You get an error if there isn't a field of the right type or if there is more than one such field. This also does not look up the inheritance hierarchy, but it would be possible to add this capability in the future.

```csharp
public partial class LoggingSample3
{
    private readonly ILogger _logger;

    public LoggingSample3(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Hello {name}")]
    public partial void LogName(string name);
}
```

Where the implementation for `LogName` would be completed by the source generator. For example, the generated code for `LoggingSample3` would look like this:

```csharp
partial class LoggingSample3
{
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
    private static readonly global::System.Action<global::Microsoft.Extensions.Logging.ILogger, string, global::System.Exception?> _LogNameCallback =
        global::Microsoft.Extensions.Logging.LoggerMessage.Define<string>(global::Microsoft.Extensions.Logging.LogLevel.Information, new global::Microsoft.Extensions.Logging.EventId(0, nameof(LogName)), "Hello {name}", skipEnabledCheck: true);

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


#### Sample with static class Log

```csharp
public static partial class Log
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Hello `{name}`")]
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

#### Sample with exception as argument to log method

Since the `ILogger.Log` API signature takes log level and optionally an exception per log call:

```csharp
public partial interface ILogger
{
    void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter);
}
```

Therefore, as a general rule, the first instance of ILogger, LogLevel, and Exception are treated specially in the log method signature of the source generator. Subsequent instances are treated like normal arguments to the message template:

```csharp
// below works
[LoggerMessage(EventId = 110, Level = LogLevel.Debug, Message = "M1 {ex3} {ex2}")]
static partial void LogMethod(ILogger logger, System.Exception ex, System.Exception ex2, System.Exception ex3);

// but this warns:
// DiagnosticSeverity.Warning - SYSLIB0013: Don't include a template for ex in the logging message since it is implicitly taken care
[LoggerMessage(EventId = 0, Level = LogLevel.Debug, Message = "M1 {ex} {ex2}")]
static partial void LogMethod(ILogger logger, System.Exception ex, System.Exception ex2);
```

### Case-insensitive parameter/template name support

The generator does case-insensitive comparison between parameters in message template and log message argument names so when the ILogger enumerates the state, the argument will be picked up by message template, which can make the logs nicer to consume:

```csharp
public partial class LoggingSample6
{
    private readonly ILogger _logger;

    public LoggingSample6(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Infomration, Message = "Welcome to {City} {Province}!")]
    public partial void LogMethodSupportsPascalCasingOfNames(string city, string province);

    public void TestLogging()
    {
        LogMethodSupportsPascalCasingOfNames("Vancouver", "BC");
    }
}
```

Json console output (notice the log arguments become uppercase):
```
{
  "EventId": 13,
  "LogLevel": "Information",
  "Category": "LoggingExample",
  "Message": "Welcome to Vancouver BC!",
  "State": {
    "Message": "Welcome to Vancouver BC!",
    "City": "Vancouver",
    "Province": "BC",
    "{OriginalFormat}": "Welcome to {city} {province}!"
  }
}
```

### Order of arguments do not matter

There is no constraint in the ordering at this point. So the user could define `ILogger` as the last argument for example:
```csharp
[LoggerMessage(EventId = 110, Level = LogLevel.Debug, Message = "M1 {ex3} {ex2}")]
static partial void LogMethod(System.Exception ex, System.Exception ex2, System.Exception ex3, ILogger logger);
```

### Miscellaneous logging samples using the generator

The samples below show we could:

- `LogWithCustomEventName`: retrieve event name via `LoggerMessage` attribute. 
- `LogWithDynamicLogLevel`: set log level dynamically, to allow log level to be set based on configuration input.
- `UsingFormatSpecifier`: use format specifiers to format logging parameters.

```csharp
public partial class LoggingSample5
{
    private readonly ILogger _logger;

    public LoggingSample5(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Critical, Message = "Value is {value:E}")]
    public static partial void UsingFormatSpecifier(ILogger logger, double value);

    [LoggerMessage(EventId = 9, Level = LogLevel.Trace, Message = "Fixed message", EventName = "CustomEventName")]
    public partial void LogWithCustomEventName();

    [LoggerMessage(EventId = 10, Message = "Welcome to {city} {province}!")]
    public partial void LogWithDynamicLogLevel(string city, LogLevel level, string province);

    public void TestLogging()
    {
        LogWithCustomEventName();
        LogWithDynamicLogLevel("Vancouver", Level = LogLevel.Warning, "BC");
        LogWithDynamicLogLevel("Vancouver", Level = LogLevel.Information, "BC");
        double val = 12345.6789;
        Log.UsingFormatSpecifier(logger, val);
    }
}
```
output to `TestLogging()` using SimpleConsole:
```
trce: LoggingExample[9]
      Fixed message
warn: LoggingExample[10]
      Welcome to Vancouver BC!
info: LoggingExample[10]
      Welcome to Vancouver BC!
crit: LoggingExample[20]
      Value is 1.234568E+004
```

same console logs formatted using JsonConsole:
```
{
  "EventId": 9,
  "LogLevel": "Trace",
  "Category": "LoggingExample",
  "Message": "Fixed message",
  "State": {
    "Message": "Fixed message",
    "{OriginalFormat}": "Fixed message"
  }
}
{
  "EventId": 10,
  "LogLevel": "Warning",
  "Category": "LoggingExample",
  "Message": "Welcome to Vancouver BC!",
  "State": {
    "Message": "Welcome to Vancouver BC!",
    "city": "Vancouver",
    "province": "BC",
    "{OriginalFormat}": "Welcome to {city} {province}!"
  }
}
{
  "EventId": 10,
  "LogLevel": "Information",
  "Category": "LoggingExample",
  "Message": "Welcome to Vancouver BC!",
  "State": {
    "Message": "Welcome to Vancouver BC!",
    "city": "Vancouver",
    "province": "BC",
    "{OriginalFormat}": "Welcome to {city} {province}!"
  }
}
{
  "EventId": 20,
  "LogLevel": "Critical",
  "Category": "LoggingExample",
  "Message": "Value is 1.234568E+004",
  "State": {
    "Message": "Value is 1.234568E+004",
    "value": 12345.6789,
    "{OriginalFormat}": "Value is {value:E}"
  }
}
```

### Added benefits with source generators not supported with `LoggerMessage.Define`

As a general approach, when possible the source generator uses `LoggerMessage.Define` to process the method generation. But for some of the limitations listed here, so long as validation helps us realize we need to fallback to another approach, the source generator would then make sure to support more cases when possible:

- _Support for an arbitrary # of logging parameters. current approach tops out at 6_

We cannot use `LoggerMessage.Define` today to take advantage of its performance benefits if the message template has more than 6 arguments. We could add more overloads for it, up to 16 to mitigate this a bit further [dotnet/runtime#50913](https://github.com/dotnet/runtime/issues/50913). But with the source generator we could auto-generate boilerplate code below to add support for arbitrary number of arguments using same usage of `LoggerMessageAttribute`:

For example given sample:

```csharp
[LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "M9 {p1} {p2} {p3} {p4} {p5} {p6} {p7}")]
public static partial void Method9(ILogger logger, int p1, int p2, int p3, int p4, int p5, int p6, int p7);
```

the generated code looks like:
```csharp
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
public static partial void Method9(Microsoft.Extensions.Logging.ILogger logger, int p1, int p2, int p3, int p4, int p5, int p6, int p7)
{
    if (logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Error))
    {
        logger.Log(
            global::Microsoft.Extensions.Logging.LogLevel.Error,
            new global::Microsoft.Extensions.Logging.EventId(8, nameof(Method9)),
            new __Method9Struct(p1, p2, p3, p4, p5, p6, p7),
            null,
            __Method9Struct.Format);
    }
}
```
where `__Method9Struct` is auto-generated, implementing `IReadOnlyList<KeyValuePair<string, object?>>`.

<details>
<summary>Implementation of `__Method9Struct`</summary>

```csharp
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
private readonly struct __Method9Struct : global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.KeyValuePair<string, object?>>
{
    private readonly int _p1;
    private readonly int _p2;
    private readonly int _p3;
    private readonly int _p4;
    private readonly int _p5;
    private readonly int _p6;
    private readonly int _p7;

    public __Method9Struct(int p1, int p2, int p3, int p4, int p5, int p6, int p7)
    {
        this._p1 = p1;
        this._p2 = p2;
        this._p3 = p3;
        this._p4 = p4;
        this._p5 = p5;
        this._p6 = p6;
        this._p7 = p7;
    }

    public override string ToString()
    {
        var p1 = this._p1;
        var p2 = this._p2;
        var p3 = this._p3;
        var p4 = this._p4;
        var p5 = this._p5;
        var p6 = this._p6;
        var p7 = this._p7;

        return $"M9 {p1} {p2} {p3} {p4} {p5} {p6} {p7}";
    }

    public static string Format(__Method9Struct state, global::System.Exception? ex) => state.ToString();

    public int Count => 8;

    public global::System.Collections.Generic.KeyValuePair<string, object?> this[int index]
    {
        get => index switch
        {
            0 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p1", this._p1),
            1 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p2", this._p2),
            2 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p3", this._p3),
            3 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p4", this._p4),
            4 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p5", this._p5),
            5 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p6", this._p6),
            6 => new global::System.Collections.Generic.KeyValuePair<string, object?>("p7", this._p7),
            7 => new global::System.Collections.Generic.KeyValuePair<string, object?>("{OriginalFormat}", "M9 {p1} {p2} {p3} {p4} {p5} {p6} {p7}"),

            _ => throw new global::System.IndexOutOfRangeException(nameof(index)),  // return the same exception LoggerMessage.Define returns in this case
        };
    }

    public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p1", this._p1);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p2", this._p2);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p3", this._p3);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p4", this._p4);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p5", this._p5);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p6", this._p6);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("p7", this._p7);
        yield return new global::System.Collections.Generic.KeyValuePair<string, object?>("{OriginalFormat}", "M9 {p1} {p2} {p3} {p4} {p5} {p6} {p7}");
    }

    global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
```
</details>

- _Support for dynamic log level, current approach doesn't support this_

Source generator can also support dynamically providing log levels. For example, this way they could be supplied through configuration.

```csharp
[LoggerMessage(EventId = 8, Message = "M8")]
public static partial void M8(ILogger logger, LogLevel level);
```

The generated code is:
```csharp
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.Extensions.Logging.Generators", "1.0.0.0")]
public static partial void M8(Microsoft.Extensions.Logging.ILogger logger, Microsoft.Extensions.Logging.LogLevel level)
{
    if (logger.IsEnabled(level))
    {
        logger.Log(
            level,
            new global::Microsoft.Extensions.Logging.EventId(8, nameof(M8)),
            new __M8Struct(),
            null,
            __M8Struct.Format);
    }
}
```
where `__M8Struct` is also auto-generated.

- _More robust support for handling message template parameters._

The code generation using `LoggerMessage.Define` doesn't handle parameters in templates not being in the same order as the logging method arguments. This could be something that the source generator supports as it evolves further.

Example:

```csharp
[LoggerMessage(EventId = 0, Level = LogLevel.Debug, Message = "{x} {x}")]
public static partial void MessageTemplateParamRepeatingShouldWork(ILogger logger, string x);

[LoggerMessage(EventId = 11111, Level = LogLevel.Debug, Message = "T1{foo} {bar}")]
public static partial void MessageTemplateParamOrderMatters(ILogger logger, string bar, string foo);

```

- _Support compile-time error generation as opposed to runtime failures, for malformed format strings._

Malformed format strings get detected only at runtime when the `LoggerMessage.Define` method is invoked. Eventhough the first versions of the source generator would be using `LoggerMessage.Define` under the hood, it would be able to use string interpolation instead, and through that produce errors if the format string was malformed by virtue of the C# compiler doing the check. 

### Diagnostics

This [gist](https://gist.github.com/maryamariyan/a1ab553bedb26b9886fbc2740ee9e954) shows 20 diagnostic messages that the generator can produce alongside use cases for each

Using SYSLIBXXXX format as the diagnostic IDs. The gist also shows diagnostic categories against each sample.

### Event ID uniqueness checks

The source generator provides useful error messages that don't exist using completely generic mechanisms to enforce logging best practices. For example, the generator can provide an event ID uniqueness diagnostics check with warning severity so it can be suppressed.

The scope of uniqueness for the event IDs would be the class in which the log method is declared. It doesn't check up the inheritance hierarchy for conflicting IDs because it also wouldn't be possible cross-assembly boundaries. The diagnostic message for this condition would be a warning indicating the class name:

```csharp
static partial class LogClass
{
    [LoggerMessage(EventId = 32, Level = LogLevel.Debug, Message = "M1")]
    static partial void M1(ILogger logger);
    [LoggerMessage(EventId = 32, Level = LogLevel.Debug, Message = "M2")]
    static partial void M2(ILogger logger);
}
```
produces diagnostic message:

```
warning SYSLIB0005: Multiple logging methods are using event id 32 in class LogClass
```
For more clarity, the documentation in the future would need to mention that the scope for event ID uniqueness is checked per class itself not at base classes.

### Q & A - The source generator:

- Question: Why would we want to enforce event ID uniqueness checks if existing log APIs still allow for using default event ID values? For example with `LogInformation` we have some overloads, one taking `EventId` and the other one not. But the proposal with `LoggerMessageAttribute` only requires taking an `EventId`.

Answer: The enforced restriction added via `LoggerMessage` attribute aims at providing best practices for library authors more so than it does for app developers, who in most cases do not care about event IDs. But it should be possible to suppress warnings regarding event ID uniqueness using the source generator.

## Supporting string interpolation builder in logging

This section describes using a language feature (called string interpolation builder) to solve the problem of strongly-typing code paths for ILogger messages, without the need of the new proposed `LoggerMessageAttribute` above.

### Usage examples / API Proposal:

As opposed to writing:

```csharp
[LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Hello `{name}`")]
public static partial void LogName(this ILogger logger, string name, Exception exception);
```

described in the previous section, to take advantage of the string interpolation builder, the usage would look like:

```csharp
public static void LogName(this ILogger logger, string name, Exception exception) =>
    logger.Log(LogLevel.Information, new EventId(0), $"Hello `{name}`", exception, (s, e) => s.ToString());
```

where `(s, e) => s.ToString()` is the format callback.

By default with C# 10, `$"Hello `{name}`"` above would call the built-in string interpolation builder in the framework. But for our logging scenario, we need an overload of `Log` taking a new `LogMessageParamsBuilder` type, since we are aiming for compiler to be able to use our custom builder instead.

```csharp
public static class LogBuilderExtensions
{ 
    public static void Log(this ILogger logger, LogLevel logLevel, EventId eventId, LogMessageParamsBuilder builder, Exception? exception, Func<LogMessageParamsBuilder, Exception?, string> formatter);
}
```

Given the overload is available, the compiler would be able to translate the above call to:

```csharp
var receiverTemp = logger;
var builder = LogMessageParamsBuilder.Create(
    baseLength: 6,
    formatHoleCount: 1,
    receiverTemp, Level = LogLevel.Information, out var builderIsValid);
_ = builderIsValid &&
    && builder.TryFormatBaseString("Hello ")
    && builder.TryFormatInterpolationHole(name, "name");

logger.Log(
    LogLevel.Information,
    new EventId(1),
    builder, // the LogMessageParamsBuilder as log state
    exception,
    (s, e) => s.ToString()
);
```

On C# 10 and above. `LogMessageParamsBuilder` would be a custom [string interpolation builder](https://github.com/333fred/csharplang/blob/2550a43b391e844faaa0a2023f66489328a41612/proposals/improved-interpolated-strings.md) defined as:

```csharp
// The builder that will actually "build" the interpolated string"
public struct LogMessageParamsBuilder : IReadOnlyList<KeyValuePair<string, object>>
{
    public static LogMessageParamsBuilder Create(int baseLength, int formatHoleCount, ILogger logger, LogLevel logLevel, out bool builderIsValid)
    {
        if (!logger.IsEnabled(logLevel))
        {
            builderIsValid = false;
            return default;
        }

        builderIsValid = true;
        return new LogMessageParamsBuilder(baseLength, formatHoleCount);
    }

    // returns the number of holes
    public int Count => ... // depends on the type used to store key value pairs

    private LogMessageParamsBuilder(int baseLength, int formatHoleCount) { }

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

Our existing logging APIs are capable of recognizing name holes, or even format specifiers from message templates. Therefore, through this new language feature we would also want to be able to allow this. The builder design presented above is limited in its ability to get both name holes and format specifiers at once. Achieving this depends on how creative we want to get with the format string. We could use some currently-unused character to specify a _separator_ between the regular format string part and the name we want the hole to have. It would need to go through further design than presented in the proposal above to achieve this completely.

The builder will actually "build" the interpolated string", but also needs to allocate space for keeping key value pair structure that would not know types ahead of time. This cannot be done lazily because the structure we generate could ultimately get serialized in different ways decided by the different consumers of `ILogger.Log` call. The end result of using the builder this way, could end up being less efficient than what we already have in our logging APIs.

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

Answer: Yes, as long as `TryFormatXx` APIs are defined on a new builder type, then this feature would be enabled. 

- Question: What happens when we use the string interpolation builder approach on older compiler versions?

Answer: Using the language feature, would require the user to supply interpolated strings as TState for `ILogger.Log` messages. Such code if compiled against C# 9 or lower, not only would cause log messages to completely lose key/value pair log structures provided from within message templates, but would make the calls very inefficient. This could be mitigated with an analyzer that warns user when they use string interpolated messages on older compiler versions.

### Benefits with the string interpolation builder approach:

- _With the interpolated string builder, callers don't need to guard the call sites if any computation is necessary to produce the arguments to the logging. Whereas with the source generator, the user call site would need to get wrapped around `IsEnabled` checks._

For example with the source generator, the user call site would need to get wrapped around `IsEnabled` check for this specific use case to skip evaluating `Describe(..)` which might be expensive to compute:

```csharp
public static void DescribeFoundCertificates(this ILogger logger, IEnumerable<X509Certificate2> matchingCertificates)
{
    if (logger.IsEnabled(LogLevel.Trace))
    {
        DescribeFoundCertificates(logger, Describe(matchingCertificates));
    }
}

[LoggerMessage(EventId = 2, Level = LogLevel.Trace, Message = "Certificates: `{matchingCertificates}`")]
private static partial void DescribeFoundCertificates(this ILogger logger, string matchingCertificates);
```

Whereas the same sample using the string interpolation builder allows writing:

```csharp
public static void DescribeFoundCertificates(this ILogger logger, IEnumerable<X509Certificate2> matchingCertificates) =>
    logger.Log(2, LogLevel.Trace, $"Certificates: `{Describe(matchingCertificates)}`");
```

and allow for expensive computations to be skipped when logging is not enabled.

- _It would be nice if we could make use of a language feature to provide a declarative approach to logging._

Similar to using `LoggerMessageAttribute`, we showed that the builder approach also reduces boilerplate code. If we could come up with an complete design presented for `LogMessageParamsBuilder` which allows for usage of format specifiers, in a way that is also efficient while keeping ILogger message structure for consumers, then we could take advantage of it to write log messages. It would however need to be combined with an analyzer which warns against its usage on compilers older than C# 10, because of the reasons we established in the Q & A above.

### Benefits with the source generator approach:

- Allows the logging structure to be preserved and enables the exact format syntax required by https://messagetemplates.org/
- Allows supplying alternative names for the holes (this may be achievable by C# 10 as well through a careful design via format specifiers)
- Allows to pass all of original data as-is without any complication around how it's stored prior to something being done with it other than creating a string.
- Provides logging-specific diagnostics, e.g. it emits warnings for duplicate event ids.
- Feature is available on older compilers too.

#### Benefits compared to using LoggerMessage.Define directly

- Shorter and simpler syntax than current approach
- Guided developer experience - the generator gives warnings to help developers do the right thing
- Support for an arbitrary # of logging parameters. current approach tops out at 6
- Support for dynamic log level, current approach doesn't support this

### Conclusion

With the interpolated string builder, callers don't need to guard the call sites if any computation is necessary to produce the arguments to the logging, whereas the callers of source generated APIs would need `IsEnabled` checks to guard against such computations. There are a couple of open design questions that the interpolated string builder approach would still need to address: (1) how to allow alternative names for name holes, (2) how to lazily hold onto structured log data for consumers of log APIs to themselves to materialize them in customized ways. If we wanted to take advantage of the C# 10 language feature that would need to go through more design iterations.

The source generator approach is not far from how we currently write performant logging today (as initially illustrated with in `LoggingSample2`) and at the same time is improving usability, maintainability (by reducing boilerplate code) and provides a proper guided experience to best practices and would be good to include. This concludes there is an argument for using the source generator.

The builder approach and using source generators are not mutually exclusive approaches. Logging is an important use case for the string interpolation builder language feature. Therefore it would be benefitial to continue building up a complete design for the builder as an opportunity to identify any gaps with the language feature. Once the design is complete then it can be used to improve `Microsoft.Extensions.Logging`.

