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

### System.Data.SqlTypes

* **SqlFileStream**
    - `Flush()`
    - `get_Name()`
    - `Read(Byte[], Int32, Int32)`
    - `Write(Byte[], Int32, Int32)`
    - `.ctor(String, Byte[], FileAccess)`
    - `.ctor(String, Byte[], FileAccess, FileOptions, Int64)`
    - `get_CanRead()`
    - `get_CanSeek()`
    - `get_CanWrite()`
    - `get_Length()`
    - `get_Position()`
    - `get_TransactionContext()`
    - `Seek(Int64, SeekOrigin)`
    - `set_Position(Int64)`
    - `SetLength(Int64)`

### System.Data.OleDb

* **OleDbCommand**
    - `.ctor()`
    - `.ctor(String)`
    - `Cancel()`
    - `Clone()`
    - `get_Parameters()`
    - `System.ICloneable.Clone()`
    - `.ctor(String, OleDbConnection)`
    - `.ctor(String, OleDbConnection, OleDbTransaction)`
    - `CreateParameter()`
    - `ExecuteNonQuery()`
    - `ExecuteReader()`
    - `ExecuteReader(CommandBehavior)`
    - `ExecuteScalar()`
    - `get_CommandText()`
    - `get_CommandTimeout()`
    - `get_CommandType()`
    - `get_Connection()`
    - `get_DesignTimeVisible()`
    - `get_Transaction()`
    - `get_UpdatedRowSource()`
    - `Prepare()`
    - `ResetCommandTimeout()`
    - `set_CommandText(String)`
    - `set_CommandTimeout(Int32)`
    - `set_CommandType(CommandType)`
    - `set_Connection(OleDbConnection)`
    - `set_DesignTimeVisible(Boolean)`
    - `set_Transaction(OleDbTransaction)`
    - `set_UpdatedRowSource(UpdateRowSource)`
    - `System.Data.IDbCommand.ExecuteReader()`
    - `System.Data.IDbCommand.ExecuteReader(CommandBehavior)`
* **OleDbCommandBuilder**
    - `.ctor()`
    - `.ctor(OleDbDataAdapter)`
    - `DeriveParameters(OleDbCommand)`
    - `get_DataAdapter()`
    - `GetDeleteCommand()`
    - `GetDeleteCommand(Boolean)`
    - `GetInsertCommand()`
    - `GetInsertCommand(Boolean)`
    - `GetUpdateCommand()`
    - `GetUpdateCommand(Boolean)`
    - `QuoteIdentifier(String)`
    - `QuoteIdentifier(String, OleDbConnection)`
    - `set_DataAdapter(OleDbDataAdapter)`
    - `UnquoteIdentifier(String)`
    - `UnquoteIdentifier(String, OleDbConnection)`
* **OleDbConnection**
    - `.ctor()`
    - `.ctor(String)`
    - `Close()`
    - `get_Provider()`
    - `Open()`
    - `System.ICloneable.Clone()`
    - `BeginTransaction()`
    - `BeginTransaction(IsolationLevel)`
    - `ChangeDatabase(String)`
    - `CreateCommand()`
    - `EnlistTransaction(Transaction)`
    - `get_ConnectionString()`
    - `get_ConnectionTimeout()`
    - `get_Database()`
    - `get_DataSource()`
    - `get_ServerVersion()`
    - `get_State()`
    - `GetOleDbSchemaTable(Guid, Object[])`
    - `GetSchema()`
    - `GetSchema(String)`
    - `GetSchema(String, String[])`
    - `ReleaseObjectPool()`
    - `ResetState()`
    - `set_ConnectionString(String)`
* **OleDbConnectionStringBuilder**
    - `.ctor()`
    - `.ctor(String)`
    - `Clear()`
    - `get_Item(String)`
    - `get_Keys()`
    - `get_Provider()`
    - `Remove(String)`
    - `set_Item(String, Object)`
    - `get_DataSource()`
    - `ContainsKey(String)`
    - `get_FileName()`
    - `get_OleDbServices()`
    - `get_PersistSecurityInfo()`
    - `set_DataSource(String)`
    - `set_FileName(String)`
    - `set_OleDbServices(Int32)`
    - `set_PersistSecurityInfo(Boolean)`
    - `set_Provider(String)`
    - `TryGetValue(String, Object)`
* **OleDbDataAdapter**
    - `.ctor()`
    - `.ctor(String, String)`
    - `System.ICloneable.Clone()`
    - `.ctor(String, OleDbConnection)`
    - `.ctor(OleDbCommand)`
    - `Fill(DataSet, Object, String)`
    - `Fill(DataTable, Object)`
    - `get_DeleteCommand()`
    - `get_InsertCommand()`
    - `get_SelectCommand()`
    - `get_UpdateCommand()`
    - `set_DeleteCommand(OleDbCommand)`
    - `set_InsertCommand(OleDbCommand)`
    - `set_SelectCommand(OleDbCommand)`
    - `set_UpdateCommand(OleDbCommand)`
    - `System.Data.IDbDataAdapter.get_DeleteCommand()`
    - `System.Data.IDbDataAdapter.get_InsertCommand()`
    - `System.Data.IDbDataAdapter.get_SelectCommand()`
    - `System.Data.IDbDataAdapter.get_UpdateCommand()`
    - `System.Data.IDbDataAdapter.set_DeleteCommand(IDbCommand)`
    - `System.Data.IDbDataAdapter.set_InsertCommand(IDbCommand)`
    - `System.Data.IDbDataAdapter.set_SelectCommand(IDbCommand)`
    - `System.Data.IDbDataAdapter.set_UpdateCommand(IDbCommand)`
* **OleDbDataReader**
    - `Close()`
    - `get_Item(Int32)`
    - `get_Item(String)`
    - `GetEnumerator()`
    - `get_Depth()`
    - `get_FieldCount()`
    - `get_HasRows()`
    - `get_IsClosed()`
    - `get_RecordsAffected()`
    - `get_VisibleFieldCount()`
    - `GetBoolean(Int32)`
    - `GetByte(Int32)`
    - `GetBytes(Int32, Int64, Byte[], Int32, Int32)`
    - `GetChar(Int32)`
    - `GetChars(Int32, Int64, Char[], Int32, Int32)`
    - `GetData(Int32)`
    - `GetDataTypeName(Int32)`
    - `GetDateTime(Int32)`
    - `GetDecimal(Int32)`
    - `GetDouble(Int32)`
    - `GetFieldType(Int32)`
    - `GetFloat(Int32)`
    - `GetGuid(Int32)`
    - `GetInt16(Int32)`
    - `GetInt32(Int32)`
    - `GetInt64(Int32)`
    - `GetName(Int32)`
    - `GetOrdinal(String)`
    - `GetSchemaTable()`
    - `GetString(Int32)`
    - `GetTimeSpan(Int32)`
    - `GetValue(Int32)`
    - `GetValues(Object[])`
    - `IsDBNull(Int32)`
    - `NextResult()`
    - `Read()`
* **OleDbEnumerator**
    - `.ctor()`
    - `GetElements()`
    - `GetEnumerator(Type)`
    - `GetRootEnumerator()`
* **OleDbError**
    - `get_Message()`
    - `get_Source()`
    - `ToString()`
    - `get_NativeError()`
    - `get_SQLState()`
* **OleDbErrorCollection**
    - `CopyTo(Array, Int32)`
    - `get_Count()`
    - `get_Item(Int32)`
    - `GetEnumerator()`
    - `System.Collections.ICollection.get_IsSynchronized()`
    - `System.Collections.ICollection.get_SyncRoot()`
    - `CopyTo(OleDbError[], Int32)`
* **OleDbException**
    - `get_ErrorCode()`
    - `GetObjectData(SerializationInfo, StreamingContext)`
    - `get_Errors()`
* **OleDbFactory**
    - `CreateParameter()`
    - `CreateCommand()`
    - `CreateCommandBuilder()`
    - `CreateConnection()`
    - `CreateConnectionStringBuilder()`
    - `CreateDataAdapter()`
* **OleDbInfoMessageEventArgs**
    - `get_ErrorCode()`
    - `get_Message()`
    - `get_Source()`
    - `ToString()`
    - `get_Errors()`
* **OleDbParameter**
    - `.ctor()`
    - `get_Direction()`
    - `get_Value()`
    - `set_Value(Object)`
    - `System.ICloneable.Clone()`
    - `ToString()`
    - `.ctor(String, OleDbType)`
    - `.ctor(String, OleDbType, Int32)`
    - `.ctor(String, OleDbType, Int32, ParameterDirection, Boolean, Byte, Byte, String, DataRowVersion, Object)`
    - `.ctor(String, OleDbType, Int32, ParameterDirection, Byte, Byte, String, DataRowVersion, Boolean, Object)`
    - `.ctor(String, OleDbType, Int32, String)`
    - `.ctor(String, Object)`
    - `get_DbType()`
    - `get_IsNullable()`
    - `get_OleDbType()`
    - `get_ParameterName()`
    - `get_Precision()`
    - `get_Scale()`
    - `get_Size()`
    - `get_SourceColumn()`
    - `get_SourceColumnNullMapping()`
    - `get_SourceVersion()`
    - `ResetDbType()`
    - `ResetOleDbType()`
    - `set_DbType(DbType)`
    - `set_Direction(ParameterDirection)`
    - `set_IsNullable(Boolean)`
    - `set_OleDbType(OleDbType)`
    - `set_ParameterName(String)`
    - `set_Precision(Byte)`
    - `set_Scale(Byte)`
    - `set_Size(Int32)`
    - `set_SourceColumn(String)`
    - `set_SourceColumnNullMapping(Boolean)`
    - `set_SourceVersion(DataRowVersion)`
* **OleDbParameterCollection**
    - `Add(Object)`
    - `Add(String, Object)`
    - `Clear()`
    - `Contains(Object)`
    - `Contains(String)`
    - `CopyTo(Array, Int32)`
    - `get_Count()`
    - `get_IsFixedSize()`
    - `get_IsReadOnly()`
    - `get_IsSynchronized()`
    - `get_Item(Int32)`
    - `get_Item(String)`
    - `get_SyncRoot()`
    - `GetEnumerator()`
    - `IndexOf(Object)`
    - `IndexOf(String)`
    - `Insert(Int32, Object)`
    - `Remove(Object)`
    - `RemoveAt(Int32)`
    - `Add(OleDbParameter)`
    - `Add(String, OleDbType)`
    - `Add(String, OleDbType, Int32)`
    - `Add(String, OleDbType, Int32, String)`
    - `AddRange(Array)`
    - `AddRange(OleDbParameter[])`
    - `AddWithValue(String, Object)`
    - `Contains(OleDbParameter)`
    - `CopyTo(OleDbParameter[], Int32)`
    - `IndexOf(OleDbParameter)`
    - `Insert(Int32, OleDbParameter)`
    - `Remove(OleDbParameter)`
    - `RemoveAt(String)`
    - `set_Item(Int32, OleDbParameter)`
    - `set_Item(String, OleDbParameter)`
* **OleDbRowUpdatedEventArgs**
    - `.ctor(DataRow, IDbCommand, StatementType, DataTableMapping)`
    - `get_Command()`
* **OleDbRowUpdatingEventArgs**
    - `.ctor(DataRow, IDbCommand, StatementType, DataTableMapping)`
    - `get_Command()`
    - `set_Command(OleDbCommand)`
* **OleDbSchemaGuid**
    - `.ctor()`
* **OleDbTransaction**
    - `get_Connection()`
    - `Begin()`
    - `Begin(IsolationLevel)`
    - `Commit()`
    - `get_IsolationLevel()`
    - `Rollback()`

### System.Diagnostics

* **Process**
    - `EnterDebugMode()`
    - `Kill(Boolean)`
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

### System.Drawing

* **FontConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `CanConvertTo(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)`
    - `CreateInstance(ITypeDescriptorContext, IDictionary)`
    - `GetCreateInstanceSupported(ITypeDescriptorContext)`
    - `GetProperties(ITypeDescriptorContext, Object, Attribute[])`
    - `GetPropertiesSupported(ITypeDescriptorContext)`
* **FontConverter+FontNameConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `GetStandardValues(ITypeDescriptorContext)`
    - `GetStandardValuesExclusive(ITypeDescriptorContext)`
    - `GetStandardValuesSupported(ITypeDescriptorContext)`
* **FontConverter+FontUnitConverter**
    - `.ctor()`
    - `GetStandardValues(ITypeDescriptorContext)`
* **IconConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `CanConvertTo(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)`
* **ImageConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `CanConvertTo(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)`
    - `GetProperties(ITypeDescriptorContext, Object, Attribute[])`
    - `GetPropertiesSupported(ITypeDescriptorContext)`
* **ImageFormatConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `CanConvertTo(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)`
    - `GetStandardValues(ITypeDescriptorContext)`
    - `GetStandardValuesSupported(ITypeDescriptorContext)`

### System.Drawing.Printing

* **MarginsConverter**
    - `.ctor()`
    - `CanConvertFrom(ITypeDescriptorContext, Type)`
    - `CanConvertTo(ITypeDescriptorContext, Type)`
    - `ConvertFrom(ITypeDescriptorContext, CultureInfo, Object)`
    - `ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)`
    - `CreateInstance(ITypeDescriptorContext, IDictionary)`
    - `GetCreateInstanceSupported(ITypeDescriptorContext)`

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

* **AnonymousPipeServerStreamAcl**
    - `Create(PipeDirection, HandleInheritability, Int32, PipeSecurity)`
* **NamedPipeServerStreamAcl**
    - `Create(String, PipeDirection, Int32, PipeTransmissionMode, PipeOptions, Int32, Int32, PipeSecurity, HandleInheritability, PipeAccessRights)`
* **NamedPipeClientStream**
    - `Connect(Int32)`
    - `get_NumberOfServerInstances()`
* **NamedPipeServerStream**
    - `WaitForConnection()`
* **PipeStream**
    - `WaitForPipeDrain()`
* **PipeTransmissionMode**
    - `Message`

### System.Media

* **SoundPlayer**
    - `.ctor()`
    - `.ctor(String)`
    - `Stop()`
    - `.ctor(Stream)`
    - `get_IsLoadCompleted()`
    - `get_LoadTimeout()`
    - `get_SoundLocation()`
    - `get_Stream()`
    - `get_Tag()`
    - `Load()`
    - `LoadAsync()`
    - `OnLoadCompleted(AsyncCompletedEventArgs)`
    - `OnSoundLocationChanged(EventArgs)`
    - `OnStreamChanged(EventArgs)`
    - `Play()`
    - `PlayLooping()`
    - `PlaySync()`
    - `set_LoadTimeout(Int32)`
    - `set_SoundLocation(String)`
    - `set_Stream(Stream)`
    - `set_Tag(Object)`
* **SystemSound**
    - `Play()`
* **SystemSounds**
    - `get_Asterisk()`
    - `get_Beep()`
    - `get_Exclamation()`
    - `get_Hand()`
    - `get_Question()`

### System.Net

* **HttpListenerTimeoutManager**
    - `set_EntityBody(TimeSpan)`
    - `set_HeaderWait(TimeSpan)`
    - `set_MinSendBytesPerSecond(Int64)`
    - `set_RequestQueue(TimeSpan)`

### System.Net.NetworkInformation

* **Ping**
    - `Send(IPAddress, Int32, Byte[], PingOptions)`

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
    - `.ctor(SafeSocketHandle)`
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
    - `ReceiveAsync()`

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
    - `.ctor()`
    - `DisplayCertificate(X509Certificate2)`
    - `DisplayCertificate(X509Certificate2, IntPtr)`
    - `SelectFromCollection(X509Certificate2Collection, String, String, X509SelectionFlag)`
    - `SelectFromCollection(X509Certificate2Collection, String, String, X509SelectionFlag, IntPtr)`

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
