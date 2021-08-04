# WASM/Blazor cryptography status \& timelines

The .NET libraries team, Blazor client team, and cryptographic experts within Microsoft have been working to define a path forward for cryptography in Blazor client (wasm) apps.

This plan is intended to address common use of cryptography in Blazor client apps and resolve the following issues.

* [#40074](https://github.com/dotnet/runtime/issues/40074) - Master tracking issue for Blazor/wasm scenarios
* [#43939](https://github.com/dotnet/runtime/issues/43939) - Support keyed hash algorithms in Blazor/wasm
* [#42384](https://github.com/dotnet/runtime/issues/42384) - Support AES in Blazor/wasm
* [core/#5357](https://github.com/dotnet/core/issues/5357) - Support keyed hashing algorithms in Blazor/wasm

The rest of this document will use the term __Browser__ to refer to Blazor client (wasm) scenarios. This term matches the build architecture we use in the .NET sources.

## .NET 6 Plan

.NET 6 will support the same algorithms in Browser environments that .NET 5 did: __SHA1__, __SHA256__, __SHA384__, and __SHA512__. These algorithms can be accessed via several mechanisms, including:

* `SHA256.Create()`
* `SHA256.HashData(...)`
* `IncrementalHash.CreateHash(...)`

There is _no_ in-box support in Browser for the HMAC versions of these algorthms, or for algorithms like PBKDF2 (`Rfc2898DeriveBytes`) which rely on HMAC as a building block. Additionally, there is _no_ in-box support for cipher algorithms like AES.

### Discussion

Modern browsers provide access to secure cryptographic primitives via the [SubtleCrypto](https://developer.mozilla.org/docs/Web/API/SubtleCrypto) APIs. These are asynchronous APIs implemented in terms of JavaScript `Promise` objects (akin to .NET's asynchronous `Task` objects). However, since .NET's cryptographic APIs are synchronous, it has proven difficult to plumb the existing .NET API surface atop the browser's SubtleCrypto API surface.

> For more information on the history behind SubtleCrypto's API shape, see [this tracking issue](https://www.w3.org/2012/webcrypto/track/issues/24) from the W3's working group and [this associated discussion](https://lists.w3.org/Archives/Public/public-webcrypto/2012Aug/0144.html) from the mailing list.

To allow the existing .NET SHA-\* APIs to work in .NET 5 and .NET 6, we have implemented the algorithms in managed code when running in Browser environments. These are correct and faithful implementations of the SHA-\* algorithms. However, they are not hardened against side channels which might leak input data.

Similarly, these implementations are not FIPS 140-2 certified. Organizations which require the use of FIPS 140-2 certified cryptographic modules must not use .NET 6's built-in cryptographic APIs when running in a Browser environment. There are no plans to seek FIPS 140-2 certification for .NET 6's cryptographic APIs when running in a Browser environment.

> .NET 6's `SHA*` classes are only backed by managed implementations when running in a Browser environment. In all other environments, .NET 6 relies on the underlying execution environment for cryptography. On Windows, this is [CNG](https://docs.microsoft.com/windows/win32/seccng/cng-portal); on Apple platforms, this is Apple's [Security framework](https://developer.apple.com/documentation/Security); and on Linux distros, this is [OpenSSL](https://www.openssl.org/).

## Ongoing investigations

Over the past few months, we have been investigating ways to get .NET's existing sync cryptographic APIs running securely on top of the browser's async SubtleCrypto APIs. This problem space is very much akin to having a standard synchronous .NET method call a `Task`-returning method, but not being able to use the _await_ keyword or _Task.Result_ in order to retrieve the result value.

One effort has been [to use `SharedArrayBuffer`](https://github.com/dotnet/runtime/pull/49511) as a way to perform sync-over-async call patterns in JavaScript. Using `SharedArrayBuffer` as a synchronization primitive generally worked, but [it is not supported on all browsers](https://caniuse.com/sharedarraybuffer), and requiring it for .NET's crypto APIs would have disadvantaged Safari users.

A more generalized way of addressing this might be through the use of [green threads](https://en.wikipedia.org/wiki/Green_threads). Under this mechanism, every .NET frame is implicitly asynchronous. The runtime would trap calls to blocking routines and snapshot the current thread state, hooking it up as a continuation once the callee finishes execution. This is a very difficult problem and would have sweeping effects across the entire .NET runtime, not just to the cryptographic APIs. And while this may be an avenue for long-term exploration, it is not a viable candidate for a short-term or intermediate-term solution to the problem of sync-over-async.

Another avenue we have explored is the introduction of async (`Task`-returning) .NET crypto APIs. In Browser environments, these APIs could sit directly atop the SubtleCrypto stack without any synchronization trickery performed by the runtime. The downside to this is that async APIs are viral in nature. New APIs could not function as a drop-in replacement. Callers would need _both_ to change their call site from `ExistingSyncApi` to `NewApiAsync` _and_ to ensure that their entire call stack was asynchronous all the way down to its entry point. We examined customers' existing call sites and concluded that very few applications were in a position to adapt their existing code to the new call patterns. This does not preclude async APIs from being added in the future, but it does mean that those APIs would serve only a very narrow customer base.

Finally, after consulting with cryptographic and security experts throughout Microsoft, we determined that it is feasible for us to ship managed implementations of select cryptographic algorithms, subject to ecosystem needs and preparing proper messaging about any security guarantees provided by these implementations. This updated guidance is reflected in the .NET 7 plan decribed below.

## .NET 7 Plan

> __Disclaimer__
> 
> This is a draft document and describes a proposed course of action. The actual behavior of .NET 7 may differ from the behaviors proposed here.

There is nothing set in stone for .NET 7. Planning for cryptographic primitive support is still fluid. But we are open to the idea of expanding our Browser cryptographic API support to encompass the following algorithms.

* HMACSHA1
* HMACSHA256
* HMACSHA384
* HMACSHA512
* PBKDF2 (via [`Rfc2898DeriveBytes`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.rfc2898derivebytes))
* AES-CBC

__This is not a comprehensive list, nor is it a promise to implement these algorithms.__ Algorithms may be added to or removed from this list as we receive additional feedback or as emergent needs dictate.

We currently have no plans to provide specific support for `X509Certificate2`, `SignedXml`, and other higher-level, non-primitive types in Browser environments.

### Discussion

We intend on providing the most secure implementations of cryptographic algorithms when available. On browsers which support `SharedArrayBuffer`, we will utilize that as a synchronization mechanism to perform a sync-over-async operation, letting the browser's secure SubtleCrypto implementation act as our cryptographic primitive. On browsers which do not support `SharedArrayBuffer`, we will fall back to in-box managed algorithm implementations of these primitives.

> The phrase "in-box managed implementation" here describes how the runtime instantiates a family of algorithms. For example, when running on Windows in a non-Browser environment, the runtime will rely on the operating system's implementation for all instantiations of AES, regardless of whether the application is using the concrete `Aes`, `RijndaelManaged`, or `AesManaged` classes. Contrarily, when running in a Browser environment where the browser does not have support for `SharedArrayBuffer`, the runtime will rely on a fully managed implementation of AES, regardless of which concrete .NET class the app is using.

The implementations of our in-box managed algorithms will be faithful to the appropriate standards. However, we will not pursue FIPS 140-2 or any other certification for them. Our in-box managed algorithm implementations are not intended to be free of side channels.

Organizations which require FIPS 140-2 compliant implementations or implementations which make stronger side channel guarantees must ensure that their Blazor client code executes only in browsers which support `SharedArrayBuffer` and which carry FIPS 140-2 compliant primitives via SubtleCrypto.

> Just as before, in non-Browser environments, .NET 7 will fall back to the operating system or a third-party library (like OpenSSL) for its implementations of cryptographic primitives.

We __do not__ intend on adding in-box managed implementations for APIs which do not appear in the SubtleCrypto spec. For example, there is no plan to support the `MD5` or `AesCcm` classes in Browser environments, regardless of which browser the application is running within. Similarly, there is no plan to support all algorithms which appear in the SubtleCrypto specification, regardless of whether .NET exposes a first-class API for that algorithm.

We __do not__ intend on providing a first-class API to query where the implementation of any given primitive might come from. The runtime's selection of an implementation is intended to be opaque: the runtime attempts to choose the best possible implementation available within the environment.

We __do not__ have any immediate plans to add `Task`-returning APIs to the _System.Security.Cryptography_ namespace. The reasoning for this is given in the _Ongoing investigations_ section earlier in this document.

## What this means for developers

Most developers needn't be concerned with this. Applications which use the SHA-1 and the SHA-2 family of algorithms will continue to work in .NET 6 and beyond.

If you are using _keyed_ algorithms (HMAC, AES) in your Blazor client application, you should see a greater number of scenarios start working in the .NET 7 timeframe. However, please be aware of general concerns which accompany the use of cryptography in Browser environments.

1.  Is it acceptable for the client to have this key in the first place? Would it present a risk to your service if the client hooked a debugger to the wasm app and dumped the raw key material?

2. Is it acceptable for the client to fall back to a managed algorithm which may not be free of side channels? If not, consider whether it would be safer to perform your cryptography entirely within JavaScript using the Promise-based SubtleCrypto API, having your application make an async-based call into your JavaScript-based helper.

### Blazor wasm + JS interop example

The following Blazor wasm 6.0 sample app demonstrates calling from managed .NET code into JavaScript to perform a Promise-based cryptographic operation using the SubtleCrypto APIs. This sample takes a secret key and an input text, and it returns the HMACSHA256 digest of the input text.

To run this sample, first create a file __crypto-demo.cshtml__ in your Blazor wasm app. Copy the contents below into the .cshtml file.

```cshtml
@page "/crypto-demo"
@inject IJSRuntime JS

<PageTitle>Cryptography demo</PageTitle>

<h1>Cryptography demo</h1>

<p>
    This page demonstrates using JS interop to make a call from a .NET Blazor client app
    into the JavaScript runtime for the purposes of computing an HMACSHA256 hash.
</p>

<p>
    Key (base64-encoded):
    <input type="text" name="key" @bind-value="keyAsString" />
</p>

<p>
    Text to be digested:
    <input type="text" name="inputText" @bind-value="inputTextAsString" />
</p>

<p>
    <button class="btn btn-primary" @onclick="GenRandomKey">Generate random key</button>
    <button class="btn btn-primary" @onclick="ComputeDigest">Compute HMACSHA256 digest</button>
</p>

<p>Computed digest (as hex): @statusMessage</p>

@code {
    private string keyAsString = "";
    private string inputTextAsString = "";
    private string statusMessage = "";

    private void GenRandomKey()
    {
        // Generate a random 256-bit key.
        byte[] randomBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        keyAsString = Convert.ToBase64String(randomBytes);
    }

    private async Task ComputeDigest()
    {
        try
        {
            // Base64-decode the key.
            byte[] keyAsBytes = Convert.FromBase64String(keyAsString);

            // Get the binary representation of the text to hash (we'll use UTF-8).
            inputTextAsString ??= string.Empty; // ensure non-null
            byte[] inputTextAsUtf8Bytes = System.Text.Encoding.UTF8.GetBytes(inputTextAsString);

            // Now use JS interop to call SubtleCrypto's Promise-based API.
            byte[] digestAsBytes = await JS.InvokeAsync<byte[]>("computeDigestUsingSubtleCrypto", keyAsBytes, inputTextAsUtf8Bytes);
            statusMessage = BitConverter.ToString(digestAsBytes);
        }
        catch (Exception ex)
        {
            statusMessage = $"ERROR: {ex}";
        }
    }
}
```

Next, insert a `<script>` element just before the closing `</body>` element in the Blazor app's __index.html__ file. Copy the contents below into the `<script>` element.

```html
<script>
    window.computeDigestUsingSubtleCrypto = async function (key, inputText) {
        // 'key' and 'inputText' are each passed as Uint8Array objects, the JS
        // equivalent of .NET's byte[]. SubtleCrypto uses ArrayBuffer objects
        // for the key and the input and output buffers.
        //
        // To convert Uint8Array -> ArrayBuffer, use the Uint8Array.buffer property.
        // To convert ArrayBuffer -> Uint8Array, call the Uint8Array constructor.
        //
        // Most of SubtleCrypto's functions - including importKey and sign - are
        // Promise-based. We await them to unwrap the Promise, similar to how we'd
        // await a Task-based function in .NET.
        //
        // https://developer.mozilla.org/docs/Web/API/SubtleCrypto/importKey

        var cryptoKey = await crypto.subtle.importKey(
            "raw" /* format */,
            key.buffer /* keyData */,
            { name: "HMAC", hash: "SHA-256" } /* algorithm (HmacImportParams) */,
            false /* extractable */,
            ["sign", "verify"] /* keyUsages */);

        // Now that we've imported the key, we can compute the HMACSHA256 digest.
        //
        // https://developer.mozilla.org/docs/Web/API/SubtleCrypto/sign

        var digest = await crypto.subtle.sign(
            "HMAC" /* algorithm */,
            cryptoKey /* key */,
            inputText.buffer /* data */);

        // 'digest' is typed as ArrayBuffer. We need to convert it back to Uint8Array
        // so that .NET properly translates it to a byte[].

        return new Uint8Array(digest);
    };
</script>
```

Now run the app, and you can step through the code and watch the HMACSHA256 operation take place. Enter __`CwsLCwsLCwsLCwsLCwsLCwsLCws=`__ for the key and __`Hi There`__ for the text. Hit _Compute_ and you'll see the output: `B0-34-4C-61-D8-DB-38-53-5C-A8-AF-CE-AF-0B-F1-2B-88-1D-C2-00-C9-83-3D-A7-26-E9-37-6C-2E-32-CF-F7`. (This is the same test vector provided by [RFC 4231, Sec. 4.2](https://datatracker.ietf.org/doc/html/rfc4231#section-4.2), which is a commonly used test to determine that a cryptographic algorithm is implemented correctly.)

> For more information on interop between .NET and JavaScript, see https://docs.microsoft.com/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet.

### What about cryptographically secure random values?

.NET (all versions) provides the `RandomNumberGenerator` class, which can be used to get cryptographically secure random values. This class is implemented in a secure manner across _all_ runtimes and operating systems, including Browser. (In browsers, this method is a wrapper around [Crypto.getRandomValues()](https://developer.mozilla.org/docs/Web/API/Crypto/getRandomValues).)

## Providing feedback

For feedback on the plan for .NET 7 and beyond, please leave a comment on https://github.com/dotnet/runtime/issues/40074. That issue has been the most active with respect to gathering user scenarios and providing requests. We can also split off feedback items into new individual issues as needed.
