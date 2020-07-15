# Obsoletions in .NET 5

**PM** [Immo Landwerth](https://github.com/terrajobst)

For .NET 5, [we're making](better-obsoletion.md) it more viable to obsolete APIs.
In this document, we're describing which APIs we intend to obsolete in .NET 5 and
which diagnostic IDs we will use for those.

With `dotnet/runtime` PR [#33248](https://github.com/dotnet/runtime/pull/33248),
the `ObsoleteAttribute` gained new `DiagnosticId` and `UrlFormat` properties. With
these properties, obsoletions can be grouped together when multiple APIs are
marked as obsolete for the same reason. This capability is reflected in the list
of obsoletions below. All of these obsoletions will use the `UrlFormat` of
`https://aka.ms/dotnet-warnings/{0}`.

1. The UTF-7 encoding is insecure and should not be used. Consider using UTF-8 instead.
    * DiagnosticId: `SYSLIB0001`
    * APIs:
        * `System.Text.UTF7Encoding` (constructors)
1. PrincipalPermissionAttribute is not honored by the runtime and must not be used.
    * DiagnosticId: `SYSLIB0002`
    * APIs:
        * `System.Security.Permissions.PrincipalPermissionAttribute` (constructor)
        * Level: **Error**
1. Code Access Security is not supported or honored by the runtime.
    * DiagnosticId: `SYSLIB0003`
    * APIs:
        * Classes: (everything public from the `System.Security.Permissions` namespace)
            * `CodeAccessSecurityAttribute`
            * `DataProtectionPermission`
            * `DataProtectionPermissionAttribute`
            * `EnvironmentPermission`
            * `EnvironmentPermissionAttribute`
            * `FileDialogPermission`
            * `FileDialogPermissionAttribute`
            * `FileIOPermission`
            * `FileIOPermissionAttribute`
            * `GacIdentityPermission`
            * `GacIdentityPermissionAttribute`
            * `HostProtectionAttribute`
            * `IsolatedStorageFilePermission`
            * `IsolatedStorageFilePermissionAttribute`
            * `IsolatedStoragePermission`
            * `IsolatedStoragePermissionAttribute`
            * `KeyContainerPermission`
            * `KeyContainerPermissionAttribute`
            * `KeyContainerPermissionAccessEntry`
            * `KeyContainerPermissionAccessEntryCollection`
            * `KeyContainerPermissionAccessEntryEnumerator`
            * `MediaPermission`
            * `MediaPermissionAttribute`
            * `PermissionSetAttribute`
            * `PrincipalPermission`
            * `PrincipalPermissionAttribute` (the type itself will become obsolete as a warning while the constructor will be obsolete as an error)
            * `PublisherIdentityPermission`
            * `PublisherIdentityPermissionAttribute`
            * `ReflectionPermission`
            * `ReflectionPermissionAttribute`
            * `RegistryPermission`
            * `RegistryPermissionAttribute`
            * `ResourcePermissionBase`
            * `ResourcePermissionBaseEntry`
            * `SecurityAttribute`
            * `SecurityPermission`
            * `SecurityPermissionAttribute`
            * `SiteIdentityPermission`
            * `SiteIdentityPermissionAttribute`
            * `StorePermission`
            * `StorePermissionAttribute`
            * `StrongNameIdentityPermission`
            * `StrongNameIdentityPermissionAttribute`
            * `StrongNamePublicKeyBlob`
            * `TypeDescriptorPermission`
            * `TypeDescriptorPermissionAttribute`
            * `UIPermission`
            * `UIPermissionAttribute`
            * `UrlIdentityPermission`
            * `UrlIdentityPermissionAttribute`
            * `WebBrowserPermission`
            * `WebBrowserPermissionAttribute`
            * `ZoneIdentityPermission`
            * `ZoneIdentityPermissionAttribute`
        * Classes that derive from `CodeAccessSecurityAttribute`
            * `System.Configuration.ConfigurationPermissionAttribute`
            * `System.Data.Common.DBDataPermissionAttribute`
            * `System.Data.OracleClient.OraclePermissionAttribute`
            * `System.Diagnostics.EventLogPermissionAttribute`
            * `System.Diagnostics.PerformanceCounterPermissionAttribute`
            * `System.DirectoryServices.DirectoryServicesPermissionAttribute`
            * `System.Drawing.Printing.PrintingPermissionAttribute`
            * `System.IdentityModel.Services.ClaimsPrincipalPermissionAttribute`
            * `System.Messaging.MessageQueuePermissionAttribute`
            * `System.Net.DnsPermissionAttribute`
            * `System.Net.SocketPermissionAttribute`
            * `System.Net.WebPermissionAttribute`
            * `System.Net.Mail.SmtpPermissionAttribute`
            * `System.Net.NetworkInformation.NetworkInformationPermissionAttribute`
            * `System.Net.PeerToPeer.PnrpPermissionAttribute`
            * `System.Net.PeerToPeer.Collaboration.PeerCollaborationPermissionAttribute`
            * `System.ServiceProcess.ServiceControllerPermissionAttribute`
            * `System.Transactions.DistributedTransactionPermissionAttribute`
            * `System.Web.AspNetHostingPermissionAttribute`
        * Interfaces:
            * `System.Security.Permissions.IUnrestrictedPermission`
            * `System.Security.IPermission`
            * `System.Security.IStackWalk`
            * `System.Security.Policy.IIdentityPermissionFactory` (has a single member that returns `IPermission`)
        * Classes that implement `IStackWalk`
            * `System.Security.PermissionSet`
                * `System.Security.NamedPermissionSet`
                * `System.Security.ReadOnlyPermissionSet`
        * Classes that implement `IPermission`
            * `System.IdentityModel.Services.ClaimsPrincipalPermission`
            * `System.Security.CodeAccessPermission`
        * Classes that derive from `CodeAccessPermission` (also includes implementations of `PermissionsIUnrestrictedPermission`)
            * `System.Configuration.ConfigurationPermission`
            * `System.Data.Common.DBDataPermission`
                * `System.Data.Odbc.OdbcPermission`
                * `System.Data.OleDb.OleDbPermission`
                * `System.Data.SqlClient.SqlClientPermission`
            * `System.Data.OracleClient.OraclePermission`
            * `System.Drawing.Printing.PrintingPermission`
            * `System.Messaging.MessageQueuePermission`
            * `System.Net.DnsPermission`
            * `System.Net.SocketPermission`
            * `System.Net.WebPermission`
            * `System.Net.Mail.SmtpPermission`
            * `System.Net.NetworkInformation.NetworkInformationPermission`
            * `System.Net.PeerToPeer.PnrpPermission`
            * `System.Net.PeerToPeer.Collaboration.PeerCollaborationPermission`
            * `System.Transactions.DistributedTransactionPermission`
            * `System.Web.AspNetHostingPermission`
            * `System.Xaml.Permissions.XamlLoadPermission`
        * Classes that derive from `ResourcePermissionBase`
            * `System.Diagnostics.EventLogPermission`
            * `System.Diagnostics.PerformanceCounterPermission`
            * `System.DirectoryServices.DirectoryServicesPermission`
            * `System.ServiceProcess.ServiceControllerPermission`
        * Enums: (everything from the `System.Security.Permissions` namespace)
            * `DataProtectionPermissionFlags`
            * `EnvironmentPermissionAccess`
            * `FileDialogPermissionAccess`
            * `FileIOPermissionAccess`
            * `HostProtectionResource`
            * `IsolatedStorageContainment`
            * `KeyContainerPermissionFlags`
            * `MediaPermissionAudio`
            * `MediaPermissionImage`
            * `MediaPermissionVideo`
            * `PermissionState`
            * `ReflectionPermissionFlag`
            * `RegistryPermissionAccess`
            * `SecurityAction` (with removal of the existing attributes on members)
            * `SecurityPermissionFlag`
            * `StorePermissionFlags`
            * `TypeDescriptorPermissionFlags`
            * `UIPermissionClipboard`
            * `UIPermissionWindow`
            * `WebBrowserPermissionLevel`
        * Classes/Members that depend on CAS types:
            * `System.AppDomain.PermissionSet` (property of type `PermissionSet`)
            * `System.Security.HostProtectionException` (heavily uses `HostProtectionResource`)
            * `System.Security.Policy.FileCodeGroup` (constructor requires `FileIOPermissionAccess`)
            * `System.Security.Policy.StrongName` (constructor requires `StrongNamePublicKeyBlob` and implements `IIdentityPermissionFactory`)
            * `System.Security.Policy.StrongNameMembershipCondition` (constructor requires `StrongNamePublicKeyBlob`)
            * `System.Security.Policy.ApplicationTrust(PermissionSet, IEnumerable<StrongName>)`
            * `System.Security.Policy.ApplicationTrust.FullTrustAssemblies` (property of type `IList<StrongName>`)
            * `System.Security.Policy.GacInstalled` (implements `IIdentityPermissionFactory`)
            * `System.Security.SecurityManager.GetStandardSandbox(Evidence)` (returns type `PermissionSet`)
            * `System.Security.Policy.PolicyStatement` (obsoleting the constructors -- they both require `PermissionSet`)
            * `System.Security.Policy.PolicyLevel.AddNamedPermissionSet(NamedPermissionSet)`
            * `System.Security.Policy.PolicyLevel.ChangeNamedPermissionSet(string, PermissionSet)`
            * `System.Security.Policy.PolicyLevel.GetNamedPermissionSet(string)` (returns type `NamedPermissionSet`)
            * `System.Security.Policy.PolicyLevel.RemoveNamedPermissionSet(NamedPermissionSet)` (returns type `NamedPermissionSet`)
            * `System.Security.Policy.PolicyLevel.RemoveNamedPermissionSet(string)` (returns type `NamedPermissionSet`)
            * `System.Security.Policy.Publisher` (implements `IIdentityPermissionFactory`)
            * `System.Security.Policy.Site` (implements `IIdentityPermissionFactory`)
            * `System.Security.Policy.Url` (implements `IIdentityPermissionFactory`)
            * `System.Security.Policy.Zone` (implements `IIdentityPermissionFactory`)
1. The Constrained Execution Region (CER) feature is not supported.
    * DiagnosticId: `SYSLIB0004`
    * APIs:
        * `System.Runtime.ConstrainedExecution.PrePrepareMethodAttribute`
        * `System.Runtime.ConstrainedExecution.ReliabilityContractAttribute`
        * `System.Runtime.ConstrainedExecution.Cer`
        * `System.Runtime.ConstrainedExecution.Consistency`
        * `System.Runtime.CompilerServices.RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup`
        * `System.Runtime.CompilerServices.RuntimeHelpers.PrepareConstrainedRegions`
        * `System.Runtime.CompilerServices.RuntimeHelpers.PrepareConstrainedRegionsNoOP`
        * `System.Runtime.CompilerServices.RuntimeHelpers.PrepareContractedDelegate`
        * `System.Runtime.CompilerServices.RuntimeHelpers.ProbeForSufficientStack`
1. The Global Assembly Cache is not supported.
    * DiagnosticId: `SYSLIB0005`
    * APIs:
        * `System.Reflection.Assembly.GlobalAssemblyCache`
1. Thread.Abort is not supported and throws PlatformNotSupportedException.
    * DiagnosticId: `SYSLIB0006`
    * APIs:
        * `System.Threading.Thread.Abort()`
        * `System.Threading.Thread.Abort(Object)`
1. The default implementation of this cryptography algorithm is not supported.
    * DiagnosticId: `SYSLIB0007`
    * APIs:
        * `System.Security.Cryptography.SymmetricAlgorithm.Create()`
        * `System.Security.Cryptography.AssymetricAlgorithm.Create()`
        * `System.Security.Cryptography.HMAC.Create()`
        * `System.Security.Cryptography.KeyedHashAlgorithm.Create()`
1. The CreatePdbGenerator API is not supported and throws PlatformNotSupportedException.
    * DiagnosticId: `SYSLIB0008`
    * APIs:
        * `System.Runtime.CompilerServices.DebugInfoGenerator.CreatePdbGenerator`
1. The AuthenticationManager Authenticate and PreAuthenticate methods are not supported and throw PlatformNotSupportedException.
    * DiagnosticId: `SYSLIB0009`
    * APIs:
        * `System.Net.AuthenticationManager.Authenticate`
        * `System.Net.AuthenticationManager.PreAuthenticate`
1. This Remoting API is not supported and throws PlatformNotSupportedException.
    * DiagnosticId: `SYSLIB0010`
    * APIs:
        * `System.MarshalByRefObject.GetLifetimeService`
        * `System.MarshalByRefObject.InitializeLifetimeService`

## Other Considerations

Several other APIs were considered for obsoletion in .NET 5, but we chose to focus
on groupings of APIs that categorically do not work in .NET 5. This strategy optimizes
for projects migrating from .NET Framework while keeping the risk of negative disruption
relatively low.

Some specific APIs had already been suggested for obsoletion. We reviewed those suggestions
during this consideration as well. For example, [`SecureString`](https://github.com/dotnet/runtime/issues/30612)
was considered but we chose to omit it for now. We are aware of some notable usage of
`SecureString` where marking it as obsolete would cause disruption, and we do not yet
have a clear alternative approach to recommend.

Obsoleting the set of APIs above will still provide enough usage of the new obsoletion
behavior that we can collect feedback from users and determine how aggressively we can
obsolete more APIs in .NET 6.

We would also like to thank @Joe4evr for helping kickstart the conversation with
[#33360](https://github.com/dotnet/runtime/issues/33360). That issue will remain open
and we will continue consider the APIs noted there in future releases.

## Warnings vs. Errors

The `Obsolete` attributes will be applied to classes, interfaces, and enums, in addition to
being applied to specific members where appropriate. When applying `Obsolete` attributes to
types, we do not support using the **error** level. This is due to how type forwarding is
set up for our downlevel .NET Framework and .NET Standard compatibility, where type forwarding
files would be referencing those types and build errors would be unavoidable.

When an **error** level is needed, a separate diagnostic ID must be used, and the attribute
must be applied to type members. The `PrincipalPermissionAttribute` constructor is obsoleted
as an **error** using `SYSLIB0002`, while the class itself is obsoleted as a **warning** using
`SYSLIB0003`. This combination allows the interfaces the class implements to also be marked
as `Obsolete` while also avoiding the type forwarding build errors.
