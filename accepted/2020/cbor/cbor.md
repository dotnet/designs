#  CBOR Reader & Writer
[Eirik Tsarpalis](https://github.com/eiriktsarpalis)

CBOR (Concise Binary Object Representation), as defined by [RFC 7049](https://tools.ietf.org/html/rfc7049),
is a binary data format loosely based on JSON, emphasizing small message sizes, 
implementation simplicity and extensibility without the need for version negotiation.

This proposal is to provide a generalized CBOR reader and writer as public API,
under the `System.Formats.Cbor` namespace.

The CBOR format defines a data model that derives from JSON.
A _data item_ consists of a single piece of CBOR data.
The structure of a data item may contain zero, one, or more nested data items. 
Each data item can be one of 8 distinct _major types_:

* Major type 0: an unsigned integer encoding, up to 64 bits long.
* Major type 1: a negative integer encoding, up to 64 bits long.
* Major type 2: a byte string, which may or may not be length prefixed.
* Major type 3: a UTF-8 string, which may or may not be length prefixed.
* Major type 4: an array of data items, which may or may not be length prefixed.
  Elements can be data items of _any_ major type.
* Major type 5: a map of pairs of data items, which may or may not be length prefixed. 
  Keys and values can be data items of _any_ major type.
* Major type 6: a semantic tag wrapping a nested data item.
* Major type 7: encodes two kinds of values:
    * IEEE 754 half, single or double precision floating-point numbers _OR_ 
    * Simple values needing no content, such as `false`, `true` and `null`.

Each data item encoding starts with an _initial byte_, which contains the major type (the high-order 3 bits) 
and additional information (the low-order 5 bits). The additional information determines any additional bytes
required for the data item encoding. Small values such as `0` or the empty array are encoded using the 
initial byte only.

CBOR data items can be rendered in text form using a JSON-like [diagnostic notation](https://tools.ietf.org/html/rfc7049#section-6):
```js
{
    "key1" : 42,                   //
    "key2" : h'd9d9f7',            // hex encoded byte string
    "key3" : [null, false, 42, ""] // 
    "key4" : 1(1590589657)         // integer value with semantic tag 1
    [-1]   : [],                   // keys can be of any type
}
```

CBOR is the messaging format used by the FIDO [Client To Authenticator Protocol v2.0 (CTAP2)](https://fidoalliance.org/specs/fido-v2.0-id-20180227/fido-client-to-authenticator-protocol-v2.0-id-20180227.html).
Adding CTAP2 support in the upcoming ASP.NET Core release is the primary motivation 
for adding a .NET CBOR implementation.

[RFC 7049](https://tools.ietf.org/html/rfc7049) defines three levels of encoding conformance: basic well-formedness,
[strict mode](https://tools.ietf.org/html/rfc7049#section-3.10) and 
[Canonical CBOR](https://tools.ietf.org/html/rfc7049#section-3.9).
Additionally, CTAP2 defines its own set of [canonical CBOR encoding rules](https://fidoalliance.org/specs/fido-v2.0-id-20180227/fido-client-to-authenticator-protocol-v2.0-id-20180227.html#ctap2-canonical-cbor-encoding-form)
which are required for any implementation of the protocol. 
The proposed implementation adds reader and writer support for all four conformance levels.

##  Scenarios and User Experience

### Writing an Elliptic Curve public key as a COSE_Key encoding

This sample writes a COSE_Key-encoded Elliptic Curve public key in EC2 format
as specified in [RFC 8152](https://tools.ietf.org/html/rfc8152#section-8.1).

Below is an example of a COSE_key object, expressed in diagnostic notation.
Note that COSE key types are always integers.

```js
{
  1:   2,  // kty: EC2 key type
  3:  -7,  // alg: ES256 signature algorithm
 -1:   1,  // crv: P-256 curve
 -2:   x,  // x-coordinate as byte string 32 bytes in length
           // e.g., in hex: 65eda5a12577c2bae829437fe338701a10aaa375e1bb5b5de108de439c08551d
 -3:   y   // y-coordinate as byte string 32 bytes in length
           // e.g., in hex: 1e52ed75701163f7f9e40ddf9f341b3dc9ba860af7e0ca7ca7e9eecd0084d19c
}
```

```csharp
private static byte[] CreateEc2CoseKeyEncoding(int signatureAlgorithmId, int curveId, byte[] xCoord, byte[] yCoord)
{
    var writer = new CborWriter(conformanceLevel: CborConformanceLevel.Ctap2Canonical);

    writer.WriteStartMap(definiteLength: 5); // push a definite-length map context

    writer.WriteInt32(1); // write the 'kty' key
    writer.WriteInt32(2); // write the value, in this case the RFC8152 EC2 kty identifier

    writer.WriteInt32(3); // write the 'alg' key
    writer.WriteInt32(signatureAlgorithmId); // write the value, in this case the RFC8152 signature algorithm id

    writer.WriteInt32(-1); // write the 'crv' key
    writer.WriteInt32(curveId); // write the value, in this case the RFC8152 curve identifier

    writer.WriteInt32(-2); // write the 'x' key
    writer.WriteByteString(xCoord); // write the encoded x-coordinate

    writer.WriteInt32(-3); // write the 'y' key
    writer.WriteByteString(yCoord); // write the encoded y-coordinate

    writer.WriteEndMap(); // pop the map context

    return writer.Encode(); // return the COSE_Key encoding
}
```

### Reading a map

The following example illustrates how a CBOR map can be read.
We assume here that all keys and values must be text strings.

```csharp
private static Dictionary<string, string> ReadCborTextMap(byte[] encoding)
{
    var results = new Dictionary<string, string>();
    var reader = new CborReader(encoding, CborConformanceLevel.Strict);

    int? length = reader.ReadStartMap();

    if (length != null)
    {
        // is a length-prefixed map encoding
        for (int i = 0; i < length; i++)
        {
            string key = reader.ReadString();
            string value = reader.ReadString();
            reader[key] = value;
        }
    }
    else
    {
        // indefinite-length map encoding
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            string key = reader.ReadString();
            string value = reader.ReadString();
            reader[key] = value;
        }
    }

    reader.ReadEndMap();
    return results;
}
```
    
##  Requirements

###  Goals

- Provide a stateful, forward-only reader class that can read CBOR encoded data.
- Provide a stateful, forward-only writer class that can write CBOR encoded data.
- Both reader and writer must support the following conformance rulesets:
    - Basic well-formedness, a.k.a. lax conformance.
    - RFC7049 Strict mode.
    - RFC7049 Canonical mode.
    - CTAP2 Canonical mode.
- Reader should allow schema-agnostic scenaria.
- Support reading and writing indefinite-length data items.
    - Optionally, the writer should allow on-the-fly conversion of indefinite-length encodings into definite-length equivalents.
- Support tagged value formats, as defined in RFC7049:
    - `System.DateTimeOffset` values,
    - `System.Numerics.BigInteger` values and
    - `System.Decimal` values.
- Reader should support skipping a data item, including any nested data items.
    - Provide a workaround for advancing through nonconforming sections of the encoding.
- Exception safety: methods that throw should not have any side-effects.

### Non-Goals

- Automatically serialize, or deserialize, between CBOR data and .NET types.
- A `CborDocument` type representing the CBOR data model.
- Converting Data between CBOR and JSON.
- Supporting CBOR diagnostic notation.
- Support writing to or reading from `System.IO.Stream`.

## Design

- A stateful reference-type writer (`CborWriter`).
    - Allocates its own expandable write buffer.
    - Exposes a suite of `Write[Type]()` methods for common types.
    - `WriteStart[Type]()` and `WriteEnd[Type]()` methods for composite constructs.
- A stateful reference-type reader (`CborReader`).
    - Decodes a user-provided buffer.
    - Exposes a suite of `Read[Type]()` methods for common types.
    - `ReadStart[Type]()` and `ReadEnd[Map]()` for compositive constructs.
    - Uses the `PeekState()` method to determine the next data item in the buffer, 
      without advancing the reader state.

### API Common to the Reader and the Writer

```csharp
/// <summary>
///   Defines supported conformance levels for encoding and decoding CBOR data.
/// </summary>
public enum CborConformanceLevel
{
    /// <summary>
    ///   Ensures that the CBOR data is well-formed, as specified in RFC7049.
    /// </summary>
    Lax,

    /// <summary>
    ///   Ensures that the CBOR data adheres to strict mode, as specified in RFC7049 section 3.10.
    /// </summary>
    Strict,

    /// <summary>
    ///   Ensures that the CBOR data is canonical, as specified in RFC7049 section 3.9.
    /// </summary>
    Canonical,

    /// <summary>
    ///   Ensures that the CBOR data is canonical, as specified by the CTAP v2.0 standard, section 6.
    /// </summary>
    Ctap2Canonical,
}

/// <summary>
///   Represents a CBOR semantic tag (major type 6).
/// </summary>
public enum CborTag : ulong
{
    /// <summary>
    ///   Tag value for RFC3339 date/time strings.
    /// </summary>
    DateTimeString = 0,

    /// <summary>
    ///   Tag value for Epoch-based date/time strings.
    /// </summary>
    UnixTimeSeconds = 1,

    /// <summary>
    ///   Tag value for unsigned bignum encodings.
    /// </summary>
    UnsignedBigNum = 2,

    /// <summary>
    ///   Tag value for negative bignum encodings.
    /// </summary>
    NegativeBigNum = 3,

    /// <summary>
    ///   Tag value for decimal fraction encodings.
    /// </summary>
    DecimalFraction = 4,

    /// <summary>
    ///   Tag value for big float encodings.
    /// </summary>
    BigFloat = 5,

    /// <summary>
    ///   Tag value for byte strings, meant for later encoding to a base64url string representation.
    /// </summary>
    Base64UrlLaterEncoding = 21,

    /// <summary>
    ///   Tag value for byte strings, meant for later encoding to a base64 string representation.
    /// </summary>
    Base64StringLaterEncoding = 22,

    /// <summary>
    ///   Tag value for byte strings, meant for later encoding to a base16 string representation.
    /// </summary>
    Base16StringLaterEncoding = 23,

    /// <summary>
    ///   Tag value for byte strings containing embedded CBOR data item encodings.
    /// </summary>
    EncodedCborDataItem = 24,

    /// <summary>
    ///   Tag value for Uri strings, as defined in RFC3986.
    /// </summary>
    Uri = 32,

    /// <summary>
    ///   Tag value for base64url-encoded text strings, as defined in RFC4648.
    /// </summary>
    Base64Url = 33,

    /// <summary>
    ///   Tag value for base64-encoded text strings, as defined in RFC4648.
    /// </summary>
    Base64 = 34,

    /// <summary>
    ///   Tag value for regular expressions in Perl Compatible Regular Expressions / Javascript syntax.
    /// </summary>
    Regex = 35,

    /// <summary>
    ///   Tag value for MIME messages (including all headers), as defined in RFC2045.
    /// </summary>
    MimeMessage = 36,

    /// <summary>
    ///   Tag value for the Self-Describe CBOR header (0xd9d9f7).
    /// </summary>
    SelfDescribeCbor = 55799,
}

/// <summary>
///   Represents a CBOR simple value (major type 7).
/// </summary>
public enum CborSimpleValue : byte
{
    /// <summary>
    ///   Represents the value 'false'.
    /// </summary>
    False = 20,

    /// <summary>
    ///   Represents the value 'true'.
    /// </summary>
    True = 21,

    /// <summary>
    ///   Represents the value 'null'.
    /// </summary>
    Null = 22,

    /// <summary>
    ///   Represents an undefined value, to be used by an encoder
    ///   as a substitute for a data item with an encoding problem.
    /// </summary>
    Undefined = 23,
}
```

### Writer API

#### Structure

```C#
/// <summary>
///   A writer for CBOR encoded data.
/// </summary>
public partial class CborWriter
{
    /// <summary>
    ///   Create a new <see cref="CborWriter"/> instance with given configuration.
    /// </summary>
    /// <param name="conformanceLevel">
    ///   Specifies a <see cref="CborConformanceLevel"/> guiding the conformance checks performed on the encoded data.
    ///   Defaults to <see cref="CborConformanceLevel.Lax" /> conformance level.
    /// </param>
    /// <param name="convertIndefiniteLengthEncodings">
    ///   Enables automatically converting indefinite-length encodings into definite-length equivalents.
    ///   Allows use of indefinite-length write APIs in conformance levels that otherwise do not permit it.
    ///   Defaults to <see langword="false" />.
    /// </param>
    /// <param name="allowMultipleRootLevelValues">
    ///   <see langword="true"/> to allow multiple root-level values to be written by the writer; otherwise, <see langword="false"/>.
    ///   The default is <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="conformanceLevel"/> is not a defined <see cref="CborConformanceLevel"/>.
    /// </exception>
    public CborWriter(
        CborConformanceLevel conformanceLevel = CborConformanceLevel.Lax, 
        bool convertIndefiniteLengthEncodings = false, 
        bool allowMultipleRootLevelValues = false) => throw null;

    /// <summary>
    ///   Declares whether this writer allows multiple root-level CBOR data items.
    /// </summary>
    /// <value>
    ///   <see langword="true"/> if the writer allows multiple root-level CBOR data items; otherwise, <see langword="false"/>.
    /// </value>
    public bool AllowMultipleRootLevelValues { get { throw null; } }

    /// <summary>
    ///   The <see cref="CborConformanceLevel"/> used by this writer.
    /// </summary>
    public CborConformanceLevel ConformanceLevel { get { throw null; } }

    /// <summary>
    ///   Gets a value that indicates whether the writer automatically converts indefinite-length encodings into definite-length equivalents.
    /// </summary>
    /// <value>
    ///   <see langword="true"/> if the writer automatically converts indefinite-length encodings into definite-length equivalents; otherwise, <see langword="false"/>.
    /// </value>
    public bool ConvertIndefiniteLengthEncodings { get { throw null; } }

    /// <summary>
    ///   Gets the total number of bytes that have been written to the buffer.
    /// </summary>
    public int BytesWritten { get { throw null; } }

    /// <summary>
    ///   Gets the writer's current level of nestedness in the CBOR document.
    /// </summary>
    public int CurrentDepth { get { throw null; } }

    /// <summary>
    ///   True if the writer has completed writing a complete root-level CBOR document,
    ///   or sequence of root-level CBOR documents.
    /// </summary>
    public bool IsWriteCompleted { get { throw null; } }

    /// <summary>
    ///   Returns a new array containing the encoded value.
    /// </summary>
    /// <returns>A precisely-sized array containing the encoded value.</returns>
    /// <exception cref="InvalidOperationException">
    ///   The writer does not contain a complete CBOR value or sequence of root-level values.
    /// </exception>
    public byte[] Encode() { throw null; }

    /// <summary>
    ///   Write the encoded representation of the data to <paramref name="destination"/>.
    /// </summary>
    public bool TryEncode(Span<byte> destination, out int bytesWritten) { throw null; }

    /// <summary>
    ///   Reset the writer to have no data, without releasing resources.
    /// </summary>
    public void Reset() { }
```

#### Data Injection

#### Integer Values (Major types 0,1)

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes an <see cref="int"/> value as a signed integer encoding (major types 0,1)
    /// </summary>
    /// <param name="value">The value to write</param>
    /// <exception cref="InvalidOperationException">
    ///   Writing a new value exceeds the definite length of the parent data item -or-
    ///   The major type of the encoded value is not permitted in the parent data item -or-
    ///   The written data is not accepted under the current conformance level
    /// </exception>
    public void WriteInt32(int value) { throw null; }
    public void WriteInt64(long value) { throw null; }

    /// <summary>
    ///   Writes an <see cref="ulong"/> value as an unsigned integer encoding (major type 0).
    /// </summary>
    public void WriteUInt32(uint value) { throw null; }
    public void WriteUInt64(ulong value) { throw null; }

    /// <summary>
    ///   Writes a <see cref="ulong"/> value as a negative integer encoding (major type 1).
    /// </summary>
    /// <param name="value">An unsigned integer denoting -1 minus the integer.</param>
    /// <remarks>
    ///   This method supports encoding integers between -18446744073709551616 and -1.
    ///   Useful for handling values that do not fit in the <see cref="long"/> type.
    /// </remarks>
    public void WriteCborNegativeIntegerEncoding(ulong value) { throw null; }
}
```

#### Byte Strings (Major type 2)

The CBOR spec permits two types of byte strings: definite and indefinite-length.
Indefinite-length strings are defined as a sequence of definite-length string chunks.

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes a buffer as a byte string encoding (major type 2).
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">
    ///   Writing a new value exceeds the definite length of the parent data item. -or-
    ///   The major type of the encoded value is not permitted in the parent data item. -or-
    ///   The written data is not accepted under the current conformance level.
    /// </exception>
    public void WriteByteString(ReadOnlySpan<byte> value) { throw null; }

    /// <summary>
    ///   Writes the start of an indefinite-length byte string (major type 2).
    /// </summary>
    /// <remarks>
    ///   Pushes a context where definite-length chunks of the same major type can be written.
    ///   In canonical conformance levels, the writer will reject indefinite-length writes unless
    ///   the <see cref="ConvertIndefiniteLengthEncodings"/> flag is enabled.
    /// </remarks>
    public void WriteStartByteString() { throw null; }

    /// <summary>
    ///   Writes the end of an indefinite-length byte string (major type 2).
    /// </summary>
    public void WriteEndByteString() { throw null; }
}
```

#### UTF-8 Strings (Major type 3)

The CBOR spec permits two types of text strings: definite and indefinite-length.
Indefinite-length strings are defined as a sequence of definite-length string chunks.

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes a buffer as a UTF-8 string encoding (major type 3).
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">
    ///   Writing a new value exceeds the definite length of the parent data item. -or-
    ///   The major type of the encoded value is not permitted in the parent data item. -or-
    ///   The written data is not accepted under the current conformance level.
    /// </exception>
    public void WriteTextString(ReadOnlySpan<char> value) { throw null; }

    /// <summary>
    ///   Writes the start of an indefinite-length UTF-8 string (major type 3).
    /// </summary>
    /// <remarks>
    ///   Pushes a context where definite-length chunks of the same major type can be written.
    ///   In canonical conformance levels, the writer will reject indefinite-length writes unless
    ///   the <see cref="ConvertIndefiniteLengthEncodings"/> flag is enabled.
    /// </remarks>
    public void WriteStartTextString() { throw null; }

    /// <summary>
    ///   Writes the end of an indefinite-length UTF-8 string (major type 3).
    /// </summary>
    public void WriteEndTextString() { throw null; }
}
```

#### Arrays (Major type 4)

Array data items can be thought of as json arrays. 
A CBOR array can contain data items of any major type.
CBOR arrays can either be definite or indefinite-length.

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes the start of a definite-length array (major type 4).
    /// </summary>
    /// <param name="definiteLength">The definite length of the array.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   The <paramref name="definiteLength"/> parameter cannot be negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   Writing a new value exceeds the definite length of the parent data item. -or-
    ///   The major type of the encoded value is not permitted in the parent data item.
    /// </exception>
    public void WriteStartArray(int definiteLength) { throw null; }

    /// <summary>
    ///   Writes the start of an indefinite-length array (major type 4).
    /// </summary>
    /// <remarks>
    ///   In canonical conformance levels, the writer will reject indefinite-length writes unless
    ///   the <see cref="ConvertIndefiniteLengthEncodings"/> flag is enabled.
    /// </remarks>
    public void WriteStartArray() { throw null; }

    /// <summary>
    ///   Writes the end of an array (major type 4).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   The written data is not accepted under the current conformance level. -or-
    ///   The definite-length array anticipates more data items.
    /// </exception>
    public void WriteEndArray() { throw null; }
}
```

#### Maps (Major type 5)

Map data items can be thought of as JSON objects, with the main difference that keys can be of any major type.
Key/value data items are written sequentially, and it is the responsibility of the caller to track if the next
write is either a key or a value. CBOR maps can either be definite or indefinite-length.

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes the start of a definite-length map (major type 5).
    /// </summary>
    /// <param name="definiteLength">The definite length of the map.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   The <paramref name="definiteLength"/> parameter cannot be negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   Writing a new value exceeds the definite length of the parent data item. -or-
    ///   The major type of the encoded value is not permitted in the parent data item.
    /// </exception>
    public void WriteStartMap(int definiteLength) { throw null; }

    /// <summary>
    ///   Writes the start of an indefinite-length map (major type 5).
    /// </summary>
    /// <remarks>
    ///   In canonical conformance levels, the writer will reject indefinite-length writes unless
    ///   the <see cref="ConvertIndefiniteLengthEncodings"/> flag is enabled.
    /// </remarks>
    public void WriteStartMap() { throw null; }

    /// <summary>
    ///   Writes the end of a map (major type 5).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   The definite-length map anticipates more key/value pairs. -or-
    ///   The latest key/value pair is lacking a value.
    /// </exception>
    public void WriteEndMap() { throw null; }
}
```

#### Tagged Values (Major type 6)

Methods adding support for semantically tagging data items,
as well as supporting semantic encodings defined by the CBOR spec.

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Assign a semantic tag (major type 6) to the next data item.
    /// </summary>
    public void WriteTag(CborTag tag) { throw null; }

    /// <summary>
    ///   Writes a <see cref="DateTimeOffset"/> value as a tagged date/time string,
    ///   as described in RFC7049 section 2.4.1.
    /// </summary>
    public void WriteDateTimeOffset(DateTimeOffset value) { throw null; }

    /// <summary>
    ///   Writes a unix time in seconds as a tagged date/time value,
    ///   as described in RFC7049 section 2.4.1.
    /// </summary>
    public void WriteUnixTimeSeconds(long seconds) { throw null; }

    /// <summary>
    ///   Writes a unix time in seconds as a tagged date/time value,
    ///   as described in RFC7049 section 2.4.1.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///   The <paramref name="seconds"/> parameter cannot be infinite or NaN.
    /// </exception>
    public void WriteUnixTimeSeconds(double seconds) { throw null; }

    /// <summary>
    ///   Writes a <see cref="BigInteger"/> value as a tagged bignum encoding,
    ///   as described in RFC7049 section 2.4.2.
    /// </summary>
    public void WriteBigInteger(BigInteger value) { throw null; }

    /// <summary>
    ///   Writes a <see cref="decimal"/> value as a tagged decimal fraction encoding,
    ///   as described in RFC7049 section 2.4.3.
    /// </summary>
    public void WriteDecimal(decimal value) { throw null; }
}
```

#### Simple & floating-point values (Major type 7)

Major type 7 data items can be either of the following:
* Simple values identified by a single 8-bit tag and no other content (eg `false`, `null`) or
* IEEE 754 half, single or double precision floating-point encodings.


```C#
public partial class CborWriter
{
    /// <summary>
    ///   Writes a simple value encoding (major type 7).
    /// </summary>
    public void WriteSimpleValue(CborSimpleValue value) { throw null; }

    /// <summary>
    ///   Writes a boolean value (major type 7).
    /// </summary>
    public void WriteBoolean(bool value) { throw null; }

    /// <summary>
    ///   Writes a null value (major type 7).
    /// </summary>
    public void WriteNull() { throw null; }
    
    /// <summary>
    ///   Writes a single-precision floating point number (major type 7).
    /// </summary>
    public void WriteSingle(float value) { throw null; }

    /// <summary>
    ///   Writes a double-precision floating point number (major type 7).
    /// </summary>
    public void WriteDouble(double value) { throw null; }
}
```

#### Writing pre-encoded values

```C#
public partial class CborWriter
{
    /// <summary>
    ///   Validates and writes a single CBOR data item which has already been encoded.
    /// </summary>
    /// <param name="encodedValue">The encoded value to write.</param>
    /// <exception cref="ArgumentException">
    ///   <paramref name="encodedValue"/> is not a well-formed CBOR encoding. -or-
    ///   <paramref name="encodedValue"/> is not valid under the current conformance level.
    /// </exception>
    public void WriteEncodedValue(ReadOnlyMemory<byte> encodedValue) { throw null; }
}
```

### CborReader API

#### Structure

```C#
/// <summary>
///   A stateful, forward-only reader for CBOR encoded data.
/// </summary>
public partial class CborReader
{
    /// <summary>
    ///   Construct a <see cref="CborReader"/> instance over <paramref name="data"/> with given configuration.
    /// </summary>
    /// <param name="data">The CBOR encoded data to read.</param>
    /// <param name="conformanceLevel">
    ///   Specifies a <see cref="CborConformanceLevel"/> guiding the conformance checks performed on the encoded data.
    ///   Defaults to <see cref="CborConformanceLevel.Lax" /> conformance level.
    /// </param>
    /// <param name="allowMultipleRootLevelValues">
    ///   Specify if multiple root-level values are to be supported by the reader.
    ///   When set to <see langword="false" />, the reader will throw an <see cref="InvalidOperationException"/>
    ///   if trying to read beyond the scope of one root-level CBOR data item.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="conformanceLevel"/> is not defined.
    /// </exception>
    public CborReader(
        ReadOnlyMemory<byte> data, 
        CborConformanceLevel conformanceLevel = CborConformanceLevel.Lax, 
        bool allowMultipleRootLevelValues = false) => throw null;

    /// <summary>
    ///   The <see cref="CborConformanceLevel"/> used by this reader.
    /// </summary>
    public CborConformanceLevel ConformanceLevel { get { throw null; } }

    /// <summary>
    ///   Declares whether this reader allows multiple root-level CBOR data items.
    /// </summary>
    public bool AllowMultipleRootLevelValues { get { throw null; } }

    /// <summary>
    ///   Gets the reader's current level of nestedness in the CBOR document.
    /// </summary>
    public int CurrentDepth { get { throw null; } }

    /// <summary>
    ///   Gets the total number of bytes that have been consumed by the reader.
    /// </summary>
    public int BytesRead { get { throw null; } }

    /// <summary>
    ///   Indicates whether or not the reader has remaining data available to process.
    /// </summary>
    public bool HasData { get { throw null; } }
}
```

#### Peeking the Reader State

The `CborReader` state can be consulted by calling the `PeekState()` method, 
which tries to read the next CBOR initial byte without advancing the reader state.
The method never throws, returning an enum of type `CborReaderstate`.

```C#
/// <summary>
///   Specifies the state of a CborReader instance.
/// </summary>
public enum CborReaderState
{
    /// <summary>
    ///   Indicates the undefined state.
    /// </summary>
    None = 0,

    /// <summary>
    ///   Indicates that the next CBOR data item is an unsigned integer (major type 0).
    /// </summary>
    UnsignedInteger,

    /// <summary>
    ///   Indicates that the next CBOR data item is a negative integer (major type 1).
    /// </summary>
    NegativeInteger,

    /// <summary>
    ///   Indicates that the next CBOR data item is a definite-length byte string (major type 2).
    /// </summary>
    ByteString,

    /// <summary>
    ///   Indicates that the next CBOR data item denotes the start of an indefinite-length byte string (major type 2).
    /// </summary>
    StartByteString,

    /// <summary>
    ///   Indicates that the reader is at the end of an indefinite-length byte string (major type 2).
    /// </summary>
    EndByteString,

    /// <summary>
    ///   Indicates that the next CBOR data item is a definite-length UTF-8 string (major type 3).
    /// </summary>
    TextString,

    /// <summary>
    ///   Indicates that the next CBOR data item denotes the start of an indefinite-length UTF-8 text string (major type 3).
    /// </summary>
    StartTextString,

    /// <summary>
    ///   Indicates that the reader is at the end of an indefinite-length UTF-8 text string (major type 3).
    /// </summary>
    EndTextString,

    /// <summary>
    ///   Indicates that the next CBOR data item denotes the start of an array (major type 4).
    /// </summary>
    StartArray,

    /// <summary>
    ///   Indicates that the reader is at the end of an array (major type 4).
    /// </summary>
    EndArray,

    /// <summary>
    ///   Indicates that the next CBOR data item denotes the start of a map (major type 5).
    /// </summary>
    StartMap,

    /// <summary>
    ///   Indicates that the reader is at the end of a map (major type 5).
    /// </summary>
    EndMap,

    /// <summary>
    ///   Indicates that the next CBOR data item is a semantic tag (major type 6).
    /// </summary>
    Tag,

    /// <summary>
    ///   Indicates that the next CBOR data item is a simple value (major type 7).
    /// </summary>
    SimpleValue,

    /// <summary>
    ///   Indicates that the next CBOR data item is an IEEE 754 Half-Precision float (major type 7).
    /// </summary>
    HalfPrecisionFloat,

    /// <summary>
    ///   Indicates that the next CBOR data item is an IEEE 754 Single-Precision float (major type 7).
    /// </summary>
    SinglePrecisionFloat,

    /// <summary>
    ///   Indicates that the next CBOR data item is an IEEE 754 Double-Precision float (major type 7).
    /// </summary>
    DoublePrecisionFloat,

    /// <summary>
    ///   Indicates that the next CBOR data item is a <see langword="null" /> literal (major type 7).
    /// </summary>
    Null,

    /// <summary>
    ///   Indicates that the next CBOR data items encodes a <see cref="bool" /> value (major type 7).
    /// </summary>
    Boolean,

    /// <summary>
    ///   Indicates that the reader has completed reading a full CBOR document.
    /// </summary>
    /// <remarks>
    ///   If <see cref="CborReader.AllowMultipleRootLevelValues"/> is set to <see langword="true" />,
    ///   the reader will report this value even if the buffer contains trailing bytes.
    /// </remarks>
    Finished,

    /// <summary>
    ///   Indicates that the reader has encountered an incomplete CBOR document.
    /// </summary>
    EndOfData,

    /// <summary>
    ///   Indicates that the reader has encountered an error in the CBOR format encoding.
    /// </summary>
    FormatError,
}

public partial class CborReader
{
    /// <summary>
    ///   Read the next CBOR token, without advancing the reader.
    /// </summary>
    public CborReaderState PeekState() { throw null; }
}
```

#### Direct Data Interaction

The general shape of read methods is of the form `Type Read[Type]()`.
The read methods will either successfully decode the requested value,
or throw an exception and _not_ advance the reader state.

#### Integer Values (Major types 0,1)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as a signed integer (major types 0,1)
    /// </summary>
    /// <returns>The decoded integer value.</returns>
    /// <exception cref="InvalidOperationException">
    ///   the next data item does not have the correct major type.
    /// </exception>
    /// <exception cref="OverflowException">
    ///   the encoded integer is out of range for <see cref="int"/>.
    /// </exception>
    /// <exception cref="FormatException">
    ///   the next value has an invalid CBOR encoding. -or-
    ///   there was an unexpected end of CBOR encoding data. -or-
    ///   the next value uses a CBOR encoding that is not valid under the current conformance level.
    /// </exception>
    public int ReadInt32() { throw null; }
    public long ReadInt64() { throw null; }

    /// <summary>
    ///   Reads the next data item as an usigned integer (major type 0).
    /// </summary>
    public uint ReadUInt32() { throw null; }
    public ulong ReadUInt64() { throw null; }

    /// <summary>
    ///   Reads the next data item as a CBOR negative integer encoding (major type 1).
    /// </summary>
    /// <returns>
    ///   An unsigned integer denoting -1 minus the integer.
    /// </returns>
    /// <remarks>
    ///   This method supports decoding integers between -18446744073709551616 and -1.
    ///   Useful for handling values that do not fit in the <see cref="long"/> type.
    /// </remarks>
    public ulong ReadCborNegativeIntegerEncoding() { throw null; }
}
```

#### Byte Strings (Major type 2)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as a byte string (major type 2).
    /// </summary>
    /// <returns>The decoded byte array.</returns>
    /// <remarks>
    ///   The method accepts indefinite length strings, which it will concatenate to a single string.
    /// </remarks>
    public byte[] ReadByteString() { throw null; }

    /// <summary>
    ///   Reads the next data item as a byte string (major type 2).
    /// </summary>
    public bool TryReadByteString(Span<byte> destination, out int bytesWritten) { throw null; }

    /// <summary>
    ///   Reads the next data item as the start of an indefinite-length byte string (major type 2).
    /// </summary>
    public void ReadStartByteString() { throw null; }
    public void ReadEndByteString() { throw null; }
}
```

#### Text Strings (Major type 3)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as a UTF-8 text string (major type 3).
    /// </summary>
    /// <remarks>
    ///   The method accepts indefinite length strings, which it will concatenate to a single string.
    /// </remarks>
    public string ReadTextString() { throw null; }
    public bool TryReadTextString(Span<char> destination, out int charsWritten) { throw null; }

    /// <summary>
    ///   Reads the next data item as the start of an indefinite-length text string (major type 3).
    /// </summary>
    public void ReadStartTextString() { throw null; }
    public void ReadEndTextString() { throw null; }
}
```

#### Arrays (Major type 4)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as the start of an array (major type 4).
    /// </summary>
    /// <returns>
    ///   The length of the definite-length array, or <see langword="null" /> if the array is indefinite-length.
    /// </returns>
    public int? ReadStartArray() { throw null; }

    /// <summary>
    ///   Reads the end of an array (major type 4).
    /// </summary>
    public void ReadEndArray() { throw null; }
}
```

#### Maps (Major type 5)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as the start of a map (major type 5).
    /// </summary>
    /// <returns>
    ///   The number of key-value pairs in a definite-length map, or <see langword="null" /> if the map is indefinite-length.
    /// </returns>
    public int? ReadStartMap() { throw null; }


    /// <summary>
    ///   Reads the end of a map (major type 5).
    /// </summary>
    public void ReadEndMap() { throw null; }
}
```

#### Tagged Values (Major type 6)

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as a semantic tag (major type 6).
    /// </summary>
    public CborTag ReadTag() { throw null; }

    /// <summary>
    ///   Reads the next data item as a semantic tag (major type 6), without advancing the reader.
    /// </summary>
    /// <remarks>
    ///   Useful in scenaria where the semantic value decoder needs to be determined at runtime.
    /// </remarks>
    public CborTag PeekTag() { throw null; }

    /// <summary>
    ///   Reads the next data item as a tagged date/time string,
    ///   as described in RFC7049 section 2.4.1.
    /// </summary>
    public DateTimeOffset ReadDateTimeOffset() { throw null; }

    /// <summary>
    ///   Reads the next data item as a tagged unix time in seconds,
    ///   as described in RFC7049 section 2.4.1.
    /// </summary>
    public DateTimeOffset ReadUnixTimeSeconds() { throw null; }

    /// <summary>
    ///   Reads the next data item as a tagged bignum encoding,
    ///   as described in RFC7049 section 2.4.2.
    /// </summary>
    public BigInteger ReadBigInteger() { throw null; }

    /// <summary>
    ///   Reads the next data item as a tagged decimal fraction encoding,
    ///   as described in RFC7049 section 2.4.3.
    /// </summary>
    /// <exception cref="OverflowException">
    ///   Decoded decimal fraction is either too large or too small for a <see cref="decimal"/> value.
    /// </exception>
    public decimal ReadDecimal() { throw null; }
}
```

#### Simple & floating-point values (Major type 7)

Major type 7 data items can be either of the following:
* Simple values identified by a single 8-bit tag and no other content (eg `false`, `null`) or
* IEEE 754 half, single or double precision floating-point encodings.

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next data item as a CBOR simple value (major type 7).
    /// </summary>
    public CborSimpleValue ReadSimpleValue() { throw null; }

    /// <summary>
    ///   Reads the next data item as a boolean value (major type 7).
    /// </summary>
    public bool ReadBoolean() { throw null; }

    /// <summary>
    ///   Reads the next data item as a null value (major type 7).
    /// </summary>
    public void ReadNull() { throw null; }
    
    /// <summary>
    ///   Reads the next data item as a single-precision floating point number (major type 7).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   the encoded value is a double-precision float.
    /// </exception>
    public float ReadSingle() { throw null; }

    /// <summary>
    ///   Reads the next data item as a double-precision floating point number (major type 7).
    /// </summary>
    public double ReadDouble() { throw null; }
}
```

#### Reading encoded values

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the next CBOR data item, returning a <see cref="ReadOnlySpan{T}"/> view
    ///   of the encoded value.
    /// </summary>
    public ReadOnlySpan<byte> ReadEncodedValue() { throw null; }
}
```

#### Skipping encoded values

```C#
public partial class CborReader
{
    /// <summary>
    ///   Reads the contents of the next value, discarding the result and advancing the reader.
    /// </summary>
    /// <param name="disableConformanceLevelChecks">
    ///   Disable conformance level validation for the skipped value,
    ///   equivalent to using <see cref="CborConformanceLevel.Lax"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///   the reader is not at the start of new value.
    /// </exception>
    public void SkipValue(bool disableConformanceLevelChecks = false) { throw null; }

    /// <summary>
    ///   Reads the remaining contents of the current value context,
    ///   discarding results and advancing the reader to the next value
    ///   in the parent context.
    /// </summary>
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///   the reader is at the root context.
    /// </exception>
    public void SkipToParent(bool disableConformanceLevelChecks = false) { throw null; }
}
```

##  Q & A

### Open Questions

- Should we release a netstandard2.0 port?
- Should we support encoding half-precision floats? 
    - Currently only decoding is supported.
    - Certain CBOR encoding implementations use lower precision when possible, 
      however CTAP2 canonical mode explicitly prohibits this.
- Should we support reuse of `CborReader` allocations?
    - Currently possible in `CborWriter` via the `Reset()` method.
- Should we relax `ReadDouble()` so that integer types are also accepted?
    - In general, the reader does support any coercions (e.g. `bool` to `int`).
