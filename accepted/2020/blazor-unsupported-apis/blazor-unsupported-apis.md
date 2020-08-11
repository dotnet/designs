# Marking APIs that are unsupported by Blazor WebAssembly

As discussed in [dotnet/designs#143] we're adding an analyzer to detect usage of
unsupported APIs. This analyzer requires a custom attribute applied to method,
containing type, or assembly.

In this proposal I'm covering which APIs we mark as unsupported in Blazor Web
Assembly. It was largely informed by the work put into [platform-compat].

[dotnet/designs#143]: https://github.com/dotnet/designs/pull/143
[platform-compat]: https://github.com/dotnet/platform-compat

## Attribute application

We'll mark the API as unsupported by all versions. Should we make some APIs work
in the future, we can modify the attribute to specify until which version they
were unsupported.

```C#
[UnsupportedOSPlatform("browser")]
```

## APIs

I've only listed namespace and type level for now; I suspect we'll end up marking
a good chunk of these at the assembly level.

Also, I've only included APIs that are unsupported in Blazor but are supported
everywhere else. For instance, there is no point in marking APIs which are
already marked as Windows-specific -- they are already flagged.

Once the Windows attribution is done, I'll update this PR again.

### Microsoft.SqlServer.Server

* **SqlDataRecord**
* **SqlFacetAttribute**
* **SqlFunctionAttribute**
* **SqlMetaData**
* **SqlMethodAttribute**
* **SqlUserDefinedAggregateAttribute**
* **SqlUserDefinedTypeAttribute**

### Microsoft.Win32

* **RegistryKey**

### Microsoft.Win32.SafeHandles

* **SafeRegistryHandle**

### System.Data.Odbc

* **OdbcCommand**
* **OdbcCommandBuilder**
* **OdbcConnection**
* **OdbcConnectionStringBuilder**
* **OdbcDataAdapter**
* **OdbcDataReader**
* **OdbcError**
* **OdbcErrorCollection**
* **OdbcException**
* **OdbcFactory**
* **OdbcInfoMessageEventArgs**
* **OdbcParameter**
* **OdbcParameterCollection**
* **OdbcRowUpdatedEventArgs**
* **OdbcRowUpdatingEventArgs**
* **OdbcTransaction**

### System.Data.Sql

* **SqlNotificationRequest**

### System.Data.SqlClient

* **SqlBulkCopy**
* **SqlBulkCopyColumnMapping**
* **SqlBulkCopyColumnMappingCollection**
* **SqlClientFactory**
* **SqlCommand**
* **SqlCommandBuilder**
* **SqlConnection**
* **SqlConnectionStringBuilder**
* **SqlCredential**
* **SqlDataAdapter**
* **SqlDataReader**
* **SqlDependency**
* **SqlError**
* **SqlErrorCollection**
* **SqlException**
* **SqlInfoMessageEventArgs**
* **SqlNotificationEventArgs**
* **SqlParameter**
* **SqlParameterCollection**
* **SqlRowsCopiedEventArgs**
* **SqlRowUpdatedEventArgs**
* **SqlRowUpdatingEventArgs**
* **SqlTransaction**

### System.DirectoryServices.Protocols

* **AddRequest**
* **AsqRequestControl**
* **AsqResponseControl**
* **BerConversionException**
* **BerConverter**
* **CompareRequest**
* **CrossDomainMoveControl**
* **DeleteRequest**
* **DirectoryAttribute**
* **DirectoryAttributeCollection**
* **DirectoryAttributeModification**
* **DirectoryAttributeModificationCollection**
* **DirectoryConnection**
* **DirectoryControl**
* **DirectoryControlCollection**
* **DirectoryException**
* **DirectoryIdentifier**
* **DirectoryNotificationControl**
* **DirectoryOperation**
* **DirectoryOperationException**
* **DirectoryRequest**
* **DirectoryResponse**
* **DirSyncRequestControl**
* **DirSyncResponseControl**
* **DomainScopeControl**
* **DsmlAuthRequest**
* **ExtendedDNControl**
* **ExtendedRequest**
* **ExtendedResponse**
* **LazyCommitControl**
* **LdapConnection**
* **LdapDirectoryIdentifier**
* **LdapException**
* **LdapSessionOptions**
* **ModifyDNRequest**
* **ModifyRequest**
* **PageResultRequestControl**
* **PageResultResponseControl**
* **PartialResultsCollection**
* **PermissiveModifyControl**
* **QuotaControl**
* **ReferralCallback**
* **SearchOptionsControl**
* **SearchRequest**
* **SearchResponse**
* **SearchResultAttributeCollection**
* **SearchResultEntry**
* **SearchResultEntryCollection**
* **SearchResultReference**
* **SearchResultReferenceCollection**
* **SecurityDescriptorFlagControl**
* **SecurityPackageContextConnectionInformation**
* **ShowDeletedControl**
* **SortKey**
* **SortRequestControl**
* **SortResponseControl**
* **TlsOperationException**
* **TreeDeleteControl**
* **VerifyNameControl**
* **VlvRequestControl**
* **VlvResponseControl**

### System.Drawing

* **Bitmap**
* **BitmapSuffixInSameAssemblyAttribute**
* **BitmapSuffixInSatelliteAssemblyAttribute**
* **Brush**
* **Brushes**
* **BufferedGraphics**
* **BufferedGraphicsContext**
* **BufferedGraphicsManager**
* **CharacterRange**
* **ColorTranslator**
* **Font**
* **FontFamily**
* **Graphics**
* **Icon**
* **Image**
* **ImageAnimator**
* **Pen**
* **Pens**
* **Region**
* **SolidBrush**
* **StringFormat**
* **SystemBrushes**
* **SystemColors**
* **SystemFonts**
* **SystemIcons**
* **SystemPens**
* **TextureBrush**
* **ToolboxBitmapAttribute**

### System.Drawing.Design

* **CategoryNameCollection**

### System.Drawing.Drawing2D

* **AdjustableArrowCap**
* **Blend**
* **ColorBlend**
* **CustomLineCap**
* **GraphicsPath**
* **GraphicsPathIterator**
* **HatchBrush**
* **LinearGradientBrush**
* **Matrix**
* **PathData**
* **PathGradientBrush**
* **RegionData**

### System.Drawing.Imaging

* **BitmapData**
* **ColorMap**
* **ColorMatrix**
* **ColorPalette**
* **Encoder**
* **EncoderParameter**
* **EncoderParameters**
* **FrameDimension**
* **ImageAttributes**
* **ImageCodecInfo**
* **ImageFormat**
* **Metafile**
* **MetafileHeader**
* **MetaHeader**
* **PropertyItem**
* **WmfPlaceableFileHeader**

### System.Drawing.Printing

* **InvalidPrinterException**
* **Margins**
* **PageSettings**
* **PaperSize**
* **PaperSource**
* **PreviewPageInfo**
* **PreviewPrintController**
* **PrintController**
* **PrintDocument**
* **PrinterResolution**
* **PrinterSettings**
* **PrinterSettings+PaperSizeCollection**
* **PrinterSettings+PaperSourceCollection**
* **PrinterSettings+PrinterResolutionCollection**
* **PrinterSettings+StringCollection**
* **PrinterUnitConvert**
* **PrintEventArgs**
* **PrintPageEventArgs**
* **QueryPageSettingsEventArgs**
* **StandardPrintController**

### System.Drawing.Text

* **FontCollection**
* **InstalledFontCollection**
* **PrivateFontCollection**

### System.IO.Ports

* **SerialDataReceivedEventArgs**
* **SerialErrorReceivedEventArgs**
* **SerialPinChangedEventArgs**
* **SerialPort**

### System.Security.AccessControl

* **AceEnumerator**
* **RegistrySecurity**

### System.Security.Cryptography

* **AesCng**
* **CngAlgorithm**
* **CngAlgorithmGroup**
* **CngKeyBlobFormat**
* **CngKeyCreationParameters**
* **CngProperty**
* **CngPropertyCollection**
* **CngProvider**
* **CngUIPolicy**
* **DSACng**
* **ECDsaCng**
* **RSACng**
* **TripleDESCng**
