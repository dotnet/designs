# Logging Generator for strongly-typed logging messages

## Summary

This is a proposal for a new approach to logging to allow for more easier setup of strongly-typed logging messages. This can be achieved by using a source generator, that gets triggered with a `LoggerMessageAttribute` on partial logging methods, and would be able to either autogenerate the implementation of such method, or produce compile-time diagnostics hinting developers into proper usage of this logging approach.

## Scenarios

Heads up that `LoggingSample1` and `LoggingSample2` show existing approaches to logging, and the remaining will illustrate the samples using the source generator approach with `LoggerMessageAttribute`.

## Background / Existing Approaches

Currently there are two forms possible for doing logging, using `Microsoft.Extensions.Logging`:

```c#
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
1. We cannot provide event ID through these APIs
2. Parameters passed are processed before LogLevel checks
3. It requires parsing of message string on every use to find templates to substitute

Because of these problems, the more efficient approach recommended today as best practices is to use [LoggerMessage.Define](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/loggermessage?view=aspnetcore-5.0) APIs instead, illustrated below with `LoggingSample2`:

```c#
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

An argument against `LoggerMessage.Define` APIs through community has been that it is hard to use, and there is a maintainance burden that hurts usability.

## Requirements

### Goals

The number one concern raised with logging seems to be, in terms of usability, on finding a way to enforce strong typing on logging. In other words, it would be great to come up with a strong convention for all developers in a project to follow a specific logging-only template.

### Non-Goals

Also it's been raised that `LoggerMessage.Define` approach triggers boxing of data, and we wanted to understand if there are ways to improve this with a new and improved design.


## Design

`LoggerMessageAttribute` As shown in the examples, is used by the developer to trigger source generation:

```c#
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

## Usage Examples: 

#### **LoggingSample3:** with Log instance method
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

Where the implementation for `LogName` would be completed by the source generator. 

## More Scenarios

#### **LoggingSample4:** with static class Log

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

#### **LoggingSample5:** Other miscellaneous logging 

```c#
public partial class LoggingSample3
{
    private readonly ILogger _logger;

    public LoggingSample3(ILogger logger)
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
output using SimpleConsole:
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


</details>

## Diagnostics

This [gist](https://gist.github.com/maryamariyan/a1ab553bedb26b9886fbc2740ee9e954) shows 20 diagnostic messages that the generator can produce alongside use cases for each

Using SYSLIBXXXX format as the diagnostic IDs. The gist also shows diagnostic categories against each sample.

## Alternative Designs Considered

In the design phase of this feature, we also considered using [improved interpolated strings](https://github.com/333fred/csharplang/blob/2550a43b391e844faaa0a2023f66489328a41612/proposals/improved-interpolated-strings.md). (Link to [Q & A](https://gist.github.com/maryamariyan/0ae190723f4aa000a7462b65f943cfed))

## Proposal using custom builder

```c#
// The builder that will actually "build" the interpolated string"
public struct CustomLoggerParamsBuilder : IReadOnlyList<KeyValuePair<string, object>>
{
    public static bool GetInterpolatedStringBuilder(int baseLength, int formatHoleCount, ILogger logger, LogLevel logLevel, out CustomLoggerParamsBuilder builder)
    {
        if (!logger.IsEnabled(logLevel))
        {
            builder = default;
            return false;
        }

        builder = new CustomLoggerParamsBuilder(baseLength, formatHoleCount, logLevel);
        return true;
    }

    private LogLevel _logLevelEnabled;

    // returns the number of holes
    public int Count => ... // depends on the type used to store key value pairs

    public static readonly Func<CustomLoggerParamsBuilder, Exception?, string> FormatCallback = (builder, exception) => builder.ToString();

    private CustomLoggerParamsBuilder(int baseLength, int formatHoleCount, LogLevel logLevelEnabled)
    {
        // Initialization logic
        _logLevelEnabled = logLevelEnabled;
    }

    public bool TryFormat(string s)
    {
        // Store and format part as required
        return true;
    }

    public bool TryFormat<T>(T t, string s)
    {
        // Store and format part as required
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

In order to make use of a builder like `CustomLoggerParamsBuilder`, we would need new overloads in the future for LogInformation (and other similar APIs):

```c#
// We need new overloads to support this call:
logger.LogInformation($"Welcome to {city}!");
```

But the sample below shows an application of the builder using an existing logging API:
```c#
var city = "Vancouver";

logger.Log(
    LogLevel.Information,
    new EventId(1),
    $"Welcome to {city}!",
    new Exception(),
    (s, e) => s);
```
The compiler would translate the above call to:
```c#
logger.Log<CustomLoggerParamsBuilder>(
    LogLevel.Information,
    new EventId(1),
    builder, // the CustomLoggerParamsBuilder
    new Exception(),
    builder.FormatCallback
);
```
Through this new compiler feature, the builder would be able to recognize name holes, (here the city) or even format specifiers. However, currently the proposed builder approach is limited in its ability to get both name holes and format specifiers at once. The logger to is able to identify both of these from message templates and would be good to have this feature available.

Also, an older compiler, the builder overload may not be detectable and therefore the above log APIs could end up calling the wrong overload, which would cause in logging messages to completely lose the structure provided in the input. This could be however be mitigated with an analyzer that warns user when they use string interpolated messages on older compiler versions.
 
### Limitations with string interpolation builder approach:

- With the interpolated string builder approach, there is currently no way to get both name holes and format specifiers, but this is supported today already by the existing logger APIs.

- The biggest concern for using the custom struct builder for our logging approach is that we'd need to store some value for name holes eagerly in the builder. This step cannot be done lazily because the structure we generate could ultimately get serialized in different ways decided by the different consumers of `ILogger.Log` call. The end result of using the builder this way, could be less efficient than what we already have in our logging APIs, even though our current approaches already do boxing.

### Benefits:

- The name hole approach adds enough value that would be good to consider in the future for cases where we are given an interpolated string rather than message templates.

### Comparison of two approaches

The analysis for the string interpolation builder was intentionally made using the `LogInformation` approach shown in `LoggingSample1`, rather than using `LoggerMessage.Define` illustrated in `LoggingSample2`. The reason is they appear to be serving two different purposes. The former provides an imperative approach and the latter provides a declarative approach to logging.

The `LoggerMessage.Define` APIs provide a declarative approach to logging and with that, the consumer does not necessarily deal with interpolated strings as input but rather uses format strings (message templates). But the capability provided with the string interpolation builder approach seems to be more helpful towards imperative approaches to logging such as the usage of `logger.LogInformation` presented earlier.

### Conclusion
- We still would like to do the logging generator approach because we would like to keep the declarative model to preserves structure.
- The string interpolation builder approach has limitations in providing us with an efficient design for doing declarative logging. It is also currently incapable of detecting both name holes and format specifiers at the same time.
- The logging generator approach, using `LoggerMessageAttribute` provides an easy way to audit and see all logs in one place.
- If the consumer likes to write imperative code, it would be nice to have a natural C# 10 interpolated string way of API, we could consider in the future.

## Future improvements

- We could consider using the C# string interpolation builder approach for our imperative-based logging APIs, like `LogInformation`, etc.