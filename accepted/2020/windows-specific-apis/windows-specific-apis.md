# Marking Windows-only APIs

As discussed in [dotnet/runtime#33331] we're adding an analyzer to detect usage
of platform-specific APIs. This analyzer requires a custom attribute applied to
method, containing type, or assembly.

In this proposal I'm covering which APIs we mark as Windows-specific an how. It
was largely informed by the work put into [platform-compat].

[dotnet/runtime#33331]: https://github.com/dotnet/runtime/issues/33331
[platform-compat]: https://github.com/dotnet/platform-compat

## Which Windows version will we use?

The lowest version of Windows that we support with .NET Core is Windows 7. Also,
we generally don't expose functionality that requires a higher version of
Windows.

We originally said we'll mark these APIs as `windows7.0` but this would mean
that callers have to call guard these APIs with `7.0` version check too, which
isn't really necessary. But what's worse is that many developers already have
written code that checks for the OS but not version, and due to the version
support that is perfectly correct code.

We decided that it's better for our analyzer to special case version-less checks
and let it be equivalent of a check for `0.0`. We also decided that applying the
attribute without a version is the same as stating the API was introduced in
`0.0`. The net effect is that consumers of the existing Windows-specific APIs
will get away with just checking for the OS.

```C#
[SupportedOSPlatform("windows")]
```

Moving forward, we'll only tag Windows-specific APIs without version if they are
supported by Windows 7 or earlier. APIs requiring newer OS versions will be
marked with the corresponding OS version.

## .NET Standard 2.0 assemblies

Marking APIs as Windows specific requires the `MinimumOSPlatformAttribute`.
Thankfully, this doesn't require unification (nor a public type) so the analyzer
can simply check for the attribute by name. Thus, in order to mark .NET Standard
2.0 assemblies, we'll include an internal version of
`MinimumOSPlatformAttribute` in those assemblies.

## Partially supported APIs

We decided that APIs that are only platform-specific for a certain combinations
of arguments will not be marked as platform-specific. In other words, we'll
optimize for fewer false positives.

In some cases, we get away with marking enum members as platform-specific. If
the API is popular enough, we may want to provide a custom analyzer that checks
for certain arguments.

## Open Issues

* We should run the tool again .NET 5
* We currently don't have a way to mark an API as unsupported on a given
  platform. That is, the design is agnostic (no attribute) or inclusion list
  (attributes with support platforms). We need to add this feature to support
  Blazor.
    - `System.Security.Cryptography.OpenSSL` should be marked as not supported
      on Windows
* We should consider special casing enum values in the analyzer and only
  complain about assignments/passing to parameters). We can probably get away
  with marking the constructors of the types that override with platform
  specific implementations.
* We should consider how marking virtual methods work in the analyzer.
* We need to find a good code base for running this analyzer on as
  dotnet/runtime, dotnet/roslyn are mostly not consuming platform-specific APIs.

## Entire assemblies

There is a set of assemblies that we can mark wholesale as being
Windows-specific:

> **OPEN**: Should `System.Threading.Overlapped.dll` be removed?

* **Microsoft.Win32.Registry.dll**
* **Microsoft.Win32.Registry.AccessControl.dll**
* **Microsoft.Win32.SystemEvents.dll**
* **System.Data.OleDb.dll**
* **System.Diagnostics.EventLog.dll**
* **System.Diagnostics.PerformanceCounter.dll**
* **System.DirectoryServices.dll**
* **System.DirectoryServices.AccountManagement.dll**
* **System.IO.FileSystem.AccessControl.dll**
* **System.IO.Pipes.AccessControl.dll**
* **System.Management.dll**
* **System.Net.Http.WinHttpHandler.dll**
* **System.Security.AccessControl.dll**
* **System.Security.Cryptography.Cng.dll**
* **System.Security.Cryptography.ProtectedData.dll**
* **System.Security.Principal.Windows.dll**
* **System.ServiceProcess.ServiceController.dll**
* **System.Threading.AccessControl.dll**
* **System.Threading.Overlapped.dll**
* **System.Windows.Extensions.dll**

## Specific APIs

### System

* **Console**
    - `Beep(Int32, Int32)`
    - `get_CapsLock()`
    - `get_CursorVisible()`
    - `get_NumberLock()`
    - `get_Title()`
    - `MoveBufferArea(Int32, Int32, Int32, Int32, Int32, Int32)`
    - `MoveBufferArea(Int32, Int32, Int32, Int32, Int32, Int32, Char, ConsoleColor, ConsoleColor)`
    - `set_BufferHeight(Int32)`
    - `set_BufferWidth(Int32)`
    - `set_CursorSize(Int32)`
    - `set_WindowHeight(Int32)`
    - `set_WindowLeft(Int32)`
    - `set_WindowTop(Int32)`
    - `set_WindowWidth(Int32)`
    - `SetBufferSize(Int32, Int32)`
    - `SetWindowPosition(Int32, Int32)`
    - `SetWindowSize(Int32, Int32)`

### System.Configuration

* **DpapiProtectedConfigurationProvider**

### System.Data.SqlTypes

* **SqlFileStream**

### System.Diagnostics

* **Process**
    - `set_MaxWorkingSet(IntPtr)`
    - `set_MinWorkingSet(IntPtr)`
    - `Start(String, String, SecureString, String)`
    - `Start(String, String, String, SecureString, String)`
* **ProcessStartInfo**
    - `get_Domain()`
    - `get_LoadUserProfile()`
    - `get_Password()`
    - `get_PasswordInClearText()`
    - `set_Domain(String)`
    - `set_LoadUserProfile(Boolean)`
    - `set_Password(SecureString)`
    - `set_PasswordInClearText(String)`
* **ProcessThread**
    - `set_PriorityLevel(ThreadPriorityLevel)`
    - `set_ProcessorAffinity(IntPtr)`

### System.IO

* **DriveInfo**
    - `set_VolumeLabel(String)`
* **File**
    - `Decrypt(String)`
    - `Encrypt(String)`
* **FileInfo**
    - `Decrypt()`
    - `Encrypt()`

### System.IO.MemoryMappedFiles

* **MemoryMappedFile**
    - `CreateOrOpen(String, Int64)`
    - `CreateOrOpen(String, Int64, MemoryMappedFileAccess)`
    - `CreateOrOpen(String, Int64, MemoryMappedFileAccess, MemoryMappedFileOptions, HandleInheritability)`
    - `OpenExisting(String)`
    - `OpenExisting(String, MemoryMappedFileRights)`
    - `OpenExisting(String, MemoryMappedFileRights, HandleInheritability)`

### System.IO.Pipes

* **NamedPipeClientStream**
    - `get_NumberOfServerInstances()`
* **NamedPipeServerStream**
    - `WaitForConnection()`
* **PipeStream**
    - `WaitForPipeDrain()`
* **PipeTransmissionMode**
    - `Message`

### System.Net

* **HttpListenerTimeoutManager**
    - `set_EntityBody(TimeSpan)`
    - `set_HeaderWait(TimeSpan)`
    - `set_MinSendBytesPerSecond(Int64)`
    - `set_RequestQueue(TimeSpan)`

### System.Net.Sockets

* **IOControlCode** (Enum -- _all members except `NonBlockingIO`, `DataToRead`, and `OobDataRead`_)
    - `AsyncIO`
    - `AssociateHandle`
    - `EnableCircularQueuing`
    - `Flush`
    - `GetBroadcastAddress`
    - `GetExtensionFunctionPointer`
    - `GetQos`
    - `GetGroupQos`
    - `MultipointLoopback`
    - `MulticastScope`
    - `SetQos`
    - `SetGroupQos`
    - `TranslateHandle`
    - `RoutingInterfaceQuery`
    - `RoutingInterfaceChange`
    - `AddressListQuery`
    - `AddressListChange`
    - `QueryTargetPnpHandle`
    - `NamespaceChange`
    - `AddressListSort`
    - `ReceiveAll`
    - `ReceiveAllMulticast`
    - `ReceiveAllIgmpMulticast`
    - `KeepAliveValues`
    - `AbsorbRouterAlert`
    - `UnicastInterface`
    - `LimitBroadcasts`
    - `BindToInterface`
    - `MulticastInterface`
    - `AddMulticastGroupOnInterface`
    - `DeleteMulticastGroupFromInterface`
* **Socket**
    - `.ctor(SocketInformation)`
    - `SetIPProtectionLevel(IPProtectionLevel)`
    - `DuplicateAndClose(Int32)`
* **TcpListener**
    - `AllowNatTraversal(Boolean)`
* **TransmitFileOptions**
    - `UseKernelApc`
    - `UseSystemThread`
    - `WriteBehind`
* **UdpClient**
    - `AllowNatTraversal(Boolean)`

### System.Runtime.InteropServices

* **ComAwareEventInfo**
    - `AddEventHandler(Object, Delegate)`
    - `RemoveEventHandler(Object, Delegate)`
* **ComEventsHelper**
    - `Combine(Object, Guid, Int32, Delegate)`
    - `Remove(Object, Guid, Int32, Delegate)`
* **DispatchWrapper**
    - `.ctor(Object)`
    - `get_WrappedObject()`
* **Marshal**
    - `AddRef(IntPtr)`
    - `BindToMoniker(String)`
    - `ChangeWrapperHandleStrength(Object, Boolean)`
    - `CreateAggregatedObject(IntPtr, Object)`
    - `CreateAggregatedObject<T>(IntPtr, T)`
    - `CreateWrapperOfType(object, Type)`
    - `CreateWrapperOfType<T, TWrapper>(T)`
    - `FinalReleaseComObject(Object)`
    - `GetComInterfaceForObject(Object, Type)`
    - `GetComInterfaceForObject(Object, Type, CustomQueryInterfaceMode)`
    - `GetComInterfaceForObject<T, TInterface>(T)`
    - `GetComObjectData(object,object)`
    - `GetIDispatchForObject(object)`
    - `GetIUnknownForObject(Object)`
    - `GetNativeVariantForObject(Object, IntPtr)`
    - `GetNativeVariantForObject<T>(T, IntPtr)`
    - `GetObjectForIUnknown(IntPtr)`
    - `GetObjectForNativeVariant(IntPtr)`
    - `GetObjectForNativeVariant<T>(IntPtr)`
    - `GetObjectsForNativeVariants(IntPtr, Int32)`
    - `GetObjectsForNativeVariants<T>(IntPtr, Int32)`
    - `GetStartComSlot(Type)`
    - `GetEndComSlot(Type)`
    - `GetTypedObjectForIUnknown(IntPtr, Type)`
    - `GetTypeFromCLSID(Guid)`
    - `GetTypeInfoName(ITypeInfo)`
    - `GetUniqueObjectForIUnknown(IntPtr)`
    - `QueryInterface(IntPtr, Guid, IntPtr)`
    - `Release(IntPtr)`
    - `ReleaseComObject(Object)`
    - `ReleaseComObject(Object)`
    - `SetComObjectData(object,object,object)`
* **ComWrappers+ComInterfaceDispatch**
    - `GetInstance<T>(ComInterfaceDispatch*)`
* **ComWrappers**
    - `GetIUnknownImpl(IntPtr, IntPtr, IntPtr)`
    - `GetOrCreateComInterfaceForObject(Object, CreateComInterfaceFlags)`
    - `GetOrCreateObjectForComInstance(IntPtr, CreateObjectFlags)`
    - `GetOrRegisterObjectForComInstance(IntPtr, CreateObjectFlags, Object)`
    - `RegisterForMarshalling(ComWrappers)`
    - `RegisterForTrackerSupport(ComWrappers)`

### System.Security.Cryptography

* **CspKeyContainerInfo**
* **DSACryptoServiceProvider**
    - `.ctor(CspParameters)`
    - `.ctor(int, CspParameters)`
    - `get_CspKeyContainerInfo()`
* **PasswordDeriveBytes**
    - `CryptDeriveKey(String, String, Int32, Byte[])`
* **RC2CryptoServiceProvider**
    - `set_UseSalt(Boolean)`
* **RSACryptoServiceProvider**
    - `.ctor(CspParameters)`
    - `.ctor(Int32, CspParameters)`
    - `get_CspKeyContainerInfo()`
* **`CspParameters`**

### System.Security.Cryptography.X509Certificates

* **X509Chain**
    - `.ctor(IntPtr)`
* **X509Certificate2UI**

### System.ServiceModel

* **ActionNotSupportedException**
    - `.ctor(SerializationInfo, StreamingContext)`
    - `.ctor(String, Exception)`
* **BasicHttpBinding**
    - `.ctor()`
    - `.ctor(BasicHttpSecurityMode)`
    - `CreateBindingElements()`
* **BasicHttpsBinding**
    - `.ctor()`
    - `.ctor(BasicHttpsSecurityMode)`
    - `CreateBindingElements()`
* **ChannelFactory**
    - `.ctor(Binding, EndpointAddress)`
    - `.ctor(ServiceEndpoint)`
    - `.ctor(String)`
    - `.ctor(String, EndpointAddress)`
    - `ApplyConfiguration(String)`
    - `CreateDescription()`
    - `InitializeEndpoint(Binding, EndpointAddress)`
    - `InitializeEndpoint(ServiceEndpoint)`
    - `InitializeEndpoint(String, EndpointAddress)`
* **ChannelTerminatedException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **ClientBase**
    - `.ctor()`
    - `.ctor(String)`
    - `.ctor(String, EndpointAddress)`
    - `.ctor(String, String)`
* **ClientBase+ChannelBase**
    - `EndInvoke(String, Object[], IAsyncResult)`
* **ClientCredentialsSecurityTokenManager**
    - `CreateSecurityTokenAuthenticator(SecurityTokenRequirement, SecurityTokenResolver)`
    - `CreateSecurityTokenProvider(SecurityTokenRequirement)`
    - `CreateSecurityTokenSerializer(SecurityTokenVersion)`
* **CommunicationException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **CommunicationObjectAbortedException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **CommunicationObjectFaultedException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **DuplexChannelFactory**
    - `.ctor(InstanceContext, Binding, EndpointAddress)`
    - `.ctor(InstanceContext, Binding, String)`
    - `.ctor(InstanceContext, String)`
    - `.ctor(InstanceContext, String, EndpointAddress)`
* **DuplexClientBase**
    - `.ctor(InstanceContext)`
    - `.ctor(InstanceContext, String)`
    - `.ctor(InstanceContext, String, EndpointAddress)`
    - `.ctor(InstanceContext, String, String)`
* **EndpointAddress**
    - `ApplyTo(Message)`
* **EndpointIdentity**
    - `Equals(Object)`
    - `GetHashCode()`
* **EndpointNotFoundException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **FaultException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **InvalidMessageContractException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **MessageHeaderException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **MessageSecurityOverTcp**
    - `get_ClientCredentialType()`
    - `set_ClientCredentialType(MessageCredentialType)`
* **NetHttpBinding**
    - `CreateBindingElements()`
* **NetHttpsBinding**
    - `.ctor()`
    - `.ctor(BasicHttpsSecurityMode)`
    - `CreateBindingElements()`
* **NetTcpBinding**
    - `.ctor(String)`
    - `CreateBindingElements()`
* **ProtocolException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **QuotaExceededException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **ServerTooBusyException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **ServiceActivationException**
    - `.ctor(SerializationInfo, StreamingContext)`

### System.ServiceModel.Channels

* **AddressHeader**
    - `Equals(Object)`
    - `GetHashCode()`
* **MessageHeaders**
    - `set_To(Uri)`
* **SecurityBindingElement**
    - `CreateSecureConversationBindingElement(SecurityBindingElement)`
* **TransportSecurityBindingElement**
    - `GetProperty<T>(BindingContext)`
* **WebSocketTransportSettings**
    - `get_DisablePayloadMasking()`
    - `set_DisablePayloadMasking(Boolean)`

### System.ServiceModel.Security

* **MessageSecurityException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **SecurityAccessDeniedException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **SecurityNegotiationException**
    - `.ctor(SerializationInfo, StreamingContext)`
* **X509ServiceCertificateAuthentication**
    - `set_CertificateValidationMode(X509CertificateValidationMode)`

### System.Threading

* **EventWaitHandle**
    - `OpenExisting(String)`
    - `TryOpenExisting(String, EventWaitHandle)`
* **Semaphore**
    - `OpenExisting(String)`
    - `TryOpenExisting(String, Semaphore)`
* **Thread**
    - `SetApartmentState(ApartmentState)`

### System.Xaml.Permissions

* **XamlAccessLevel**
    - `AssemblyAccessTo(Assembly)`
    - `AssemblyAccessTo(AssemblyName)`
    - `get_AssemblyAccessToAssemblyName()`
    - `get_PrivateAccessToTypeName()`
    - `PrivateAccessTo(String)`
    - `PrivateAccessTo(Type)`

### Microsoft.VisualBasic

* **DateAndTime**
    - `set_DateString(String)`
    - `set_TimeOfDay(DateTime)`
    - `set_TimeString(String)`
    - `set_Today(DateTime)`
* **FileSystem**
    - `ChDrive(Char)`
    - `ChDrive(String)`
    - `CurDir(Char)`
    - `Dir(String, FileAttribute)`
    - `Rename(String, String)`
* **Interaction**
    - `Beep()`
    - `DeleteSetting(String, String, String)`
    - `GetAllSettings(String, String)`
    - `GetObject(String, String)`
    - `GetSetting(String, String, String, String)`
    - `SaveSetting(String, String, String, String)`
* **Strings**
    - `StrConv(String, VbStrConv, Int32)`

### Microsoft.VisualBasic.FileIO

* **FileSystem**
    - `CopyDirectory(String, String)`
    - `CopyDirectory(String, String, UIOption)`
    - `CopyDirectory(String, String, UIOption, UICancelOption)`
    - `CopyDirectory(String, String, Boolean)`
    - `CopyFile(String, String)`
    - `CopyFile(String, String, UIOption)`
    - `CopyFile(String, String, UIOption, UICancelOption)`
    - `CopyFile(String, String, Boolean)`
    - `DeleteDirectory(String, DeleteDirectoryOption)`
    - `DeleteDirectory(String, UIOption, RecycleOption)`
    - `DeleteDirectory(String, UIOption, RecycleOption, UICancelOption)`
    - `DeleteFile(String)`
    - `DeleteFile(String, UIOption, RecycleOption)`
    - `DeleteFile(String, UIOption, RecycleOption, UICancelOption)`
    - `MoveDirectory(String, String)`
    - `MoveDirectory(String, String, UIOption)`
    - `MoveDirectory(String, String, UIOption, UICancelOption)`
    - `MoveDirectory(String, String, Boolean)`
    - `MoveFile(String, String)`
    - `MoveFile(String, String, UIOption)`
    - `MoveFile(String, String, UIOption, UICancelOption)`
    - `MoveFile(String, String, Boolean)`
