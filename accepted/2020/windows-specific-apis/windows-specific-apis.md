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

## Entire assemblies

There is a set of assemblies that we can mark wholesale as being
Windows-specific:

> **OPEN**: Should `System.Threading.Overlapped.dll` be removed?
> **OPEN**: I was told that part of `DirectoryServices` is now cross-platform. Is that true?

* **Microsoft.Win32.Registry.dll**
* **Microsoft.Win32.Registry.AccessControl.dll**
* **Microsoft.Win32.SystemEvents.dll**
* **System.Diagnostics.EventLog.dll**
* **System.Diagnostics.PerformanceCounter.dll**
* **System.DirectoryServices.dll**
* **System.DirectoryServices.AccountManagement.dll**
* **System.DirectoryServices.Protocols.dll**
* **System.IO.FileSystem.AccessControl.dll**
* **System.IO.FileSystem.DriveInfo.dll**
* **System.IO.Pipes.AccessControl.dll**
* **System.Management.dll**
* **System.Net.Http.WinHttpHandler.dll**
* **System.Runtime.InteropServices.WindowsRuntime.dll**
* **System.Security.AccessControl.dll**
* **System.Security.Cryptography.Cng.dll**
* **System.Security.Cryptography.ProtectedData.dll**
* **System.Security.Principal.Windows.dll**
* **System.ServiceProcess.ServiceController.dll**
* **System.Threading.AccessControl.dll**
* **System.Threading.Overlapped.dll**

## Specific APIs

### System

> **OPEN** Should we treat those as Windows-only or should we consider them as
> partially portable?

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

> **OPEN** Should we mark the entire type?

* **DpapiProtectedConfigurationProvider**
    - `Decrypt(XmlNode)`
    - `Encrypt(XmlNode)`

### System.Diagnostics

* **Process**
    - `GetProcessById(Int32, String)`
    - `GetProcesses(String)`
    - `GetProcessesByName(String, String)`
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

### System.Drawing

> **OPEN** Should we mark the entire type?

* **Graphics**
    - `CopyFromScreen(Int32, Int32, Int32, Int32, Size)`
    - `CopyFromScreen(Int32, Int32, Int32, Int32, Size, CopyPixelOperation)`
    - `CopyFromScreen(Point, Point, Size)`
    - `CopyFromScreen(Point, Point, Size, CopyPixelOperation)`

### System.IO.MemoryMappedFiles

* **MemoryMappedFile**
    - `CreateFromFile(FileStream, String, Int64, MemoryMappedFileAccess, HandleInheritability, Boolean)`
    - `CreateFromFile(String)`
    - `CreateFromFile(String, FileMode)`
    - `CreateFromFile(String, FileMode, String)`
    - `CreateFromFile(String, FileMode, String, Int64)`
    - `CreateFromFile(String, FileMode, String, Int64, MemoryMappedFileAccess)`
    - `CreateNew(String, Int64)`
    - `CreateNew(String, Int64, MemoryMappedFileAccess)`
    - `CreateNew(String, Int64, MemoryMappedFileAccess, MemoryMappedFileOptions, HandleInheritability)`
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
    - `RunAsClient(PipeStreamImpersonationWorker)`
* **PipeStream**
    - `set_ReadMode(PipeTransmissionMode)`
    - `WaitForPipeDrain()`
* **PipeTransmissionMode**
    - `Message`

### System.IO.Ports

> **OPEN** Should we mark the entire type/assembly?

* **SerialDataReceivedEventArgs**
    - `get_EventType()`
* **SerialErrorReceivedEventArgs**
    - `get_EventType()`
* **SerialPinChangedEventArgs**
    - `get_EventType()`
* **SerialPort**
    - `.ctor()`
    - `.ctor(IContainer)`
    - `.ctor(String)`
    - `.ctor(String, Int32)`
    - `.ctor(String, Int32, Parity)`
    - `.ctor(String, Int32, Parity, Int32)`
    - `.ctor(String, Int32, Parity, Int32, StopBits)`
    - `Close()`
    - `DiscardInBuffer()`
    - `DiscardOutBuffer()`
    - `get_BaseStream()`
    - `get_BaudRate()`
    - `get_BreakState()`
    - `get_BytesToRead()`
    - `get_BytesToWrite()`
    - `get_CDHolding()`
    - `get_CtsHolding()`
    - `get_DataBits()`
    - `get_DiscardNull()`
    - `get_DsrHolding()`
    - `get_DtrEnable()`
    - `get_Encoding()`
    - `get_Handshake()`
    - `get_IsOpen()`
    - `get_NewLine()`
    - `get_Parity()`
    - `get_ParityReplace()`
    - `get_PortName()`
    - `get_ReadBufferSize()`
    - `get_ReadTimeout()`
    - `get_ReceivedBytesThreshold()`
    - `get_RtsEnable()`
    - `get_StopBits()`
    - `get_WriteBufferSize()`
    - `get_WriteTimeout()`
    - `GetPortNames()`
    - `Open()`
    - `Read(Byte[], Int32, Int32)`
    - `Read(Char[], Int32, Int32)`
    - `ReadByte()`
    - `ReadChar()`
    - `ReadExisting()`
    - `ReadLine()`
    - `ReadTo(String)`
    - `set_BaudRate(Int32)`
    - `set_BreakState(Boolean)`
    - `set_DataBits(Int32)`
    - `set_DiscardNull(Boolean)`
    - `set_DtrEnable(Boolean)`
    - `set_Encoding(Encoding)`
    - `set_Handshake(Handshake)`
    - `set_NewLine(String)`
    - `set_Parity(Parity)`
    - `set_ParityReplace(Byte)`
    - `set_PortName(String)`
    - `set_ReadBufferSize(Int32)`
    - `set_ReadTimeout(Int32)`
    - `set_ReceivedBytesThreshold(Int32)`
    - `set_RtsEnable(Boolean)`
    - `set_StopBits(StopBits)`
    - `set_WriteBufferSize(Int32)`
    - `set_WriteTimeout(Int32)`
    - `Write(Byte[], Int32, Int32)`
    - `Write(Char[], Int32, Int32)`
    - `Write(String)`
    - `WriteLine(String)`

### System.Net

* **HttpListenerTimeoutManager**
    - `set_EntityBody(TimeSpan)`
    - `set_HeaderWait(TimeSpan)`
    - `set_MinSendBytesPerSecond(Int64)`
    - `set_RequestQueue(TimeSpan)`
* **IPEndPoint**
    - `Create(SocketAddress)`
* **SocketAddress**
    - `.ctor(AddressFamily)`
    - `.ctor(AddressFamily, Int32)`
    - `get_Family()`
    - `ToString()`

### System.Net.Sockets

* **Socket**
    - `AcceptAsync(SocketAsyncEventArgs)`
    - `BeginAccept(Int32, AsyncCallback, Object)`
    - `BeginAccept(Socket, Int32, AsyncCallback, Object)`
    - `BeginReceiveFrom(Byte[], Int32, Int32, SocketFlags, EndPoint, AsyncCallback, Object)`
    - `BeginReceiveMessageFrom(Byte[], Int32, Int32, SocketFlags, EndPoint, AsyncCallback, Object)`
    - `IOControl(Int32, Byte[], Byte[])`
    - `IOControl(IOControlCode, Byte[], Byte[])`
    - `ReceiveFrom(Byte[], EndPoint)`
    - `ReceiveFrom(Byte[], Int32, Int32, SocketFlags, EndPoint)`
    - `ReceiveFrom(Byte[], Int32, SocketFlags, EndPoint)`
    - `ReceiveFrom(Byte[], SocketFlags, EndPoint)`
    - `ReceiveFromAsync(SocketAsyncEventArgs)`
    - `ReceiveMessageFrom(Byte[], Int32, Int32, SocketFlags, EndPoint, IPPacketInformation)`
    - `ReceiveMessageFromAsync(SocketAsyncEventArgs)`
    - `SetIPProtectionLevel(IPProtectionLevel)`
* **SocketTaskExtensions**
    - `AcceptAsync(Socket)`
    - `AcceptAsync(Socket, Socket)`
    - `ReceiveFromAsync(Socket, ArraySegment<Byte>, SocketFlags, EndPoint)`
    - `ReceiveMessageFromAsync(Socket, ArraySegment<Byte>, SocketFlags, EndPoint)`
* **TcpListener**
    - `AllowNatTraversal(Boolean)`
* **TransmitFileOptions**
    - `Disconnect`
    - `ReuseSocket`
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
    - `FinalReleaseComObject(Object)`
    - `GetComInterfaceForObject(Object, Type)`
    - `GetComInterfaceForObject(Object, Type, CustomQueryInterfaceMode)`
    - `GetComInterfaceForObject<T, TInterface>(T)`
    - `GetIUnknownForObject(Object)`
    - `GetNativeVariantForObject(Object, IntPtr)`
    - `GetNativeVariantForObject<T>(T, IntPtr)`
    - `GetObjectForIUnknown(IntPtr)`
    - `GetObjectForNativeVariant(IntPtr)`
    - `GetObjectForNativeVariant<T>(IntPtr)`
    - `GetObjectsForNativeVariants(IntPtr, Int32)`
    - `GetObjectsForNativeVariants<T>(IntPtr, Int32)`
    - `GetStartComSlot(Type)`
    - `GetTypedObjectForIUnknown(IntPtr, Type)`
    - `GetTypeFromCLSID(Guid)`
    - `GetTypeInfoName(ITypeInfo)`
    - `GetUniqueObjectForIUnknown(IntPtr)`
    - `QueryInterface(IntPtr, Guid, IntPtr)`
    - `Release(IntPtr)`
    - `ReleaseComObject(Object)`

### System.Security.Cryptography

* **CspKeyContainerInfo**
    - `get_Accessible()`
    - `get_Exportable()`
    - `get_HardwareDevice()`
    - `get_KeyContainerName()`
    - `get_KeyNumber()`
    - `get_MachineKeyStore()`
    - `get_Protected()`
    - `get_ProviderName()`
    - `get_ProviderType()`
    - `get_RandomlyGenerated()`
    - `get_Removable()`
    - `get_UniqueKeyContainerName()`
* **DSACryptoServiceProvider**
    - `get_CspKeyContainerInfo()`
    - `ImportCspBlob(Byte[])`
* **PasswordDeriveBytes**
    - `CryptDeriveKey(String, String, Int32, Byte[])`
* **RC2CryptoServiceProvider**
    - `set_UseSalt(Boolean)`
* **RSACryptoServiceProvider**
    - `.ctor(CspParameters)`
    - `.ctor(Int32, CspParameters)`
    - `get_CspKeyContainerInfo()`
    - `ImportCspBlob(Byte[])`

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
    - `.ctor(Boolean, EventResetMode, String)`
    - `.ctor(Boolean, EventResetMode, String, Boolean)`
    - `OpenExisting(String)`
    - `TryOpenExisting(String, EventWaitHandle)`
* **Semaphore**
    - `.ctor(Int32, Int32, String)`
    - `.ctor(Int32, Int32, String, Boolean)`
    - `OpenExisting(String)`
    - `TryOpenExisting(String, Semaphore)`
* **Thread**
    - `SetApartmentState(ApartmentState)`
* **WaitHandle**
    - `SignalAndWait(WaitHandle, WaitHandle)`
    - `SignalAndWait(WaitHandle, WaitHandle, Int32, Boolean)`
    - `SignalAndWait(WaitHandle, WaitHandle, TimeSpan, Boolean)`