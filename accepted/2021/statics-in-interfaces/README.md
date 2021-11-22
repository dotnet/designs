# Statics in Interfaces

**DRAFT**

**Owners** [Tanner Gooding](https://github.com/tannergooding) | [David Wrighton](https://github.com/davidwrighton) | [Mads Torgersen](https://github.com/MadsTorgersen) | [Immo Landwerth](https://github.com/terrajobst)

C# is looking at enabling static abstract members in interfaces (https://github.com/dotnet/csharplang/issues/4436). From the libraries perspective, this is an opportunity to enable "generic math" which could define a new baseline for how developers write algorithms in .NET.

## Scenarios

### Allow easier maintenance for generic code

Tanner is building `Vector<T>`, an important numeric helper type for SIMD acceleration. Today, this type supports 10 underlying primitive types for `T` and they can use generic specialization in an internal helper to ensure the implementation is performant for each:

```csharp
private static T ScalarAdd<T>(T left, T right)
    where T : struct
{
    if (typeof(T) == typeof(byte))
    {
        return (T)(object)(byte)((byte)(object)left + (byte)(object)right);
    }
    else if (typeof(T) == typeof(sbyte))
    {
        return (T)(object)(sbyte)((sbyte)(object)left + (sbyte)(object)right);
    }
    else if (typeof(T) == typeof(ushort))
    {
        return (T)(object)(ushort)((ushort)(object)left + (ushort)(object)right);
    }
    else if (typeof(T) == typeof(short))
    {
        return (T)(object)(short)((short)(object)left + (short)(object)right);
    }
    else if (typeof(T) == typeof(uint))
    {
        return (T)(object)(uint)((uint)(object)left + (uint)(object)right);
    }
    else if (typeof(T) == typeof(int))
    {
        return (T)(object)(int)((int)(object)left + (int)(object)right);
    }
    else if (typeof(T) == typeof(ulong))
    {
        return (T)(object)(ulong)((ulong)(object)left + (ulong)(object)right);
    }
    else if (typeof(T) == typeof(long))
    {
        return (T)(object)(long)((long)(object)left + (long)(object)right);
    }
    else if (typeof(T) == typeof(float))
    {
        return (T)(object)(float)((float)(object)left + (float)(object)right);
    }
    else if (typeof(T) == typeof(double))
    {
        return (T)(object)(double)((double)(object)left + (double)(object)right);
    }
    else
    {
        throw new NotSupportedException(SR.Arg_TypeNotSupported);
    }
}
```

Given the operation is the same for each type, only differing on the actual type in question, Tanner would expect this to be easier to author and maintain than this. Additionally, each new type that needs to be supported adds additional branches and complexity to the code. For example, in .NET 6 support for `nint` and `nuint` support is desired.

Static abstracts in interfaces will allow Tanner to define an interface such as:
```csharp
public interface IAdditionOperators<TSelf>
    where TSelf : IAdditionOperators<TSelf>
{
    static abstract TSelf operator +(TSelf left, TSelf right);
}
```

Such an interface now allows the implementation of `ScalarAdd` to be rewritten as:
```csharp
private static T ScalarAdd<T>(T left, T right)
    where T : struct, IAdditionOperators<T>
{
    ThrowHelper.ThrowForUnsupportedVectorBaseType<T>();
    return left + right;
}
```

This reduces the amount of code that must be maintained and allows new types to be easily supported by changing `ThrowForUnsupportedVectorBaseType`.

### Allow easier support for a non-finite list of types

Jeff is trying to compute the standard deviation of a list of numbers. They are surprised to find that such a function isn't built into .NET. After searching the web, Jeff finds a blog post with the following method (https://dotnetcodr.com/2017/06/29/calculate-standard-deviation-of-integers-with-c-net-3/):
```csharp
public static double GetStandardDeviation(this IEnumerable<int> values)
{
    double standardDeviation = 0;
    int[] enumerable = values as int[] ?? values.ToArray();
    int count = enumerable.Count();
    if (count > 1)
    {
        double avg = enumerable.Average();
        double sum = enumerable.Sum(d => (d - avg) * (d - avg));
        standardDeviation = Math.Sqrt(sum / count);
    }
    return standardDeviation;
}
```

Jeff copies this code into his project and is now able to calculate the standard deviation for a set of integers. However, Jeff also wants to support other types such as `float`, `double`, or even user-defined inputs. Jeff has realized that creating a reusable and easily maintainable implementation would allow producing a NuGet package that other developers seeking the same could depend upon.

After thinking over the possible implementations, Jeff realizes that the only reliable way to implement this in .NET 5 is to define an interface with instance methods that users must then implement on their own types or to hardcode a list of types in their own library. They do not believe this to be as reusable or maintainable as hoped.

Jeff learns that a future version of .NET will offer a new feature called static abstracts in interfaces. Such a feature will have the .NET Libraries expose a set of interfaces that allow Jeff to implement the desired method without requiring users types to take a dependency on types they declared.

The .NET Libraries are exposing interfaces such as:
```csharp
public interface IAdditionOperators<TSelf>
    where TSelf : IAdditionOperators<TSelf>
{
    static abstract TSelf operator +(TSelf left, TSelf right);
}

public interface ISubtractionOperators<TSelf>
    where TSelf : ISubtractionOperators<TSelf>
{
    static abstract TSelf operator -(TSelf left, TSelf right);
}

public interface IMultiplyOperators<TSelf>
    where TSelf : IMultiplyOperators<TSelf>
{
    static abstract TSelf operator *(TSelf left, TSelf right);
}

public interface IDivisionOperators<TSelf>
    where TSelf : IDivisionOperators<TSelf>
{
    static abstract TSelf operator /(TSelf left, TSelf right);
}

public interface INumber<TSelf>
    : IAdditionOperators<TSelf>
    : ISubtractionOperators<TSelf>
    : IMultiplyOperators<TSelf>
    : IDivisionOperators<TSelf>
    where TSelf : INumber<TSelf>
{
}

// New methods on System.Linq.Enumerable for `Sum<T>` and `Average<T>`, `where T : INumber<T>`
```

Jeff can now implement their method such as:
```csharp
public static double GetStandardDeviation<T>(this IEnumerable<T> values)
    where T : INumber<T>
{
    double standardDeviation = 0;
    T[] enumerable = values as T[] ?? values.ToArray();
    int count = enumerable.Count();
    if (count > 1)
    {
        double avg = enumerable.Average();
        double sum = enumerable.Sum(d => ((double)d - avg) * ((double)d - avg));
        standardDeviation = Math.Sqrt(sum / count);
    }
    return standardDeviation;
}
```

## Designs

The overall design has been influenced by the existing .NET surface area, the Swift numeric protocols (https://developer.apple.com/documentation/swift/swift_standard_library/numbers_and_basic_values/numeric_protocols), and the Rust op traits (https://doc.rust-lang.org/std/ops/index.html). It is broken up into a few layers describing a hierarchy of interfaces and what types implement them. The interfaces aim to be reusable by a variety of types and so do not provide guarantees around concepts like associativity, commutativity or distributivity. Instead, these concepts may become available to tooling in the future via another mechanism, such as attributes on the respective operator implementations.

<!--
@startuml
interface "IAdditionOperators<TSelf, TOther, TResult>"
interface "IAdditiveIdentity<TSelf, TResult>"
interface "IBinaryFloatingPoint<TSelf>"
interface "IBinaryInteger<TSelf>"
interface "IBinaryNumber<TSelf>"
interface "IBitwiseOperators<TSelf, TOther, TResult>"
interface "IComparable"
interface "IComparable<T>"
interface "IComparisonOperators<TSelf, TOther>"
interface "IConvertible"
interface "IDecrementOperators<TSelf>"
interface "IDeserializationCallback"
interface "IDivisionOperators<TSelf, TOther, TResult>"
interface "IEqualityOperators<TSelf, TOther>"
interface "IEquatable<T>"
interface "IFloatingPoint<TSelf>"
interface "IIncrementOperators<TSelf>"
interface "IFormattable"
interface "IMinMaxValue<TSelf>"
interface "IModulusOperators<TSelf, TOther, TResult>"
interface "IMultiplicativeIdentity<TSelf, TResult>"
interface "IMultiplyOperators<TSelf, TOther, TResult>"
interface "INumber<TSelf>"
interface "IParseable<TSelf>"
interface "ISerializable"
interface "IShiftOperators<TSelf, TOther, TResult>"
interface "ISignedNumber<TSelf>"
interface "ISpanFormattable"
interface "ISpanParseable<TSelf>"
interface "ISubtractionOperators<TSelf, TOther, TResult>"
interface "IUnsignedNumber<TSelf>"
interface "IUnaryNegationOperators<TSelf, TResult>"
interface "IUnaryPlusOperators<TSelf, TResult>"
interface "IVector<TSelf, TScalar>"

"IBinaryNumber<TSelf>"                          <|-- "IBinaryFloatingPoint<TSelf>"
"IFloatingPoint<TSelf>"                         <|-- "IBinaryFloatingPoint<TSelf>"

"IBinaryNumber<TSelf>"                          <|-- "IBinaryInteger<TSelf>"
"IShiftOperators<TSelf, TOther, TResult>"       <|-- "IBinaryInteger<TSelf>"

"IBitwiseOperators<TSelf, TOther, TResult>"     <|-- "IBinaryNumber<TSelf>"
"INumber<TSelf>"                                <|-- "IBinaryNumber<TSelf>"

"IComparable"                                   <|-- "IComparisonOperators<TSelf, TOther>"
"IComparable<T>"                                <|-- "IComparisonOperators<TSelf, TOther>"
"IEqualityOperators<TSelf, TOther>"             <|-- "IComparisonOperators<TSelf, TOther>"

"IEquatable<T>"                                 <|-- "IEqualityOperators<TSelf, TOther>"

"ISignedNumber<TSelf>"                          <|-- "IFloatingPoint<TSelf>"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "INumber<TSelf>"
"IComparisonOperators<TSelf, TOther>"           <|-- "INumber<TSelf>"
"IDecrementOperators<TSelf>"                    <|-- "INumber<TSelf>"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"IIncrementOperators<TSelf>"                    <|-- "INumber<TSelf>"
"IModulusOperators<TSelf, TOther, TResult>"     <|-- "INumber<TSelf>"
"IMultiplicativeIdentity<TSelf, TResult>"       <|-- "INumber<TSelf>"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"ISpanFormattable"                              <|-- "INumber<TSelf>"
"ISpanParseable<TSelf>"                         <|-- "INumber<TSelf>"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "INumber<TSelf>"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "INumber<TSelf>"
"IUnaryPlusOperators<TSelf, TResult>"           <|-- "INumber<TSelf>"

"INumber<TSelf>"                                <|-- "ISignedNumber<TSelf>"

"IFormattable"                                  <|-- "ISpanFormattable"

"IParseable<TSelf>"                             <|-- "ISpanParseable<TSelf>"

"INumber<TSelf>"                                <|-- "IUnsignedNumber<TSelf>"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "IVector<TSelf, TScalar>"
"IBitwiseOperators<TSelf, TOther, TResult>"     <|-- "IVector<TSelf, TScalar>"
"IComparisonOperators<TSelf, TOther>"           <|-- "IVector<TSelf, TScalar>"
"IDecrementOperators<TSelf>"                    <|-- "INumber<TSelf>"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"IIncrementOperators<TSelf>"                    <|-- "INumber<TSelf>"
"IModulusOperators<TSelf, TOther, TResult>"     <|-- "INumber<TSelf>"
"IMultiplicativeIdentity<TSelf, TResult>"       <|-- "IVector<TSelf, TScalar>"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"ISpanFormattable"                              <|-- "IVector<TSelf, TScalar>"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "IVector<TSelf, TScalar>"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "IVector<TSelf, TScalar>"

class "Byte"
class "Char"
class "DateOnly"
class "DateTime"
class "DateTimeOffset"
class "Decimal"
class "Double"
class "Enum"
class "Guid"
class "Half"
class "Int16"
class "Int32"
class "Int64"
class "IntPtr"
class "Object"
class "SByte"
class "Single"
class "TimeSpan"
class "UInt16"
class "UInt32"
class "UInt64"
class "UIntPtr"
class "ValueType"
class "Vector<T>"
class "Vector64<T>"
class "Vector128<T>"
class "Vector256<T>"
class "Vector2"
class "Vector3"
class "Vector4"

"IBinaryInteger<TSelf>"                         <|-- "Byte"
"IConvertible"                                  <|-- "Byte"
"IMinMaxValue<TSelf>"                           <|-- "Byte"
"IUnsignedNumber<TSelf>"                        <|-- "Byte"
"ValueType"                                     <|-- "Byte"

"IBinaryInteger<TSelf>"                         <|-- "Char"
"IConvertible"                                  <|-- "Char"
"IMinMaxValue<TSelf>"                           <|-- "Char"
"IUnsignedNumber<TSelf>"                        <|-- "Char"
"ValueType"                                     <|-- "Char"

"IComparisonOperators<TSelf, TOther>"           <|-- "DateOnly"
"IMinMaxValue<TSelf>"                           <|-- "DateOnly"
"ISpanFormattable"                              <|-- "DateOnly"
"ISpanParseable<TSelf>"                         <|-- "DateOnly"
"ValueType"                                     <|-- "DateOnly"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "DateTime"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "DateTime"
"IComparisonOperators<TSelf, TOther>"           <|-- "DateTime"
"IConvertible"                                  <|-- "DateTime"
"IMinMaxValue<TSelf>"                           <|-- "DateTime"
"ISerializable"                                 <|-- "DateTime"
"ISpanFormattable"                              <|-- "DateTime"
"ISpanParseable<TSelf>"                         <|-- "DateTime"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "DateTime"
"ValueType"                                     <|-- "DateTime"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "DateTimeOffset"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "DateTimeOffset"
"IComparisonOperators<TSelf, TOther>"           <|-- "DateTimeOffset"
"IDeserializationCallback"                      <|-- "DateTimeOffset"
"IMinMaxValue<TSelf>"                           <|-- "DateTimeOffset"
"ISerializable"                                 <|-- "DateTimeOffset"
"ISpanFormattable"                              <|-- "DateTimeOffset"
"ISpanParseable<TSelf>"                         <|-- "DateTimeOffset"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "DateTimeOffset"
"ValueType"                                     <|-- "DateTimeOffset"

"IConvertible"                                  <|-- "Decimal"
"IDeserializationCallback"                      <|-- "Decimal"
"IMinMaxValue<TSelf>"                           <|-- "Decimal"
"ISerializable"                                 <|-- "Decimal"
"ISignedNumber<TSelf>"                          <|-- "Decimal"
"ValueType"                                     <|-- "Decimal"

"IBinaryFloatingPoint<TSelf>"                   <|-- "Double"
"IConvertible"                                  <|-- "Double"
"IMinMaxValue<TSelf>"                           <|-- "Double"
"ValueType"                                     <|-- "Double"

"IComparable"                                   <|-- "Enum"
"IConvertible"                                  <|-- "Enum"
"IFormattable"                                  <|-- "Enum"
"ValueType"                                     <|-- "Enum"

"IComparisonOperators<TSelf, TOther>"           <|-- "Guid"
"ISpanFormattable"                              <|-- "Guid"
"ISpanParseable<TSelf>"                         <|-- "Guid"
"ValueType"                                     <|-- "Guid"

"IBinaryFloatingPoint<TSelf>"                   <|-- "Half"
"IMinMaxValue<TSelf>"                           <|-- "Half"
"ValueType"                                     <|-- "Half"

"IBinaryInteger<TSelf>"                         <|-- "Int16"
"IConvertible"                                  <|-- "Int16"
"IMinMaxValue<TSelf>"                           <|-- "Int16"
"ISignedNumber<TSelf>"                          <|-- "Int16"
"ValueType"                                     <|-- "Int16"

"IBinaryInteger<TSelf>"                         <|-- "Int32"
"IConvertible"                                  <|-- "Int32"
"IMinMaxValue<TSelf>"                           <|-- "Int32"
"ISignedNumber<TSelf>"                          <|-- "Int32"
"ValueType"                                     <|-- "Int32"

"IBinaryInteger<TSelf>"                         <|-- "Int64"
"IConvertible"                                  <|-- "Int64"
"IMinMaxValue<TSelf>"                           <|-- "Int64"
"ISignedNumber<TSelf>"                          <|-- "Int64"
"ValueType"                                     <|-- "Int64"

"IBinaryInteger<TSelf>"                         <|-- "IntPtr"
"IMinMaxValue<TSelf>"                           <|-- "IntPtr"
"ISignedNumber<TSelf>"                          <|-- "IntPtr"
"ISerializable"                                 <|-- "IntPtr"
"ValueType"                                     <|-- "IntPtr"

"IBinaryInteger<TSelf>"                         <|-- "SByte"
"IConvertible"                                  <|-- "SByte"
"IMinMaxValue<TSelf>"                           <|-- "SByte"
"ISignedNumber<TSelf>"                          <|-- "SByte"
"ValueType"                                     <|-- "SByte"

"IBinaryFloatingPoint<TSelf>"                   <|-- "Single"
"IConvertible"                                  <|-- "Single"
"IMinMaxValue<TSelf>"                           <|-- "Single"
"ValueType"                                     <|-- "Single"

"IComparisonOperators<TSelf, TOther>"           <|-- "TimeOnly"
"IMinMaxValue<TSelf>"                           <|-- "TimeOnly"
"ISpanFormattable"                              <|-- "TimeOnly"
"ISpanParseable<TSelf>"                         <|-- "TimeOnly"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "TimeOnly"
"ValueType"                                     <|-- "TimeOnly"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "TimeSpan"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "TimeSpan"
"IComparisonOperators<TSelf, TOther>"           <|-- "TimeSpan"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "TimeSpan"
"IMinMaxValue<TSelf>"                           <|-- "TimeSpan"
"IMultiplicativeIdentity<TSelf, TResult>"       <|-- "TimeSpan"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "TimeSpan"
"ISpanFormattable"                              <|-- "TimeSpan"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "TimeSpan"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "TimeSpan"
"ValueType"                                     <|-- "TimeSpan"

"IBinaryInteger<TSelf>"                         <|-- "UInt16"
"IConvertible"                                  <|-- "UInt16"
"IMinMaxValue<TSelf>"                           <|-- "UInt16"
"IUnsignedNumber<TSelf>"                        <|-- "UInt16"
"ValueType"                                     <|-- "UInt16"

"IBinaryInteger<TSelf>"                         <|-- "UInt32"
"IConvertible"                                  <|-- "UInt32"
"IMinMaxValue<TSelf>"                           <|-- "UInt32"
"IUnsignedNumber<TSelf>"                        <|-- "UInt32"
"ValueType"                                     <|-- "UInt32"

"IBinaryInteger<TSelf>"                         <|-- "UInt64"
"IConvertible"                                  <|-- "UInt64"
"IMinMaxValue<TSelf>"                           <|-- "UInt64"
"IUnsignedNumber<TSelf>"                        <|-- "UInt64"
"ValueType"                                     <|-- "UInt64"

"IBinaryInteger<TSelf>"                         <|-- "UIntPtr"
"IMinMaxValue<TSelf>"                           <|-- "UIntPtr"
"ISerializable"                                 <|-- "UIntPtr"
"IUnsignedNumber<TSelf>"                        <|-- "UIntPtr"
"ValueType"                                     <|-- "UIntPtr"

"Object"                                        <|-- "ValueType"

"IVector<TSelf, TScalar>"                       <|-- "Vector<T>"

"IVector<TSelf, TScalar>"                       <|-- "Vector64<T>"
"IVector<TSelf, TScalar>"                       <|-- "Vector128<T>"
"IVector<TSelf, TScalar>"                       <|-- "Vector256<T>"

"IVector<TSelf, TScalar>"                       <|-- "Vector2"
"IVector<TSelf, TScalar>"                       <|-- "Vector3"
"IVector<TSelf, TScalar>"                       <|-- "Vector4"
@enduml
-->

![UML](http://www.plantuml.com/plantuml/svg/r9ZDRjim3CVlVeeWv_NGZmKx5CNMZsuvP0dcf7VQOL9kifn9T3SFU_Yb7QecbXpAHZ5WlHHao9z8IVbJo7lBOBWgL4AQqQmWny5m_66v9APIJpTeW4jZp-SfgjMRmNpAzsYs_x-WhHHV30Df3pXUecRYscK5eoz9WwblLGbCUZqhIVFVX63SUEjPepaGyRagijO0_a4MExPpLHORC90fRFEVpuCfP5ktpelN3sYOl3MkCJTOeEO6vw8PPj4GAFe5Z-Tr1Kfba7zh1D43sUwdUVEz0aLSYvsuQ0xkYNp2OntLx6rf2c3s3sH2UW8_xq1L64oSbCjALRPZ_vEjYpQASl3lj91JTrpgmDMTWR48cGftbZwVlhSdwJsjk6CPAQqrBWyKasv0Xqx1VIXKMsLi8E-XB0jjfTeMJng0QmZYVQpBcQdAHiRVORuDSZ5f3WhCDYO9Qz2WzU_yzz6H884jNvp-oFvbXXKu-ex5m98E8ktHN3-kijW61o8hsTLxWUHWaPEWEKfU4otBTrzqqX3xs1sHvqaIbY2Ht7hRuzynZkQ9SSIpngl9fqHjhKmvy2vmgH6K-1V18SfOlq8jySFv02LwM0krn8_lTihUb9H4Gw1uuxGxfSl8RQ54prQXbeXfAtRKJzizNN4m-PnycFyCIWBlj5YGbzYxH_-Hr5y7snuxlGHHmCKFVWdNHsW5tBzNNA6e_rXwfTPwQR20YnTZ2TTRJyEu7i8gu99SWRM3uMNDE7mshkx1EECQ6ATQrNkEEHNeEQQhbKL-SMDE1QWNkwmoXSwyqLNXZ8yLBPtn2TJA6MFDnwDTw_HarngTxLepDiwSPbynPsUcUnscfDSxjJnM_tZeph7ONtQnl-vYV-55O-Md7nNczUQ5_xpx5mtFwCpt7P-yyvqdRqS1PyC-RTXdVqgrOvi8WY28elv6GGmSmYO7H0RW0HxvOw3MrqdiQlG6__Vjr1MzDTtNxQsxqq8c63VSZPf25jb_1YoBxDbRQObD_8ULlrWKQ_vRGAnO42iMn1eMn1gMNffr4pjqlBNfU4tByI9UF4UikgzagdXDFV7c-7WrGFpT91C-NmqKJmQAXs0GRmQCtp9WRtktOLLspLYLi-L6vCgpk36hulQiogv9g-9MhOeRjYfxjgqATPyogJWbd2mIpju8bv5dk7m3uSVbmk24B_xR5nSDmaV3oQDfh3yN7Jbsx3G0OE78mr9NdiiSTAvOw5V3me67DMlFXHMi6GiFFInTM-PYBRuCXYC7GnSB1i62NR7R8oaM38e5WsB1e5YmA_RyH8e5WsB1e5WmA1PCYZN4WbD7mx6ZCPr4WshHADihJwo2GV9WK3mO90kcTrOJK90i6ZIBbdD7Ophvc_Nw12hU4awyfPnuku_5W_pnFi5gs8wZWtWsG3oisEBTGF6m1ukNnLUy6JYU3HWFkwnuDN0ylcpUE3f7YtcYSUmV3S95ksoUdwD9jcYJpIDqjClc8JgQP8issJn7HwjidAJ3FGo7it8UfiEFsOAliF5zw76S_dJWka8BRw_GWjnBj4_t6gtVzuhiFSCDyNuCyFBn_VxpzVaB)

### Base Interfaces

The base interfaces each describe one or more core operations that are used to build up the core interfaces listed later. In many cases, such as for addition, subtraction, multiplication, and division, there is only a single operator per interface. This split is important in order for them to be reusable against downstream types, like `DateTime` where addition does not imply subtraction or `Matrix` where multiplication does not imply division. These interfaces are not expected to be used by many users and instead primarily by developers looking at defining their own abstractions, as such we are not concerned with adding simplifying interfaces like `IAdditionOperators<TSelf> : IAdditionOperators<TSelf, TSelf>`. Likewise, every interface added introduces increased size and startup overhead, so we need to strike a balance between expressiveness, exxtensibility, and cost.

Some of the code below assumes that https://github.com/dotnet/csharplang/issues/4665 is going to be accepted into the language. If it is not accepted or if the approved syntax differs, the declarations will need to be updated.

```csharp
namespace System
{
    public interface IParseable<TSelf>
        where TSelf : IParseable<TSelf>
    {
        // These do not take NumberStyles as not all parseable types are numbers (such as Guid)
        // Instead, INumber<TSelf> exposes additional parse methods that do take NumberStyles

        static abstract TSelf Parse(string s, IFormatProvider? provider);

        static abstract bool TryParse(string s, IFormatProvider? provider, out TSelf result);
    }

    public interface ISpanParseable<TSelf> : IParseable<TSelf>
        where TSelf : ISpanParseable<TSelf>
    {
        // This inherits from IParseable, much as ISpanFormattable is planned to inherit from IFormattable

        static abstract TSelf Parse(ReadOnlySpan<char> s, IFormatProvider? provider);

        static abstract bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TSelf result);
    }

    public interface IEqualityOperators<TSelf, TOther> : IEquatable<TOther>
        where TSelf : IEqualityOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IEquatable<TSelf, TOther>

        // This should not be `IEqualityOperators<TSelf>` as there are types like `Complex` where
        // TOther can be `double` and represent an optimization, both in size and perf

        static abstract bool operator ==(TSelf left, TOther right);

        static abstract bool operator !=(TSelf left, TOther right);
    }

    public interface IComparisonOperators<TSelf, TOther> : IComparable, IComparable<TOther>, IEqualityOperators<TSelf, TOther>
        where TSelf : IComparisonOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IComparable<TSelf, TOther>

        // This inherits from IEqualityOperators<TSelf, TOther> as even though IComparable<T> does not
        // inherit from IEquatable<T>, the <= and >= operators as well as CompareTo iTSelf imply equality

        static abstract bool operator <(TSelf left, TOther right);

        static abstract bool operator <=(TSelf left, TOther right);

        static abstract bool operator >(TSelf left, TOther right);

        static abstract bool operator >=(TSelf left, TOther right);
    }

    public interface IAdditiveIdentity<TSelf, TResult>
        where TSelf : IAdditiveIdentity<TSelf, TResult>
    {
        static abstract TResult AdditiveIdentity { get; }
    }

    public interface IAdditionOperators<TSelf, TOther, TResult>
        where TSelf : IAdditionOperators<TSelf, TOther, TResult>
    {
        // An additive identity is not exposed here as it may be problematic for cases like
        // DateTime that implements both IAdditionOperators<DateTime, DateTime> and IAdditionOperators<DateTime, TimeSpan>
        // This would require overloading by return type and may be problematic for some scenarios where TOther might not have an additive identity

        // We can't assume TOther + TSelf is valid as not everything is commutative

        // We can't expose TSelf - TOther as some types only support one of the operations

        static abstract TResult operator +(TSelf left, TOther right);

        static abstract TResult checked operator +(TSelf left, TOther right);
    }

    public interface ISubtractionOperators<TSelf, TOther, TResult>
        where TSelf : ISubtractionOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator -(TSelf left, TOther right);

        static abstract TResult checked operator -(TSelf left, TOther right);
    }

    public interface IIncrementOperators<TSelf>
        where TSelf : IIncrementOperators<TSelf>
    {
        // We don't inherit IAdditionOperators as TOther isn't well-defined for incrementable types
        // Incrementing twice is not necessarily the same as self + 2 and not all languages
        // use incrementing for numbers, they are sometimes used by iterators for example

        // We can't expose TSelf-- as some types only support one of the operations

        // We don't support TResult as C# requires TSelf to be the return type

        static abstract TSelf operator ++(TSelf value);

        static abstract TSelf checked operator ++(TSelf value);
    }

    public interface IDecrementOperators<TSelf>
        where TSelf : IDecrementOperators<TSelf>
    {
        static abstract TSelf operator --(TSelf value);

        static abstract TSelf checked operator --(TSelf value);
    }

    public interface IMultiplicativeIdentity<TSelf, TResult>
        where TSelf : IMultiplicativeIdentity<TSelf, TResult>
    {
        static abstract TResult MultiplicativeIdentity { get; }
    }

    public interface IMultiplyOperators<TSelf, TOther, TResult>
        where TSelf : IMultiplyOperators<TSelf, TOther, TResult>
    {
        // A multiplicative identity is not exposed here for the same reasons as IAdditionOperators<TSelf, TOther>

        // We can't assume TOther * TSelf is valid as not everything is commutative

        // We can't expose TSelf / TOther as some types, such as matrices, don't support dividing with respect to TOther

        // We can't inherit from IAdditionOperators<TSelf, TOther> as some types, such as matrices, support Matrix * scalar but not Matrix + scalar

        static abstract TResult operator *(TSelf left, TOther right);

        static abstract TResult checked operator *(TSelf left, TOther right);
    }

    public interface IDivisionOperators<TSelf, TOther, TResult>
        where TSelf : IDivisionOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator /(TSelf left, TOther right);

        static abstract TResult checked operator /(TSelf left, TOther right);
    }

    public interface IModulusOperators<TSelf, TOther, TResult>
        where TSelf : IModulusOperators<TSelf, TOther, TResult>
    {
        // The ECMA name is op_Modulus and so the behavior isn't actually "remainder" with regards to negative values

        // Likewise the name IModulusOperators doesn't fit with the other names, so a better one is needed

        static abstract TResult operator %(TSelf left, TOther right);

        static abstract TResult checked operator %(TSelf left, TOther right);
    }

    public interface IUnaryNegationOperators<TSelf, TResult>
        where TSelf : IUnaryNegationOperators<TSelf, TResult>
    {
        static abstract TResult operator -(TSelf value);

        static abstract TResult checked operator -(TSelf value);
    }

    public interface IUnaryPlusOperators<TSelf, TResult>
        where TSelf : IUnaryPlusOperators<TSelf, TResult>
    {
        static abstract TResult operator +(TSelf value);

        static abstract TResult checked operator +(TSelf value);
    }

    public interface IBitwiseOperators<TSelf, TOther, TResult>
        where TSelf : IBitwiseOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator &(TSelf left, TOther right);

        static abstract TResult operator |(TSelf left, TOther right);

        static abstract TResult operator ^(TSelf left, TOther right);

        static abstract TResult operator ~(TSelf value);
    }

    public interface IShiftOperators<TSelf, TOther, TResult>
        where TSelf : IShiftOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator <<(TSelf value, TOther shiftAmount);

        static abstract TResult operator >>(TSelf value, TOther shiftAmount);

        // Logical right shift
        static abstract TResult operator >>>(TSelf value, TOther shiftAmount);
    }

    public interface IMinMaxValue<TSelf>
        where TSelf : IMinMaxValue<TSelf>
    {
        // MinValue and MaxValue are more general purpose than just "numbers", so they live on this common base type

        static abstract TSelf MinValue { get; }

        static abstract TSelf MaxValue { get; }
    }
}
```

### Numeric Interfaces

The numeric interfaces build upon the base interfaces by defining the core abstraction that most users are expected to interact with.

```csharp
namespace System
{
    public interface INumber<TSelf>
        : IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IComparisonOperators<TSelf, TSelf>,   // implies IEqualityOperators<TSelf, TSelf>
          IDecrementOperators<TSelf>,
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IIncrementOperators<TSelf>,
          IModulusOperators<TSelf, TSelf, TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplyOperators<TSelf, TSelf, TSelf>,
          ISpanFormattable,                     // implies IFormattable
          ISpanParseable<TSelf>,                // implies IParseable<TSelf>
          ISubtractionOperators<TSelf, TSelf, TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>,
          IUnaryPlusOperators<TSelf, TSelf>
        where TSelf : INumber<TSelf>
    {
        // For the Create methods, there is some concern over users implementing them. It is not necessarily trivial to take an arbitrary TOther
        // and convert it into an arbitrary TSelf. If you know the type of TOther, you may be able to optimize in a few ways. Otherwise, you
        // may have to fail and throw in the worst case.

        static abstract TSelf Create<TOther>(TOther value)
            where TOther : INumber<TOther>;

        static abstract TSelf CreateSaturating<TOther>(TOther value)
            where TOther : INumber<TOther>;

        static abstract TSelf CreateTruncating<TOther>(TOther value)
            where TOther : INumber<TOther>;

        // There is an open question on whether properties like IsSigned, IsBinary, IsFixedWidth, Base/Radix, and others are beneficial
        // We could expose them for trivial checks or users could be required to check for the corresponding correct interfaces

        // Alias for MultiplicativeIdentity
        static abstract TSelf One { get; }

        // Alias for AdditiveIdentity
        static abstract TSelf Zero { get; }

        // Abs mirrors Math.Abs and returns the same type. This can fail for MinValue of signed integer types
        // Swift has an associated type that can be used here, which would require an additional type parameter in .NET
        // However that would hinder the reusability of these interfaces in constraints

        static abstract TSelf Abs(TSelf value);

        static abstract TSelf Clamp(TSelf value, TSelf min, TSelf max);

        static abstract (TSelf Quotient, TSelf Remainder) DivRem(TSelf left, TSelf right);

        static abstract TSelf Max(TSelf x, TSelf y);

        static abstract TSelf Min(TSelf x, TSelf y);

        static abstract TSelf Parse(string s, NumberStyles style, IFormatProvider? provider);

        static abstract TSelf Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider);

        // Math only exposes this Sign for signed types, but it is well-defined for unsigned types
        // it can simply never return -1 and only 0 or 1 instead

        static abstract int Sign(TSelf value);

        static abstract bool TryCreate<TOther>(TOther value, out TSelf result)
            where TOther : INumber<TOther>;

        static abstract bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out TSelf result);

        static abstract bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out TSelf result);
    }

    public interface ISignedNumber<TSelf>
        : INumber<TSelf>
        where TSelf : ISignedNumber<TSelf>
    {
        // It's not possible to check for lack of an interface in a constraint, so ISignedNumber<TSelf> is likely required

        static abstract TSelf NegativeOne { get; }
    }

    public interface IUnsignedNumber<TSelf>
        : INumber<TSelf>
        where TSelf : IUnsignedNumber<TSelf>
    {
        // It's not possible to check for lack of an interface in a constraint, so IUnsignedNumber<TSelf> is likely required
    }

    public interface IBinaryNumber<TSelf>
        : IBitwiseOperators<TSelf, TSelf, TSelf>,
          INumber<TSelf>
        where TSelf : IBinaryNumber<TSelf>
    {
        // Having the bitwise operators on IBinaryNumber<TSelf> means they will be available to floating-point types
        // The operations are well-defined [for floats] but many languages don't directly expose them, we already support
        // them in SIMD contexts today, as do most languages with SIMD support, so there is not great concern here.

        static abstract bool IsPow2(TSelf value);

        static abstract TSelf Log2(TSelf value);
    }

    public interface IBinaryInteger<TSelf>
        : IBinaryNumber<TSelf>,
          IShiftOperators<TSelf, TSelf, TSelf>
        where TSelf : IBinaryInteger<TSelf>
    {
        // We might want to support "big multiplication" for fixed width types
        // This would return a tuple (TSelf high, TSelf low) or similar, to match what we do for System.Math

        // Returning int is currently what BitOperations does, however this can be cumbersome or prohibitive
        // in various algorithms where returning TSelf is better.

        static abstract TSelf LeadingZeroCount(TSelf value);

        static abstract TSelf PopCount(TSelf value);

        static abstract TSelf RotateLeft(TSelf value, TSelf rotateAmount);

        static abstract TSelf RotateRight(TSelf value, TSelf rotateAmount);

        static abstract TSelf TrailingZeroCount(TSelf value);
    }

    public interface IFloatingPoint<TSelf>
        : ISignedNumber<TSelf>
        where TSelf : IFloatingPoint<TSelf>
    {
        // This currently implies IEEE floating-point types and so decimal does not implement it
        // If we want decimal to implement it, we would need to move several methods down and define
        // some IeeeFloatingPoint interface instead. This wasn't done for simplicity and because decimal
        // is not an industry standard type.

        // TODO: The Exponent and Signifcand need to be exposed here
        // They should return INumber<TSelf> here and IBinaryNumber<TSelf> on IBinaryFloatingPoint<TSelf>

        // TODO: We may want to expose constants related to the subnormal and finite boundaries

        // The following constants are defined by System.Math and System.MathF today

        static abstract TSelf E { get; }

        static abstract TSelf PI { get; }

        static abstract TSelf Tau { get; }

        // The following methods are defined by System.Math and System.MathF today
        // Exposing them on the interfaces means they will become available as statics on the primitive types
        // This, in a way, obsoletes Math/MathF and brings float/double inline with how non-primitive types support similar functionality
        // API review will need to determine if we'll want to continue adding APIs to Math/MathF in the future

        static abstract TSelf Acos(TSelf x);

        static abstract TSelf Acosh(TSelf x);

        static abstract TSelf Asin(TSelf x);

        static abstract TSelf Asinh(TSelf x);

        static abstract TSelf Atan(TSelf x);

        static abstract TSelf Atan2(TSelf y, TSelf x);

        static abstract TSelf Atanh(TSelf x);

        static abstract TSelf BitIncrement(TSelf x);

        static abstract TSelf BitDecrement(TSelf x);

        static abstract TSelf Cbrt(TSelf x);

        static abstract TSelf Ceiling(TSelf x);

        // CopySign is likely more general purpose and may apply to any signed number type
        // It may even be applicable to unsigned numbers where it simply returns x, for convenience

        static abstract TSelf CopySign(TSelf x, TSelf y);

        static abstract TSelf Cos(TSelf x);

        static abstract TSelf Cosh(TSelf x);

        static abstract TSelf Exp(TSelf x);

        static abstract TSelf Floor(TSelf x);

        static abstract TSelf FusedMultiplyAdd(TSelf x, TSelf y, TSelf z);

        static abstract TSelf IEEERemainder(TSelf x, TSelf y);

        // IEEE defines the result to be an integral type, but not the size

        static abstract TInteger ILogB<TInteger>(TSelf x)
            where TInteger : IBinaryInteger<TInteger>;

        static abstract TSelf Log(TSelf x);

        static abstract TSelf Log(TSelf x, TSelf newBase);

        static abstract TSelf Log2(TSelf x);

        static abstract TSelf Log10(TSelf x);

        static abstract TSelf MaxMagnitude(TSelf x, TSelf y);

        static abstract TSelf MinMagnitude(TSelf x, TSelf y);

        static abstract TSelf Pow(TSelf x, TSelf y);

        static abstract TSelf Round(TSelf x);

        static abstract TSelf Round<TInteger>(TSelf x, TInteger digits)
            where TInteger : IBinaryInteger<TInteger>;

        static abstract TSelf Round(TSelf x, MidpointRounding mode);

        static abstract TSelf Round<TInteger>(TSelf x, TInteger digits, MidpointRounding mode)
            where TInteger : IBinaryInteger<TInteger>;

        // IEEE defines n to be an integral type, but not the size

        static abstract TSelf ScaleB<TInteger>(TSelf x, TInteger n)
            where TInteger : IBinaryInteger<TInteger>;

        static abstract TSelf Sin(TSelf x);

        static abstract TSelf Sinh(TSelf x);

        static abstract TSelf Sqrt(TSelf x);

        static abstract TSelf Tan(TSelf x);

        static abstract TSelf Tanh(TSelf x);

        static abstract TSelf Truncate(TSelf x);

        // The following members are exposed on the floating-point types as constants today
        // This may be of concern when implementing the interface

        static abstract TSelf Epsilon { get; }

        static abstract TSelf NaN { get; }

        static abstract TSelf NegativeInfinity { get; }

        static abstract TSelf NegativeZero { get; }

        static abstract TSelf PositiveInfinity { get; }

        // The following methods are exposed on the floating-point types today

        static abstract bool IsFinite(TSelf value);

        static abstract bool IsInfinity(TSelf value);

        static abstract bool IsNaN(TSelf value);

        static abstract bool IsNegative(TSelf value);

        static abstract bool IsNegativeInfinity(TSelf value);

        static abstract bool IsNormal(TSelf value);

        static abstract bool IsPositiveInfinity(TSelf value);

        static abstract bool IsSubnormal(TSelf value);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf AcosPi(TSelf x);

        static abstract TSelf AsinPi(TSelf x);

        static abstract TSelf AtanPi(TSelf x);

        static abstract TSelf Atan2Pi(TSelf y, TSelf x);

        static abstract TSelf Compound(TSelf x, TSelf n);

        static abstract TSelf CosPi(TSelf x);

        static abstract TSelf ExpM1(TSelf x);

        static abstract TSelf Exp2(TSelf x);

        static abstract TSelf Exp2M1(TSelf x);

        static abstract TSelf Exp10(TSelf x);

        static abstract TSelf Exp10M1(TSelf x);

        static abstract TSelf Hypot(TSelf x, TSelf y);

        static abstract TSelf LogP1(TSelf x);

        static abstract TSelf Log2P1(TSelf x);

        static abstract TSelf Log10P1(TSelf x);

        static abstract TSelf MaxMagnitudeNumber(TSelf x, TSelf y);

        static abstract TSelf MaxNumber(TSelf x, TSelf y);

        static abstract TSelf MinMagnitudeNumber(TSelf x, TSelf y);

        static abstract TSelf MinNumber(TSelf x, TSelf y);

        static abstract TSelf Root(TSelf x, TSelf n);

        static abstract TSelf SinPi(TSelf x);

        static abstract TSelf TanPi(TSelf x);

        // The majority of the IEEE required operations are listed below
        // This doesn't include the recommended operations such as sin, cos, acos, etc

        // TODO: There are a couple methods below not covered in the list above, we should
        // review these and determine if they should be implemented

        // 5.3.1
        //  TSelf RoundToIntegral(TSelf x);
        //      TiesToEven
        //      TiesToAway
        //      TowardZero
        //      TowardPositive
        //      TowardNegative
        //      Exact
        //  TSelf NextUp(TSelf x);
        //  TSelf NextDown(TSelf x);
        //  TSelf Remainder(TSelf x, TSelf y);

        // 5.3.3
        //  TSelf ScaleB(TSelf x, TLogBFormat n);
        //  TLogBFormat LogB(TSelf x);

        // 5.4.1
        //  TSelf Addition(TSelf x, TSelf y);
        //  TSelf Subtraction(TSelf x, TSelf y);
        //  TSelf Multiplication(TSelf x, TSelf y);
        //  TSelf Division(TSelf x, TSelf y);
        //  TSelf SquareRoot(TSelf x);
        //  TSelf FusedMultiplyAdd(TSelf x, TSelf y, TSelf z);
        //  TSelf ConvertFromInt(TInt x);
        //  TInt ConvertToInteger(TSelf x);
        //      TiesToEven
        //      TowardZero
        //      TowardPositive
        //      TowardNegative
        //      TiesToAway
        //      ExactTiesToEven
        //      ExactTowardZero
        //      ExactTowardPositive
        //      ExactTowardNegative
        //      ExactTiesToAway

        // 5.4.2
        //  TOther ConvertFormat(TSelf x);
        //  TSelf ConvertFromDecimalCharacter(string s);
        //  string ConvertToDecimalCharacter(TSelf x, TFormat format);

        // 5.4.3
        //  TResult ConvertFromHexCharacter(string s);
        //  string ConvertToHexCharacter(TSelf x, TFormat format);

        // 5.5.1
        //  TSelf Copy(TSelf x);
        //  TSelf Negate(TSelf x);
        //  TSelf Abs(TSelf x);
        //  TSelf CopySign(TSelf x, TSelf y);

        // 5.6.1
        //  bool Compare(TSelf x, TSelf y);
        //      QuietEqual
        //      QuietNotEqual
        //      SignalingEqual
        //      SignalingGreater
        //      SignalingGreaterEqual
        //      SignalingLess
        //      SignalingLessEqual
        //      SignalingNotEqual
        //      SignalingNotGreater
        //      SignalingLessUnordered
        //      SignalingNotLess
        //      SignalingGreaterUnordered
        //      QuietGreater
        //      QuietGreaterEqual
        //      QuietLess
        //      QuietLessEqual
        //      QuietUnordered
        //      QuietNotGreater
        //      QuietLessUnordered
        //      QuietNotLess
        //      QuietGreaterUnordered
        //      QuietOrdered

        // 5.7.1
        //  bool Is754Version1985
        //  bool Is754Version2008
        //  bool Is754Version2019

        // 5.7.2
        //  enum Class(TSelf x);
        //  bool IsSignMinus(TSelf x);
        //  bool IsNormal(TSelf x);
        //  bool IsFinite(TSelf x);
        //  bool IsZero(TSelf x);
        //  bool IsSubnormal(TSelf x);
        //  bool IsInfinite(TSelf x);
        //  bool IsNaN(TSelf x);
        //  bool IsSignaling(TSelf x);
        //  bool IsCanonical(TSelf x);
        //  enum Radix(TSelf x);
        //  bool TotalOrder(TSelf x, TSelf y);
        //  bool TotalOrderMag(TSelf x, TSelf y);

        // 9.4
        //  TSelf Sum(TSelf[] x);
        //  TSelf Dot(TSelf[] x, TSelf[] y);
        //  TSelf SumSquare(TSelf[] x);
        //  TSelf SumAbs(TSelf[] x);
        //  (TSelf, TInt) ScaledProd(TSelf[] x);
        //  (TSelf, TInt) ScaledProdSum(TSelf[] x, TSelf[] y);
        //  (TSelf, TInt) ScaledProdDiff(TSelf[] x, TSelf[] y);

        // 9.5
        //  (TSelf, TSelf) AugmentedAddition(TSelf x, TSelf y);
        //  (TSelf, TSelf) AugmentedSubtraction(TSelf x, TSelf y);
        //  (TSelf, TSelf) AugmentedMultiplication(TSelf x, TSelf y);

        // 9.6
        //  TSelf Minimum(TSelf x, TSelf y);
        //  TSelf MinimumNumber(TSelf x, TSelf y);
        //  TSelf Maximum(TSelf x, TSelf y);
        //  TSelf MaximumNumber(TSelf x, TSelf y);
        //  TSelf MinimumMagnitude(TSelf x, TSelf y);
        //  TSelf MinimumMagnitudeNumber(TSelf x, TSelf y);
        //  TSelf MaximumMagnitude(TSelf x, TSelf y);
        //  TSelf MaximumMagnitudeNumber(TSelf x, TSelf y);

        // 9.7
        //  TSelf GetPayload(TSelf x);
        //  TSelf SetPayload(TSelf x);
        //  TSelf SetPayloadSignaling(TSelf x);
    }

    public interface IBinaryFloatingPoint<TSelf>
        : IBinaryNumber<TSelf>,
          IFloatingPoint<TSelf>
        where TSelf : IBinaryFloatingPoint<TSelf>
    {
    }

    public interface IDecimalFloatingPoint<TSelf>
        : IFloatingPoint<TSelf>
        where TSelf : IDecimalFloatingPoint<TSelf>
    {
        // This interface is defined for convenience of viewing the IEEE requirements
        // it would not actually be defined until the .NET libraries requires it

        // The majority of the IEEE required operations are listed below
        // This doesn't include the recommended operations such as sin, cos, acos, etc

        // 5.3.2
        //  TSelf Quantize(TSelf x, TSelf y);
        //  TSelf Quantum(TSelf x);

        // 5.5.2
        //  TDecEnc EncodeDecimal(TSelf x);
        //  TSelf DecodeDecimal(TDecEnc x);
        //  TBinEnc EncodeBinary(TSelf x);
        //  TSelf DecodeBinary(TBinEnc x);

        // 5.7.3
        //  bool SameQuantum(TSelf x, TSelf y);
    }
}

namespace System.Numerics
{
    public interface IVector<TSelf, TScalar>
        : IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IBitwiseOperators<TSelf, TSelf, TSelf>,
          IComparisonOperators<TSelf, TSelf>,   // implies IEqualityOperators<TSelf, TSelf>
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IDivisionOperators<TSelf, TScalar, TSelf>,
          IMultiplyOperators<TSelf, TSelf, TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplicativeIdentity<TSelf, TScalar>,
          IMultiplyOperators<TSelf, TScalar, TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>,
          ISpanFormattable,                     // implies IFormattable
          ISubtractionOperators<TSelf, TSelf, TSelf>
        where TSelf : IVector<TSelf, TScalar>
    {
        static abstract TSelf Create(TScalar value);
        static abstract TSelf Create(TScalar[] values);
        static abstract TSelf Create(TScalar[] values, int startIndex);
        static abstract TSelf Create(ReadOnlySpan<TScalar> values);

        static abstract int Count { get; }

        static abstract TSelf One { get; }

        static abstract TSelf Zero { get; }

        TScalar this[int index] { get; }

        static abstract TSelf Abs(TSelf value);
        static abstract TSelf AndNot(TSelf left, TSelf right);
        static abstract TSelf ConditionalSelect(TSelf condition, TSelf left, TSelf right);
        static abstract TSelf Dot(TSelf left, TSelf right);
        static abstract TSelf Max(TSelf left, TSelf right);
        static abstract TSelf Min(TSelf left, TSelf right);
        static abstract TSelf SquareRoot(TSelf value);

        static abstract TSelf Equals(TSelf left, TSelf right);
        static abstract TSelf GreaterThan(TSelf left, TSelf right);
        static abstract TSelf GreaterThanOrEqual(TSelf left, TSelf right);
        static abstract TSelf LessThan(TSelf left, TSelf right);
        static abstract TSelf LessThanOrEqual(TSelf left, TSelf right);

        static abstract bool EqualsAll(TSelf left, TSelf right);
        static abstract bool GreaterThanAll(TSelf left, TSelf right);
        static abstract bool GreaterThanOrEqualAll(TSelf left, TSelf right);
        static abstract bool LessThanAll(TSelf left, TSelf right);
        static abstract bool LessThanOrEqualAll(TSelf left, TSelf right);

        static abstract bool EqualsAny(TSelf left, TSelf right);
        static abstract bool GreaterThanAny(TSelf left, TSelf right);
        static abstract bool GreaterThanOrEqualAny(TSelf left, TSelf right);
        static abstract bool LessThanAny(TSelf left, TSelf right);
        static abstract bool LessThanOrEqualAny(TSelf left, TSelf right);

        void CopyTo(TScalar[] destination);
        void CopyTo(TScalar[] destination, int startIndex);
        void CopyTo(Span<TScalar> destination);

        bool TryCopyTo(Span<TScalar> destination);
    }
}
```

### Interface Implementors

Various types in the System namespace would implement the above interfaces as follows.

```csharp
namespace System
{
    public struct Byte
        : IBinaryInteger<byte>,
          IConvertible,
          IMinMaxValue<byte>,
          IUnsignedNumber<byte>
    {
    }

    public struct Char
        : IBinaryInteger<char>,
          IConvertible,
          IMinMaxValue<char>,
          IUnsignedNumber<char>
    {
    }

    public struct DateOnly
        : IComparisonOperators<DateOnly, DateOnly>,
          IMinMaxValue<DateOnly>,
          ISpanFormattable,
          ISpanParseable<DateOnly>
    {
    }

    public struct DateTime
        : IAdditionOperators<DateTime, TimeSpan, DateTime>,
          IAdditiveIdentity<DateTime, TimeSpan>,
          IComparisonOperators<DateTime, DateTime>,
          IConvertible,
          IMinMaxValue<DateTime>,
          ISerializable,
          ISpanFormattable,
          ISpanParseable<DateTime>,
          ISubtractionOperators<DateTime, TimeSpan, DateTime>,
          ISubtractionOperators<DateTime, DateTime, TimeSpan>
    {
    }

    public struct DateTimeOffset
        : IAdditionOperators<DateTimeOffset, TimeSpan, DateTimeOffset>,
          IAdditiveIdentity<DateTimeOffset, TimeSpan>,
          IComparisonOperators<DateTimeOffset, DateTimeOffset>,
          IDeserializationCallback,
          IMinMaxValue<DateTimeOffset>,
          ISerializable,
          ISpanFormattable,
          ISpanParseable<DateTimeOffset>,
          ISubtractionOperators<DateTimeOffset, TimeSpan, DateTimeOffset>,
          ISubtractionOperators<DateTimeOffset, DateTimeOffset, TimeSpan>
    {
        // TODO: DateTimeOffset defines an implicit conversion to DateTime, should that be modeled in the interfaces?
    }

    public struct Decimal
        : IConvertible,
          IDeserializationCallback,
          IMinMaxValue<decimal>,
          ISerializable,
          ISignedNumber<decimal>
    {
        // Decimal defines a few additional operations like Ceiling, Floor, Round, and Truncate
        // The rest of the IEEE operations are missing.

        // Decimal and some other types likewise define "friendly" names for operators, should we expose them?
    }

    public struct Double
        : IBinaryFloatingPoint<double>,
          IConvertible,
          IMinMaxValue<double>
    {
    }

    public class Enum
        : ValueType,
          IComparable,
          IConvertible,
          IFormattable
    {
    }

    public struct Guid
        : IComparisonOperators<Guid, Guid>,
          ISpanFormattable,
          ISpanParseable<Guid>
    {
    }

    public struct Half
        : IBinaryFloatingPoint<Half>,
          IMinMaxValue<Half>
    {
    }

    public struct Int16
        : IBinaryInteger<short>,
          IConvertible,
          IMinMaxValue<short>,
          ISignedNumber<short>
    {
    }

    public struct Int32
        : IBinaryInteger<int>,
          IConvertible,
          IMinMaxValue<int>,
          ISignedNumber<int>
    {
    }

    public struct Int64
        : IBinaryInteger<long>,
          IConvertible,
          IMinMaxValue<long>,
          ISignedNumber<long>
    {
    }

    public struct IntPtr
        : IBinaryInteger<nint>,
          IMinMaxValue<nint>,
          ISerializable,
          ISignedNumber<nint>
    {
    }

    public struct SByte
        : IBinaryInteger<sbyte>,
          IConvertible,
          IMinMaxValue<sbyte>,
          ISignedNumber<sbyte>
    {
    }

    public struct Single
        : IBinaryFloatingPoint<float>,
          IConvertible,
          IMinMaxValue<float>
    {
    }

    public struct TimeOnly
        : IComparisonOperators<TimeOnly, TimeOnly>,
          IMinMaxValue<TimeOnly>,
          ISpanFormattable,
          ISpanParseable<TimeOnly>,
          ISubtractionOperators<TimeOnly, TimeOnly, TimeSpan>
    {
    }

    public struct TimeSpan
        : IAdditionOperators<TimeSpan, TimeSpan, TimeSpan>,
          IAdditiveIdentity<TimeSpan, TimeSpan>,
          IComparisonOperators<TimeSpan, TimeSpan>,
          IDivisionOperators<TimeSpan, double, TimeSpan>,
          IDivisionOperators<TimeSpan, TimeSpan, double>,
          IMinMaxValue<TimeSpan>,
          IMultiplicativeIdentity<TimeSpan, double>,
          IMultiplyOperators<TimeSpan, double, TimeSpan>,
          IUnaryNegationOperators<TimeSpan, TResult>,
          ISpanFormattable,
          ISubtractionOperators<TimeSpan, TimeSpan, TimeSpan>
    {
    }

    public struct UInt16
        : IBinaryInteger<ushort>,
          IConvertible,
          IMinMaxValue<ushort>,
          IUnsignedNumber<ushort>
    {
    }

    public struct UInt32
        : IBinaryInteger<uint>,
          IConvertible,
          IMinMaxValue<uint>,
          IUnsignedNumber<uint>
    {
    }

    public struct UInt64
        : IBinaryInteger<ulong>,
          IConvertible,
          IMinMaxValue<ulong>,
          IUnsignedNumber<ulong>
    {
    }

    public struct UIntPtr
        : IBinaryInteger<nuint>,
          IMinMaxValue<nuint>,
          ISerializable,
          IUnsignedNumber<nuint>
    {
    }
}

namespace System.Numerics
{
    public struct Vector<T> : IVector<Vector<T>, T>
    {
    }

    public struct Vector2 : IVector<Vector2, float>
    {
    }

    public struct Vector3 : IVector<Vector3, float>
    {
    }

    public struct Vector4 : IVector<Vector4, float>
    {
    }
}

namespace System.Runtime.Intrinsics
{
    public struct Vector64<T> : IVector<Vector64<T>, T>
    {
    }

    public struct Vector128<T> : IVector<Vector128<T>, T>
    {
    }

    public struct Vector256<T> : IVector<Vector256<T>, T>
    {
    }
}
```

### Pending Concepts

There are several types which may benefit from some interface support. These include, but aren't limited to:
* System.Array
* System.Enum
* System.Index
* System.Range
* System.Tuple
* System.ValueTuple
* System.Numerics.BigInteger
* System.Numerics.Complex
* System.Numerics.Matrix3x2
* System.Numerics.Matrix4x4
* System.Numerics.Plane
* System.Numerics.Quaternion

Likewise, there are many comments within the defined interfaces above that call out key points that need additional modeling/consideration. Several of these concepts will be determined via user research and outreach while others will be determined via API review and feedback from other area experts. Others may be determined by language or runtime limitations in what is feasible for them to support.

Do we want a way to denote "primitives"? If we have it, should this be language primitives, runtime primitives, or ABI primitives?

Do we want an explicit `IConvertible<TSelf, TOther>` (or `IConvertibleFrom`/`IConvertibleTo`)?

Do we want a way to track "scalars" for vectors and matrices?

## Requirements
