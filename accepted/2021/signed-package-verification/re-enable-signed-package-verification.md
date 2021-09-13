# Re-enabling signed NuGet package verification

Owners: [Richard Lander](https://github.com/richlander), [Damon Tivel](https://github.com/dtivel)

[NuGet package signing](https://docs.microsoft.com/nuget/create-packages/sign-a-package) is a critical scenario that enables package consumers to validate packages for authenticity and integrity. Unfortunately and unexpectedly, we had to [disable signed package validation](https://github.com/dotnet/announcements/issues/180) for Linux and macOS in April 2021 due to discovering that those operating systems [do not support code signing](https://blog.mozilla.org/security/2021/05/10/beware-of-applications-misusing-root-stores/) as a general-purpose capability. This document describes the approach by which we will re-enable signed package validation as part of .NET 6.

[Signed package validation](https://docs.microsoft.com/dotnet/core/tools/dotnet-nuget-verify) remains enabled for Windows. Windows offers the only comprehensive and general-purpose program for [code signing](https://en.wikipedia.org/wiki/Code_signing) and [timestamping](https://en.wikipedia.org/wiki/Trusted_timestamping) (CS/TS) of any operating system. In fact, the same folks at Microsoft that enable Windows to have such a comprehensive program have agreed to help us (.NET Team) with NuGet package signing.

We talked to folks at Apple about CS/TS. We discovered that macOS does not offer a general-purpose code signing system. As a result, we will use the same approach on macOS that we use on Linux.

## Goals

This plan is oriented on the following goals:

* Re-enable package signing and validation on Linux and macOS, ASAP.
* Offer a reliable trust model and experience across operating systems.
* Enable administrators to manage trust for CS/TS certificates via operating system control where they have that capability (presently only Windows).
* Earnestly apply [guidance from Mozilla](https://blog.mozilla.org/security/2021/05/10/beware-of-applications-misusing-root-stores/).

Note: A brief description of NuGet package signing is provided as an appendix. Readers unfamiliar with NuGet signing may want to start with that information.

## High-level approach

The .NET SDK will implement a hybrid trust store model for CS/TS:

* On operating systems that support CS/TS, the .NET SDK will use the code signing system trust store as the default source of trusted CS/TS certificates. At present, Windows is the only operating system that satisfies this requirement.  Red Hat and Canonical have expressed interest to us in supporting code signing, and we welcome that.
* On all other operating systems, the .NET SDK will distribute and exclusively rely on a [Certificate Trust List (CTL)](https://docs.microsoft.com/windows/win32/seccrypto/certificate-trust-list-overview) that it includes as part of its distribution. This CTL will be sourced from Microsoft and include the same CS/TS certificates as the system store on Windows.

Note: The [Microsoft Trusted Root Program](https://docs.microsoft.com/security/trusted-root/program-requirements) is a source for the [Common CA Database](https://www.ccadb.org/).

Note: The CTL will also be distributed with the .NET SDK on Windows, but as an opt-in trust store. It will not be generally recommended or supported, but used primarily for testing the CTL on Windows as a proxy for Linux and macOS behavior.

Note: The CTL will be distributed and updated with the .NET SDK. Initially (and likely ongoing), the only supported way to acquire an updated CTL will be to acquire an updated .NET SDK.

There has been an [industry movement to distrust Symantec TLS certificates](https://blog.mozilla.org/security/2018/03/12/distrust-symantec-tls-certificates/) going back multiple years. The [April NuGet incident](https://github.com/dotnet/announcements/issues/180) was specifically related to the Symantec [Certificate Authority (CA)](https://en.wikipedia.org/wiki/Certificate_authority) and the final changes to distrust it. Going forward, we will not use the Symantec CA for any purpose. That's required from the perspective of ecosystem credibility and preparing for that CA to potentially be eventually distrusted for CS/TS as it already has been for [TLS](https://en.wikipedia.org/wiki/Transport_Layer_Security)/[SMIME](https://en.wikipedia.org/wiki/S/MIME).

The Symantec CA is still trusted on Windows for CS/TS and the same will be true for the CTL that NuGet will soon use (as distributed via the .NET SDK). That means that the existing NuGet.org catalog -- specifically the packages that include timestamp-based signatures that were generated using Symantec CA certificates -- will validate without issue. We were previously considering re-signing all of NuGet.org to remove any dependence on the Symantec CA, however, doing that now seems unnecessary.

We will move forward with this project in two phases, in order:

1. Transition NuGet.org, including Microsoft author-signing, to using a new CA, replacing all forward-looking uses of the Symantec CA.
1. Re-enable signed package validation in the .NET SDK.

## Transition to Microsoft CA

The following steps are required to transition to a new CA.

1. Announce plans to transition away from Symantec CA certificates. This document is not an announcement.
1. Select a new CA to use.
   * We have chosen the [Microsoft CA](https://www.microsoft.com/pkiops/docs/repository.htm) for CS/TS signing.
   * We previously did not use the Microsoft CA since no other operating system trusts it. However, we have since learned that no other operating system has a TS/CS model, making that concern moot.
   * Later, if another operating system (A) establishes a TS/CS certificate capability, and (B) wants to support NuGet package signing, they will need to enable trust for the Microsoft CA for CS/TS. That will be easy if they use the Microsoft-provided CTL.
   * One advantage of using the Microsoft CA is that it is unlikely that a similar style of incident as occurred in April will ever occur again.
1. Microsoft signing service (ESRP) will transition Microsoft package author-signing to the Microsoft CA.
1. Microsoft signing service (ESRP) will transition NuGet.org repo-signing to the Microsoft CA.
1. Contact authors that are using Symantec certificates for CS/TS and ask them to consider switching to a different CA. Those authors carry a higher risk that their packages will be considered invalid at some later time, even though this plan is intended to prevent that. NuGet.org data suggests that there are less than a dozen authors still using Symantec-based certificates.

Note: The use of the Microsoft CA is forward looking. There is no plan to re-sign existing packages on NuGet.org that use the Symantec (or any other) CA.

## Enable hybrid trust store

The following steps are required to enable the hybrid trust store model.

1. Source a set of root certificate authorities (CA) trusted for CS and TS (not TLS) from Microsoft and add to [dotnet/sdk](https://github.com/dotnet/sdk). These certificates will form the .NET SDK certificate trust list (CTL) for CS/TS.
1. Update NuGet client to read, process, and honor the CTL.
1. Update NuGet client to use the CTL on Linux and macOS and optionally on Windows (using TBD configuration).
1. Disallow (or do not offer) using the TLS system store on Linux or macOS, which has proven to be untrustworthy and unreliable for CS/TS.
1. Re-enable signed package validation with .NET 6, with the goal of targeting the .NET SDK 6.0.200 release.
1. Update the CTL (on GitHub and in the .NET SDK) with each ~ monthly .NET SDK release, aligning with the ~ monthly update model for the CTL.

## Enabling system store usage on Linux

Red Hat has expressed an interest in establishing a CS/TS system store to satisfy NuGet code signing needs. In particular, they would prefer to enable Red Hat Enterprise Linux users to manage all certificates (TLS, CS, TS) via the `ca-certificates` package and not via installing software like the .NET SDK. That approach cleanly maps to the model that exists on Windows.

The Microsoft Trust Root Program (TRP) Team will publish the CTL on GitHub on a monthly ongoing basis, in a new repo in the Microsoft org. The CTL will be published in (at least) the [NSS](https://en.wikipedia.org/wiki/Network_Security_Services) format, which is the primary certificate trust format used in the Linux ecosystem and enables standard certificate patterns and workflows. `ca-certificates` package maintainers from any Linux distribution are welcomed and encouraged to source TS/CS certificates from the TRP team repo to enable NuGet package signing and also other code signing scenarios (since the Microsoft CS/TS CTL is general purpose). We will establish a new communication mechanism for distribution maintainers that opt to source these certificates to ease adoption and provide confidence in their long-term operational use.

Initial feedback from Red Hat suggests that this approach will be satisfactory.

We have also had initial conversations with folks at Canonical. They have interest in the same model, for Ubuntu. We expect other Linux distributions will also have interest.

Note: There is discussion of two GitHub locations in this document, which might be confusing. The TRP Team will publish trusted root information to their repo (possibly `microsoft/trusted-roots`) on a monthly basis. Either the TRP or .NET team will copy CS/TS roots to the `dotnet/sdk` repo. There are various technical reasons why we do not want to rely on the TRP repo as part of the .NET SDK build.

## Enhancements to consider later

Afterwards, consider further refinements:

1. Both repo and author signing include a signature from a will-eventually-expire certificate and another from a will-not-expire-for-a-long-time timestamp service. This means that packages exclusively rely on the timestamp service signature for validation after they hit a certain age (relative to the certificate used for signing) and for the majority of their lifetime. Relying on just one timestamp service has proven to be challenging, per the [January](https://github.com/NuGet/Announcements/issues/49) and [April](https://github.com/dotnet/announcements/issues/180) incidents. We should consider using multiple timestamp services to enable redundancy.
1. Consider re-repo-signing high-use packages to significantly reduce the importance of Symantec signatures from the NuGet ecosystem, preparing for the eventual possibility that the Symantec CA may be distrusted for CS/TS by Microsoft. We should wait at least a year before considering this option.

We may be asked to consider offering additional administrator control (e.g. add/remove certs to trust) for the CTL distributed in the .NET SDK. This also has overlap with private PKI. We do not intend to expose such control in the short- or long-term. If you desire such control, you are encourage to request your operating system distribution adopt the model that has been documented for Red Hat with the `ca-certificates` package.
 
## Appendix: NuGet package signing

There are several aspects of NuGet package signing that are important to understand to consider this proposal.

### Benefits

Package signing offers two benefits

* **Validates authenticity** -- You can be sure that a package came from the author or organization you expect, per your trust of that author or organization.
* **Validates integrity** -- You can be sure that a package was not tampered with at any point.

These two terms are similar to "pedigree" and "provenance", respectively, which are terms used in the secure supply chain domain. Package signing is an important building block for [secure supply chain](https://openssf.org/).

Note: This document refers to package authenticity multiple times. From a NuGet perspective we can only know the that registered certificate was used to sign a package and that the registered credentials were used to upload it. We have no way of knowing if the actual package author or an attacker was in possession of the signing certificate and the credentials. 

### Characteristics

Package signing offers two critical characteristics:

* **Validation is durable** -- You can validate packages over time, even after the certificates used for signing have expired, while not reducing trustworthiness.
* **Validation is trustworthy** -- You can expect that certificates validation uses a trustworthy certificate store and follows standard x.509 certificate practices.

The durability characteristic is established by using redundant signing schemes, specifically both code signing (expires) and timestamping (does not expire).

These characteristics were demonstrated to have weak foundations and were effectively the underlying cause for [package signing validation being disabled on Linux and macOS](https://github.com/dotnet/announcements/issues/180) earlier this year. The proposal above is squarely oriented on resolving that.

### Code signing vs timestamping

Code signing and timestamping are both oriented on establishing trust using cryptographic hashes. They [differ in key ways](https://en.wikipedia.org/wiki/Code_signing#Time-stamping):

* Code signing
    * Fails to be functional once a certificate expires.
    * The to-be-signed object is the only input to the cryptographic signing operation.
    * Validation proves that the object is as expected/represented or not.
* Timestamping
    * For NuGet, timestamping is resilient to certificate expiration (NuGet trusts expired timestamping certificates, which isn't a general pattern for timestamping service usage).
    * A hash of the to-be-signed object and a timestamp are inputs to the cryptographic signing operation.
    * Validation proves that the object is as expected/represented or not and establishes certainty that signing occurred before a certain time.

In essence, code signing offers a means of revoking trust (and is the expected primary signing model because of that) while timestamping enables durability.

For example, NuGet will always block packages that are signed with revoked certificates, even if the certificate hasn't expired.

### Signing types

NuGet offers two types of package signing, the first of which is required (for NuGet.org).

* **Repo-signing** -- Every package on NuGet.org is repo-signed. The repo signature validates the state of the package at the point it was uploaded to the NuGet.org "respository". If you trust Microsoft and the processes it uses, then you can trust the validation result of the repo signature. The repo signature makes a claim on package integrity AFTER NuGet.org comes into possession of the package. It makes absolutely no claim on whether the package is safe to use (it could contain malware; however, NuGet.org does best-effort malware and virus scanning).
* **Author-signing** -- Package authors can optionally sign packages BEFORE uploading them to NuGet.org (or another NuGet repository). If you trust the author and the processes that they use, then you can trust the validation result of the author signature. The author signature makes a claim on package authenticity and integrity both BEFORE and AFTER NuGet.org comes into possession of the package. 

These signatures are more nuanced than they appear. At first glance, one might think that you could use one signature, either the repo-signed or author-signed signature, for both authenticity and integrity. In theory, you could, but it's not feasible in practice, particularly for the author signature. It's too easy to intercept and change package contents and then replace one "Joanna Smith" certificate for another. That's why NuGet.org requires authors to register their certificates and blocks upload when they do not match.

To continue this thought, there are other models:

* Adopt a more distributed certificate model similar to [keybase.io](https://book.keybase.io/docs/server#what-keybase-is-really-doing) that manages a inventory of certificate issuers and enables directly validating a package with a certificate registered with its author.
* Establish a public and distributed blockchain of package hashes that is available to everyone for validation purposes.
* NuGet users trust the package at every point of rest and movement and ensure the use of TLS across those movements.

None of these approaches are feasible for us to adopt generally, due to safety, industry acceptance, or other concern. The NuGet signed package model is based on the CA system, which is formal, audited, and accepted by governments and has a well-defined control system for administrators. The next section describes how trust is established with NuGet.

### Establishing trust

The NuGet package signing system establishes trust using the following scheme.

1. Package author signs package, which adds signing certificate(s) (public keys) into package.
1. Package authors upload signed packages to NuGet.org.
1. NuGet.org validates that the package was (A) signed by one of the [registered certificates](https://docs.microsoft.com/nuget/create-packages/sign-a-package#register-the-certificate-on-nugetorg) establishing authenticity, and (B) validates package integrity with the same certificate.
1. Package is repo-counter-signed and timestamped by NuGet.org, which also adds signing certificates into the package.
1. Later, NuGet client downloads package, per a `PackageReference`.
1. NuGet client validates package for integrity:
   1. It validates the [repo signature](https://docs.microsoft.com/nuget/api/repository-signatures-resource) for integrity per the certificate that [NuGet.org asserts](https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json) (per the matching date for the package) with the certificate included in the package.
   1. It validate the author signature for integrity with the certificate included in the package.
   1. It validates that the author and repo CS and TS certificates [chain to a trusted certificate root](https://docs.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509chain) using a trust store.

Note: There is no check for authenticity, per se, with the NuGet client. Step 3 above, performed by NuGet.org, validates authenticity. This check and the knowlege of its success is preserved by virtue of the repo counter-signature. The repo counter-signature enables us to trust that the package was author-signed with one of the certificates registered with NuGet.org for the author.

Note: Many packages are not author-signed but only repo-signed. In that case, the workflow is the same except that the author doesn't play a role, the only signature is the repo signature, and there is no counter-signature. It also means that there is very shallow trust in author authenticity. The authors credentials could have been compromised and [two factor authentication](https://docs.microsoft.com/nuget/nuget-org/individual-accounts#enable-two-factor-authentication-2fa) is offered but not required on NuGet.org.

This validation scheme is specific to NuGet.org. However, it can be slightly adapted with reasonable fidelity when used with other NuGet feeds. If you download and validate NuGet packages from NuGet.org, then push them to a service you trust with processes that you trust, then you can consider that trust is maintained (per your standards), assuming that TLS was used across all network endpoints. When you download those packages from your private NuGet feed, the author and repo signatures will still be validated, however, the `repository-signatures` endpoint will likely be unavailable to validate that the correct repo signature was used. You can, however, take advantage of [local repository validation](https://docs.microsoft.com/nuget/consume-packages/installing-signed-packages#trust-all-packages-from-a-repository) that largely mimics the NuGet.org behavior. In that case, you need to create a [nuget.config file](https://docs.microsoft.com/nuget/reference/nuget-config-file) that includes `trustedSigners` content that matches the [`repository-signatures`](https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json) NuGet.org endpoint.

```xml
<trustedSigners>
  <repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">
    <!-- Accept nuget.org's old repository signing certificate -->
    <certificate fingerprint="0E5F38F57DC1BCC806D8494F4F90FBCEDD988B46760709CBEEC6F4219AA6157D" hashAlgorithm="SHA256" allowUntrustedRoot="false" />

    <!-- Accept nuget.org's new repository signing certificate -->
    <certificate fingerprint="5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
  </repository>
</trustedSigners>
```
