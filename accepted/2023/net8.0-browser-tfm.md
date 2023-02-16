# net8.0-browser TFM for applications running in the browser

**Owner** [Javier Calvarro](https://github.com/javiercn) | [Javier Calvarro](https://github.com/javiercn)
**Contact** [Daniel Roth](https://github.com/danroth27) | [Daniel Roth](https://github.com/danroth27)
**Contact** [Artak Mkrtchyan](https://github.com/mkartakmsft) | [Artak Mkrtchyan](https://github.com/mkartakmsft)

For a video introduction to what we are trying to achieve, see:
https://www.youtube.com/live/kIH_Py8ytlY?feature=share&t=473
https://www.youtube.com/watch?v=48G_CEGXZZM

We have been shipping .NET in the browser via webassembly since the introduction of Blazor 3.2.0. During that initial release we decided to avoid adding a TFM for the browser as we were not clear on whether we needed it and we knew it was a one way operation. Once we introduced a TFM, we would never be able to take it back.

Our reasoning was that we could likely get away with a RID as that would allow third-parties to provide alternative pre-compiled webassembly assets (including native dependencies compiled to wasm).

We reasoned that we did not need a TFM as we could always provide a PAL over any functionality we needed and we would annotate the APIs with the `[SupportedOSPlatform]` attribute and an analyzer that would warn against incorrect usage when targeting the browser.

Over time, we have learned that the lack of a TFM is limiting for us and our customers. The lack of a TFM introduces friction in two ways:
* **Exposing additional customer-facing APIs when running in browser (and not on desktop)**: For example, Blazor includes many platform agnostic abstractions like IJSRuntime, IJSInProcessRuntime and different implementations to be able to reflect the different capabilities of the different platforms. This makes taking advantage of some of the webassembly unique capabilities more challenging as this kind of functionality needs to be carefully designed, and creates other problems like lack of discoverability (you need to know the existence of IJSInProcessRuntime and downcast to it).
  ```mermaid
  classDiagram
    IJSRuntime <|-- IJSInProcessRuntime
    class IJSRuntime {
      <<interface>>
      InvokeAsync<&zwj;TValue>(string identifier, object[] args) ValueTask~TValue~
      InvokeAsync<&zwj;TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) ValueTask~TValue~
    }
    class IJSInProcessRuntime {
      <<interface>>
      Invoke<&zwj;TResult>(string identifier, params object?[]? args) TResult
    }
  ```
* **Varying framework/library code contents/behavior based on whether it's running in the browser or not:** For example, having DI dependencies or other library code references varying by platform. Doing it based on TFM (not on RuntimeInformation) helps compilation and trimming.
  * A library that targets Blazor Server and Webassembly must provide at a minimum an additional package for the server to avoid bringing the server dependencies to webassembly.
  * A webassembly app with a companion server backend must also have, a project to host the webassembly app, a project to share common abstractions and a project to host the server backend. This exact scheme is reflected in the default Blazor Webassembly Hosted template.

One of the reasons we were hesitant to ask for a TFM in the initial release of Blazor was that we were concerned about people writing .NET webassembly specific code that did not work with Blazor Server. Over time, we have determined that it is not a real risk for two reasons:
* Blazor Server has a healthy user base, which makes it desirable for library authors to target net8.0.
* A core piece of Blazor functionality (prerendering) depends on being able to run the application on net8.0.

Given this, we consider unlikely that developers will author wasm specific code (net8.0-wasm) without also including a net8.0 version, as it will make it incompatible with any other Blazor flavor.

In addition this, we also have the experience with Blazor Hybrid and Maui where developers already have to work with multiple TFMs (one per platform) and Blazor Hybrid being part of that ecosystem needs to deal with that too.

Finally, we are looking at two new core experiences for which a TFM is heavily desirable:
* Blazor Web (United): A version of Blazor that combines static rendering + progressive enhancement to deliver the best of both worlds, webassembly and server. In this model, we have an application in a single project that starts as a server application, and can then transition to run directly in the browser via webassembly.
  * In this model customers can author components that contain webassembly specific dependencies and implementations without requiring a common contract with the server (shared abstractions in a separate library) and can have different sets of dependencies without requiring a separate package/dll.
    * For example, the server specific code can reference primitives in ASP.NET Core without that dependency flowing to webassembly.
  * This also applies to library authors that create Razor Class Libraries, as they can provide an implementation for Blazor Server and Webassembly in a single nuget package.
  * For more context click to see a demo [![here](https://img.youtube.com/vi/48G_CEGXZZM/3.jpg)](https://www.youtube.com/watch?v=48G_CEGXZZM)
* Blazor Universal: A single project type that produces both MAUI and web applications. Multitargeting across TFMs is how we can produce multiple outputs for these different platforms.

## Scenarios and User Experience

* Projects can have conventions based on the TFM (like MAUI)
  ![image](https://user-images.githubusercontent.com/6995051/219371569-0699ae29-8bbf-4e93-b1e1-904dc6e3616d.png)

* Projects can have specific dependencies for the browser (like MAUI)
  ```xml
  <ItemGroup Condition="'$(Browser)' == 'true'">
    <Reference Include="Microsoft.AspNetCore.Components.WebAssembly" />
    <Reference Include="System.Net.Http.Json" />
  </ItemGroup>
  ```
* Project can have different API area surfaces for the browser
  ```csharp
  public class RecipesStore
  {
  #if BROWSER
      private readonly HttpClient _http;
  #else
      IDictionary<string, Recipe> recipes;
      ConcurrentDictionary<string, byte[]> images = new();
      InMemorySearchProvider searchProvider;
  #endif

  #if !BROWSER
      public RecipesStore()
      {
        ...
      }
  #else
      public RecipesStore(HttpClient http)
      {
          _http = http;
      }
  #endif

  #if BROWSER
      public async Task<string> AddImage(Stream imageData)
      {
          var response = await _http.PostAsync("api/images", new StreamContent(imageData));
          return await response.Content.ReadAsStringAsync();
      }

      public async Task<string> AddRecipe(Recipe recipe)
      {
          var response = await _http.PostAsJsonAsync("api/recipes", recipe);
          return await response.Content.ReadAsStringAsync();
      }
  #else
      public async Task<IEnumerable<Recipe>> GetRecipes(string? query)
      {
          ...
      }

      public Task<Recipe?> GetRecipe(string id)
      {
          ...
      }

      public Task<Recipe> UpdateRecipe(Recipe recipe)
      {
          ...
      }

      public Task<string> AddRecipe(Recipe recipe)
      {
          ...
      }

      public async Task<string> AddImage(Stream imageData)
      {
          ...
      }

      public Task<byte[]> GetImage(string filename)
          => ...;
  #endif
  }
  ```

## Requirements

Introduce a new net8.0-browser TFM that enables developers to author dlls with an API surface tailored for running in the browser, to leverage browser specific APIs, and to pivot their dependecies based on the platform.

The net8.0-browser TFM will be a superset of net8.0, which means that an app or library targeting net8.0-browser will be able to consume net8.0 dependencies (for unsupported APIs we will continue using `[SupportedOSPlatform]`), however if a net8.0-browser version of the library is available, that would be preferred over the net8.0 version.

The general rule will be that this TFM will work in the same way as net8.0-android, net8.0-ios, net8.0-windows, net8.0-maccatalyst, etc. do for Maui apps today.

### Goals

* Enable single project experiences for Blazor Webassembly applications that target ASP.NET Core environments and the browser with the same codebase.
* Enable library authors to have dependencies thar are tailored for the browser.

### Non-Goals

**TL;DR**
* Per browser TFM
* Per browser version TFM
* Browser specific API sets (although might be expanded in the future to cover this)

#### Details

It is well-known that the web ecosystem is a very large, diverse and changing landscape. There are multiple browsers with different sets of supported APIs and APIs are added and removed on a regular basis, which introduces risks for the stability of the platform.

For these reasons, we are going to be very conservative about shipping additional APIs as part of the runtime and define a very restrictive criteria for when we think it is acceptable to add a new API. One such criteria can be:
* The API is part of the current web standards.
* All major vendors (Chrome, Firefox, Safari) ship an implementation of the API.
* The API is stable and there are no announced plans to deprecate it.

Many browser APIs already fit in this bucket. A list of which can be found [here](https://developer.mozilla.org/en-US/docs/Web/API). But to name a few common cases:
* DOM.
* WebCrypto.
* Storage.

Addressing the differences in API surfaces and versions provided by browsers is not something we are keen on doing, so we do not think we need browser/version specific TFMs.

Browsers vendors have adopted a very aggressive support policy and as such, it is fair to say that new framework versions do not necesarily need to worry about older browser versions. As an example:
* Edge support policy for enterprise customers [here](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-support-lifecycle#service-and-assisted-support-timeline)
* Firefox support policy [here](https://support.mozilla.org/en-US/kb/choosing-firefox-update-channel?_gl=1*12w45u6*_ga*MTM1MTA0NzU1MS4xNjcxMTk4MDQz*_ga_MQ7767QQQW*MTY3MTE5OTUyMy4xLjAuMTY3MTE5OTUyMy4wLjAuMA..)
* Safari support policy is typically linked to the OS support policy and reaches the two last major OS versions.

So in general, any API that has been supported by browsers for at least 1 year is a candidate to be added if we choose to. With that said, for .NET 8.0 we do not necessarily want to add additional browser APIs in the runtime itself, just enable developers to leverage this capability.

## Stakeholders and Reviewers

@lewing, @pavelsavara, @marek-safar, @terrajobst, more to be added here.

## Design

The general design guidelines for this feature follow the same principles as the TFMs for other environments like ios, maccatalyst, android and windows.

The ASP.NET Core team will build a similar experience on top of the TFM with similar conventions.

## Q & A

What will be the outcome of a net8.0 project referencing a net8.0-browser? (No multi-targeting)
