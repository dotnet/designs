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

1. The Constrained Execution Region (CER) feature is no longer implemented
    * DiagnosticId: `MSLIB0004`
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
1. The Global Assembly Cache is no longer implemented
    * DiagnosticId: `MSLIB0005`
    * APIs:
        * `System.Reflection.Assembly.GlobalAssemblyCache`
1. `Thread.Abort` is no longer supported and throws PlatformNotSupportedException
    * DiagnosticId: `MSLIB0006`
    * APIs:
        * `System.Threading.Thread.Abort()`
        * `System.Threading.Thread.Abort(Object)`
1. The default implementations of these cryptography algorithms are no longer supported and throw PlatformNotSupportedException
    * DiagnosticId: `MSLIB0007`
    * APIs:
        * `System.Security.Cryptography.SymmetricAlgorithm.Create()`
        * `System.Security.Cryptography.AssymetricAlgorithm.Create()`
        * `System.Security.Cryptography.HMAC.Create()`
        * `System.Security.Cryptography.KeyedHashAlgorithm.Create()`
1. The CreatePdbGenerator API is no longer supported and throws PlatformNotSupportedException
    * DiagnosticId: `MSLIB0008`
    * APIs:
        * `System.Runtime.CompilerServices.DebugInfoGenerator.CreatePdbGenerator`
1. The AuthenticationManager Authenticate and PreAuthenticate APIs are no longer supported and throw PlatformNotSupportedException
    * DiagnosticId: `MSLIB0009`
    * APIs:
        * `System.Net.AuthenticationManager.Authenticate`
        * `System.Net.AuthenticationManager.PreAuthenticate`
1. These Remoting APIs are no longer supported and throw PlatformNotSupportedException
    * DiagnosticId: `MSLIB0010`
    * APIs:
        * `System.MarshalByRefObject.GetLifetimeService`
        * `System.MarshalByRefObject.InitializeLifetimeService`
1. Code Access Security (CAS) APIs are no longer supported
    * DiagnosticId: `MSLIB0002` (reusing the existing DiagnosticId from `PrincipalPermissionAttribute`)
    * APIs:
        * Classes: (everything from the `System.Security.Permissions` namespace)
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
            * `MediaPermission`
            * `MediaPermissionAttribute`
            * `PermissionSetAttribute`
            * `PrincipalPermission`
            * `PrincipalPermissionAttribute` (already has the attribute)
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
            * `System.Security.Permissions.DataProtectionPermissionAttribute`
            * `System.Security.Permissions.EnvironmentPermissionAttribute`
            * `System.Security.Permissions.FileDialogPermissionAttribute`
            * `System.Security.Permissions.FileIOPermissionAttribute`
            * `System.Security.Permissions.GacIdentityPermissionAttribute`
            * `System.Security.Permissions.HostProtectionAttribute`
            * `System.Security.Permissions.IsolatedStoragePermissionAttribute`
            * `System.Security.Permissions.KeyContainerPermissionAttribute`
            * `System.Security.Permissions.MediaPermissionAttribute`
            * `System.Security.Permissions.PermissionSetAttribute`
            * `System.Security.Permissions.PrincipalPermissionAttribute`
            * `System.Security.Permissions.PublisherIdentityPermissionAttribute`
            * `System.Security.Permissions.ReflectionPermissionAttribute`
            * `System.Security.Permissions.RegistryPermissionAttribute`
            * `System.Security.Permissions.SecurityPermissionAttribute`
            * `System.Security.Permissions.SiteIdentityPermissionAttribute`
            * `System.Security.Permissions.StorePermissionAttribute`
            * `System.Security.Permissions.StrongNameIdentityPermissionAttribute`
            * `System.Security.Permissions.TypeDescriptorPermissionAttribute`
            * `System.Security.Permissions.UIPermissionAttribute`
            * `System.Security.Permissions.UrlIdentityPermissionAttribute`
            * `System.Security.Permissions.WebBrowserPermissionAttribute`
            * `System.Security.Permissions.ZoneIdentityPermissionAttribute`
            * `System.ServiceProcess.ServiceControllerPermissionAttribute`
            * `System.Transactions.DistributedTransactionPermissionAttribute`
            * `System.Web.AspNetHostingPermissionAttribute`
        * Interfaces:
            * `System.Security.PermissionsIUnrestrictedPermission`
            * `System.Security.IPermission`
            * `System.Security.IStackWalk`
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
