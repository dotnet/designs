# Marking APIs that are unsupported by Blazor WebAssembly

**PM** [Immo Landwerth](https://github.com/terrajobst)

As discussed in [dotnet/designs#143] we're adding an analyzer to detect usage of
unsupported APIs. This analyzer requires a custom attribute applied to method,
containing type, or assembly.

In this proposal I'm covering which APIs we mark as unsupported in Blazor Web
Assembly. It was largely informed by the work put into [platform-compat].

[dotnet/designs#143]: https://github.com/dotnet/designs/pull/143
[platform-compat]: https://github.com/dotnet/platform-compat

## Attribute application

We'll mark the API as unsupported by all versions. Should we make some APIs work
in the future, we can modify the attribute to specify until which version they
were unsupported.

```C#
[UnsupportedOSPlatform("browser")]
```

## Focused on Microsoft.NetCore.App

We focus on APIs that are part of the default reference set for Blazor
applications, that is APIs that are part of `Microsoft.NetCore.App`.

## Entire assemblies

* System.Diagnostics.FileVersionInfo
* System.Diagnostics.Process
* System.IO.Compression.Brotli
* System.IO.FileSystem.Watcher
* System.IO.IsolatedStorage
* System.IO.Pipes
* System.Net.Mail
* System.Net.NameResolution
* System.Net.NetworkInformation
* System.Net.Ping
* System.Net.Requests
* System.Net.Security
* System.Net.Sockets
* System.Net.WebClient
* System.Security.Cryptography.Csp
* System.Security.Cryptography.Encoding
* System.Security.Cryptography.Primitives
* System.Security.Cryptography.X509Certificates

## APIs

I've only listed namespace and type level for now; I suspect we'll end up marking
a good chunk of these at the assembly level.

Also, I've only included APIs that are unsupported in Blazor but are supported
everywhere else. For instance, there is no point in marking APIs which are
already marked as Windows-specific -- they are already flagged.

Once the Windows attribution is done, I'll update this PR again.

### System

* **Console**
    - BackgroundColor
    - Beep()
    - BufferHeight.get
    - BufferWidth.get
    - CancelKeyPress
    - CursorLeft
    - CursorSize.get
    - CursorTop
    - CursorVisible.set
    - ForegroundColor
    - GetCursorPosition()
    - In
    - InputEncoding
    - LargestWindowHeight
    - LargestWindowWidth
    - OpenStandardInput()
    - OpenStandardInput(int)
    - Read()
    - ReadKey()
    - ReadKey(bool)
    - ReadLine()
    - ResetColor()
    - SetCursorPosition(int, int)
    - SetIn(TextReader)
    - Title.set
    - TreatControlCAsInput
    - WindowHeight.get
    - WindowWidth.get
* **System.Diagnostics.Process**
* **System.IO.Compression.Brotli**
* **System.IO.FileSystem.Watcher**
* **System.IO.IsolatedStorage**
* **System.IO.Pipes**
* **System.Net.Http**

### System.ComponentModel

* **LicenseManager**
    - CreateWithContext(Type, LicenseContext)
    - CreateWithContext(Type, LicenseContext, object[])
* **MaskedTextProvider**
    - Clone()
* **TypeDescriptionProvider**
    - CreateInstance(IServiceProvider, Type, Type[], object[])
* **TypeDescriptor**
    - CreateInstance(IServiceProvider, Type, Type[], object[])

### System.Net

* **HttpListener**
    - ExtendedProtectionPolicy.set
* **HttpListenerRequest**
    - IsLocal
    - LocalEndPoint
    - RemoteEndPoint
    - UserHostAddress
* **HttpListenerResponse**
    - Abort()
    - Close()
    - Close(byte[], bool)
* **System.Net.NameResolution**
* **System.Net.NetworkInformation**
* **System.Net.Ping**
* **System.Net.Requests**
* **System.Net.Security**
* **System.Net.Sockets**
* **System.Net.WebClient**
* **System.Net.WebSockets.Client**

### System.Net.Http

* **HttpClientHandler**
    - AutomaticDecompression
    - CheckCertificateRevocationList
    - ClientCertificates
    - CookieContainer
    - Credentials
    - DefaultProxyCredentials
    - MaxAutomaticRedirections
    - MaxConnectionsPerServer
    - MaxResponseHeadersLength
    - PreAuthenticate
    - Proxy
    - ServerCertificateCustomValidationCallback
    - SslProtocols
    - UseCookies
    - UseDefaultCredentials
    - UseProxy
* **SocketsHttpHandler**

### System.Net.WebSockets

* **ClientWebSocketOptions**
    - ClientCertificates
    - Cookies
    - Credentials
    - KeepAliveInterval
    - Proxy
    - RemoteCertificateValidationCallback
    - SetBuffer(int, int)
    - SetBuffer(int, int, ArraySegment<byte>)
    - SetRequestHeader(string, string)
    - UseDefaultCredentials

### System.Security.Authentication.ExtendedProtection

* **ExtendedProtectionPolicyTypeConverter**
    - ConvertTo(ITypeDescriptorContext, CultureInfo, object, Type)

* **System.Net.HttpListener**

### System.Security.Cryptography

* **Aes**
    - AesCcm
    - AesGcm
    - AesManaged
    - AsymmetricKeyExchangeDeformatter
    - AsymmetricKeyExchangeFormatter
    - AsymmetricSignatureDeformatter
    - AsymmetricSignatureFormatter
    - CryptoConfig
    - DeriveBytes
    - DES
    - DSA
    - DSASignatureDeformatter
    - DSASignatureFormatter
    - ECCurve
    - ECDiffieHellman
    - ECDsa
    - ECParameters
    - HKDF
    - HMACMD5
    - HMACSHA1
    - HMACSHA256
    - HMACSHA384
    - HMACSHA512
    - IncrementalHash
    - CreateHMAC(HashAlgorithmName, byte[])
    - CreateHMAC(HashAlgorithmName, ReadOnlySpan<byte>)
* **MaskGenerationMethod**
    - MD5
    - PKCS1MaskGenerationMethod
    - RandomNumberGenerator
    - Create(string)
* **RC2**
    - Rfc2898DeriveBytes
    - Rijndael
    - RijndaelManaged
    - RSA
    - RSAEncryptionPadding
    - RSAOAEPKeyExchangeDeformatter
    - RSAOAEPKeyExchangeFormatter
    - RSAPKCS1KeyExchangeDeformatter
    - RSAPKCS1KeyExchangeFormatter
    - RSAPKCS1SignatureDeformatter
    - RSAPKCS1SignatureFormatter
    - RSASignaturePadding
    - SignatureDescription
    - TripleDES
* **System.Security.Cryptography.Csp**
* **System.Security.Cryptography.Encoding**
* **System.Security.Cryptography.Primitives**
* **System.Security.Cryptography.X509Certificates**
* **System.Threading.ThreadPool**

### System.Threading

* **RegisteredWaitHandle**
    - ThreadPool
    - RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, int, bool)
    - RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, long, bool)
    - RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, TimeSpan, bool)
    - RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, uint, bool)
    - UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, int, bool)
    - UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, long, bool)
    - UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, TimeSpan, bool)
    - UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, uint, bool)
