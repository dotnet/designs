# Platform Specific HttpClient Support in dotnet/runtime

## Overview
The underlying networking libraries and capabilities in iOS, Android, and Wasm have required us to implement custom **HttpMessageHandler** classes using platform-specific APIs. Because each platform has unique API, different release pressures, and deeper infrastructure dependencies, the Mono team pushed the platform implementation into the individual SDKs. 

The purpose of this document is to outline how such an approach could work for .NET 5 and help drive consensus.

## Required Scenarios

### Default HttpClient and HttpClientHandler will always use the most suitable HttpMessageHandler on the platform
SocketHttpHandler, which is the current implementation under HttpClientHandler in dotnet/runtime, works great on desktop but it has limitations on platforms where sockets are not available or don’t offer the required level of control. The intention is to have the most common HttpClient construction to work out of the box on all platforms and use the platform most reliable HTTP provider.

**Example**
```c#
// This should use the default message handler for the application. Either as
// preselected by the framework or set in the Project Options for the project.

HttpClient client = new HttpClient();

// Same behavour should be observed when using following code
HttpClient client = new HttpClient(new HttpClientHandler());

// However this can throw PlatformNotSupportedException as not all properties are supported everywhere
HttpClient client = new HttpClient(new HttpClientHandler { SomeProperty = ... });
```

There are several reasons why using `SocketHttpHandler` is not desirable or not even possible on some platforms

* Integrated TLS. A TLS implementation integrated with the native HTTP stack can lead to better performance in various situations.
* Application size. Including an HTTP stack when the mobile platform already includes one can lead to an unnecessarily larger application size.
* Unavailability of Socket APIs.  Some platforms simply don't expose the socket primitives necessary to implement SocketsHttpHandler.
* Power consumption. Native HTTP stacks on various mobile platforms are able to more effectively manage power consumption as it relates to things like when communication devices are enabled or powered down.
* Platform settings. Some platform-level settings aren't exposed in a way that SocketsHttpHandler could view them but are directly integrated with the native HTTP stack.

### Platform-specific Http properties need to be available for customization
In cases where HTTP handler wraps platform-specific implementation and the implementation has a way to control HTTP connection behaviour or properties, such properties need to be exposed to C# developers on the public platform-specific type.  If HttpClientHandler itself exposes such a knob, it needs to control the associated customization on the underlying platform.

A few examples of platform handlers control properties

* [cachePolicy](https://developer.apple.com/documentation/foundation/nsurlrequest/cachepolicy/useprotocolcachepolicy)
* [allowsConstrainedNetworkAccess](https://developer.apple.com/documentation/foundation/nsmutableurlrequest/3325676-allowsconstrainednetworkaccess)
* [allowsExpensiveNetworkAccess](https://developer.apple.com/documentation/foundation/nsmutableurlrequest/3325677-allowsexpensivenetworkaccess)
* [networkServiceType](https://developer.apple.com/documentation/foundation/nsmutableurlrequest/1412378-networkservicetype)
* [ReadTimeout](https://docs.oracle.com/javase/7/docs/api/java/net/URLConnection.html#setReadTimeout(int))
* [AllowUserInteraction](https://docs.oracle.com/javase/7/docs/api/java/net/URLConnection.html#setAllowUserInteraction(boolean))

## Xamarin.iOS/tvOS/watchOS
There are 3 handlers available (SocketsHttpHandler, CFNetworkHandler, and NSUrlSessionHandler), with **NSUrlSessionHandler** as the default (except on watchOS, where the only supported handler is NSUrlSessionHandler).

The handler is only preselected as the default with [UI options available](https://docs.microsoft.com/en-us/xamarin/cross-platform/macios/http-stack#selecting-an-httpclient-stack) using VS Project Settings dialog to change them. This is required to “fix” existing HttpClient code which was written with the assumption that the default handler will be the one working best on the platform.

The ILLinker is used to remove the unnecessary handlers from the final binary in this case.

### Handlers in Detail
**SocketsHttpHandler Handler**

The handler, which is purely managed, and behaves generally the same across all platforms except WatchOS (sockets are not available). The downside of using it is mostly felt in networking connection switching when needed, for examaple when switching back and forth between the wifi/cellular antennas.

**CFNetwork Handler**

The CFNetwork handler is based in the old Network Stack API provided by Apple. This handler, while it has better performance, lacks features compared with the other ones.

**NSUrlSession Handler**

The NSUrlSession handler is the most modern of the native handlers provided. This handler has great performance since it relies on the OS and provides caching and other advanced features such as proxy support. The downside of using the handler comes in behaviour differences between the other platforms.

## Xamarin.Android
The users can choose from two handlers with AndroidClientHandler being the default and preferred over socket-based handler. The setting of which handler to use is also available via [Project Settings](https://docs.microsoft.com/en-us/xamarin/android/app-fundamentals/http-stack?context=xamarin%2Fcross-platform&tabs=windows) as for Xamarin.iOS.

### Handlers in Detail
**SocketsHttpHandler Handler**

The handler which is purely managed and behaves generally the same across all platforms. The same downsides as iOS apply here as well.

**AndroidClientHandler Handler**

The AndroidClientHandler is a bridge for Java.Net.HttpURLConnection. Not only does it provide a better performance, but it also helps drive a more consistent Android experience when it comes to networking. 

## WebAssembly

The web browser-based handler is the only option on this platform as no sockets are available.

### Handlers in Detail

**WasmHttpMessageHandler**

This handler is a special bridge between C# and Javascript. As Wasm matures, the shape of the handler and what it supports will change significantly and we could also rewrite it to be WASI based.

**SocketsHttpHandler Handler**

It’s not supported at all due to lack of Sockets on the platform. Changing it to throw PNSE for every public member will maintain API compatibility and reduce size.


## .NET 5 Adoption Proposals
Platform-specific handlers are implemented on top of the enhanced platform interop layer. They are in most cases relatively complex even with such abstractions available. It’s possible to implement them fully using basic interop mechanics like pinvoke but that would be tedious and very error-prone due to duplication of all the interop subtle details Xamarin and WebAssembly SDK built over the years. We would also need to introduce Xamarin namespace and build TFM specific versions of relevant assemblies as well as convert all publicly exposes types to primitive types or types which derive only from BCL types to avoid second-level dependencies which might not be desirable.

In general, keeping the platform dependant handlers away from .NET Core BCL and having them available only as part of binding APIs in optional SDKs (e.g. Xamarin.iOS) seems like the most effective integration.

### Proposal #1: Use static state

Extend HttpClient to be configurable via static configuration type.
```c#
public static class HttpClientHandlers
{
  public static HttpClientHandler DefaultPlatformHandler { get; internal set; }
}
```

```c#
public class HttpClient
{
  public HttpClient()
    : this(HttpClientHandlers.DefaultPlatformHandler)
  {
  }

  ...
}
```

**Disadvantages**

* It handles only HttpClient API and we’d need to “obsolete” manual HttpClientHandler construction.
* Initialization of static state from Xamarin SDKs could be ugly.

**Conclusion**

REJECTED.  It only supports `new HttpClient()`, not `new HttpClient(new HttpClientHandler())`.

### Proposal #2: Convert HttpClientHandler to Proxy pattern
Make HttpClientHandler a partial class, make all its members virtual, and on iOS/Android have the implementation of the class wrap an HttpClientHandler instance field. The instantiation of the underlying instance will have the flexibility to be controlled by platform implementation.

**Located in dotnet/runtime repo**
```c#
public class HttpClientHandler
{
  readonly HttpClientHandler platformInstance;

  public HttpClientHandler ()
  {
   if (GetType() == type (HttpClientHandler))
     platformInstance = /* createNewInstance for platform */
  }

  public virtual bool SupportsProxy => platformInstance.SupportsProxy;

  public /*virtual*/ System.Net.ICredentials Credentials {
    get => platformInstance.Credentials;
    set => platformInstance.Credentials = value;
  }
  public /*virtual*/ int MaxAutomaticRedirections{
    get => platformInstance.MaxAutomaticRedirections;
    set => platformInstance.MaxAutomaticRedirections = value;
  }

  // Remaining properties are wrapped the same way
}
```

The example of how this could look like for whole type is at [https://github.com/mono/mono/blob/master/mcs/class/System.Net.Http/HttpClientHandler.cs](https://github.com/mono/mono/blob/master/mcs/class/System.Net.Http/HttpClientHandler.cs)

**Located in xamarin/xamarin-macios repo**
```c#
// ios SDK part
public class CFNetworkHandler : HttpClientHandler
{
  public CFNetworkHandle (): base (true)
  {
  }

  public override bool SupportsProxy => objc.SendMessage ("some data") as bool;

  // Every property would have to have override
}
```

**Disadvantages**

* Testing will have to be done at the integration level.
* The existing handlers would need to be changed to derive from common type the proxy could operate on (HttpClientHandler, an interface, or something else).
* All HttpClientHandler properties will have to be virtual or we’ll have to introduce IHttpClientHandler which would also solve the recursion problem.
* Every method and property will have to have implementation override otherwise will hang in recursion / stack overflow.
* Factory for creating the platform instance won’t be part of public contract.
* Adding new public members will be difficult without IHttpClientHandler.

**Conclusion**

This is the cheapest option to implement which meets our needs as it’s closest to what we Mono and Xamarin use today.

### Platform Handler Construction
This subsection covers various options we have to construct the underlying platform instance.

**Proposal 2a: ILLinker Magic**

Each SDK would write optional ILLinker custom step which would rewrite construction logic for platform instance with implementation which is controlled by SDK.
```c#
// System.Net.Http.dll (for iOS, Android, etc)

public class HttpClientHandler
{
  readonly HttpClientHandler platformInstance;

  public HttpClientHandler ()
  {
   if (GetType() == type (HttpClientHandler))
     platformInstance = CreateNewInstance ();
  }

  // ILinker will recognize this method and change the body to be platform specific
  private static HttpClientHandler CreateNewInstance ()
  {
    throw new PlatformNotSupportedException();    
    
  }
}
```

**Disadvantages**

* Requires ILLinker to always run which slows down build process.
* Changing internal state of HttpClientHandler will be tricky as it can break hook used by ILLinker.

**Proposal 2b: Use Reflection**

The construction code would use reflection call to static method for platform where the late binding is required.
```c#
// System.Net.Http.dll

public class HttpClientHandler
{
  #if iOS
  [PreserveDependency ("Create", "HttpClientHandlerFactory", "Xamarin.iOS")
  #endif
  public HttpClientHandler ()
  {
    platformInstance = Type.GetType ("HttpClientHandlerFactory").GetMethod("Create").Invoke () as HttpClientHandler;
  }
}
```

**Disadvantages**

* Costly and slow reflection infrastructure would be pulled in.
* Need to add PreserveDependency to all possible target assemblies.

**Proposal 2c: Introduce a new System.Net.Http.HandlerFactory.dll assembly**

.NET introduce a new assembly which the only purpose will be to hold the factory contract for the handler construction. All other types will remain in System.Net.Http.dll which will make the build complicated due to circular references.
```c#
// System.Net.Http.HandlerFactory.dll
public static class HttpHandlerFactory
{
  // It could be of object type not to take dependency on System.Net.Http
  public HttpClientHandler Create () => throw new NotImplementedException();
}
```

```c#
// System.Net.Http.dll
public HttpClientHandler ()
{
  platformInstance = HttpHandlerFactory.Create();
}
```

**Disadvantages**

* Single method assembly would be added to the shared framework
* A circular dependency between System.Net.Http.HandlerFactory.dll and System.Net.Http.dll will exist if we use HttpClientHandler as the “common” type.

**Proposal 2d: Add SDK reference assemblies with factory contract to dotnet/runtime**

This is what Mono does today. The reference assemblies which describe the factory method contract will be available during dotnet/runtime build and used for each RID specific build of System.Net.Http.dll.
```c#
// System.Net.Http.dll
public HttpClientHandler ()
{
#if iOS
  platformInstance = Xamarin.iOS.HttpHandleFactory.Create();
#else
...
#endif
}
```

### Proposal #3: Move HttpClientHandler to a new assembly

The assembly called System.Net.Http.Private.dll would be introduced and would include only implementation of HttpClientHandler. The implementation then would be fully platform specific (per RID) and **for Xamarin like SDKs coming from their optional SDK pack**. For the desktop version version we could just leave HttpClientHandler in implementation version of System.Net.Http.dll.

**Located in dotnet/runtime repo**
```c#
// System.Net.Http.dll

#if iOS || android || tvOS || wasm
// The reference for the type comes from System.Net.Http.Private.dll
[assembly: TypeForwardTo (typeof (System.Net.Http.HttpClientHandler)) 
#endif
```

**Located in xamarin/xamarin-macios repo**
```c#
// Sample implementation in System.Net.Http.Private.dll build as part of Xamarin.iOS
public class HttpClientHandler
{
  public HttpClientHandler ()
  {
  }

  public virtual bool SupportsProxy => nativecall ("has-proxy") as bool;

  public int MaxAutomaticRedirections {
    get => 100;
    set => throw PlatformNotSupportedException();
  }

  // The rest of API contract follows
```

**Disadvantages**

* Single type assembly is against our design practices
* A circular dependency between System.Net.Http.Private.dll and System.Net.Http.dll might exist in optional components
* API contract would live in different assembly than implementation
* Reliance on more dotnet publish to figure out right implementation assembly
* Adding public member to HttpClientHandler would be a breaking change
* HttpClientHandler acts as a wrapper in most SDKs and the code would have to be duplicated
* Platform specific implementation would need to write their own copy of proxy logic

**Conclusion**

This is very light-weight solution which pushes a lot of responsibility to optional components and build system. The possibility of including RID specific version of System.Net.Http.Private.dll inside Xamarin.iOS.nupkg needs to be confirmed.

### Proposal #4: Add HttpClientHandler type forwarder to SDKs assemblies

.NET builds ref version System.Net.Http for all optional components inside dotnet/runtime by using references to copies of optional component reference assemblies. The implementation then type forwards to optional components implementation assemblies.

We could also use have only shared ref version of HttpClientHandler but then every optional SDKs would have to create their own copy of proxy logic as described in **Proposal #2**.

**Located in dotnet/runtime repo**
```c#
// System.Net.Http.dll - ref for TFM net5.0-ios
public class HttpClientHandler
{
  public HttpClientHandler ()
  {
  }

  public long MaxInputInMemory { get; set; }

  public NSUrlSessionHandlerTrustOverrideCallback TrustOverride { get; set; }
  
  // More unique or common properties to follow
}
```

```c#
// System.Net.Http.dll

#if iOS
// The reference for the type comes from Xamarin.iOS.dll
[assembly: TypeForwardTo (typeof (System.Net.Http.HttpClientHandler)) 
#elif (android)
// The reference for the type comes from Xamarin.Android.dll
[assembly: TypeForwardTo (typeof (System.Net.Http.HttpClientHandler)) 
#else if ..

#endif
```

**Disadvantages**

* dotnet/runtime will need to include reference assemblies of all optional components to be able to build ref and impl assemblies.
* Public HttpClientHandler API shape might be different between TFM as it’s controlled by optional components
* Updating HttpClientHandler public surface for optional component will require build a new version of dotnet/runtime
* Testing will have to be done at the integration level

**Conclusion**

The big advantage of this proposal is that the default HttpClientHandler would be have the most optimal implementation on every platform (performance and size wise). The availability of only platform specific properties on HttpClientHandler has its pros & cons as well.

### Proposal #5: All HttpHandlers are fully implemented inbox

Implement all platform-specific handlers fully inside dotnet/runtime using platform-specific interop libraries. We would extend low-level interop API which and use it to implement the managed API for HttpClientHandler and other types which are exposed from it.

There is already a small foundation for CF interop at [https://github.com/dotnet/runtime/tree/master/src/libraries/Common/src/Interop/OSX](https://github.com/dotnet/runtime/tree/master/src/libraries/Common/src/Interop/OSX). This will need to be extended to include Objective-C bridge for necessary APIs and possibly tooling to handle Objective-C binding possible.

We could also expose this Interop layer to Xamarin internally to avoid code duplication as they use the same platform API.

**Disadvantages**

* Implementation of HttpClientHandler might require implementing more dependent type (e.g. Android.Net.SSLSocketFactory)
* Problems with handling platform-specific exceptions
* Not all decorations used for Xamarin bindings will be available (e.g. [iOS (7,0)])
* Starting from scratch for Android

**Conclusion**

No build magic and more shared code prospect, as well as the ability to test HttpClient changes easily across all platforms, are very appealing.

## The plan for .NET 5

The decision is to use different approaches for each platform for .NET 5. We might consider unifying the approaches eventually but not in .NET 5 time-frame.

A new feature like setting will be introduced which will allow controlling the implicit handler selection. The internal property will be available inside System.Net.Http namespace and mapped to msbuild property or AppContext setting.

```c#
namespace System.Net.Http
​​{
​​  public partial class HttpClientHandler
​​  {
​​    internal static HandlerType DefaultHandlerType { get; } = HttpClientHandlerType.PlatformNative;
​​  }
}
```

The implementation has to use the property in the way to allow the ILLinker to remove the unnecessary dependency of unused handler when the type is not used elsewhere.

### Xamarin.iOS/Xamarin.tvOS/Xamarin.watchOS
Follow proposal #2b "Use Reflection" and hook up existing `NSUrlSessionHandler` using `Activator.CreateInstance ("Foundation.NSUrlSessionHandler, Xamarin.iOS/Xamarin.tvOS/Xamarin.watchOS")​`. The actual implementation will remain in `xamarin/xamarin-macios`​ repo.

The plan ignores CFNetworkHandler and other legacy handlers. They will stay in `xamarin/xamarin-macios` and will be available only when constructed manually. The implementation logic inside HttpClientHandler will rely on DefaultHandlerType selection to set the implicit handler behaviour between NSUrlSessionHandler and SocketHttpHandler.

### Xamarin.Mac
There won’t be any platform specific handlers implicitly called by HttpClient OSX RID build of libraries (Runtime Pack). The existing SocketHttpHandler is what .NET Core supports today and have all features available for cross platform support (e.g. gRPC) for .NET. Xamarin.Mac could still ship any platform specific handler but they won’t be used unless explicitly constructed and passed to `HttpClient​` constructor.

### Xamarin.Android

Follow proposal #2b "Use Reflection" and hook up existing AndroidClientHandler using `Activator.CreateInstance ("Xamarin.Android.Net.AndroidClientHandler, Mono.Android")`. The whole AndroidClientHandler implementation will remain in `xamarin/xamarin-android` repo.

Only RID specific version of System.Net.Http will be built as there won’t be any new API available as part of `dotnet/runtime` version.

The code will most likely have to include special linker annotations to keep the dependency on the constructor inside external “unknown” assembly.

### WebAssembly
Follow proposal #5 "All HttpHandlers are fully implemented inbox" and port WasmHttpMessageHandler to dotnet/runtime together with wasm interop layer.

The public implementation of WasmHttpMessageHandler will be packaged into special NuGet which will be created to hold only this public type. The interop layer needed to support this handler will be also included into the NuGet package. To match existing naming it will be called

* System.Net.Http.WebAssemblyHttpHandler

## Future plans
If we manage to extract Objective-C or Java interop into special subcomponents we might be able to switch to solution which will allow us to ship and test everything inbox (inside `dotnet/runtime` repo).

### Xamarin.iOS/Xamarin.tvOS/Xamarin.watchOS
Follow proposal #5 "All HttpHandlers are fully implemented inbox" and port [NSUrlSessionHandler](https://github.com/xamarin/xamarin-macios/blob/master/src/Foundation/NSUrlSessionHandler.cs) to `dotnet/runtime` repo and extend existing interop layer to include bridge to platform specific APIs to support the implementation. The sources will be located under [src/libraries/System.Het.Http/](https://github.com/dotnet/runtime/tree/master/src/libraries/System.Net.Http/). 

The publicly available version of the NSUrlSessionHandler will be distributed in separate NuGet. Following new packages will be created to carry the implementation for each TFM. The result of that will be that the same implementation will be included in multiple assemblies

New NuGet Packages Name

* System.Net.Http.iOSHttpHandler
* System.Net.Http.tvOSHttpHandler
* System.Net.Http.macOSHttpHandler
* System.Net.Http.watchOSHttpHandler
