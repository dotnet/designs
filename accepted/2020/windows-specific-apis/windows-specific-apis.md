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

Thus, I propose that we use `7.0` as the minimum version number instead of
hunting down which Windows version actually introduced a given feature, which
means the custom attribute will look as follows:

```C#
[MinimumOSPlatform("windows7.0")]
```

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

### System.Diagnostics

* **Process**
    - `EnterDebugMode()`
    - `LeaveDebugMode()`
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
    - `Connect(Int32)`
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
    - `SetIPProtectionLevel(IPProtectionLevel)`
    - `DuplicateAndClose`
* **TcpListener**
    - `AllowNatTraversal(Boolean)`
* **TransmitFileOptions**
    - `UseKernelApc`
    - `UseSystemThread`
    - `WriteBehind`
* **UdpClient**
    - `AllowNatTraversal(Boolean)`

### System.Runtime.InteropServices

* **DispatchWrapper**
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
