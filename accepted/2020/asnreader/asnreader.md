#  ASN.1 BER/CER/DER Reader & Writer
[Jeremy Barton](https://github.com/bartonjs)

ASN.1 (Abstract Syntax Notation One), defined by [ITU-T X.680](https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-X.680-201508-I!!PDF-E&type=items),
is a data modelling language used to describe objects used in cryptographic key transmission, X.509 public key certificates,
X.509 attribute certificates, and LDAP.
This proposal is to provide a generalized ASN.1 BER family reader and writer as public API.

ASN.1 describes a number of primitive value types, as well as a complex type definition syntax.

[ITU-T X.690](https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-X.690-201508-I!!PDF-E&type=items) describes the Basic Encoding Rules (BER) for turning ASN.1 data into bytes.
It also describes two refinements to BER: Canonical Encoding Rules (CER) and Distinguished Encoding Rules (DER).

In the context of cryptography, DER is almost always used, with the exception of the PKCS#7/CMS data structures, which explicitly use BER.
(Windows `CryptEncodeObject` refers to these two rulesets as `X509_ASN_ENCODING` (DER) and `PKCS_7_ASN_ENCODING` (BER)).

All BER-family data is encoded as TLV (type, length, value) triplets, where the type identifier has one binary encoding,
the length has a second encoding, and the value encoding is different for each value type.
The CER or DER encoding of the value "INTEGER 7" is `02 01 07` (BER allows 126 different representations, `02 01 07`,
and `02 84 00 00 00 01 07` being the most common).

.NET Cryptography makes use of BER and DER data internally, as part of processing X.509 public key certificate chains,
importing and exporting cryptographic key files, and processing SignedCms and EnvelopedCms messages.
The platform exposes both X.509 certificate extensions and CMS attributes in a polymorphic way to provide easy reading and writing of common values,
but without a generalized solution for interpreting (or creating) the payloads users can't easily work with extensions/attributes that we don't have special platform support for.

##  Scenarios and User Experience

### Write an X.509 Authority Key Identifier Extension

This sample creates an `X509Extension` object, suitable for use with `CertificateRequest`,
for an X.509 extension type that does not have an existing class in the platform.

```asn.1
DEFINITIONS IMPLICIT TAGS ::=
...

id-ce-authorityKeyIdentifier OBJECT IDENTIFIER ::=  { id-ce 35 }

AuthorityKeyIdentifier ::= SEQUENCE {
  keyIdentifier             [0] KeyIdentifier           OPTIONAL,
  authorityCertIssuer       [1] GeneralNames            OPTIONAL,
  authorityCertSerialNumber [2] CertificateSerialNumber OPTIONAL  }

KeyIdentifier ::= OCTET STRING

...
```

ASN.1 explanation:

* An AuthorityKeyIdentifier value is a sequence of elements.
  * If the first element uses the tag value 0 with tag class context-specific, it has the functional role of `keyIdentifier`, and is an encoded `KeyIdentifier` value except for the tag substitution.
    * A `KeyIdentifier` value is encoded as an octet string.
  * If the next (possibly first) element uses the tag value 1 with tag class context-specific, it has the functional role of `authorityCertIssuer`, ...
  * If the next (possibly first) element uses the tag value 2 with the tag class context-specific, ...
  * There will be no other fields in this type.
* The object identifier `id-ce-authorityKeyIdentifier` is arc 35 under object identifier `id-ce`.
  * (Following the chain we end up with "2.5.29.35")

```C#
private static X509Extension CreateAKID(byte[] caSubjectKeyId)
{
    using (AsnWriter writer = new AsnWriter(AsnEncodingRules.DER))
    {
        // AuthorityKeyIdentifier
        writer.PushSequence();
        // keyIdentifier [0] KeyIdentifier (OCTET STRING)
        writer.WriteOctetString(new Asn1Tag(TagClass.ContextSpecific, 0), caSubjectKeyId);
        writer.PopSequence();
        return new X509Extension("2.5.29.35", writer.Encode(), critical: false);
    }
}
```

### Read an ECPrivateKey value into ECParameters

This sample illustrates reading from a key file format into the equivalent .NET key parameters structure.
(This is simplified from the cryptographic key import feature in .NET Core 3.0.)

```asn.1
DEFINITIONS EXPLICIT TAGS ::=
...

ECPrivateKey ::= SEQUENCE {
  version        INTEGER { ecPrivkeyVer1(1) } (ecPrivkeyVer1),
  privateKey     OCTET STRING,
  parameters [0] ECParameters {{ NamedCurve }} OPTIONAL,
  publicKey  [1] BIT STRING OPTIONAL
  
...
```

ASN.1 explanation:

* An ECPrivateKey value is a sequence of elements.
  * The first element is named `version`, it is an integer.
    * In the context of `version`, let `ecPrivkeyVer1` be an identifier with the value 1.
    * `version` is restricted to only allow the value `ecPrivkeyVer1`.
  * The second element is named `privateKey`, it is an octet string.
  * The third element is named `parameters`, it is an `ECParameters` value restricted to only the "NamedCurve" CHOICE.
    * This element is optional.
    * This element, when present, is wrapped inside an extra tag, `[CONTEXT-SPECIFIC 0]`.
  * The fourth element is named `publicKey`, it is a bit string.
    * This element is optional.
    * This element, when present, is wrapped inside an extra tag, `[CONTEXT-SPECIFIC 1]`.
  * There will be no other fields in this type.

```C#
private static ECParameters FromECPrivateKey(byte[] ecPrivateKey)
{
    AsnReader reader = new AsnReader(ecPrivateKey, AsnEncodingRules.BER);
    AsnReader sequenceReader = reader.ReadSequence();
    reader.ThrowIfNotEmpty();

    if (!sequenceReader.TryReadUInt8(out byte version) || version != 1)
    {
        throw new InvalidOperationException();
    }

    ECParameters ecParameters = new ECParameters
    {
        D = sequenceReader.ReadOctetString(),
    };

    Asn1Tag context0 = new Asn1Tag(TagClass.ContextSpecific, 0);
    Asn1Tag context1 = new Asn1Tag(TagClass.ContextSpecific, 1);

    // Don't test for the ECParameters, since we didn't accept external parameters.
    //if (sequenceReader.HasData && sequenceReader.PeekTag().HasSameClassAndValue(context0))
    {
        AsnReader ecParamsReader = reader.ReadSequence(context0);
        ecParameters.Curve = ECCurve.CreateFromValue(ecParamsReader.ReadObjectIdentifierAsString());
        ecParamsReader.ThrowIfNotEmpty();
    }

    // Don't test for the presence of public key, we require it.
    //if (sequenceReader.HasData && sequenceReader.PeekTag().HasSameClassAndValue(context1))
    {
        AsnReader publicKeyReader = reader.ReadSequence(context1);
        byte[] encodedKey = publicKeyReader.ReadBitString(out int unused);
        publicKeyReader.ThrowIfNotEmpty();

        if (unused != 0 || encodedKey.Length % 2 != 1 || encodedKey[0] != 0x04)
        {
            throw new NotSupportedException();
        }

        ecParameters.Q.X = encodedKey.AsSpan(1, encodedKey.Length / 2).ToArray();
        ecParameters.Q.Y = encodedKey.AsSpan(1 + encodedKey.Length / 2).ToArray();
    }

    sequenceReader.ThrowIfNotEmpty();
    return ecParameters;
}
```

### Read an ECPrivateKey value into ECParameters, min-alloc

This is a repeat of the previous scenario, but only using heap allocations for assigning arrays on the
ECParameters struct, and reading the object identifier string.

```C#
private static ECParameters FromECPrivateKey(ReadOnlySpan<byte> ecPrivateKey)
{
    AsnValueReader reader = new AsnValueReader(ecPrivateKey, AsnEncodingRules.BER);
    AsnValueReader sequenceReader = reader.ReadSequence();
    reader.ThrowIfNotEmpty();

    if (!sequenceReader.TryReadUInt8(out byte version) || version != 1)
    {
        throw new InvalidOperationException();
    }

    ECParameters ecParameters = new ECParameters
    {
        D = sequenceReader.ReadOctetString(),
    };

    Asn1Tag context0 = new Asn1Tag(TagClass.ContextSpecific, 0);
    Asn1Tag context1 = new Asn1Tag(TagClass.ContextSpecific, 1);

    // Don't test for the ECParameters, since we didn't accept external parameters.
    //if (sequenceReader.HasData && sequenceReader.PeekTag().HasSameClassAndValue(context0))
    {
        AsnValueReader ecParamsReader = reader.ReadSequence(context0);
        // Don't check for named vs explicit vs implicit, just assume named.
        ecParameters.Curve = ECCurve.CreateFromValue(ecParamsReader.ReadObjectIdentifierAsString());
        ecParamsReader.ThrowIfNotEmpty();
    }

    // Don't test for the presence of public key, we require it.
    //if (sequenceReader.HasData && sequenceReader.PeekTag().HasSameClassAndValue(context1))
    {
        // The longest key we know about is secp521r1, at 133 bytes
        Span<byte> encodedKey;
        unsafe
        {
            byte* stackPtr = stackalloc byte[133];
            encodedKey = new Span<byte>(stackPtr, 133);
        }

        AsnValueReader publicKeyReader = sequenceReader.ReadSequence(context1);

        if (!publicKeyReader.TryCopyBitStringBytes(encodedKey, out int unused, out int bytesWritten) ||
            unused != 0 ||
            bytesWritten % 2 != 1 ||
            encodedKey[0] != 0x04)
        {
            throw new NotSupportedException();
        }

        publicKeyReader.ThrowIfNotEmpty();

        int elementLength = bytesWritten / 2;
        ecParameters.Q.X = encodedKey.Slice(1, elementLength).ToArray();
        ecParameters.Q.Y = encodedKey.Slice(1 + elementLength, elementLength).ToArray();
    }

    sequenceReader.ThrowIfNotEmpty();
    return ecParameters;
}
```
    
##  Requirements

###  Goals

 - Provide a stateful reference type reader that can read ITU-T X.690 BER data, except the data types explicitly excluded.
   - Allow callers to opt-in to conformance checking for the more stringent CER and DER rules.
   - The reader design and implementation need to assume the inputs are untrusted data, used in trust decisions.
  - Provide a stateful writer that allows callers to use linear writing patterns for ITU-T X.690 BER data without worrying about managing prepended length values.
    - The writer will apply automatic conformance to CER and DER restrictions, as requested.
  - Provide a `ref struct` version of the reader for performance-critical scenarios.
  - The reader and writer use C#/.NET paradigms, and translate to BER paradigms when they do not align.
  - Ensure callers have workarounds for types that the reader and writer do not have first-class support for.

###  Non-Goals

 - Automatically serialize, or deserialize, between BER-family-encoded data and .NET types.
 - Compile the ASN.1 language into .NET types.
	 - Perhaps this is a source generation feature for later?
 - Read, or write, ASN.1 types which are not needed for PKIX, CMS, or common LDAP values, such as
   - Real
   - `SET` (`SET-OF` is supported)
   - Relative Object Identifier
   - OID Internationalized Resource Identifier
   - Relative OID Internationalized Resource Identifier
   - `TIME`
   - `DATE`
   - `TIME-OF-DAY`
   - `DATE-TIME` (both `GeneralizedTime` and `UTCTime` are supported)
   - `DURATION`
  - Support ASN.1 encodings outside of the BER family (XER, PER, UPER, OER, JER, etc)

##  Design

 - A stateful reference-type writer (`AsnWriter`)
 - Stateful readers
   - Verbs
     - Peek: Return a value without advancing the reader.
     - Read: Return a value, advancing the reader.
     - Copy: Write a value to a provided destination, advancing the reader.
    - Variants
      - Reference type (`AsnReader`)
      - Mutable ref-like value type (`AsnValueReader`)

### API Common to Readers and the Writer

```C#
/// <summary>
///   This type represents an ASN.1 tag, as described in ITU-T Recommendation X.680.
/// </summary>
public readonly struct Asn1Tag : IEquatable<Asn1Tag>
{
    /// <summary>
    ///   The tag class to which this tag belongs.
    /// </summary>
    public TagClass TagClass { get; }

    /// <summary>
    ///   Indicates if the tag represents a constructed encoding (<c>true</c>), or
    ///   a primitive encoding (<c>false</c>).
    /// </summary>
    public bool IsConstructed { get; }

    /// <summary>
    ///   The numeric value for this tag.
    /// </summary>
    /// <remarks>
    ///   If <see cref="TagClass"/> is <see cref="Asn1.TagClass.Universal"/>, this value can
    ///   be interpreted as a <see cref="UniversalTagNumber"/>.
    /// </remarks>
    public int TagValue { get; }

    /// <summary>
    ///   Create an <see cref="Asn1Tag"/> for a tag from the UNIVERSAL class.
    /// </summary>
    /// <param name="universalTagNumber">
    ///   The <see cref="UniversalTagNumber"/> value to represent as a tag.
    /// </param>
    /// <param name="isConstructed">
    ///   <c>true</c> for a constructed tag, <c>false</c> for a primitive tag.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="universalTagNumber"/> is not a known value.
    /// </exception>
    public Asn1Tag(UniversalTagNumber universalTagNumber, bool isConstructed = false) { }

    /// <summary>
    ///   Create an <see cref="Asn1Tag"/> for a specified value within a specified tag class.
    /// </summary>
    /// <param name="tagClass">
    ///   The <see cref="TagClass"/> for this tag.
    /// </param>
    /// <param name="tagValue">
    ///   The numeric value for this tag.
    /// </param>
    /// <param name="isConstructed">
    ///   <c>true</c> for a constructed tag, <c>false</c> for a primitive tag.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="tagClass"/> is not a known value --OR--
    ///   <paramref name="tagValue" /> is negative.
    /// </exception>
    /// <remarks>
    ///   This constructor allows for the creation undefined UNIVERSAL class tags.
    /// </remarks>
    public Asn1Tag(TagClass tagClass, int tagValue, bool isConstructed = false) { }

    /// <summary>
    ///   Produce an <see cref="Asn1Tag"/> with the same <seealso cref="TagClass"/> and
    ///   <seealso cref="TagValue"/> values, but whose <seealso cref="IsConstructed"/> is <c>true</c>.
    /// </summary>
    public Asn1Tag AsConstructed() => throw null;

    /// <summary>
    ///   Produce an <see cref="Asn1Tag"/> with the same <see cref="TagClass"/> and
    ///   <see cref="TagValue"/> values, but whose <see cref="IsConstructed"/> is <c>false</c>.
    /// </summary>
    /// <returns>
    public Asn1Tag AsPrimitive() => throw null;
    
    /// <summary>
    ///   Read a BER-encoded tag which starts at <paramref name="source"/>.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if a tag was correctly decoded, <c>false</c> otherwise.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> source, out Asn1Tag tag, out int bytesConsumed) => throw null;
    
    /// <summary>
    ///   Report the number of bytes required for the BER-encoding of this tag.
    /// </summary>
    /// <seealso cref="TryEncode(Span{byte},out int)"/>
    public int CalculateEncodedSize() => throw null;

    /// <summary>
    ///   Write the BER-encoded form of this tag to <paramref name="destination"/>.
    /// </summary>
    ///   <c>false</c> if <paramref name="destination"/>.<see cref="Span{T}.Length"/> &lt;
    ///   <see cref="CalculateEncodedSize"/>(), <c>true</c> otherwise.
    /// </returns>
    public bool TryEncode(Span<byte> destination, out int bytesWritten) => throw null;
            
    /// <summary>
    ///   Write the BER-encoded form of this tag to <paramref name="destination"/>.
    /// </summary>
    /// <seealso cref="CalculateEncodedSize"/>
    /// <exception cref="CryptographicException">
    ///   <paramref name="destination"/>.<see cref="Span{T}.Length"/> &lt; <see cref="CalculateEncodedSize"/>.
    /// </exception>
    public int Encode(Span<byte> destination) => throw null;

    /// <summary>
    ///   Tests if <paramref name="other"/> has the same encoding as this tag.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if <paramref name="other"/> has the same values for
    ///   <see cref="TagClass"/>, <see cref="TagValue"/>, and <see cref="IsConstructed"/>;
    ///   <c>false</c> otherwise.
    /// </returns>
    public bool Equals(Asn1Tag other) => throw null;

    public override bool Equals(object? obj) => throw null;
    public override int GetHashCode() => throw null;
    public static bool operator ==(Asn1Tag left, Asn1Tag right) => throw null;
    public static bool operator !=(Asn1Tag left, Asn1Tag right) => throw null;

    /// <summary>
    ///   Tests if <paramref name="other"/> has the same <see cref="TagClass"/> and <see cref="TagValue"/>
    ///   values as this tag, and does not compare <see cref="IsConstructed"/>.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if <paramref name="other"/> has the same <see cref="TagClass"/> and <see cref="TagValue"/>
    ///   as this tag, <c>false</c> otherwise.
    /// </returns>
    public bool HasSameClassAndValue(Asn1Tag other)
    {
        return TagValue == other.TagValue && TagClass == other.TagClass;
    }

    /// <summary>
    ///   Provides a text representation of this tag suitable for debugging.
    /// </summary>
    public override string ToString() => throw null;

    // Accelerators
    public static readonly Asn1Tag EndOfContents = ...;
    public static readonly Asn1Tag Boolean = ...;
    public static readonly Asn1Tag Integer = ...;
    public static readonly Asn1Tag PrimitiveBitString = ...;
    public static readonly Asn1Tag ConstructedBitString = ...;
    public static readonly Asn1Tag PrimitiveOctetString = ...;
    public static readonly Asn1Tag ConstructedOctetString = ...;
    public static readonly Asn1Tag Null = ...;
    public static readonly Asn1Tag ObjectIdentifier = ...;
    public static readonly Asn1Tag Enumerated = ...;
    public static readonly Asn1Tag Sequence = ...;
    public static readonly Asn1Tag SetOf = ...;
    public static readonly Asn1Tag UtcTime = ...;
    public static readonly Asn1Tag GeneralizedTime = ...;
}

/// <summary>
///   The tag class for a particular ASN.1 tag.
/// </summary>
public enum TagClass : byte
{
    /// <summary>
    ///   The Universal tag class
    /// </summary>
    Universal = 0,

    /// <summary>
    ///   The Application tag class
    /// </summary>
    Application = 0b0100_0000,

    /// <summary>
    ///   The Context-Specific tag class
    /// </summary>
    ContextSpecific = 0b1000_0000,

    /// <summary>
    ///   The Private tag class
    /// </summary>
    Private = 0b1100_0000,
}

/// <summary>
///   Tag assignments for the UNIVERSAL class in ITU-T X.680.
/// </summary>
public enum UniversalTagNumber
{
    /// <summary>
    ///   The reserved identifier for the End-of-Contents marker in an indefinite
    ///   length encoding.
    /// </summary>
    EndOfContents = 0,

    /// <summary>
    ///   The universal class tag value for Boolean.
    /// </summary>
    Boolean = 1,

    /// <summary>
    ///   The universal class tag value for Integer.
    /// </summary>
    Integer = 2,

    /// <summary>
    ///   The universal class tag value for Bit String.
    /// </summary>
    BitString = 3,

    /// <summary>
    ///   The universal class tag value for Octet String.
    /// </summary>
    OctetString = 4,

    /// <summary>
    ///   The universal class tag value for Null.
    /// </summary>
    Null = 5,

    /// <summary>
    ///   The universal class tag value for Object Identifier.
    /// </summary>
    ObjectIdentifier = 6,

    /// <summary>
    ///   The universal class tag value for Object Descriptor.
    /// </summary>
    ObjectDescriptor = 7,

    /// <summary>
    ///   The universal class tag value for External.
    /// </summary>
    External = 8,

    /// <summary>
    ///   The universal class tag value for Instance-Of.
    /// </summary>
    InstanceOf = External,

    /// <summary>
    ///   The universal class tag value for Real.
    /// </summary>
    Real = 9,

    /// <summary>
    ///   The universal class tag value for Enumerated.
    /// </summary>
    Enumerated = 10,

    /// <summary>
    ///   The universal class tag value for Embedded-PDV.
    /// </summary>
    Embedded = 11,

    /// <summary>
    ///   The universal class tag value for UTF8String.
    /// </summary>
    UTF8String = 12,

    /// <summary>
    ///   The universal class tag value for Relative Object Identifier.
    /// </summary>
    RelativeObjectIdentifier = 13,

    /// <summary>
    ///   The universal class tag value for Time.
    /// </summary>
    Time = 14,

    // 15 is reserved

    /// <summary>
    ///   The universal class tag value for Sequence.
    /// </summary>
    Sequence = 16,

    /// <summary>
    ///   The universal class tag value for Sequence-Of.
    /// </summary>
    SequenceOf = Sequence,

    /// <summary>
    ///   The universal class tag value for Set.
    /// </summary>
    Set = 17,

    /// <summary>
    ///   The universal class tag value for Set-Of.
    /// </summary>
    SetOf = Set,

    /// <summary>
    ///   The universal class tag value for NumericString.
    /// </summary>
    NumericString = 18,

    /// <summary>
    ///   The universal class tag value for PrintableString.
    /// </summary>
    PrintableString = 19,

    /// <summary>
    ///   The universal class tag value for TeletexString (T61String).
    /// </summary>
    TeletexString = 20,

    /// <summary>
    ///   The universal class tag value for T61String (TeletexString).
    /// </summary>
    T61String = TeletexString,

    /// <summary>
    ///   The universal class tag value for VideotexString.
    /// </summary>
    VideotexString = 21,

    /// <summary>
    ///   The universal class tag value for IA5String.
    /// </summary>
    IA5String = 22,

    /// <summary>
    ///   The universal class tag value for UTCTime.
    /// </summary>
    UtcTime = 23,

    /// <summary>
    ///   The universal class tag value for GeneralizedTime.
    /// </summary>
    GeneralizedTime = 24,

    /// <summary>
    ///   The universal class tag value for GraphicString.
    /// </summary>
    GraphicString = 25,

    /// <summary>
    ///   The universal class tag value for VisibleString (ISO646String).
    /// </summary>
    VisibleString = 26,

    /// <summary>
    ///   The universal class tag value for ISO646String (VisibleString).
    /// </summary>
    ISO646String = VisibleString,

    /// <summary>
    ///   The universal class tag value for GeneralString.
    /// </summary>
    GeneralString = 27,

    /// <summary>
    ///   The universal class tag value for UniversalString.
    /// </summary>
    UniversalString = 28,

    /// <summary>
    ///   The universal class tag value for an unrestricted character string.
    /// </summary>
    UnrestrictedCharacterString = 29,

    /// <summary>
    ///   The universal class tag value for BMPString.
    /// </summary>
    BMPString = 30,

    /// <summary>
    ///   The universal class tag value for Date.
    /// </summary>
    Date = 31,

    /// <summary>
    ///   The universal class tag value for Time-Of-Day.
    /// </summary>
    TimeOfDay = 32,

    /// <summary>
    ///   The universal class tag value for Date-Time.
    /// </summary>
    DateTime = 33,

    /// <summary>
    ///   The universal class tag value for Duration.
    /// </summary>
    Duration = 34,

    /// <summary>
    ///   The universal class tag value for Object Identifier
    ///   Internationalized Resource Identifier (IRI).
    /// </summary>
    ObjectIdentifierIRI = 35,

    /// <summary>
    ///   The universal class tag value for Relative Object Identifier
    ///   Internationalized Resource Identifier (IRI).
    /// </summary>
    RelativeObjectIdentifierIRI = 36,
}

/// <summary>
///   The encoding ruleset for an <see cref="AsnReader"/> or <see cref="AsnWriter"/>.
/// </summary>
public enum AsnEncodingRules
{
    /// <summary>
    /// ITU-T X.690 Basic Encoding Rules
    /// </summary>
    BER,

    /// <summary>
    /// ITU-T X.690 Canonical Encoding Rules
    /// </summary>
    CER,

    /// <summary>
    /// ITU-T X.690 Distinguished Encoding Rules
    /// </summary>
    DER,
}
```

### Writer API

#### Structure

```C#
/// <summary>
///   A writer for BER-, CER-, and DER-encoded ASN.1 data.
/// </summary>
public sealed partial class AsnWriter : IDisposable
{
    /// <summary>
    ///   Create a new <see cref="AsnWriter"/> with a given set of encoding rules.
    /// </summary>
    /// <param name="ruleSet">The encoding constraints for the writer.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ruleSet"/> is not defined.
    /// </exception>
    public AsnWriter(AsnEncodingRules ruleSet) { }
    
    /// <summary>
    ///   The <see cref="AsnEncodingRules"/> in use by this writer.
    /// </summary>
    public AsnEncodingRules RuleSet { get; }

    /// <summary>
    ///   Release the resources held by this writer.
    /// </summary>
    public void Dispose() => throw null;

    /// <summary>
    ///   Reset the writer to have no data, without releasing resources.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void Reset() => throw null;
    
    /// <summary>
    ///   Gets the number of bytes that would be written by <see cref="TryEncode"/>.
    /// </summary>
    /// <returns>
    ///   The number of bytes that would be written by <see cref="TryEncode"/>, or -1
    ///   if a <see cref="PushSequence()"/> or <see cref="PushSetOf()"/> has not been completed.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public int GetEncodedLength() => throw null;

    /// <summary>
    ///   Write the encoded representation of the data to <paramref name="destination"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   A <see cref="PushSequence()"/> or <see cref="PushSetOf()"/> has not been closed via
    ///   <see cref="PopSequence()"/> or <see cref="PopSetOf()"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public bool TryEncode(Span<byte> destination, out int bytesWritten) => throw null;
    public byte[] Encode() => throw null;

    /// <summary>
    ///   Determines if <see cref="Encode"/> would produce an output identical to
    ///   <paramref name="other"/>.
    /// </summary>
    /// <returns>
    ///   <see langword="true"/> if the pending encoded data is identical to <paramref name="other"/>,
    ///   <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///   A <see cref="PushSequence()"/> or <see cref="PushSetOf()"/> has not been closed via
    ///   <see cref="PopSequence()"/> or <see cref="PopSetOf()"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public bool ValueEquals(ReadOnlySpan<byte> other) => throw null;
}
```

#### Data Injection

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write a single value which has already been encoded.
    /// </summary>
    /// <param name="preEncodedValue">The value to write.</param>
    /// <remarks>
    ///   This method only checks that the tag and length are encoded according to the current ruleset,
    ///   and that the end of the value is the end of the input. The contents are not evaluated for
    ///   semantic meaning.
    /// </remarks>
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   <paramref name="preEncodedValue"/> could not be read under the current encoding rules --OR--
    ///   <paramref name="preEncodedValue"/> has data beyond the end of the first value
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteEncodedValue(ReadOnlySpan<byte> preEncodedValue) => throw null;
}
```

#### Boolean

The general shape of each method group is "void Write\[Type\]\(value\)" and "void Write\[Type\]\(tag, value\)",
because from a code-review perspective it's important to not miss the tag replacement.

The writer never respects the Primitive/Constructed state of an input tag, it writes the form that is correct
for the value being written.

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write a Boolean value with tag UNIVERSAL 1.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteBoolean(bool value) => throw null;

    /// <summary>
    ///   Write a Boolean value with a specified tag.
    /// </summary>
    /// <param name="tag">The tag to write.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentException">
    ///   <paramref name="tag"/>.<see cref="Asn1Tag.TagClass"/> is
    ///   <see cref="TagClass.Universal"/>, but
    ///   <paramref name="tag"/>.<see cref="Asn1Tag.TagValue"/> is not correct for
    ///   the method
    /// </exception>
    public void WriteBoolean(Asn1Tag tag, bool value) => throw null;
}
```

#### Other Simple Types (Null, Object Identifier)

```C#
partial class AsnWriter
{
    public void WriteNull() => throw null;
    public void WriteNull(Asn1Tag tag) => throw null;

    /// <summary>
    ///   Write an Object Identifier with tag UNIVERSAL 6.
    /// </summary>
    /// <param name="oid">The object identifier to write.</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="oid"/> is <c>null</c>
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   <paramref name="oid"/>.<see cref="Oid.Value"/> is not a valid dotted decimal
    ///   object identifier
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteObjectIdentifier(Oid oid) => throw null;
    public void WriteObjectIdentifier(string oidValue) => throw null;
    public void WriteObjectIdentifier(ReadOnlySpan<char> oidValue) => throw null;
    public void WriteObjectIdentifier(Asn1Tag tag, Oid oid) => throw null;
    public void WriteObjectIdentifier(Asn1Tag tag, string oidValue) => throw null;
    public void WriteObjectIdentifier(Asn1Tag tag, ReadOnlySpan<char> oidValue) => throw null;
}
```

#### Integers

ASN.1/BER integers are similar to .NET's BigInteger: unbounded signed integral values (though BigInteger internally is Little Endian, BER is Big Endian)

```C#
partial class AsnWriter
{
    public void WriteInteger(int value) => throw null;
    public void WriteInteger(uint value) => throw null;
    public void WriteInteger(long value) => throw null;
    public void WriteInteger(ulong value) => throw null;
    public void WriteInteger(BigInteger value) => throw null;

    /// <summary>
    ///   Write an Integer value with tag UNIVERSAL 2.
    /// </summary>
    /// <param name="value">The integer value to write, in signed big-endian byte order.</param>
    /// <exception cref="CryptographicException">
    ///   the 9 most sigificant bits are all set --OR--
    ///   the 9 most sigificant bits are all unset
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteInteger(ReadOnlySpan<byte> value) => throw null;
    public void WriteInteger(Asn1Tag tag, ReadOnlySpan<byte> value) => throw null;
}
```

#### Bit Strings

A bit string is encoded as multiples of 8 bits, but can indicate the number of bits in the last byte that are only there because of byte alignment ("unused bits").

CER and DER require that unused bits be set to 0, the writer validates that they're 0 in all modes (it does not coerce the data, or infer an unused bit count).

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write a Bit String value with a tag UNIVERSAL 3.
    /// </summary>
    /// <param name="bitString">The value to write.</param>
    /// <param name="unusedBitCount">
    ///   The number of trailing bits which are not semantic.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="unusedBitCount"/> is not in the range [0,7]
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   <paramref name="bitString"/> has length 0 and <paramref name="unusedBitCount"/> is not 0 --OR--
    ///   <paramref name="bitString"/> is not empty and any of the bits identified by
    ///   <paramref name="unusedBitCount"/> is set
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteBitString(ReadOnlySpan<byte> bitString, int unusedBitCount = 0) => throw null;
    public void WriteBitString(Asn1Tag tag, ReadOnlySpan<byte> bitString, int unusedBitCount = 0) => throw null;

    /// <summary>
    ///   Write a Bit String value via a callback, with tag UNIVERSAL 3.
    /// </summary>
    /// <param name="byteLength">The total number of bytes to write.</param>
    /// <param name="state">A state object to pass to <paramref name="action"/>.</param>
    /// <param name="action">A callback to invoke for populating the Bit String.</param>
    /// <param name="unusedBitCount">
    ///   The number of trailing bits which are not semantic.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="byteLength"/> is negative --OR--
    ///   <paramref name="unusedBitCount"/> is not in the range [0,7]
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   <paramref name="byteLength"/> is 0 and <paramref name="unusedBitCount"/> is not 0 --OR--
    ///   <paramref name="byteLength"/> is not 0 and any of the bits identified by
    ///   <paramref name="unusedBitCount"/> is set
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    public void WriteBitString<TState>(
        int byteLength,
        TState state,
        SpanAction<byte, TState> action,
        int unusedBitCount = 0) => throw null;
    public void WriteBitString<TState>(
        Asn1Tag tag,
        int byteLength,
        TState state,
        SpanAction<byte, TState> action,
        int unusedBitCount = 0) => throw null;
}
```

#### Sequences, SequenceOfs, SetOfs

An ASN.1 sequence is an ordered set of fields, effectively a class/struct declaration.
It is encoded the same as an ASN.1 sequence-of, which is an ordered list of semi-homogenous values ("semi-" because the `ANY` type or a `CHOICE` type can be used as the element type).
In both cases, the data is written in the order it is presented.

An ASN.1 set is nominally an unordered set of fields.
The requirements of CER-encoded sets cannot be fulfilled by a general-purpose writer (it requires the data schema),
so `SET` types are not supported. (SET types are not used in practice.)

An ASN.1 set-of is an unordered collection.
For CER and DER encodings the `SET-OF` values must be sorted, per a spec-defined sort.
The writer re-normalizes all writes to a Set-Of to have been in the correct order when the context closes--except in BER mode,
where the order is preserved as written.
(DER `SET` and `SET-OF` are encoded the same, CER `SET` and `SET-OF` are not necessarily encoded the same).

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Begin writing a Sequence with tag UNIVERSAL 16.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="PushSequence(Asn1Tag)"/>
    /// <seealso cref="PopSequence()"/>
    public void PushSequence() => throw null;
    public void PushSequence(Asn1Tag tag) => throw null;
    // Because SEQUENCE and SEQUENCE-OF are always encoded the same
    // there is not a separate method group for SequenceOf.
        
    /// <summary>
    ///   Indicate that the open Sequence with tag UNIVERSAL 16 is closed,
    ///   returning the writer to the parent context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   the writer is not currently positioned within a Sequence with tag UNIVERSAL 16
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="PopSequence(Asn1Tag)"/>
    /// <seealso cref="PushSequence()"/>
    public void PopSequence() => throw null;
    public void PopSequence(Asn1Tag tag) => throw null;

    /// <summary>
    ///   Begin writing a Set-Of with a tag UNIVERSAL 17.
    /// </summary>
    public void PushSetOf() => throw null;
    public void PushSetOf(Asn1Tag tag) => throw null;
    public void PopSetOf() => throw null;
    public void PopSetOf(Asn1Tag tag) => throw null;
    // SET is not actually supported by the writer, only the rules for SET-OF are used.
}
```

#### Octet Strings

An octet string is just `ReadOnlySpan<byte>`.

Some ASN.1 types say a value is an OCTET STRING, then (usually contextually) say that the value is a BER/DER-encoded representation of some other type.
The writer supports pushing/popping octet strings to enable linearly writing those types of value.

```C#
partial class AsnWriter
{
    public void WriteOctetString(ReadOnlySpan<byte> octetString) => throw null;
    public void WriteOctetString(Asn1Tag tag, ReadOnlySpan<byte> octetString) => throw null;

    public void WriteOctetString<TState>(
        int byteLength,
        TState state,
        SpanAction<byte, TState> action) => throw null;
    public void WriteOctetString<TState>(
        Asn1Tag tag,
        int byteLength,
        TState state,
        SpanAction<byte, TState> action) => throw null;

    // Technically not a constructed/composable type, but allows encoding nested
    // values in open construction types.
    //
    // For more information on the exception model, see Sequence.
    public void PushOctetString() => throw null;
    public void PushOctetString(Asn1Tag tag) => throw null;
    public void PopOctetString() => throw null;
    public void PopOctetString(Asn1Tag tag) => throw null;
}
```

#### Enumerated Values

An ASN.1 Enumerated value is equivalent to a .NET non-\[Flags\] enum:
Possible values are named a priori, and only one value can be chosen.

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write a non-[<see cref="FlagsAttribute"/>] enum value as an Enumerated with
    ///   tag UNIVERSAL 10.
    /// </summary>
    /// <param name="enumValue">The boxed enumeration value to write</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="enumValue"/> is <c>null</c>
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <paramref name="enumValue"/> is not a boxed enum value --OR--
    ///   the unboxed type of <paramref name="enumValue"/> is declared [<see cref="FlagsAttribute"/>]
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteEnumeratedValue(Asn1Tag,object)"/>
    /// <seealso cref="WriteEnumeratedValue{T}(T)"/>
    public void WriteEnumeratedValue(object enumValue) => throw null;

    public void WriteEnumeratedValue<TEnum>(TEnum enumValue) where TEnum : Enum => throw null;

    public void WriteEnumeratedValue(Asn1Tag tag, object enumValue) => throw null;
    public void WriteEnumeratedValue<TEnum>(Asn1Tag tag, TEnum enumValue) where TEnum : Enum => throw null;
}
```

#### NamedBitList Values

An ASN.1 NamedBitList value is equivalent to a .NET \[Flags\] enum:
Possible values are named a priori, and multiple values can be chosen.

BER/CER/DER-encoded NamedBitList values write bits "from the left", but the natural .NET ordering is "from the right".
The writer assumes .NET ordering for enums and translates.

```asn.1
KeyUsage ::= BIT STRING {
     digitalSignature        (0),
     nonRepudiation          (1), -- recent editions of X.509 have
                                  -- renamed this bit to contentCommitment
     keyEncipherment         (2),
     dataEncipherment        (3),
     keyAgreement            (4),
     keyCertSign             (5),
     cRLSign                 (6),
     encipherOnly            (7),
     decipherOnly            (8) }
```

Allows for a .NET enum like

```C#
[Flags]
public enum X509KeyUsageCSharpStyle
{
    None = 0,
    DigitalSignature = 1 << 0,
    NonRepudiation = 1 << 1,
    KeyEncipherment = 1 << 2,
    DataEncipherment = 1 << 3,
    KeyAgreement = 1 << 4,
    KeyCertSign = 1 << 5,
    CrlSign = 1 << 6,
    EncipherOnly = 1 << 7,
    DecipherOnly = 1 << 8,
}
```

(Note that this does not match the real `X509KeyUsage` enum, because the real type matches the BER encoding for legacy reasons.)

`WriteNamedBitList(DigitalSignature | KeyEncipherment | DataEncipherment)` encodes as `03 02 04 B0` (BIT STRING, 2 bytes, 4 unused/padding bits, bits 1, 3, 4 (from the left, 1-indexed) are set (`0b1011_xxxx`)).

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write a [<see cref="FlagsAttribute"/>] enum value as a NamedBitList with
    ///   tag UNIVERSAL 3.
    /// </summary>
    /// <param name="enumValue">The boxed enumeration value to write</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="enumValue"/> is <c>null</c>
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <paramref name="enumValue"/> is not a boxed enum value --OR--
    ///   the unboxed type of <paramref name="enumValue"/> is not declared [<see cref="FlagsAttribute"/>]
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteNamedBitList(Asn1Tag,object)"/>
    /// <seealso cref="WriteNamedBitList{T}(T)"/>
    public void WriteNamedBitList(object enumValue) => throw null;
    public void WriteNamedBitList<TEnum>(TEnum enumValue) where TEnum : Enum => throw null;
    public void WriteNamedBitList(Asn1Tag tag, object enumValue) => throw null;
    public void WriteNamedBitList<TEnum>(Asn1Tag tag, TEnum enumValue) where TEnum : Enum => throw null;
}
```

#### Date/Time Types (UTCTime, GeneralizedTime)

The ASN.1 UTCTime type represents time, to the second, with a two-digit year.
BER allows for a variety of encodings, CER and DER always have to write as yyMMddHHmmssZ.
This writer always writes in the CER/DER compatible form.
Writing a UTCTime has an overload that allows the caller to indicate the maximum two digit year value, and the writer will throw an exception if the input time is out of that range.

The ASN.1 GeneralizedTime type represents time, to an arbitrary decimal subsecond, with a four-digit year.
BER allows for a variety of encodings (such as hours and fractional hours), but CER and DER always have to write as yyyyMMddHHmmss[.sss]Z.
This writer always writes in the CER/DER-compatible form.
Because some specifications, such as X.509 Public Key Certificates, indicate that fractional seconds should be omitted, there's a parameter to ignore (truncate) the fractional seconds from a parameter value.

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write the provided <see cref="DateTimeOffset"/> as a UTCTime with tag
    ///   UNIVERSAL 23, and accepting the two-digit year as valid in context.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteUtcTime(Asn1Tag,DateTimeOffset)"/>
    /// <seealso cref="WriteUtcTime(DateTimeOffset,int)"/>
    public void WriteUtcTime(DateTimeOffset value) => throw null;
    public void WriteUtcTime(Asn1Tag tag, DateTimeOffset value) => throw null;

    /// <summary>
    ///   Write the provided <see cref="DateTimeOffset"/> as a UTCTime with tag
    ///   UNIVERSAL 23, provided the year is in the allowed range.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="twoDigitYearMax">
    ///   The maximum valid year for <paramref name="value"/>, after conversion to UTC.
    ///   For the X.509 Time.utcTime range of 1950-2049, pass <c>2049</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="value"/>.<see cref="DateTimeOffset.Year"/> (after conversion to UTC)
    ///   is not in the range
    ///   (<paramref name="twoDigitYearMax"/> - 100, <paramref name="twoDigitYearMax"/>]
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteUtcTime(Asn1Tag,DateTimeOffset,int)"/>
    /// <seealso cref="System.Globalization.Calendar.TwoDigitYearMax"/>
    public void WriteUtcTime(DateTimeOffset value, int twoDigitYearMax) => throw null;
    public void WriteUtcTime(Asn1Tag tag, DateTimeOffset value, int twoDigitYearMax) => throw null;     

    /// <summary>
    ///   Write the provided <see cref="DateTimeOffset"/> as a GeneralizedTime with tag
    ///   UNIVERSAL 24, optionally excluding the fractional seconds.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="omitFractionalSeconds">
    ///   <c>true</c> to treat the fractional seconds in <paramref name="value"/> as 0 even if
    ///   a non-zero value is present.
    /// </param>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteGeneralizedTime(Asn1Tag,DateTimeOffset,bool)"/>
    public void WriteGeneralizedTime(DateTimeOffset value, bool omitFractionalSeconds = false) => throw null;
    public void WriteGeneralizedTime(Asn1Tag tag, DateTimeOffset value, bool omitFractionalSeconds = false) => throw null;
}
```
    
#### Text Strings

Rather than distinct overloads for each type of textual encoding, use one method group and have the caller
provide the encoding type via a UniversalTagNumber.

 1. UTF8String (12) - UTF-8
 2. NumericString (18) - ASCII digits + ASCII space
 3. PrintableString(19) - ASCII upper + ASCII lower + ASCII digits + select punctuation
 4. T61String (20) - Encodes as UTF-8 for complex compatibility reasons.
 5. IA5String (22) - ASCII 0x00-0x7F (inclusive)
 6. VisibleString (26) - ASCII 0x20-0x7E (inclusive)
 7. BMPString (30) - UTF-16BE, surrogates disallowed.

The other textual strings (e.g. GraphicalString) are not supported, and have to use WriteEncodedValue.

```C#
partial class AsnWriter
{
    /// <summary>
    ///   Write the provided string using the specified encoding type using the UNIVERSAL
    ///   tag corresponding to the encoding type.
    /// </summary>
    /// <param name="encodingType">
    ///   The <see cref="UniversalTagNumber"/> corresponding to the encoding to use.
    /// </param>
    /// <param name="str">The string to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="str"/> is <c>null</c></exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="encodingType"/> is not a restricted character string encoding type --OR--
    ///   <paramref name="encodingType"/> is a restricted character string encoding type that is not
    ///   currently supported by this method
    /// </exception>
    /// <exception cref="EncoderFallbackException">
    ///  <paramref name="str"/> is not valid in the requested encoding.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The writer has been Disposed.</exception>
    /// <seealso cref="WriteCharacterString(Asn1Tag,UniversalTagNumber,string)"/>
    public void WriteCharacterString(UniversalTagNumber encodingType, string str) => throw null;
    public void WriteCharacterString(UniversalTagNumber encodingType, ReadOnlySpan<char> str) => throw null;
    public void WriteCharacterString(Asn1Tag tag, UniversalTagNumber encodingType, string str) => throw null;
    public void WriteCharacterString(Asn1Tag tag, UniversalTagNumber encodingType, ReadOnlySpan<char> str) => throw null;
}
```

### AsnReader API

#### Structure

```C#
/// <summary>
///   A stateful, forward-only reader for BER-, CER-, or DER-encoded ASN.1 data.
/// </summary>
public partial class AsnReader
{
    /// <summary>
    ///   Construct an <see cref="AsnReader"/> over <paramref name="data"/> with a given ruleset.
    /// </summary>
    /// <param name="data">The data to read.</param>
    /// <param name="ruleSet">The encoding constraints for the reader.</param>
    /// <remarks>
    ///   This constructor does not evaluate <paramref name="data"/> for correctness,
    ///   any correctness checks are done as part of member methods.
    ///
    ///   This constructor does not copy <paramref name="data"/>. The caller is responsible for
    ///   ensuring that the values do not change until the reader is finished.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ruleSet"/> is not defined.
    /// </exception>
    public AsnReader(ReadOnlyMemory<byte> data, AsnEncodingRules ruleSet) => throw null;

    /// <summary>
    ///   The <see cref="AsnEncodingRules"/> in use by this reader.
    /// </summary>
    public AsnEncodingRules RuleSet { get; }

    /// <summary>
    ///   An indication of whether or not the reader has remaining data available to process.
    /// </summary>
    public bool HasData { get; }

    /// <summary>
    ///   Throws a standardized <see cref="CryptographicException"/> if the reader has remaining
    ///   data, performs no function if <see cref="HasData"/> returns <c>false</c>.
    /// </summary>
    /// <remarks>
    ///   This method provides a standardized target and standardized exception for reading a
    ///   "closed" structure, such as the nested content for an explicitly tagged value.
    /// </remarks>
    public void ThrowIfNotEmpty() => throw null;

    /// <summary>
    ///   Read the encoded tag at the next data position, without advancing the reader.
    /// </summary>
    /// <returns>
    ///   The decoded <see cref="Asn1Tag"/> value.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   a tag could not be decoded at the reader's current position.
    /// </exception>
    public Asn1Tag PeekTag() => throw null;
}
```
    
#### Direct Data Interaction

```C#
partial class AsnReader
{
    /// <summary>
    ///   Get a <see cref="ReadOnlyMemory{byte}"/> view of the next encoded value without
    ///   advancing the reader. For indefinite length encodings this includes the
    ///   End of Contents marker.
    /// </summary>
    /// <returns>A <see cref="ReadOnlyMemory{byte}"/> view of the next encoded value.</returns>
    /// <exception cref="CryptographicException">
    ///   The reader is positioned at a point where the tag or length is invalid
    ///   under the current encoding rules.
    /// </exception>
    /// <seealso cref="PeekContentBytes"/>
    /// <seealso cref="ReadEncodedValue"/>
    public ReadOnlyMemory<byte> PeekEncodedValue() => throw null;

    /// <summary>
    ///   Get a <see cref="ReadOnlyMemory{byte}"/> view of the content octets (bytes) of the
    ///   next encoded value without advancing the reader.
    /// </summary>
    /// <returns>
    ///   A <see cref="ReadOnlyMemory{byte}"/> view of the contents octets of the next encoded value.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   The reader is positioned at a point where the tag or length is invalid
    ///   under the current encoding rules.
    /// </exception>
    /// <seealso cref="PeekEncodedValue"/>
    public ReadOnlyMemory<byte> PeekContentBytes() => throw null;

    /// <summary>
    ///   Get a <see cref="ReadOnlyMemory{byte}"/> view of the next encoded value,
    ///   and advance the reader past it. For an indefinite length encoding this includes
    ///   the End of Contents marker.
    /// </summary>
    /// <returns>A <see cref="ReadOnlyMemory{byte}"/> view of the next encoded value.</returns>
    /// <seealso cref="PeekEncodedValue"/>
    public ReadOnlyMemory<byte> ReadEncodedValue() => throw null;
}
```

#### Boolean

The general shape of each method group is "\[Type\] Read\[Type\]\(\)" and "\[Type\] Read\[Type\]\(tag\)".

The reader never respects the Primitive/Constructed state of an input tag, it matches only on the class and value.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as a Boolean with tag UNIVERSAL 1.
    /// </summary>
    /// <returns>The next value as a Boolean.</returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    public bool ReadBoolean() => ReadBoolean(Asn1Tag.Boolean);

    /// <summary>
    ///   Reads the next value as a Boolean with a specified tag.
    /// </summary>
    /// <param name="expectedTag">The tag to check for before reading.</param>
    /// <returns>The next value as a Boolean.</returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <paramref name="expectedTag"/>.<see cref="Asn1Tag.TagClass"/> is
    ///   <see cref="TagClass.Universal"/>, but
    ///   <paramref name="expectedTag"/>.<see cref="Asn1Tag.TagValue"/> is not correct for
    ///   the method
    /// </exception>
    public bool ReadBoolean(Asn1Tag expectedTag) => throw null;
}
```

#### Other Simple Types (Null, Object Identifier)

```C#
partial class AsnReader
{
    public void ReadNull() => throw null;
    public void ReadNull(Asn1Tag tag) => throw null;

    // Maybe only the string versions are needed?
    public Oid ReadObjectIdentifier() => throw null;
    public Oid ReadObjectIdentifier(Asn1Tag tag) => throw null;
    public string ReadObjectIdentifierAsString() => throw null;
    public string ReadObjectIdentifierAsString(Asn1Tag expectedTag) => throw null;
}
```

#### Integers

ASN.1/BER integers are similar to .NET's BigInteger: unbounded signed integral values (though BigInteger internally is Little Endian, BER is Big Endian)

```C#
partial class AsnReader
{
    public ReadOnlyMemory<byte> ReadIntegerBytes() => throw null;
    public ReadOnlyMemory<byte> ReadIntegerBytes(Asn1Tag tag) => throw null;
    public BigInteger ReadInteger() => throw null;
    public BigInteger ReadInteger(Asn1Tag expectedTag) => throw null;

    /// <summary>
    ///   Reads the next value as an Integer with tag UNIVERSAL 2, interpreting the contents
    ///   as an <see cref="int"/>.
    /// </summary>
    /// <param name="value">
    ///   On success, receives the <see cref="int"/> value represented
    /// </param>
    /// <returns>
    ///   <c>false</c> and does not advance the reader if the value is not between
    ///   <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>, inclusive; otherwise
    ///   <c>true</c> is returned and the reader advances.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    public bool TryReadInt32(out int value) => throw null;
    public bool TryReadInt32(Asn1Tag expectedTag, out int value) => throw null;
    public bool TryReadUInt32(out uint value) => throw null;
    public bool TryReadUInt32(Asn1Tag expectedTag, out uint value) => throw null;
    
    public bool TryReadInt64(out long value) => throw null;
    public bool TryReadInt64(Asn1Tag expectedTag, out long value) => throw null;
    public bool TryReadUInt64(out ulong value) => throw null;
    public bool TryReadUInt64(Asn1Tag expectedTag, out ulong value) => throw null;
    
    // Maybe 16/8 bits versions aren't needed?
    public bool TryReadInt16(out short value) => throw null;
    public bool TryReadInt16(Asn1Tag expectedTag, out short value) => throw null;
    public bool TryReadUInt16(out ushort value) => throw null;
    public bool TryReadUInt16(Asn1Tag expectedTag, out ushort value) => throw null;
    
    public bool TryReadInt8(out sbyte value) => throw null;
    public bool TryReadInt8(Asn1Tag expectedTag, out sbyte value) => throw null;
    public bool TryReadUInt8(out byte value) => throw null;
    public bool TryReadUInt8(Asn1Tag expectedTag, out byte value) => throw null;
}
```

#### Bit Strings

A bit string is encoded as multiples of 8 bits,
but can indicate the number of bits in the last byte that are only there because of byte alignment ("unused bits").

Layout:
- In DER a `BIT STRING` value must always be in a single, primitive value; all data is contiguous.
- In CER a value of 7992 bits, or fewer, must be in a single, primitive value.
  Otherwise data must be chunked every 7992 bits into smaller nested encodings.
- In BER the data can be in a single primitive value, or arbitrarily chunked in smaller encodings.

Validation:
- For the constructed encoding, any segment other than the last must have 0 unused bits. This is enforced by the reader.
- For CER and DER any unused bits must have a zero-value. This is enforced by the reader in those modes.
- The 7992-bit chunking is enforced when reading in CER mode.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as a BIT STRING with tag UNIVERSAL 3, returning the contents
    ///   as a <see cref="ReadOnlySpan{T}"/> over the original data.
    /// </summary>
    /// <param name="unusedBitCount">
    ///   On success, receives the number of bits in the last byte which were reported as
    ///   "unused" by the writer.
    /// </param>
    /// <param name="value">
    ///   On success, receives a <see cref="ReadOnlySpan{T}"/> over the original data
    ///   corresponding to the value of the BIT STRING.
    /// </param>
    /// <returns>
    ///   <c>true</c> and advances the reader if the BIT STRING value had a primitive encoding,
    ///   <c>false</c> and does not advance the reader if it had a constructed encoding.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <seealso cref="TryCopyBitStringBytes(Span{byte},out int,out int)"/>
    public bool TryReadPrimitiveBitStringValue(out int unusedBitCount, out ReadOnlyMemory<byte> value) => throw null;
    public bool TryReadPrimitiveBitStringValue(Asn1Tag tag, out int unusedBitCount, out ReadOnlyMemory<byte> value) => throw null;
    
    // REVIEW: "Copy" or "Read"?
    public bool TryCopyBitStringBytes(
        Span<byte> destination,
        out int unusedBitCount,
        out int bytesWritten) => throw null;

    // REVIEW: Parameter ordering? Tag first for local consistency,
    // destination first for Try-Write consistency
    public bool TryCopyBitStringBytes(
        Asn1Tag tag,
        Span<byte> destination,
        out int unusedBitCount,
        out int bytesWritten) => throw null;

    public byte[] ReadBitString(out int unusedBitCount) => throw null;
    public byte[] ReadBitString(Asn1Tag tag, out int unusedBitCount) => throw null;
}
```

#### Octet Strings

An octet string is just `ReadOnlySpan<byte>`.

Layout:
- In DER a `OCTET STRING` value must always be in a single, primitive value; all data is contiguous.
- In CER a value of 1000 bytes, or fewer, must be in a single, primitive value.
  Otherwise data must be chunked every 1000 bytes into smaller nested encodings.
- In BER the data can be in a single primitive value, or arbitrarily chunked in smaller encodings.

Validation:
- The 1000-byte chunking is enforced in CER mode.

```C#
partial class AsnReader
{
    public bool TryReadPrimitiveOctetStringBytes(out ReadOnlyMemory<byte> contents) => throw null;
    public bool TryReadPrimitiveOctetStringBytes(Asn1Tag expectedTag, out ReadOnlyMemory<byte> contents) => throw null;

    public bool TryCopyOctetStringBytes(
        Span<byte> destination,
        out int bytesWritten) => throw null;

    public bool TryCopyOctetStringBytes(
        Asn1Tag expectedTag,
        Span<byte> destination,
        out int bytesWritten)  => throw null;

    public byte[] ReadOctetString() => throw null;
    public byte[] ReadOctetString(Asn1Tag expectedTag) => throw null;
}
```

#### Sequences, SequenceOfs, SetOfs

An ASN.1 sequence is an ordered set of fields, effectively a class/struct declaration.
It is encoded the same as an ASN.1 sequence-of, which is an ordered list of semi-homogenous values ("semi-" because the `ANY` type or a `CHOICE` type can be used as the element type).
In both cases, the data is read in the order it is presented.

An ASN.1 set is nominally an unordered set of fields.
The requirements of CER-encoded sets cannot be verified by a general-purpose reader (it requires the data schema), so `SET` types are not supported.
(SET types are not used in practice.)

An ASN.1 set-of is an unordered collection.
For CER and DER encodings the `SET-OF` values must be sorted, per a spec-defined sort.
The reader verifies the elements are sorted during the call to `ReadSetOf` for CER and DER modes, unless requested otherwise.
This allows selective interoperability with non-conforming readers, as well as allows the user to opt-in to reading a `SET` value using the data in the order presented.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as a SEQUENCE or SEQUENCE-OF with tag UNIVERSAL 16
    ///   and returns the result as an <see cref="AsnReader"/> positioned at the first
    ///   value in the sequence (or with <see cref="HasData"/> == <c>false</c>).
    /// </summary>
    /// <remarks>
    ///   the nested content is not evaluated by this method, and may contain data
    ///   which is not valid under the current encoding rules.
    /// </remarks>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <see cref="ReadSequence(Asn1Tag)"/>
    public AsnReader ReadSequence() => ReadSequence(Asn1Tag.Sequence);
    public AsnReader ReadSequence(Asn1Tag tag) => throw null;
    // Because SEQUENCE and SEQUENCE-OF are always encoded the same
    // there is not a separate method group for SequenceOf.
    
    /// <summary>
    ///   Reads the next value as a SET-OF with the specified tag
    ///   and returns the result as an <see cref="AsnReader"/> positioned at the first
    ///   value in the set-of (or with <see cref="HasData"/> == <c>false</c>).
    /// </summary>
    /// <param name="skipSortOrderValidation">
    ///   <c>true</c> to always accept the data in the order it is presented,
    ///   <c>false</c> to verify that the data is sorted correctly when the
    ///   encoding rules say sorting was required (CER and DER).
    /// </param>
    /// <remarks>
    ///   the nested content is not evaluated by this method (aside from sort order, when
    ///   required), and may contain data which is not valid under the current encoding rules.
    /// </remarks>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    public AsnReader ReadSetOf(bool skipSortOrderValidation = false) => throw null;
    public AsnReader ReadSetOf(Asn1Tag tag, bool skipSortOrderValidation = false) => throw null;
    // SET is not actually supported by the writer, only the rules for SET-OF are used.
}
```

#### Enumerated Values

An ASN.1 Enumerated value is equivalent to a .NET non-\[Flags\] enum:
Possible values are named a priori, and only one value can be chosen.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as an Enumerated value with tag UNIVERSAL 10,
    ///   returning the contents as a <see cref="ReadOnlySpan{T}"/> over the original data.
    /// </summary>
    /// <returns>
    ///   The bytes of the Enumerated value, in signed big-endian form.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <seealso cref="ReadEnumeratedValue{TEnum}()"/>
    public ReadOnlyMemory<byte> ReadEnumeratedBytes() => throw null;
    public ReadOnlyMemory<byte> ReadEnumeratedBytes(Asn1Tag tag) => throw null;

    /// <summary>
    ///   Reads the next value as an Enumerated value with tag UNIVERSAL 10, converting it to
    ///   the non-[<see cref="FlagsAttribute"/>] enum specified by <typeparamref name="TEnum"/>.
    /// </summary>
    /// <typeparam name="TEnum">Destination enum type</typeparam>
    /// <returns>
    ///   the Enumerated value converted to a <typeparamref name="TEnum"/>.
    /// </returns>
    /// <remarks>
    ///   This method does not validate that the return value is defined within
    ///   <typeparamref name="TEnum"/>.
    /// </remarks>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules --OR--
    ///   the encoded value is too big to fit in a <typeparamref name="TEnum"/> value
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <typeparamref name="TEnum"/> is not an enum type --OR--
    ///   <typeparamref name="TEnum"/> was declared with <see cref="FlagsAttribute"/>
    /// </exception>
    /// <seealso cref="ReadEnumeratedValue{TEnum}(Asn1Tag)"/>
    public TEnum ReadEnumeratedValue<TEnum>() where TEnum : Enum => throw null;
    public TEnum ReadEnumeratedValue<TEnum>(Asn1Tag expectedTag) where TEnum : Enum => throw null;
    
    public Enum ReadEnumeratedValue(Type tEnum) => throw null;
    // REVIEW: Parameter ordering
    public Enum ReadEnumeratedValue(Asn1Tag tag, Type tEnum) => throw null;
}
```

#### NamedBitList Values

An ASN.1 NamedBitList value is equivalent to a .NET \[Flags\] enum:
Possible values are named a priori, and multiple values can be chosen.

BER/CER/DER-encoded NamedBitList values write bits "from the left", but the natural .NET ordering is "from the right".
The reader assumes .NET ordering for enums and translates.

```asn.1
KeyUsage ::= BIT STRING {
     digitalSignature        (0),
     nonRepudiation          (1), -- recent editions of X.509 have
                                  -- renamed this bit to contentCommitment
     keyEncipherment         (2),
     dataEncipherment        (3),
     keyAgreement            (4),
     keyCertSign             (5),
     cRLSign                 (6),
     encipherOnly            (7),
     decipherOnly            (8) }
```

Allows for a .NET enum like

```C#
[Flags]
public enum X509KeyUsageCSharpStyle
{
    None = 0,
    DigitalSignature = 1 << 0,
    NonRepudiation = 1 << 1,
    KeyEncipherment = 1 << 2,
    DataEncipherment = 1 << 3,
    KeyAgreement = 1 << 4,
    KeyCertSign = 1 << 5,
    CrlSign = 1 << 6,
    EncipherOnly = 1 << 7,
    DecipherOnly = 1 << 8,
}
```

(Note that this does not match the real `X509KeyUsage` enum, because the real type matches the BER encoding for legacy reasons.)

`ReadNamedBitList<X509KeyUsageCSharpStyle>()` for the data `03 02 04 B0` (BIT STRING, 2 bytes, 4 unused/padding bits, bits 1, 3, 4 (from the left, 1-indexed) are set (`0b1011_xxxx`)) decodes as `DigitalSignature | KeyEncipherment | DataEncipherment`.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as a NamedBitList with tag UNIVERSAL 3, converting it to the
    ///   [<see cref="FlagsAttribute"/>] enum specified by <typeparamref name="TFlagsEnum"/>.
    /// </summary>
    /// <typeparam name="TFlagsEnum">Destination enum type</typeparam>
    /// <returns>
    ///   the NamedBitList value converted to a <typeparamref name="TFlagsEnum"/>.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules --OR--
    ///   the encoded value is too big to fit in a <typeparamref name="TFlagsEnum"/> value
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <typeparamref name="TFlagsEnum"/> is not an enum type --OR--
    ///   <typeparamref name="TFlagsEnum"/> was not declared with <see cref="FlagsAttribute"/>
    /// </exception>
    /// <seealso cref="ReadNamedBitListValue{TFlagsEnum}(Asn1Tag)"/>
    public TFlagsEnum ReadNamedBitListValue<TFlagsEnum>() where TFlagsEnum : Enum => throw null;
    public TFlagsEnum ReadNamedBitListValue<TFlagsEnum>(Asn1Tag tag) where TFlagsEnum : Enum => throw null;

    public Enum ReadNamedBitListValue(Type tFlagsEnum) => throw null;
    public Enum ReadNamedBitListValue(Asn1Tag expectedTag, Type tFlagsEnum) => throw null;
}
```
  
#### Date/Time Types (UTCTime, GeneralizedTime)

The ASN.1 UTCTime type represents time, to the second, with a two-digit year.
BER allows for a variety of encodings, CER and DER always have to write as yyMMddHHmmssZ.
Reading a UTCTime requires specifying the two-digit year max, which determines the century.
This parameter is defaulted to 2049, which matches the X.509 Public Key Certificate transition from UTCTime (1950-2049) to GeneralizedTime (2050+).
The reader validates that CER/DER data is in the correct format, but supports the alternative formats in BER mode.

The ASN.1 GeneralizedTime type represents time, to an arbitrary decimal subsecond, with a four-digit year.
BER allows for a variety of encodings (such as hours and fractional hours), but CER and DER always have to write as yyyyMMddHHmmss[.sss]Z.
Because some specifications, such as X.509 Public Key Certificates, indicate that fractional seconds should be omitted, there's a parameter to report an error for fractional seconds having been specified (even if they interpret as 0).
The reader validates that CER/DER data is in the correct format.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as a UTCTime with tag UNIVERSAL 23.
    /// </summary>
    /// <param name="twoDigitYearMax">
    ///   The largest year to represent with this value.
    ///   The default value, 2049, represents the 1950-2049 range for X.509 certificates.
    /// </param>
    /// <returns>
    ///   a DateTimeOffset representing the value encoded in the UTCTime.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <seealso cref="System.Globalization.Calendar.TwoDigitYearMax"/>
    /// <seealso cref="ReadUtcTime(System.Security.Cryptography.Asn1.Asn1Tag,int)"/>
    public DateTimeOffset ReadUtcTime(int twoDigitYearMax = 2049) => throw null;
    public DateTimeOffset ReadUtcTime(Asn1Tag expectedTag, int twoDigitYearMax = 2049) => throw null;

    /// <summary>
    ///   Reads the next value as a GeneralizedTime with tag UNIVERSAL 24.
    /// </summary>
    /// <param name="disallowFractions">
    ///   <c>true</c> to cause a <see cref="CryptographicException"/> to be thrown if a
    ///   fractional second is encountered, such as the restriction on the PKCS#7 Signing
    ///   Time attribute.
    /// </param>
    /// <returns>
    ///   a DateTimeOffset representing the value encoded in the GeneralizedTime.
    /// </returns>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    public DateTimeOffset ReadGeneralizedTime(bool disallowFractions = false) => throw null;
    public DateTimeOffset ReadGeneralizedTime(Asn1Tag expectedTag, bool disallowFractions = false) => throw null;
}
```
    
#### Text Strings

Text strings share the chunked encoding rules with OCTET STRING values (chunking at bytes, not at code units or codepoints).
Rather than distinct method groups for each type of supported textual encoding, the caller provides the encoding type via a UniversalTagNumber.

 1. UTF8String (12) - UTF-8
 2. NumericString (18) - ASCII digits + ASCII space
 3. PrintableString(19) - ASCII upper + ASCII lower + ASCII digits + select punctuation
 4. T61String (20) - Reads as UTF-8 with a fallback to ISO-8859-1 ("latin1") for complex compatibility reasons.
 5. IA5String (22) - ASCII 0x00-0x7F (inclusive)
 6. VisibleString (26) - ASCII 0x20-0x7E (inclusive)
 7. BMPString (30) - UTF-16BE, surrogates disallowed.

The other textual strings (e.g. GraphicalString) are not supported for reading as `string` or `char` values, and have to use the "Bytes" methods.

```C#
partial class AsnReader
{
    /// <summary>
    ///   Reads the next value as character string with a UNIVERSAL tag appropriate to the specified
    ///   encoding type, returning the decoded value as a <see cref="string"/>.
    /// </summary>
    /// <param name="encodingType">
    ///   A <see cref="UniversalTagNumber"/> corresponding to the value type to process.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="encodingType"/> is not a known character string type.
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules --OR--
    ///   the string did not successfully decode
    /// </exception>
    /// <seealso cref="TryReadPrimitiveCharacterStringBytes(UniversalTagNumber,out ReadOnlyMemory{byte})"/>
    /// <seealso cref="TryCopyCharacterStringBytes(UniversalTagNumber,Span{byte},out int)"/>
    /// <seealso cref="TryCopyCharacterString(UniversalTagNumber,Span{char},out int)"/>
    /// <seealso cref="ReadCharacterString(Asn1Tag,UniversalTagNumber)"/>
    public string ReadCharacterString(UniversalTagNumber encodingType) => throw null;
    public string ReadCharacterString(Asn1Tag expectedTag, UniversalTagNumber encodingType) => throw null;
               
    public bool TryCopyCharacterString(
        UniversalTagNumber encodingType,
        Span<char> destination,
        out int charsWritten) => throw null;
    public bool TryCopyCharacterString(
        Asn1Tag expectedTag,
        UniversalTagNumber encodingType,
        Span<char> destination,
        out int charsWritten) => throw null;
    
    /// <summary>
    ///   Reads the next value as character string with a UNIVERSAL tag appropriate to the specified
    ///   encoding type, returning the contents as an unprocessed <see cref="ReadOnlyMemory{byte}"/>
    ///   over the original data.
    /// </summary>
    /// <param name="encodingType">
    ///   A <see cref="UniversalTagNumber"/> corresponding to the value type to process.
    /// </param>
    /// <param name="contents">
    ///   On success, receives a <see cref="ReadOnlyMemory{byte}"/> over the original data
    ///   corresponding to the contents of the character string.
    /// </param>
    /// <returns>
    ///   <c>true</c> and advances the reader if the value had a primitive encoding,
    ///   <c>false</c> and does not advance the reader if it had a constructed encoding.
    /// </returns>
    /// <remarks>
    ///   This method does not determine if the string used only characters defined by the encoding.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="encodingType"/> is not a known character string type.
    /// </exception>
    /// <exception cref="CryptographicException">
    ///   the next value does not have the correct tag --OR--
    ///   the length encoding is not valid under the current encoding rules --OR--
    ///   the contents are not valid under the current encoding rules
    /// </exception>
    /// <seealso cref="TryCopyCharacterStringBytes(UniversalTagNumber,Span{byte},out int)"/>
    public bool TryReadPrimitiveCharacterStringBytes(
        UniversalTagNumber encodingType,
        out ReadOnlyMemory<byte> contents) => throw null;
    public bool TryReadPrimitiveCharacterStringBytes(
        Asn1Tag expectedTag,
        UniversalTagNumber encodingType,
        out ReadOnlyMemory<byte> contents) => throw null;

    public bool TryCopyCharacterStringBytes(
        UniversalTagNumber encodingType,
        Span<byte> destination,
        out int bytesWritten) => throw null;
    public bool TryCopyCharacterStringBytes(
        Asn1Tag expectedTag,
        UniversalTagNumber encodingType,
        Span<byte> destination,
        out int bytesWritten) => throw null;
}
```

### AsnValueReader API

The mutable AsnValueReader ref struct has the same general API as the reference-type reader.
The only significant differences are that the constructor takes `ReadOnlySpan<byte>` instead of `ReadOnlyMemory<byte>`,
methods that return (or out) `ReadOnlyMemory<byte>` return `ReadOnlySpan<byte>`,
and methods that return `AsnReader` return `AsnValueReader`.

```C#
/// <summary>
///   A stateful, forward-only reader for BER-, CER-, or DER-encoded ASN.1 data.
/// </summary>
public ref partial struct AsnValueReader
{
    public AsnValueReader(ReadOnlySpan<byte> data, AsnEncodingRules ruleSet) { }

    public ReadOnlySpan<byte> PeekEncodedValue() => throw null;
    
    public ReadOnlySpan<byte> PeekContentBytes() => throw null;
    
    public ReadOnlySpan<byte> ReadEncodedValue() => throw null;

    public bool TryReadPrimitiveBitStringValue(out int unusedBitCount, out ReadOnlySpan<byte> value) => throw null;
    public bool TryReadPrimitiveBitStringValue(
        Asn1Tag expectedTag,
        out int unusedBitCount,
        out ReadOnlySpan<byte> value) => throw null;

    public bool TryReadPrimitiveOctetStringBytes(out ReadOnlySpan<byte> contents) => throw null;
    public bool TryReadPrimitiveOctetStringBytes(Asn1Tag expectedTag, out ReadOnlySpan<byte> contents) => throw null;

    public AsnValueReader ReadSequence() => throw null;
    public AsnValueReader ReadSequence(Asn1Tag expectedTag) => throw null;

    public AsnValueReader ReadSetOf(bool skipSortOrderValidation = false) => throw null;
    public AsnValueReader ReadSetOf(Asn1Tag expectedTag, bool skipSortOrderValidation = false) => throw null;

    public bool TryReadPrimitiveCharacterStringBytes(
        UniversalTagNumber encodingType,
        out ReadOnlySpan<byte> contents) => throw null;
    public bool TryReadPrimitiveCharacterStringBytes(
        Asn1Tag expectedTag,
        UniversalTagNumber encodingType,
        out ReadOnlySpan<byte> contents) => throw null;
}
```

##  Q & A

### Open Questions

- Is "Asn" an appropriate prefix, given that this is for the ASN.1 BER-family, not ASN.1 the textual language?
  - BER is both a specific set of rules, and the acronym for the encoding family, so BerWriter(DER) feels weird.
- Should we introduce new exception types here? If yes, should they extend CryptographicException?
  - AsnException? AsnEncodingException? AsnDecodingException?
  - The current model uses CryptographicException because that's what the crypto internals threw when asking Win32 to do similar operations. Crypto can wrap these exceptions if need be.
- Should the writer have a PushBitString()? (presumably it would always require unusedBits=0). 
- Should the writer produce IDisposable (using-compatible) state for Push and avoid the need for calling Pop?
- Would an `AsnWriter.WriteEncodedValue(AsnWriter otherWriter)` be reasonable?
- Should the writer just avoid pooled arrays and not be `IDisposable`?
- Should `AsnEncodingRules` either expand, or alias-expand, the acronyms?
- Namespace: The preferred namespace is `System.BinaryEncodings.Asn1`, or a similar general approach to binary encodings (which will align with the CBOR feature also for .NET 5). The fallback is `System.Security.Cryptography.Asn1`, but the reader will be used by more than just cryptography (though that is its main consumer).
- Distribution: Dual-building as an OOB netstandard2.0 package and an inbox netcoreapp-current package with no public contract.
- Instead of using default parameters for the skipSortValidation, twoDigitYearMax, and disallowFractionalSeconds on the reader, should we add an AsnReaderOptions class (with properties for the mode, twoDigitYearMax, skipSortValidation, etc) and do proper overloads like `ReadSetOf() => ReadSetOf(_options.SkipSetSortValidation);`? This allows per-call configuration with reader-specific defaults instead of compile-time defaults.  The mode-only constructor would remain, with the default options being the current compile-defaults.

### Answered Questions

- Q. How many different representations of `false` exist in BER?
  - A. 126. (`010100`, `01810100`, `0182000100`, ... `01FE0000..000100`)
 - Q. How many different representations of `true` exist in BER?
   - A. 126^8 - 126. (63,527,879,748,485,250 (63.5Qd))
- Would an `AsnReader.TryReadAnyString(Asn1Tag, out UniversalTagNumber, out string)` be reasonable?
  - No, the encoding can't be inferred for tags other than the Universal Class tags.
    The tagless overload could work, but introduces an asymmetry and is easily implemented by a caller.
- Does the reader support reading PEM-encoded data, to decode on the fly?
  - No. If it did, we'd stop exposing buffer data, and Peek operations become a copy.

