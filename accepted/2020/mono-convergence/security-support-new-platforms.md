# Security support for new .NET 5 platforms

## Overview
Currently in .NET Core, we have a solid [strategy](https://github.com/dotnet/runtime/blob/master/docs/design/features/cross-platform-cryptography.md) for dealing with cryptography on Linux and OSX.  At the native level, `System.Security.Cryptography.Native` relies upon OpenSSL for Linux and `System.Security.Cryptography.Native.Apple` relies upon platform specific API’s for OSX.  

With the introduction of Android, iOS, and Wasm workloads, there are implications to consider that may alter our strategy and require additional work.  This document seeks to describe the implications and what options we may have.

## Security Dependencies
For `System.Security.Cryptography.Native` (Linux):

[src/libraries/Native/Unix/System.Security.Cryptography.Native] (https://github.com/dotnet/runtime/tree/master/src/libraries/Native/Unix/System.Security.Cryptography.Native)

For `System.Security.Cryptography.Native.Apple` (OSX):

[src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple] (https://github.com/dotnet/runtime/tree/master/src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple)

### Android
There is currently no platform level API’s that can work with Android, so any native dependency we take must be bundled with the individual application.

**System.Security.Cryptography.Native**

Since Xamarin's reliance on a fork of BoringSSL is not viable, the current .NET dependency on OpenSSL could work for us. There is support for either building on Android or taking a maven dependency that Google hosts (https://maven.google.com/).  This is nice because it would seemingly ‘just fit’ with what we already have.  However, any dependency we take on poses the following challenges:

* Can / should we take on the maven dependency?  What are the potential pitfalls?  
* Do we fork a copy instead and build it?  
* What is our process for shipping security vulnerability updates?  Can we take on this responsibility?
* Should we consider alternatives?
  * [https://tls.mbed.org/](https://tls.mbed.org/) - only solves the TLS part
  * Any others?

**System.Net.Security.Native**

In the past, Xamarin relied upon a custom implementation that poses maintainability / portability challenges.  Since .NET has relied upon libgss to do the work, it makes sense to see if libgss could do the work.  Unfortunately, it is currently unknown if we *can* support it on Android.  Some initial [discovery](https://github.com/dotnet/runtime/issues/32680#issuecomment-599307356) has been started by a community member and would need to be expanded on / validated.  Additionally, we should consider prototyping [Kerberos.Net](https://github.com/dotnet/Kerberos.NET/) as that might be a viable managed solution.

To move forward, we should try to decide on the best approach.

Do we:
* Investigate libgss further and flush out all the dependencies / challenges?
* Prototype Kerberos.NET.  What does that look like?  Who would do it?

## iOS/tvOS
iOS, similar to OSX, has platform level API’s that System.Security.Cryptography.Native.Apple hooks into.  Thankfully, we have found that most just work and so we would need to decide what to do about the set that a does not (simply PNSE?).

Note: There are probably more API’s that we need to find as the build gives up after a while.  This just gives you a rough idea:

**Available on a certain API version**
[src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_sec.c](https://github.com/dotnet/runtime/blob/master/src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_sec.c)

```'SecCopyErrorMessageString' is only available on iOS 11.3 or newer```

**Unavailable on iOS**
[src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_keychain.h](https://github.com/dotnet/runtime/blob/master/src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_keychain.h)
```
    SecKeychainRef
    SecKeychainItemRef
    
    PALEXPORT int32_t AppleCryptoNative_SecKeychainItemCopyKeychain(SecKeychainItemRef item, SecKeychainRef* pKeychainOut);
    PALEXPORT int32_t AppleCryptoNative_SecKeychainDelete(SecKeychainRef keychain);
    PALEXPORT int32_t AppleCryptoNative_SecKeychainCopyDefault(SecKeychainRef* pKeychainOut);
    PALEXPORT int32_t AppleCryptoNative_SecKeychainOpen(const char* pszKeychainPath, SecKeychainRef* pKeychainOut);
    PALEXPORT int32_t AppleCryptoNative_SecKeychainUnlock(SecKeychainRef keychain,
    uint32_t passphraseLength,
    const uint8_t* passphraseUtf8);
    PALEXPORT int32_t AppleCryptoNative_SetKeychainNeverLock(SecKeychainRef keychain);
    PALEXPORT int32_t
    AppleCryptoNative_SecKeychainEnumerateCerts(SecKeychainRef keychain, CFArrayRef* pCertsOut, int32_t* pOSStatus);
    PALEXPORT int32_t AppleCryptoNative_SecKeychainEnumerateIdentities(SecKeychainRef keychain,CFArrayRef* pIdentitiesOut,int32_t* pOSStatus);
    PALEXPORT int32_t AppleCryptoNative_X509StoreAddCertificate(CFTypeRef certOrIdentity, SecKeychainRef keychain, int32_t* pOSStatus);
    PALEXPORT int32_t AppleCryptoNative_X509StoreRemoveCertificate(CFTypeRef certOrIdentity, SecKeychainRef keychain, uint8_t isReadOnlyMode, int32_t* pOSStatus);
```

[src/libraries/Native/Unix/System.Security.Cryptography.Native.Apply/pal_seckey.h](https://github.com/dotnet/runtime/blob/master/src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_seckey.h)
```
    SecExternalItemType 
    
    OSStatus ExportImportKey(SecKeyRef* key, SecExternalItemType type);
```

[src/libraries/Native/Unix/System.Security.Cryptography.Native.Apply/pal_rsa.c](https://github.com/dotnet/runtime/blob/master/src/libraries/Native/Unix/System.Security.Cryptography.Native.Apple/pal_rsa.c)
```
    SecTransformRef
    kSecUseKeychain
    
    static int32_t ExecuteCFDataTransform(SecTransformRef xform, uint8_t* pbData, int32_t cbData, CFDataRef* pDataOut, CFErrorRef* pErrorOut);
    
    /* kSecUseKeyChain not supported on iOS and is used in the function */
    int32_t AppleCryptoNative_RsaGenerateKey(int32_t keySizeBits, SecKeychainRef tempKeychain, SecKeyRef* pPublicKey, SecKeyRef* pPrivateKey, int32_t* pOSStatus)
```

## WebAssembly
Once we have solution for Android we could use similar approach for WebAssembly which also does not have any security APIs available. This might be a trickier than Android in the sense that any advanced libc like functionality might not be fully supported in emscripten.
