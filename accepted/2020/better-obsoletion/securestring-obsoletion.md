# SecureString obsoletions and SensitiveBuffer proposal

Author: @GrabYourPitchforks

## SecureString obsoletion proposal

Obsolete the following APIs as warning, following the [better obsoletions](https://github.com/dotnet/designs/blob/master/accepted/2020/better-obsoletion/better-obsoletion.md) spec:

- All `System.Security.SecureString` [ctor overloads](https://docs.microsoft.com/dotnet/api/system.security.securestring.-ctor)
- All `System.Net.NetworkCredential` [ctor overloads](https://docs.microsoft.com/dotnet/api/system.net.networkcredential.-ctor) which accept SecureString as a parameter
- [`System.Net.NetworkCredential.SecurePassword`](https://docs.microsoft.com/dotnet/api/system.net.networkcredential.securepassword)
- All [`System.Diagnostics.Process.Start`](https://docs.microsoft.com/dotnet/api/system.diagnostics.process.start) overloads which accept SecureString as a parameter
- [`System.Diagnostics.ProcessStartInfo.Password`](https://docs.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.password)
- All overloads of the following `System.Security.Cryptography.X509Certificates.X509Certificate` APIs which accept SecureString as a parameter:
  - [The ctor](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.-ctor)
  - [`Export`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.export)
  - [`Import`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.import)
  - Any equivalent ctor or method on [`X509Certificate2`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate2)

> See later in this document for the list of APIs which would be preferred instead.

This does not change the user-observable runtime behavior of the obsoleted APIs. Any existing code compiled against these call sites will continue to work.

The above only proposes to obsolete the SecureString constructor and some specific receivers. The intent is that if an application does wish to continue using SecureString as an exchange type or for interop or manipulation purposes, we don't want to force them to sprinkle a bunch of unnecessary suppressions throughout the code base. Suppressing just the small handful of instances where these types are created or consumed should be enough for those developers to signal that they're comfortable using the type.

## Background \& motivation

The specter of removing SecureString has been a topic of debate for some time. It first came up during [the .NET Core 1.x API discussions](https://github.com/dotnet/apireviews/blob/ac765393a7281184ec30948b7a6d86b7ad58af1c/2015/07-14-securestring/README.md). At that time introduction of the SecureString type was rejected. It was later added during the .NET Core 2.0 push to migrate a wider set of existing .NET Framework APIs. And more recently there has been a [desire to move people away](https://github.com/dotnet/runtime/issues/30612) from the type given that [it cannot fulfill the security promises](https://github.com/dotnet/platform-compat/blob/master/docs/DE0001.md) it guarantees.

We want users to be able to rely on the contracted guarantees of .NET cross-platform. This means that the framework should discourage the use of APIs which users believe may incorrectly fulfill a compliance requirement for them. `CryptProtectMemory`, which the Windows implementation of SecureString relies upon, [is even documented as inappropriate](https://docs.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptprotectmemory) for the scenario that SecureString uses it for.

## Today's use cases for SecureString

> This list is largely drawn from the feedback at https://github.com/dotnet/runtime/issues/30612 plus our analysis of internal consumers.

### Storing credentials

Some users may use SecureString as a convenient way to store credentials for a remote server, such as a SQL server. This allows them to pass around a "credentials object" that has somewhat more foolproof usage patterns than handing around a simple string.

For these scenarios we should strongly encourage users to use passwordless mechanisms, such as using X.509 certificates or platform-specific credential management. These provide better, more modern security guarantees than typical username + password combinations.

For scenarios which continue to use username + password, we should encourage users to pass around a `NetworkCredential` object instead. The NetworkCredential class abstracts away how the password is represented in memory: it can be a string, a legacy SecureString, or any future mechanism we create. Encouraging users to pass around this container rather than a raw SecureString instance allows their code to take advantage of future improvements we make to this space, even while they continue to use username + password credentials.

### Preventing secrets from getting into crash dumps

Users might rely on the fact that the SecureString backing memory is enciphered (only on Windows) to prevent secrets from getting into crash dumps. This is not ideal for a variety of reasons. The secret won't be protected before it's converted into a SecureString or after it's converted from a SecureString (such as during a p/invoke call). `SocketsHttpHandler` [converts SecureString instances to normal strings](https://github.com/dotnet/runtime/blob/0e23ef47ff8a2de6a5b5898a849ba00303aba8df/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/AuthenticationHelper.cs#L176-L185) as part of its operation. WPF's `PasswordBox` control [contains code](https://github.com/dotnet/wpf/blob/53812c590c5f140297ed96ee2a78d05dffa7be35/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/PasswordTextContainer.cs#L74-L97) to manipulate SecureString instances from string-based input. `SqlClient` [copies SecureString data into normal managed arrays](https://github.com/dotnet/SqlClient/blob/ead806e87a15be9f54c99db2ebe8918025c20fc9/src/Microsoft.Data.SqlClient/netfx/src/Microsoft/Data/Interop/SNINativeMethodWrapper.cs#L989-L993) during manipulation. In all of these cases the secret remains eligible for inclusion in a memory dump.

That secrets originate as strings deserves a bit more attention. The secret almost certainly entered the application via an HTTP request, or it was read from config, or it was entered into a WinForms textbox. These copies of the secrets are still floating around in memory (potentially even in a shared pool!), and SecureString does nothing to mitigate their being leaked.

The only reliable mechanism for preventing secrets from getting into crash dumps is to disable crash dumps for applications which process sensitive data. For web servers, this might mean separating out the "sensitive" parts of the application (login, payment processing, etc.) into a separate service running in a separate secured environment. The remaining aspects of the web service can then run with normal crash dump diagnostics provided they never have access to the plaintext form of the sensitive data.

### Minimizing accidental exposure of sensitive data

Sometimes the goal is not to provide a foolproof mechanism for keeping data out of crash dumps. Rather, the goal might be to reduce the window in which secrets are available. One side effect of this is that unsafe-equivalent code (such as use of the uninitialized `ArrayPool<T>.Shared`) has less chance to inadvertently leak these secrets.

One way to accomplish this is by using .NET 5.0's new [pinned object heap](https://github.com/dotnet/runtime/pull/32283). Arrays allocated on the heap are guaranteed not to be moved during a GC, so applications don't need to worry about sensitive contents being copied before the application logic has had a chance to clear the memory. This has the benefit that an application can treat the data like a normal array.

Another way is to invest in [a proposed runtime feature](https://github.com/dotnet/runtime/issues/10480) which would make the GC more aggressive about zeroing memory on compaction. This has the benefit that _many_ different types – including standard strings – would become candidates for having their data zeroed out during a compaction phase. High-sensitivity applications may wish to opt in to this GC mode anyway depending on their risk analysis.

### Minimizing misuse of sensitive data

Here, the goal isn't to hide secrets from memory, but to signal to developers "this data is sensitive and should be handled cautiously." In other words, the goal is to minimize the risk of a developer _accidentally_ using the data in an insecure fashion, such as writing it to a log file. SecureString actually does a fine job of this by virtue of the fact that callers must drop down to unsafe code or use the `Marshal` class to interact with it. However, the restrictive SecureString API surface and the fact that we force use of `Marshal` can lead to developers introducing unmanaged memory errors into their applications.

There is opportunity for .NET to provide a better type for this scenario, described in more detail later.

### Using it because it has "secure" in the name

Based both on internal and external conversations, there seems to be a significant audience using the SecureString type because it has "secure" in its name. I have seen many code bases which have contorted themselves into using SecureString in as many places as possible in order to fulfill of a well-intentioned but ill-executed "security compliance" edict. Since this sort of maneuvering generally involves unsafe code, this often ends up _introducing_ security bugs. (One partner was even using unsafe code to zero-out string instances after converting them to SecureString.)

During these conversations it's apparent that they're not attempting to mitigate any specific threats. They're instead using SecureString because it's listed as a requirement in their engineering processes. These users would be best served by using the existing string class or by using the `NetworkCredential` wrapper as suggested earlier.

## A proposal for marking data as sensitive

Of all the scenarios mentioned above, the one that strikes me as most interesting is the use of SecureString as a marker class to mean "you should be careful when using this data." It's possible to extend this into a more generalized proposal that can serve as a wrapper for arbitary types _T_, so it can be used to hold both `byte` and `char`-based buffers.

```cs
namespace System.Security
{
    public sealed class SensitiveData<T> : IDisposable where T : unmanaged
    {
        public SensitiveData(ReadOnlySpan<T> contents);
        public int Length { get; }
        public void CopyTo(Span<T> destination);
        public void Dispose();
    }
}
```

In this proposal, "sensitive" means that the buffer is nominally hidden from view, but it's not guaranteed to be enciphered or otherwise protected from active inspection. The actual storage mechanism utilized by `SensitiveData<T>` is an implementation detail. The backing buffer may be managed or unmanaged, in-proc or out-of-proc, enciphered or not enciphered, etc. The type should make best efforts to prevent _accidental_ misuse of data, but no effort is taken to prevent _active_ inspection by an enitity with full memory dump capabilities. The contents are copied by the ctor, and the buffer is immutable. The caller can extract the contents into a destination buffer of their own choosing.

The API surface clearly delineates buffers for which the caller maintains responsibility (the `ctor` and `CopyTo` methods). If the caller wishes that the plaintext data is never copied during a GC, then the destination buffer they pass to `CopyTo` should be allocated from the pinned object heap, the stack, or the native memory pool. The API surface gives them full control over that process.

```cs
// Example showing unshrouding a SensitiveData<char> into a temp char[].
SensitiveData<char> sensitiveData = GetSensitiveStringData();
char[] tempArray = GC.AllocateArray<char>(sensitiveData.Length, pinned: true);
try
{
    sensitiveData.CopyTo(tempArray);
    // use 'tempArray' here
}
finally
{
    Array.Clear(tempArray);
}

// Example showing unshrouding a SensitiveData<char> into a string.
SensitiveData<char> sensitiveData = GetSensitiveStringData();
string plaintextString = string.Create(
    sensitiveData.Length, sensitiveData, (span, sensitiveData) =>
    {
        sensitiveData.CopyTo(span);
    });
```

We would clearly document that this type is not intended to offer cryptographic protection of the buffer contents; it's simply intended to enforce more hygienic usage of data by callers.

The type also intentionally does not give callers access to the raw backing span. This is because how the backing data is stored is strictly an implementation detail (it might not even be in-proc!), and exposing the span would leak that detail into the public API surface. The `CopyTo` method is the only way to get the raw contents. This also means that such buffers are immutable once created.

__We should take care not to position this "sensitive buffer" type as a security feature for the purpose of achieving compliance.__ It's intended solely for code hygiene purposes and to avoid accidental disclosure. Scenarios like achieving PCI compliance require more fundamental runtime work, such as that under consideration at https://github.com/dotnet/runtime/issues/10480.

This proposal intentionally excludes `ToString`, `ToArray`, or other methods which might unshroud the buffer. `ToString` is called frequently by library code as part of logging, and we always want unshrouding to be a _deliberate_ action. Additionally, we want the caller to maintain full control over the unshrouded buffer where the contents are written, which precludes "opinionated" APIs like `ToArray` which create such buffers themselves.

### Best practices for implementation

If the backing buffer for the sensitive data is stored in-proc, we should attempt to use a separate unmanaged heap for storing these values, and we should not hand out direct references to this heap. This helps mitigate the possibility of sensitive data being leaked due to corruption of the normal heap. This concept is [already in use by OpenSSL](https://www.openssl.org/docs/man1.1.0/man3/CRYPTO_secure_malloc_initialized.html) and a few high-sensitivity Windows services. If using a separate heap is not feasible, we should attempt to use .NET's pinned object heap.

The backing buffer may be (but is not required to be) enciphered or marked do-not-dump. In practice, we will probably encipher data when we can to avoid accidental leakage of the sensitive data. We will probably not mark the pages as do-not-dump due to the difficulty of bookkeeping and the fact that both Linux and Windows primarily intend do-not-dump regions to be for large data sections (such as texture data for games), and they institute an upper bound on the number of pages which may be annotated as do-not-dump.

## New overloads on existing APIs

Some APIs - particularly those dealing with cryptographic keys - are ideal candidates for being enlightened about `SensitiveData<T>`. A non-exhaustive list is provided below.

```cs
namespace System.Security.Cryptography
{
    public sealed class AesGcm
    {
        /* existing ctors */
        public AesGcm(byte[] key);
        public AesGcm(ReadOnlySpan<byte> key);

        /* new proposed ctor */
        public AesGcm(SensitiveData<byte> key);
    }

    public class Rfc2898DeriveBytes
    {
        /* existing method */
        public static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength);

        /* new proposed overload - takes and returns a SensitiveData parameter */
        public static SensitiveData<byte> Pbkdf2(SensitiveData<byte> password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength);
    }
}
```

## New proposed SecureString-replacement APIs

For the X.509 APIs, we should expose APIs which take the password as a `SensitiveData<char>` or a `SensitiveData<byte>` directly. These would serve as overloads of the existing `SecureString`-consuming APIs.

```cs
namespace System.Security.Cryptography.X509Certificates
{
    public class X509Certificate
    {
        public X509Certificate(byte[] rawData, SensitiveData<char> password, X509KeyStorageFlags keyStorageFlags);
        public virtual void Import(byte[] rawData, SensitiveData<char> password, X509KeyStorageFlags keyStorageFlags);
        /* ... */
    }
}
```

For `Process.Start` and `ProcessStartInfo` we should expose overloads which take a `NetworkCredential`. These overloads would only work on Windows.

```cs
namespace System.Diagnostics
{
    public class Process
    {
        public static Process Start(string fileName, NetworkCredential credential);
    }

    public class ProcessStartInfo
    {
        public NetworkCredential Credentials { get; set; }
    }
}
```

For `NetworkCredential` we should expose a property to accept a shrouded password.

```cs
namespace System.Net
{
    public class NetworkCredential
    {
        public SensitiveData<char> SensitivePassword { get; set; }
    }
}
```

Finally, there should be a conversion mechanism between `SecureString` and `SensitiveData<char>`. This should help transition developers who currently have SecureString instances to APIs which accept shrouded buffers, and vice versa (for interoping with legacy code).

```cs
namespace System.Security
{
    public sealed class SecureString
    {
        // new ctor would be marked [Obsolete]
        public SecureString(SensiitveData<char> value);
        public SensitiveData<char> ToSensitiveData();
    }
}
```

## Suggested behavioral changes to SecureString for .NET 6

If we implement the sensitive buffer concept in .NET 6, I suggest we rewrite `SecureString` in terms of it. This will provide a uniform implementation across both Windows and non-Windows: the data is intentionally difficult to get to but is not enciphered on any OS.

There is already [a working prototype](https://github.com/GrabYourPitchforks/runtime/pull/5) of this rewrite of SecureString. There is no user-visible behavioral change to the SecureString APIs after this rewrite.
