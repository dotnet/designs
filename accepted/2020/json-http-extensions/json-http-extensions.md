# JSON extension methods for HttpClient

**PM** [Immo Landwerth](https://github.com/terrajobst)

Serializing and deserializing JSON payloads from the network is a very common
operation for clients, especially in the upcoming Blazor environment. Right now,
sending a JSON payload to the server requires multiple lines of code, which will
be a major speed bump for those customers. We'd like to add extension methods on
top of `HttpClient` that allows doing those operations with a single method
call.

## Scenarios and User Experience

### Getting data from and to the server is a one-liner

Jake is writing the Blazor front-end code for his CRM product. He's using the
repository pattern by implementing their `ICustomerRepository` interface over an
`HttpClient`. Using the extension methods, he's able to keep his code very
concise:

```C#
public class RestCustomerRepository : ICustomerRepository
{
    private readonly HttpClient _client;

    public RestCustomerRepository(HttpClient client)
    {
        _client = client;
    }

    public Task<IReadOnlyList<Customer>> GetAllCustomersAsync()
    {
        return _client.GetFromJsonAsync<IReadOnlyList<Customer>>("/customers");
    }

    public Task<Customer?> GetCustomerByIdAsync(int id)
    {
        return _client.GetFromJsonAsync<Customer?>($"/customers/{id}");
    }

    public Task UpdateCustomerAsync(Customer customer)
    {
        return _client.PutAsJsonAsync($"/customers/{customerId}", customer);
    }
}
```

### Dealing with HTTP responses is also still doable

Gina is tasked with changing `GetCustomerByIdAsync()` to return `null` instead
of throwing a generic `HttpRequestException`. She changes the previous one-liner
implementations to manually create a message and handling the response. However,
she can still use a one-liner to deserialize the JSON from the response:

```C#
public async Task<Customer> GetCustomerByIdAsync(int id)
{
    var request = new HttpRequestMessage(HttpMethod.Get, $"customers/{id}");
    var response = await _httpClient.SendAsync(request);

    if (response.StatusCode == HttpStatusCode.NotFound)
        return null;

    return await response.Content.ReadFromJsonAsync<Customer>();
}
```

### Constructing HTTP requests is still doable

Jake is adding a creation method for customers. The REST API requires the
presence of some special headers. Using the JsonContent.Create() he can
construct the message while still using a one-liner to serialize the Customer
object.

```C#
public async Task CreateCustomerAsync<T>(Customer customer)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "customers/new");
    AddCustomerHeaders(request)

    request.Content = JsonContent.Create(customer);

    var response = await _httpClient.SendAsync(request);

    // ...
}
```

## Requirements

### Goals

* Must work on .NET Standard 2.1, but 2.0 would be preferred.
* ~~We need to ship this at Build with Blazor (prerelease is OK, but it needs to
  be an official release on nuget.org)~~
* We need stable release by Build nuget.org and a preview by mid March
* Build a pit-of-success for `HttpClient` and `System.Text.Json` when you are
  the client (servers are often clients too!)
* Make it terse to do the common things – based on experience it is also the
  most efficient thing
* Don't hide underlying HTTP objects more than necessary, they are valuabl –
  especially the response.
* We should align this feature with C#'s planned source generators to make sure
  that that these APIs work well when used with a generated serializer.

### Non-Goals

* Don't need the complex parts of `Microsoft.AspNet.WebApi.Client` – just simple
  methods

## Design

### APIs

**Assembly**: System.Net.Http.Json (new)  
**Dependencies**: System.Net.Http, System.Text.Json  
**NuGet Package**: System.Net.Http.Json (new)

```C#
#nullable enable

namespace System.Net.Http.Json {
    public static class HttpClientJsonExtensions {
        public static Task<object> GetFromJsonAsync(
            this HttpClient client,
            string requestUri,
            Type type,  
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<object> GetFromJsonAsync(
            this HttpClient client,
            Uri requestUri,
            Type type,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<T> GetFromJsonAsync<T>(
            this HttpClient client,
            string requestUri,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<T> GetFromJsonAsync<T>(
            this HttpClient client,
            Uri requestUri,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PostAsJsonAsync(
            this HttpClient client,
            string requestUri,
            Type type,
            object value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PostAsJsonAsync(
            this HttpClient client,
            Uri requestUri,
            Type type,
            object? value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
            this HttpClient client,
            string requestUri,
            T value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
            this HttpClient client,
            Uri requestUri,
            T value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PutAsJsonAsync(
            this HttpClient client,
            string requestUri,
            Type type,
            object? value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PutAsJsonAsync(
            this HttpClient client,
            Uri requestUri,
            Type type,
            object? value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PutAsJsonAsync<T>(
            this HttpClient client,
            string requestUri,
            T value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<HttpResponseMessage> PutAsJsonAsync<T>(
            this HttpClient client,
            Uri requestUri,
            T value,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        }
    public static class HttpContentJsonExtensions {
        public static Task<object> ReadFromJsonAsync(
            this HttpContent content,
            Type type,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
        public static Task<T> ReadFromJsonAsync<T>(
            this HttpContent content,
            JsonSerializerOptions options = null,
            CancellationToken cancellationToken = default);
    }
    public class JsonContent : HttpContent {
        public static JsonContent Create<T>(  
            T value  
            JsonSerializerOptions options = null);
        public static JsonContent Create<T>(
            T value,
            MediaTypeHeaderValue mediaType,  
            JsonSerializerOptions options = null);
        public static JsonContent Create<T>(  
            T value,  
            string mediaType,  
            JsonSerializerOptions options = null);
        public JsonContent(  
            Type type,  
            object? value,  
            JsonSerializerOptions options = null);
        public JsonContent(  
            Type type,  
            object? value,  
            MediaTypeHeaderValue mediaType,  
            JsonSerializerOptions options = null);
        public JsonContent(  
            Type type,  
            object? value,  
            string mediaType,
            JsonSerializerOptions options = null);
            public Type ObjectType { get; }
            public object? Value { get; }
    }
}
```

### Default serialization options

The default should match the defaults that ASP.NET Core is using, e.g.
`camelCasing`, as opposed to the defaults `System.Text.Json` is using. Otherwise
the most common case (calling your own web API from a Blazor client) would
require explicit configuration.

To avoid having to sync the settings between ASP.NET Core and this library, we
should expose a new API. We should move the default configuration from ASP.NET
Core to this method and change ASP.NET Core to call this API instead. This
ensures we don't break the defaults for `System.Text.Json` (which is what you
get by calling the default constructor) while also providing a single home of
the default configuration used for web.

```C#
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        public static JsonSerializerOptions CreateForWeb();
    }
}
```

## Q & A

### Why didn't we put the code in the System.Net.Http.Formatting assembly?

The existing assembly (which ships in
[Microsoft.AspNet.WebApi.Client](https://www.nuget.org/packages/Microsoft.AspNet.WebApi.Client)
package) provides the integration between HttpClient and JSON.NET. Mixing this
with the new `System.Text.Json` would create a mess. Also, we don't believe
we'll need any of the complexity provided by the formatting infrastructure.

### Why didn't we put the extension methods into the System.Net.Http namespace?

If we did, consumers whose closure contains the existing
System.Net.Http.Formatting assembly would have a hard time using the extension
methods. Using a new namespace makes sure that customers have a way to opt-in
the appropriate extension they want to use.

### What's the behavior of `Content-Type`?

Specifically, what is the behavior of `GetFromJsonAsync()` if `Content-Type` is
not `json+utf8`? What about `text/plain`?

Non-UTF8 encodings should be transcoded and `application/json` and `text/plain`
should both be accepted.

### Can we remove `JsonContent.Value`?

No, it's often needed for unit tests.
