# Re-enabling Cross-Platform NuGet Signed Package Verification Technical Specification

Owners:  [Damon Tivel](https://github.com/dtivel), [Richard Lander](https://github.com/richlander)

This document specifies the technical details to [re-enable cross-platform NuGet signed package verification](https://github.com/dotnet/designs/pull/245).

## Context

NuGet package signing and verification relies on .NET's [`X509Chain`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chain?view=net-5.0) class for cross-platform X.509 certificate chain building functionality.

.NET 5 [introduced](https://github.com/dotnet/runtime/issues/20302) a feature that enables overriding default system trust anchors with custom trust anchors via [`X509ChainPolicy.TrustMode`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.trustmode?view=net-5.0) and [`X509ChainPolicy.CustomTrustStore`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.customtruststore?view=net-5.0).  This feature will be central to re-enabling cross-platform NuGet signed package verification.

.NET has a basic trust model for trust anchors (system or custom):  an anchor is fully trusted based on the scoping features of the certificate itself (i.e.:  validity period, key usage, etc.).  CTL's and trust stores typically include additional trust metadata to enable layering of additional restrictions on their certificates.  This layering happens through metadata-aware API's.

* On Windows, .NET uses Win32 CAPI APIs for chain building, which starts from [`X509Chain.Build()`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chain.build?view=net-5.0) and then calls into [`CertGetCertificateChain(...)`](https://docs.microsoft.com/windows/win32/api/wincrypt/nf-wincrypt-certgetcertificatechain), which applies its trust metadata automatically before returning a result.
* On Linux, .NET uses OpenSSL for chain building, but only supports plain PEM-encoded X.509 certificates as trust anchors.  (.NET does not support OpenSSL's "trusted certificate" format, as described in the [`x509`(1) manual page](https://www.openssl.org/docs/man1.1.1/man1/x509.html).)  Because the supported certificate file format has no metadata, there is no metadata to enforce.
* On macOS, .NET uses `SecTrust*` functions from Security.framework, which presumably also apply trust metadata automatically before returning a result.

Trust metadata is important, as it enables trust store operators to make fine-grained trust statements about a certificate.  Trust metadata can enable scenarios like distrusting certificates issued by a particular certificate authority (CA) after a specific date, while continuing to trust certificates issued before that date.  Without this metadata, the only alternatives are all or nothing:  trust the certificate hierarchy for its full, built-in validity period or don't.

Because .NET has no explicit knowledge or enforcement of trust metadata, it would seem that Linux and macOS trust experiences are less capable than that of Windows.  However, as long as https://nuget.org continues performing signed package verification on Windows, the NuGet packages are validated with trust metadata implicitly (as a one-time trust decision at package upload time).

## Terminology

According to [Certificate Trust List Overview](https://docs.microsoft.com/windows/win32/seccrypto/certificate-trust-list-overview), a certificate trust list (CTL):

1. is "a predefined list of items signed by a trusted entity"
2. is "a list of hashes of certificates or a list of file names"
3. may contain arbitrary certificates (e.g.:  root, intermediate, leaf, trusted, explicitly distrusted, etc.)
4. may contain additional trust metadata

A definition for **certificate bundle** might include only #3 and #4 above.  Unlike CTL's, bundles support a set of certificates with no trust metadata.  This means that certificate bundles are often usable with simple PEM decoding, while CTL's often require a metadata-aware API.

The remainder of this document will apply these terms:

* **certificate bundle** or simply **bundle**, when referring to a file containing certificates
* **CTL**, only when referring to the Windows CTL

## Solution

### Certificate Bundles

Two bundles are needed to re-enable cross-platform package signing and signed package verification:  a system-provided bundle (or "system bundle") and a fallback bundle.

Bundle files must contain PEM-encoded X.509 certificates in the `BEGIN/END CERTIFICATE` format described in the [`x509`(1) manual page](https://www.openssl.org/docs/man1.1.1/man1/x509.html).  NuGet will use [`X509Certificate2Collection.ImportFromPemFile(string)`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate2collection.importfrompemfile?view=net-5.0) to parse certificates.

To be valid for use, a bundle must contain root CA certificates that are trusted for both code signing and timestamping.  Non-root certificates (e.g.:  intermediate) will be ignored.

If the fallback bundle is needed, but there is a problem processing it (e.g.:  nonexistent, empty, malformed), then no root CA certificates will be trusted, and package signing and signed package verification will fail.

#### System Bundle

For NuGet to use a system bundle, the bundle must contain at least 1 certificate.

#### Fallback Bundle

The .NET SDK will ship a fallback bundle, which will represent a subset of the Windows CTL that is trusted for both code signing and timestamping.  The bundle will install to the versioned SDK directory (`dotnet\sdk\<version>`) where NuGet assemblies also install.

### Windows

Windows already has a trust store that supports code signing and timestamping.  It also supports advanced distrust capabilities, which NuGet gets for free by .NET using Windows API's.

As it does already, NuGet will continue to use Windows' trust store as-is.  This is achieved using an [`X509Chain`](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chain?view=net-5.0) with `X509Chain.ChainPolicy.TrustMode` set to its default of `X509ChainTrustMode.System`.

A bundle is not required on Windows.

### Linux

On Linux, NuGet will first probe for a system bundle using a list of well-known paths.  The first successful match will be used.  If no match is found or if there are problems processing the system bundle, NuGet will use the fallback bundle.

Initially, the probe list will be the following but may be updated at any time.
```
/etc/pki/ca-trust/extracted/pem/objsign-ca-bundle.pem
```

This initial probe path was chosen because:

1. It is a root-owned location, and probing it does not invalidate the NuGet threat model.
2. Linux already defines the purpose and content of this file.  See the [`update-ca-trust`(8) manual page](https://www.linux.org/docs/man8/update-ca-trust.html) for details.
3. The meaning of this file is expected to be consistent in all environments where NuGet runs on Linux.

NuGet will log which bundle is used, preferrably as a diagnostic (verbosity) message.

### macOS

NuGet will use the fallback bundle.