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

The .NET Libraries are exposing interfaces such as (NOTE: simplified names/examples are given below):
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
interface "IComparable"
interface "IComparable<T>"
interface "IConvertible"
interface "IDeserializationCallback"
interface "IEquatable<T>"
interface "IFormattable"
interface "IParsable<TSelf>"
interface "ISerializable"
interface "ISpanFormattable"
interface "ISpanParsable<TSelf>"

interface "IAdditionOperators<TSelf, TOther, TResult>"
interface "IAdditiveIdentity<TSelf, TResult>"
interface "IBinaryFloatingPointIeee754<TSelf>"
interface "IBinaryInteger<TSelf>"
interface "IBinaryNumber<TSelf>"
interface "IBitwiseOperators<TSelf, TOther, TResult>"
interface "IComparisonOperators<TSelf, TOther>"
interface "IDecrementOperators<TSelf>"
interface "IDivisionOperators<TSelf, TOther, TResult>"
interface "IEqualityOperators<TSelf, TOther>"
interface "IExponentialFunctions<TSelf>"
interface "IFloatingPoint<TSelf>"
interface "IFloatingPointIeee754<TSelf>"
interface "IHyperbolicFunctions<TSelf>"
interface "IIncrementOperators<TSelf>"
interface "ILogarithmicFunctions<TSelf>"
interface "IMinMaxValue<TSelf>"
interface "IModulusOperators<TSelf, TOther, TResult>"
interface "IMultiplicativeIdentity<TSelf, TResult>"
interface "IMultiplyOperators<TSelf, TOther, TResult>"
interface "INumber<TSelf>"
interface "INumberBase<TSelf>"
interface "INumberConverter<T, U>"
interface "INumberConverterChecked<T, U>"
interface "INumberConverterSaturating<T, U>"
interface "INumberConverterTruncating<T, U>"
interface "IPowerFunctions<TSelf>"
interface "IRootFunctions<TSelf>"
interface "IShiftOperators<TSelf, TOther, TResult>"
interface "ISignedNumber<TSelf>"
interface "ISubtractionOperators<TSelf, TOther, TResult>"
interface "ITrigonometricFunctions<TSelf>"
interface "IUnaryNegationOperators<TSelf, TResult>"
interface "IUnaryPlusOperators<TSelf, TResult>"
interface "IUnsignedNumber<TSelf>"
interface "IVector<TSelf, TScalar>"

"IBinaryNumber<TSelf>"                          <|-- "IBinaryFloatingPointIeee754<TSelf>"
"IFloatingPointIeee754<TSelf>"                  <|-- "IBinaryFloatingPointIeee754<TSelf>"

"IBinaryNumber<TSelf>"                          <|-- "IBinaryInteger<TSelf>"
"IShiftOperators<TSelf, TOther, TResult>"       <|-- "IBinaryInteger<TSelf>"

"IBitwiseOperators<TSelf, TOther, TResult>"     <|-- "IBinaryNumber<TSelf>"
"INumber<TSelf>"                                <|-- "IBinaryNumber<TSelf>"

"IComparable"                                   <|-- "IComparisonOperators<TSelf, TOther>"
"IComparable<T>"                                <|-- "IComparisonOperators<TSelf, TOther>"
"IEqualityOperators<TSelf, TOther>"             <|-- "IComparisonOperators<TSelf, TOther>"

"IEquatable<T>"                                 <|-- "IEqualityOperators<TSelf, TOther>"

"INumber<TSelf>"                                <|-- "IFloatingPoint<TSelf>"
"ISignedNumber<TSelf>"                          <|-- "IFloatingPoint<TSelf>"

"IExponentialFunctions<TSelf>"                  <|-- "IFloatingPointIeee754<TSelf>"
"IFloatingPoint<TSelf>"                         <|-- "IFloatingPointIeee754<TSelf>"
"IHyperbolicFunctions<TSelf>"                   <|-- "IFloatingPointIeee754<TSelf>"
"ILogarithmicFunctions<TSelf>"                  <|-- "IFloatingPointIeee754<TSelf>"
"IPowerFunctions<TSelf>"                        <|-- "IFloatingPointIeee754<TSelf>"
"IRootFunctions<TSelf>"                         <|-- "IFloatingPointIeee754<TSelf>"
"ITrigonometricFunctions<TSelf>"                <|-- "IFloatingPointIeee754<TSelf>"

"IComparisonOperators<TSelf, TOther>"           <|-- "INumber<TSelf>"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"IModulusOperators<TSelf, TOther, TResult>"     <|-- "INumber<TSelf>"
"INumberBase<TSelf>"                            <|-- "INumber<TSelf>"
"ISpanFormattable"                              <|-- "INumber<TSelf>"
"ISpanParsable<TSelf>"                          <|-- "INumber<TSelf>"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "INumberBase<TSelf>"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "INumberBase<TSelf>"
"IDecrementOperators<TSelf>"                    <|-- "INumberBase<TSelf>"
"IEqualityOperators<TSelf, TOther>"             <|-- "INumberBase<TSelf>"
"IIncrementOperators<TSelf>"                    <|-- "INumberBase<TSelf>"
"IMultiplicativeIdentity<TSelf, TResult>"       <|-- "INumberBase<TSelf>"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "INumberBase<TSelf>"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "INumberBase<TSelf>"
"IUnaryPlusOperators<TSelf, TResult>"           <|-- "INumberBase<TSelf>"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "INumberBase<TSelf>"

"IFormattable"                                  <|-- "ISpanFormattable"

"IParsable<TSelf>"                              <|-- "ISpanParsable<TSelf>"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "IVector<TSelf, TScalar>"
"IBitwiseOperators<TSelf, TOther, TResult>"     <|-- "IVector<TSelf, TScalar>"
"IComparisonOperators<TSelf, TOther>"           <|-- "IVector<TSelf, TScalar>"
"IDecrementOperators<TSelf>"                    <|-- "IVector<TSelf, TScalar>"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"IIncrementOperators<TSelf>"                    <|-- "IVector<TSelf, TScalar>"
"IModulusOperators<TSelf, TOther, TResult>"     <|-- "IVector<TSelf, TScalar>"
"IMultiplicativeIdentity<TSelf, TResult>"       <|-- "IVector<TSelf, TScalar>"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "IVector<TSelf, TScalar>"
"ISpanFormattable"                              <|-- "IVector<TSelf, TScalar>"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "IVector<TSelf, TScalar>"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "IVector<TSelf, TScalar>"

class "BigInteger"
class "Byte"
class "Char"
class "CLong"
class "Complex"
class "CULong"
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
class "Int128"
class "IntPtr"
class "NFloat"
class "Object"
class "SByte"
class "Single"
class "TimeSpan"
class "UInt16"
class "UInt32"
class "UInt64"
class "UInt128"
class "UIntPtr"
class "ValueType"
class "Vector<T>"
class "Vector64<T>"
class "Vector128<T>"
class "Vector256<T>"
class "Vector2"
class "Vector3"
class "Vector4"

"IBinaryInteger<TSelf>"                         <|-- "BigInteger"
"ISignedNumber<TSelf>"                          <|-- "BigInteger"
"ValueType"                                     <|-- "BigInteger"

"IBinaryInteger<TSelf>"                         <|-- "Byte"
"IConvertible"                                  <|-- "Byte"
"IMinMaxValue<TSelf>"                           <|-- "Byte"
"IUnsignedNumber<TSelf>"                        <|-- "Byte"
"ValueType"                                     <|-- "Byte"

"IBinaryInteger<TSelf>"                         <|-- "CLong"
"IMinMaxValue<TSelf>"                           <|-- "CLong"
"IUnsignedNumber<TSelf>"                        <|-- "CLong"
"ValueType"                                     <|-- "CLong"

"IBinaryInteger<TSelf>"                         <|-- "Char"
"IConvertible"                                  <|-- "Char"
"IMinMaxValue<TSelf>"                           <|-- "Char"
"IUnsignedNumber<TSelf>"                        <|-- "Char"
"ValueType"                                     <|-- "Char"

"IBinaryInteger<TSelf>"                         <|-- "CULong"
"IMinMaxValue<TSelf>"                           <|-- "CULong"
"IUnsignedNumber<TSelf>"                        <|-- "CULong"
"ValueType"                                     <|-- "CULong"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "Complex"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "Complex"
"IDivisionOperators<TSelf, TOther, TResult>"    <|-- "Complex"
"IFormattable"                                  <|-- "Complex"
"IExponentialFunctions<TSelf>"                  <|-- "Complex"
"IHyperbolicFunctions<TSelf>"                   <|-- "Complex"
"ILogarithmicFunctions<TSelf>"                  <|-- "Complex"
"IMultiplyOperators<TSelf, TOther, TResult>"    <|-- "Complex"
"INumberBase<TSelf>"                            <|-- "Complex"
"IPowerFunctions<TSelf>"                        <|-- "Complex"
"IRootFunctions<TSelf>"                         <|-- "Complex"
"ITrigonometricFunctions<TSelf>"                <|-- "Complex"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "Complex"
"ValueType"                                     <|-- "Complex"

"IComparisonOperators<TSelf, TOther>"           <|-- "DateOnly"
"IMinMaxValue<TSelf>"                           <|-- "DateOnly"
"ISpanFormattable"                              <|-- "DateOnly"
"ISpanParsable<TSelf>"                          <|-- "DateOnly"
"ValueType"                                     <|-- "DateOnly"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "DateTime"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "DateTime"
"IComparisonOperators<TSelf, TOther>"           <|-- "DateTime"
"IConvertible"                                  <|-- "DateTime"
"IMinMaxValue<TSelf>"                           <|-- "DateTime"
"ISerializable"                                 <|-- "DateTime"
"ISpanFormattable"                              <|-- "DateTime"
"ISpanParsable<TSelf>"                          <|-- "DateTime"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "DateTime"
"ValueType"                                     <|-- "DateTime"

"IAdditionOperators<TSelf, TOther, TResult>"    <|-- "DateTimeOffset"
"IAdditiveIdentity<TSelf, TResult>"             <|-- "DateTimeOffset"
"IComparisonOperators<TSelf, TOther>"           <|-- "DateTimeOffset"
"IDeserializationCallback"                      <|-- "DateTimeOffset"
"IMinMaxValue<TSelf>"                           <|-- "DateTimeOffset"
"ISerializable"                                 <|-- "DateTimeOffset"
"ISpanFormattable"                              <|-- "DateTimeOffset"
"ISpanParsable<TSelf>"                          <|-- "DateTimeOffset"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "DateTimeOffset"
"ValueType"                                     <|-- "DateTimeOffset"

"IConvertible"                                  <|-- "Decimal"
"IDeserializationCallback"                      <|-- "Decimal"
"IFloatingPoint<TSelf>"                         <|-- "Decimal"
"IMinMaxValue<TSelf>"                           <|-- "Decimal"
"ISerializable"                                 <|-- "Decimal"
"ValueType"                                     <|-- "Decimal"

"IBinaryFloatingPointIeee754<TSelf>"            <|-- "Double"
"IConvertible"                                  <|-- "Double"
"IMinMaxValue<TSelf>"                           <|-- "Double"
"ValueType"                                     <|-- "Double"

"IComparable"                                   <|-- "Enum"
"IConvertible"                                  <|-- "Enum"
"IFormattable"                                  <|-- "Enum"
"ValueType"                                     <|-- "Enum"

"IComparisonOperators<TSelf, TOther>"           <|-- "Guid"
"ISpanFormattable"                              <|-- "Guid"
"ISpanParsable<TSelf>"                          <|-- "Guid"
"ValueType"                                     <|-- "Guid"

"IBinaryFloatingPointIeee754<TSelf>"            <|-- "Half"
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

"IBinaryInteger<TSelf>"                         <|-- "Int128"
"IConvertible"                                  <|-- "Int128"
"IMinMaxValue<TSelf>"                           <|-- "Int128"
"ISignedNumber<TSelf>"                          <|-- "Int128"
"ValueType"                                     <|-- "Int128"

"IBinaryInteger<TSelf>"                         <|-- "IntPtr"
"IMinMaxValue<TSelf>"                           <|-- "IntPtr"
"ISignedNumber<TSelf>"                          <|-- "IntPtr"
"ISerializable"                                 <|-- "IntPtr"
"ValueType"                                     <|-- "IntPtr"

"IBinaryFloatingPointIeee754<TSelf>"            <|-- "NFloat"
"IConvertible"                                  <|-- "NFloat"
"IMinMaxValue<TSelf>"                           <|-- "NFloat"
"ValueType"                                     <|-- "NFloat"

"IBinaryInteger<TSelf>"                         <|-- "SByte"
"IConvertible"                                  <|-- "SByte"
"IMinMaxValue<TSelf>"                           <|-- "SByte"
"ISignedNumber<TSelf>"                          <|-- "SByte"
"ValueType"                                     <|-- "SByte"

"IBinaryFloatingPointIeee754<TSelf>"            <|-- "Single"
"IConvertible"                                  <|-- "Single"
"IMinMaxValue<TSelf>"                           <|-- "Single"
"ValueType"                                     <|-- "Single"

"IComparisonOperators<TSelf, TOther>"           <|-- "TimeOnly"
"IMinMaxValue<TSelf>"                           <|-- "TimeOnly"
"ISpanFormattable"                              <|-- "TimeOnly"
"ISpanParsable<TSelf>"                          <|-- "TimeOnly"
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
"ISpanParsable<TSelf>"                          <|-- "TimeSpan"
"ISubtractionOperators<TSelf, TOther, TResult>" <|-- "TimeSpan"
"IUnaryNegationOperators<TSelf, TResult>"       <|-- "TimeSpan"
"IUnaryPlusOperators<TSelf, TResult>"           <|-- "TimeSpan"
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

"IBinaryInteger<TSelf>"                         <|-- "UInt128"
"IConvertible"                                  <|-- "UInt128"
"IMinMaxValue<TSelf>"                           <|-- "UInt128"
"IUnsignedNumber<TSelf>"                        <|-- "UInt128"
"ValueType"                                     <|-- "UInt128"

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

![UML](http://www.plantuml.com/plantuml/svg/h9XBKzi-4C3l_XIPdFySU91_3mpJyg1ufZGPd73VE1j7hIobaWouqm_V6AY29MUZAEJ2x7hrq-gn3_5P6r2cAdd2X44rXnnx_VHIbajGCENOtw0_6v-xdyGZAiEyCLUeKJ7Wx3SO9iKbS3w5_6VRwFfN1QQJUoDL2SRulen0wTSH6VAvCofxcz8VbYr1ECpsHu_RilWocx5c6SCbAZ1IwLUp_thZeLcWMlszHrrninhd3tl4T8R2C5FRKPtM5qo0gc-u1CD4CP9Cc1GH_pyziKxvvkbAKw0YBBvNvNIZWNbY6eDMvLu7fZTkXsD_XRd24eLnp5qpziXqs1wxTuWpKuVvSlsybA8v3E0tbSYRC-tsfdKCX4N0GTtMIrHJoLbEJvcAe7tw9WjGp2pARRmx9kxW-G5uXTtVvQpYbTvnl-zMAhRaB8VGcst7r3jEvTrSzzC5wEwbZUGJAdftxgKqj4MsO7EpeyiPAmJE2CUpQceKv179PAnO8OKiqQXj9pzvYNiie7CQ7s_7Z7YbW-trjhK-OBxYM4oM0mVLfDRkrDJR-3lxSt0GbY3fi8mdnplTdQQ3RrS8BDaXa_iqvm3TiEkHFvgLj7eAccDXWHN6RKi-41rGL6BHYTFq1Er8K9MBFRZkGjUToI9X2Lrt2Lfuf3kmM2PHgwEPLC6EPNONkFtMxbV4_VUJBbenp2Geq7oeb-R2-qs24jv44RwuxGonxnJ5VUIG39hYlePsfIIxF9aSZDFM1RoYh3CqYNY9M4G8AQ904AHKV8nFmPrvEAdUz-nsR7S9Kb1_6hHFmTtnLj8A5Hvj5kM7QGEY0uu0kGFZGezercFYZy15jAe4Bg8O4BZe547W8eeCWKl5XtaNKRm8N4G22iNL--zTJ7KaS36PZS159YK2b-GSjExrBrZnzbRiMrLjq0gN2rX_QGA9uxELhy3WKF2wfHYp4Zt5S3xNQDPgp5a9V2tBQihNewv5LLhXQyLcLhW5FhT2AipXuBrqVFHU6foybqP6MN4u_O6viMBMMd76HC7NOkDzSo-iOjAUTjAUTzAUUEBC_FAljd6zNFF_7T2vencS-Bh3eq--ykXqqA5qv6D7_bki7Tmq40DX6BrlEIJYJ0-83k06tEWV2Mdq4SGYw_b00S-mVoRExDZNfXND2fRQ-Gf6Nm6_hr0Wjsnz1NgCAVARqud8elrbUhfepsPZ2nOYor9Oimb0M5a8AmjXZOMmnbgRr2RPJ_i_SEeyupAFYTEO94uJaJWs1d5usOxJw2LEKvOu3LJYrBN4yViMfsQzFiJscRprW7cRZUGjlpplMVLjz2wNzmym3pyoUIo0_EqD0VZxMa1vAg0yV7_pKe3vhGBiFM6t35l5Yx5LJc-gH9V-5o_CLl7oR9NT8cqLBzLMyO9jbRsoRHNRVNgRnKg9otbLt2eiBgKLbqyWVgoSiF_q3GxiWqj3ScbuyZGTWC-8H1uxEni0M7ZaiRGhpnX8Tgwni5-DXGVEKcdFg0jACml_z5YQ5Cn83keo38ySXf5KM08BUsANEqWi38c58R4m9HRsnEwla5WO4Wj3Oc58B8o9DVwMfqx3OwUXlKXBQccExH67n668F0o9XocoCFrcTNO2aQKjipGyTngxaw-t29z0vR73oLkNavVtiRp6dyS1GiTqDZh8Qm7aPMwVvmBAmo5xteRdUI_WF1LmNWeu3mVjURs7bBSlK1v_x9wyTKzByQRXf2zjmQKzDkpVqnPRscFJ5ZxjiM4JdxROqXwRzl5fZyqx-RWrut5Fpflv-37tTrPilZzw7AVtelvUeQiwla9NUto9RWBv6bqSsbjaxt-u8EyQqyFB-_F7s-id)

### Base Interfaces

The base interfaces each describe one or more core operations that are used to build up the core interfaces listed later. In many cases, such as for addition, subtraction, multiplication, and division, there is only a single operator per interface. This split is important in order for them to be reusable against downstream types, like `DateTime` where addition does not imply subtraction or `Matrix` where multiplication does not imply division. These interfaces are not expected to be used by many users and instead primarily by developers looking at defining their own abstractions, as such we are not concerned with adding simplifying interfaces like `IAdditionOperators<TSelf> : IAdditionOperators<TSelf, TSelf>`. Likewise, every interface added introduces increased size and startup overhead, so we need to strike a balance between expressiveness, exxtensibility, and cost.

Some of the code below assumes that https://github.com/dotnet/csharplang/issues/4665 is going to be accepted into the language. If it is not accepted or if the approved syntax differs, the declarations will need to be updated.

```csharp
namespace System
{
    public interface IParsable<TSelf>
        where TSelf : IParsable<TSelf>
    {
        // These do not take NumberStyles as not all Parsable types are numbers (such as Guid)
        // Instead, INumber<TSelf> exposes additional parse methods that do take NumberStyles

        static abstract TSelf Parse(string s, IFormatProvider? provider);

        static abstract bool TryParse(string s, IFormatProvider? provider, out TSelf result);
    }

    public interface ISpanParsable<TSelf> : IParsable<TSelf>
        where TSelf : ISpanParsable<TSelf>
    {
        // This inherits from IParsable, much as ISpanFormattable is planned to inherit from IFormattable

        static abstract TSelf Parse(ReadOnlySpan<char> s, IFormatProvider? provider);

        static abstract bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TSelf result);
    }
}

namespace System.Numerics
{
    public interface IAdditionOperators<TSelf, TOther, TResult>
        where TSelf : IAdditionOperators<TSelf, TOther, TResult>
    {
        // An additive identity is not exposed here as it may be problematic for cases like
        // DateTime that implements both IAdditionOperators<DateTime, DateTime> and IAdditionOperators<DateTime, TimeSpan>
        // This would require overloading by return type and may be problematic for some scenarios where TOther might not have an additive identity

        // We can't assume TOther + TSelf is valid as not everything is commutative

        // We can't expose TSelf - TOther as some types only support one of the operations

        static abstract TResult operator +(TSelf left, TOther right);

        public static virtual TResult operator checked +(TSelf left, TOther right) => left + right;
    }

    public interface IBitwiseOperators<TSelf, TOther, TResult>
        where TSelf : IBitwiseOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator &(TSelf left, TOther right);

        static abstract TResult operator |(TSelf left, TOther right);

        static abstract TResult operator ^(TSelf left, TOther right);

        static abstract TResult operator ~(TSelf value);
    }

    public interface IComparisonOperators<TSelf, TOther> : IComparable, IComparable<TOther>, IEqualityOperators<TSelf, TOther>
        where TSelf : IComparisonOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IComparable<TSelf, TOther>

        // This inherits from IEqualityOperators<TSelf, TOther> as even though IComparable<T> does not
        // inherit from IEquatable<T>, the <= and >= operators as well as CompareTo iTSelf imply equality

        static abstract bool operator <(TSelf left, TOther right);

        static abstract bool operator <=(TSelf left, TOther right);     // ? DIM

        static abstract bool operator >(TSelf left, TOther right);

        static abstract bool operator >=(TSelf left, TOther right);     // ? DIM
    }

    public interface IDecrementOperators<TSelf>
        where TSelf : IDecrementOperators<TSelf>
    {
        static abstract TSelf operator --(TSelf value);

        public static virtual TSelf operator checked --(TSelf value) => --value;
    }

    public interface IDivisionOperators<TSelf, TOther, TResult>
        where TSelf : IDivisionOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator /(TSelf left, TOther right);

        public static virtual TResult operator checked /(TSelf left, TOther right) => left / right;
    }

    public interface IEqualityOperators<TSelf, TOther> : IEquatable<TOther>
        where TSelf : IEqualityOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IEquatable<TSelf, TOther>

        // This should not be `IEqualityOperators<TSelf>` as there are types like `Complex` where
        // TOther can be `double` and represent an optimization, both in size and perf

        static abstract bool operator ==(TSelf left, TOther right);

        static abstract bool operator !=(TSelf left, TOther right);     // ? DIM
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

        public static virtual TSelf operator checked ++(TSelf value) => ++value;
    }

    public interface IModulusOperators<TSelf, TOther, TResult>
        where TSelf : IModulusOperators<TSelf, TOther, TResult>
    {
        // The ECMA name is op_Modulus and so the behavior isn't actually "remainder" with regards to negative values

        // Likewise the name IModulusOperators doesn't fit with the other names, so a better one is needed

        static abstract TResult operator %(TSelf left, TOther right);
    }

    public interface IMultiplyOperators<TSelf, TOther, TResult>
        where TSelf : IMultiplyOperators<TSelf, TOther, TResult>
    {
        // A multiplicative identity is not exposed here for the same reasons as IAdditionOperators<TSelf, TOther>

        // We can't assume TOther * TSelf is valid as not everything is commutative

        // We can't expose TSelf / TOther as some types, such as matrices, don't support dividing with respect to TOther

        // We can't inherit from IAdditionOperators<TSelf, TOther> as some types, such as matrices, support Matrix * scalar but not Matrix + scalar

        static abstract TResult operator *(TSelf left, TOther right);

        public static virtual TResult operator checked *(TSelf left, TOther right) => left * right;
    }

    public interface IShiftOperators<TSelf, TOther, TResult>
        where TSelf : IShiftOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator <<(TSelf value, TOther shiftAmount);

        static abstract TResult operator >>(TSelf value, TOther shiftAmount);

        // Logical right shift
        static abstract TResult operator >>>(TSelf value, TOther shiftAmount);
    }

    public interface ISubtractionOperators<TSelf, TOther, TResult>
        where TSelf : ISubtractionOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator -(TSelf left, TOther right);

        public static virtual TResult operator checked -(TSelf left, TOther right) => left - right;
    }

    public interface IUnaryNegationOperators<TSelf, TResult>
        where TSelf : IUnaryNegationOperators<TSelf, TResult>
    {
        static abstract TResult operator -(TSelf value);

        public static virtual TResult operator checked -(TSelf value) => -value;
    }

    public interface IUnaryPlusOperators<TSelf, TResult>
        where TSelf : IUnaryPlusOperators<TSelf, TResult>
    {
        public static virtual TResult operator +(TSelf value) => value;
    }
}

namespace System.Numerics
{
    public interface IAdditiveIdentity<TSelf, TResult>
        where TSelf : IAdditiveIdentity<TSelf, TResult>
    {
        static abstract TResult AdditiveIdentity { get; }
    }

    public interface IMinMaxValue<TSelf>
        where TSelf : IMinMaxValue<TSelf>
    {
        // MinValue and MaxValue are more general purpose than just "numbers", so they live on this common base type

        static abstract TSelf MinValue { get; }

        static abstract TSelf MaxValue { get; }
    }

    public interface IMultiplicativeIdentity<TSelf, TResult>
        where TSelf : IMultiplicativeIdentity<TSelf, TResult>
    {
        static abstract TResult MultiplicativeIdentity { get; }
    }
}
```

### Numeric Interfaces

The numeric interfaces build upon the base interfaces by defining the core abstraction that most users are expected to interact with.

```csharp
namespace System.Numerics
{
    public interface IExponentialFunctions<TSelf>
        where TSelf : IExponentialFunctions<TSelf>, INumberBase<TSelf>
    {
        // These could have DIM implementations if they inherit
        // IPowerFunctions and had access to the constant `E`

        static abstract TSelf Exp(TSelf x);

        public static virtual TSelf ExpM1(TSelf x) => Exp(x) - TSelf.One;

        static abstract TSelf Exp2(TSelf x);

        public static virtual TSelf Exp2M1(TSelf x) => Exp2(x) - TSelf.One;

        static abstract TSelf Exp10(TSelf x);

        public static virtual TSelf Exp10M1(TSelf x) => Exp10(x) - TSelf.One;
    }

    public interface IHyperbolicFunctions<TSelf>
        where TSelf : IHyperbolicFunctions<TSelf>, INumberBase<TSelf>
    {
        static abstract TSelf Acosh(TSelf x);

        static abstract TSelf Asinh(TSelf x);

        static abstract TSelf Atanh(TSelf x);

        static abstract TSelf Cosh(TSelf x);

        static abstract TSelf Sinh(TSelf x);

        static abstract TSelf Tanh(TSelf x);
    }

    public interface ILogarithmicFunctions<TSelf>
        where TSelf : ILogarithmicFunctions<TSelf>, INumberBase<TSelf>
    {
        static abstract TSelf Log(TSelf x);

        static abstract TSelf Log(TSelf x, TSelf newBase);      // ? DIM - Needs special floating-point handling for NaN, One, and Zero

        static abstract TSelf Log2(TSelf x);

        static abstract TSelf Log10(TSelf x);

        public static virtual TSelf LogP1(TSelf x) => Log(x + TSelf.One);

        public static virtual TSelf Log2P1(TSelf x) => Log2(x + TSelf.One);

        public static virtual TSelf Log10P1(TSelf x) => Log10(x + TSelf.One);
    }

    public interface IPowerFunctions<TSelf>
        where TSelf : IPowerFunctions<TSelf>, INumberBase<TSelf>
    {
        static abstract TSelf Pow(TSelf x, TSelf y);
    }

    public interface IRootFunctions<TSelf>
        where TSelf : IRootFunctions<TSelf>, INumberBase<TSelf>
    {
        static abstract TSelf Cbrt(TSelf x);

        static abstract TSelf Sqrt(TSelf x);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf Hypot(TSelf x, TSelf y);

        static abstract TSelf Root(TSelf x, TSelf n);
    }

    public interface ITrigonometricFunctions<TSelf>
        where TSelf : ITrigonometricFunctions<TSelf>, INumberBase<TSelf>
    {
        static abstract TSelf Acos(TSelf x);

        static abstract TSelf Asin(TSelf x);

        static abstract TSelf Atan(TSelf x);

        static abstract TSelf Atan2(TSelf y, TSelf x);

        static abstract TSelf Cos(TSelf x);

        static abstract TSelf Sin(TSelf x);

        static abstract (TSelf Sin, TSelf Cos) SinCos(TSelf x);

        static abstract TSelf Tan(TSelf x);

        // The following methods are approved but not yet implemented in the libraries
        // These could be DIM if they had access to the constant 'Pi'

        static abstract TSelf AcosPi(TSelf x);

        static abstract TSelf AsinPi(TSelf x);

        static abstract TSelf AtanPi(TSelf x);

        static abstract TSelf Atan2Pi(TSelf y, TSelf x);

        static abstract TSelf CosPi(TSelf x);

        static abstract TSelf SinPi(TSelf x);

        static abstract TSelf TanPi(TSelf x);
    }
}

namespace System.Numerics
{
    public interface IBinaryFloatingPointIeee754<TSelf>
        : IBinaryNumber<TSelf>,
          IFloatingPointIeee754<TSelf>
        where TSelf : IBinaryFloatingPointIeee754<TSelf>
    {
    }

    public interface IBinaryInteger<TSelf>
        : IBinaryNumber<TSelf>,
          IShiftOperators<TSelf, TSelf, TSelf>,
          IShiftOperators<TSelf, int, TSelf>
        where TSelf : IBinaryInteger<TSelf>
    {
        // We might want to support "big multiplication" for fixed width types
        // This would return a tuple (TSelf high, TSelf low) or similar, to match what we do for System.Math

        // Returning int is currently what BitOperations does, however this can be cumbersome or prohibitive
        // in various algorithms where returning TSelf is better.

        public static virtual (TSelf Quotient, TSelf Remainder) DivRem(TSelf left, TSelf right)
        {
            TSelf quotient = left / right;
            return (quotient, (left - (quotient * right)));
        }

        public static virtual TSelf LeadingZeroCount(TSelf value)
        {
            TSelf bitCount = TSelf.CreateChecked(value.GetByteCount() * 8L);

            if (value == Zero)
            {
                return TSelf.CreateChecked();
            }

            return (bitCount - TSelf.One) ^ Log2(value);
        }

        static abstract TSelf PopCount(TSelf value);

        public static virtual TSelf RotateLeft(TSelf value, TSelf rotateAmount)
        {
            TSelf bitCount = TSelf.CreateChecked(value.GetByteCount() * 8L);
            return (value << rotateAmount) | (value >> (bitCount - rotateAmount));
        }

        public static virtual TSelf RotateRight(TSelf value, TSelf rotateAmount)
        {
            TSelf bitCount = TSelf.CreateChecked(value.GetByteCount() * 8L);
            return (value >> rotateAmount) | (value << (bitCount - rotateAmount));
        }

        static abstract TSelf TrailingZeroCount(TSelf value);

        // These methods allow getting the underlying bytes that represent the binary integer

        int GetByteCount();

        long GetShortestBitLength()
        {
            long bitCount = (GetByteCount() * 8L);
            return bitCount - long.CreateChecked(TSelf.LeadingZeroCount(this));
        }

        bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten);

        int WriteLittleEndian(byte[] destination)
        {
            if (!TryWriteLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        int WriteLittleEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteLittleEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        int WriteLittleEndian(Span<byte> destination)
        {
            if (!TryWriteLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }
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

        static abstract TSelf Log2(TSelf value);        // "Conflicts" with ILogarithmicFunctions.Log2
    }

    public interface IDecimalFloatingPointIeee754<TSelf>
        : IFloatingPointIeee754<TSelf>
        where TSelf : IDecimalFloatingPointIeee754<TSelf>
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

    public interface IFloatingPoint<TSelf>
        : INumber<TSelf>,
          ISignedNumber<TSelf>
        where TSelf : IFloatingPoint<TSelf>
    {
        static abstract TSelf Ceiling(TSelf x);

        static abstract TSelf Floor(TSelf x);

        static abstract TSelf Round(TSelf x);

        static abstract TSelf Round(TSelf x, int digits);

        static abstract TSelf Round(TSelf x, MidpointRounding mode);

        static abstract TSelf Round(TSelf x, int digits, MidpointRounding mode);

        static abstract TSelf Truncate(TSelf x);

        // These methods allow getting the underlying bytes that represent the IEEE 754 floating-point

        long GetExponentShortestBitLength();

        int GetExponentByteCount();

        long GetSignificandShortestBitLength();

        int GetSignificandByteCount();

        bool TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten);

        bool TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten);

        int WriteExponentLittleEndian(byte[] destination);

        int WriteExponentLittleEndian(byte[] destination, int startIndex);

        int WriteExponentLittleEndian(Span<byte> destination);

        int WriteSignificandLittleEndian(byte[] destination);

        int WriteSignificandLittleEndian(byte[] destination, int startIndex);

        int WriteSignificandLittleEndian(Span<byte> destination);
    }

    public interface IFloatingPointIeee754<TSelf>
        : IExponentialFunctions<TSelf>,
          IFloatingPoint<TSelf>,
          IHyperbolicFunctions<TSelf>,
          ILogarithmicFunctions<TSelf>,
          IPowerFunctions<TSelf>,
          IRootFunctions<TSelf>,
          ITrigonometricFunctions<TSelf>
        where TSelf : IFloatingPointIeee754<TSelf>
    {
        // The following constants are defined by System.Math and System.MathF today

        static abstract TSelf E { get; }

        static abstract TSelf Pi { get; }

        static abstract TSelf Tau { get; }

        // The following methods are defined by System.Math and System.MathF today
        // Exposing them on the interfaces means they will become available as statics on the primitive types
        // This, in a way, obsoletes Math/MathF and brings float/double inline with how non-primitive types support similar functionality
        // API review will need to determine if we'll want to continue adding APIs to Math/MathF in the future

        static abstract TSelf BitDecrement(TSelf x);

        static abstract TSelf BitIncrement(TSelf x);

        static abstract TSelf FusedMultiplyAdd(TSelf left, TSelf right, TSelf addend);

        static abstract TSelf Ieee754Remainder(TSelf left, TSelf right);

        // IEEE defines the result to be an integral type, but not the size

        static abstract int ILogB(TSelf x);

        public static virtual TSelf ReciprocalEstimate(TSelf x) => TSelf.One / x;

        public static virtual TSelf ReciprocalSqrtEstimate(TSelf x) => TSelf.One / TSelf.Sqrt(x);

        // IEEE defines n to be an integral type, but not the size

        static abstract TSelf ScaleB(TSelf x, int n);

        // The following members are exposed on the floating-point types as constants today
        // This may be of concern when implementing the interface

        // TODO: We may want to expose constants related to the subnormal and finite boundaries

        static abstract TSelf Epsilon { get; }

        static abstract TSelf NaN { get; }

        static abstract TSelf NegativeInfinity { get; }

        static abstract TSelf NegativeZero { get; }

        static abstract TSelf PositiveInfinity { get; }

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf Compound(TSelf x, TSelf n);

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

        // 5.4.1
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

        // 5.4.3
        //  TResult ConvertFromHexCharacter(string s);
        //  string ConvertToHexCharacter(TSelf x, TFormat format);

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
        //  bool IsZero(TSelf x);
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

        // 9.7
        //  TSelf GetPayload(TSelf x);
        //  TSelf SetPayload(TSelf x);
        //  TSelf SetPayloadSignaling(TSelf x);
    }

    public interface INumber<TSelf>
        : IComparisonOperators<TSelf, TSelf>,   // implies IEqualityOperators<TSelf, TSelf>
          IModulusOperators<TSelf, TSelf, TSelf>,
          INumberBase<TSelf>,
          ISpanParsable<TSelf>                  // implies IParsable<TSelf>
        where TSelf : INumber<TSelf>
    {
        // There is an open question on whether properties like IsSigned, IsBinary, IsFixedWidth, Base/Radix, and others are beneficial
        // We could expose them for trivial checks or users could be required to check for the corresponding correct interfaces

        // Abs mirrors Math.Abs and returns the same type. This can fail for MinValue of signed integer types
        // Swift has an associated type that can be used here, which would require an additional type parameter in .NET
        // However that would hinder the reusability of these interfaces in constraints

        public static TSelf Abs(TSelf value)
        {
            if (IsNegative(value))
            {
                return checked(-value);
            }
            return value;
        }

        public static TSelf Clamp(TSelf value, TSelf min, TSelf max)
        {
            if (min > max)
            {
                throw new ArgumentException("min cannot be greater than max");
            }

            TSelf result = value;

            result = Max(result, min);
            result = Min(result, max);

            return result;
        }

        public static TSelf CopySign(TSelf value, TSelf sign)
        {
            TSelf result = value;

            if (IsNegative(value) != IsNegative(sign))
            {
                result = checked(-result);
            }

            return result;
        }

        public static virtual TSelf Max(TSelf x, TSelf y)
        {
            if (val1 != val2)
            {
                if (!IsNaN(val1))
                {
                    return val2 < val1 ? val1 : val2;
                }

                return val1;
            }

            return IsNegative(val2) ? val1 : val2;
        }

        public static virtual TSelf MaxMagnitude(TSelf x, TSelf y)
        {
            TSelf ax = Abs(x);
            TSelf ay = Abs(y);

            if ((ax > ay) || IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? y : x;
            }

            return y;
        }

        public static virtual TSelf MaxMagnitudeNumber(TSelf x, TSelf y)
        {
            TSelf ax = Abs(x);
            TSelf ay = Abs(y);

            if ((ax > ay) || IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? y : x;
            }

            return y;
        }

        public static virtual TSelf MaxNumber(TSelf x, TSelf y)
        {
            if (x != y)
            {
                if (!IsNaN(y))
                {
                    return y < x ? x : y;
                }

                return x;
            }

            return IsNegative(y) ? x : y;
        }

        public static virtual TSelf Min(TSelf x, TSelf y)
        {
            if (val1 != val2 && !IsNaN(val1))
            {
                return val1 < val2 ? val1 : val2;
            }

            return IsNegative(val1) ? val1 : val2;
        }

        public static virtual TSelf MinMagnitude(TSelf x, TSelf y)
        {
            TSelf ax = Abs(x);
            TSelf ay = Abs(y);

            if ((ax < ay) || IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? x : y;
            }

            return y;
        }

        public static virtual TSelf MinMagnitudeNumber(TSelf x, TSelf y)
        {
            TSelf ax = Abs(x);
            TSelf ay = Abs(y);

            if ((ax < ay) || IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? x : y;
            }

            return y;
        }

        public static virtual TSelf MinNumber(TSelf x, TSelf y)
        {
            if (x != y)
            {
                if (!IsNaN(y))
                {
                    return x < y ? x : y;
                }

                return x;
            }

            return IsNegative(x) ? x : y;
        }

        static abstract TSelf Parse(string s, NumberStyles style, IFormatProvider? provider);

        static abstract TSelf Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider);

        // Math only exposes this Sign for signed types, but it is well-defined for unsigned types
        // it can simply never return -1 and only 0 or 1 instead

        public static virtual int Sign(TSelf value)
        {
            if (value != Zero)
            {
                IsNegative(value) ? -1 : +1;
            }

            return 0;
        }

        static abstract bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out TSelf result);

        static abstract bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out TSelf result);
    }

    public interface INumberBase<TSelf>
        : IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IDecrementOperators<TSelf>,
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IEqualityOperators<TSelf, TSelf>,     // implies IEquatable<TSelf>
          IIncrementOperators<TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplyOperators<TSelf, TSelf>,
          ISpanFormattable,                     // implies IFormattable
          ISubtractionOperators<TSelf, TSelf, TSelf>,
          IUnaryPlusOperators<TSelf, TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>
        where TSelf : INumberBase<TSelf>
    {
        // Alias for MultiplicativeIdentity
        static abstract TSelf One { get; }

        // Alias for AdditiveIdentity
        static abstract TSelf Zero { get; }

        public static TSelf CreateChecked<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            TSelf result;

            if (TSelf.TryConvertFromChecked(value, out result))
            {
                return result;
            }

            if (TOther.TryConvertToChecked(value, out result))
            {
                return result;
            }

            throw new NotSupportedException();
        }

        static TSelf CreateSaturating<TOther>(TOther value)
            where TOther : INumber<TOther>
        {
            TSelf result;

            if (TSelf.TryConvertFromSaturating(value, out result))
            {
                return result;
            }

            if (TOther.TryConvertToSaturating(value, out result))
            {
                return result;
            }

            throw new NotSupportedException();
        }

        static TSelf CreateTruncating<TOther>(TOther value)
            where TOther : INumber<TOther>
        {
            TSelf result;

            if (TSelf.TryConvertFromTruncating(value, out result))
            {
                return result;
            }

            if (TOther.TryConvertToTruncating(value, out result))
            {
                return result;
            }

            throw new NotSupportedException();
        }

        // IsEven, IsOdd, IsZero

        static abstract bool IsFinite(TSelf value);

        static abstract bool IsInfinity(TSelf value);

        static abstract bool IsNaN(TSelf value);

        static abstract bool IsNegative(TSelf value);

        public static virtual bool IsNegativeInfinity(TSelf value) => IsNegative(value) && IsInfinity(value);

        static abstract bool IsNormal(TSelf value);

        public static virtual bool IsPositive(TSelf value) => !IsNegative(value);

        public static virtual bool IsPositiveInfinity(TSelf value) => IsPositive(value) && IsInfinity(value);

        static abstract bool IsSubnormal(TSelf value);

        protected static abstract bool TryConvertFromChecked<TOther>(TOther value, out TSelf? result)
            where TOther : INumber<TOther>;

        protected static abstract bool TryConvertFromSaturating<TOther>(TOther value, out TSelf? result)
            where TOther : INumber<TOther>;

        protected static abstract bool TryConvertFromTruncating<TOther>(TOther value, out TSelf? result)
            where TOther : INumber<TOther>;

        protected static abstract bool TryConvertToChecked<TOther>(TSelf value, out TOther? result)
            where TOther : INumber<TOther>;

        protected static abstract bool TryConvertToSaturating<TOther>(TSelf value, out TOther? result)
            where TOther : INumber<TOther>;

        protected static abstract bool TryConvertToTruncating<TOther>(TSelf value, out TOther? result)
            where TOther : INumber<TOther>;
    }

    public interface ISignedNumber<TSelf>
        where TSelf : INumberBase<TSelf>, ISignedNumber<TSelf>
    {
        // It's not possible to check for lack of an interface in a constraint, so ISignedNumber<TSelf> is likely required

        static abstract TSelf NegativeOne { get; }
    }

    public interface IUnsignedNumber<TSelf>
        where TSelf : INumberBase<TSelf>, IUnsignedNumber<TSelf>
    {
        // It's not possible to check for lack of an interface in a constraint, so IUnsignedNumber<TSelf> is likely required
    }
}

namespace System.Numerics
{
    public interface INumberConverter<T, U>
        where T : INumber<T>
        where U : INumber<U>
    {
        static abstract T Convert(U value);
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
        public const byte AdditiveIdentity = 0;                                             // ? Expose
        public const byte MaxValue = 255;                                                   // Existing
        public const byte MinValue = 0;                                                     // Existing
        public const byte MultiplicativeIdentity = 1;                                       // ? Expose
        public const byte One = 1;                                                          // ? Expose
        public const byte Zero = 0;                                                         // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Char
        : IBinaryInteger<char>,
          IConvertible,
          IMinMaxValue<char>,
          IUnsignedNumber<char>
    {
        public const short AdditiveIdentity = 0;                                             // ? Expose
        public const short MaxValue = 65535;                                                 // Existing
        public const short MinValue = 0;                                                     // Existing
        public const short MultiplicativeIdentity = 1;                                       // ? Expose
        public const short One = 1;                                                          // ? Expose
        public const short Zero = 0;                                                         // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)                                                // ? Explicit
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing - Explicit
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // ? Explicit
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // ? Explicit
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // ? Explicit
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // ? Explicit
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // ? Explicit
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)                          // ? Explicit
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Explicit
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)                            // ? Explicit
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)               // ? Explicit
    }

    public struct DateOnly
        : IComparisonOperators<DateOnly, DateOnly>,
          IMinMaxValue<DateOnly>,
          ISpanFormattable,
          ISpanParsable<DateOnly>
    {
        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }                                                      // * Existing
        //   * TSelf MinValue { get; }                                                      // * Existing
        //
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct DateTime
        : IAdditionOperators<DateTime, TimeSpan, DateTime>,
          IAdditiveIdentity<DateTime, TimeSpan>,
          IComparisonOperators<DateTime, DateTime>,
          IConvertible,
          IMinMaxValue<DateTime>,
          ISerializable,
          ISpanFormattable,
          ISpanParsable<DateTime>,
          ISubtractionOperators<DateTime, TimeSpan, DateTime>,
          ISubtractionOperators<DateTime, DateTime, TimeSpan>
    {
        public static readonly DateTime MaxValue;                                           // Existing
        public static readonly DateTime MinValue;                                           // Existing

        // Explicitly Implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }

        // Implicitly Implemented interfaces
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked -(TSelf, TSelf)
    }

    public struct DateTimeOffset
        : IAdditionOperators<DateTimeOffset, TimeSpan, DateTimeOffset>,
          IAdditiveIdentity<DateTimeOffset, TimeSpan>,
          IComparisonOperators<DateTimeOffset, DateTimeOffset>,
          IDeserializationCallback,
          IMinMaxValue<DateTimeOffset>,
          ISerializable,
          ISpanFormattable,
          ISpanParsable<DateTimeOffset>,
          ISubtractionOperators<DateTimeOffset, TimeSpan, DateTimeOffset>,
          ISubtractionOperators<DateTimeOffset, DateTimeOffset, TimeSpan>
    {
        // TODO: DateTimeOffset defines an implicit conversion to DateTime, should that be modeled in the interfaces?

        public static readonly DateTimeOffset MaxValue;                                     // Existing
        public static readonly DateTimeOffset MinValue;                                     // Existing

        // Explicitly Implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }

        // Implicitly Implemented interfaces
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked -(TSelf, TSelf)
    }

    public struct Decimal
        : IConvertible,
          IDeserializationCallback,
          IFloatingPoint<decimal>,
          IMinMaxValue<decimal>,
          ISerializable
    {
        // Decimal defines a few additional operations like Ceiling, Floor, Round, and Truncate
        // The rest of the IEEE operations are missing.

        // Decimal and some other types likewise define "friendly" names for operators, should we expose them?

        // Decimal exposes a member named MinusOne

        public const decimal AdditiveIdentity = 0;                                          // ? Expose
        public const decimal MaxValue = 79228162514264337593543950335m;                     // Existing
        public const decimal MinusOne = -1;                                                 // Existing - "Conflicts" with NegativeOne
        public const decimal MinValue = -79228162514264337593543950335m;                    // Existing
        public const decimal MultiplicativeIdentity = 1;                                    // ? Expose
        public const decimal NegativeOne = -1;                                              // ? Expose
        public const decimal One = 1;                                                       // Existing
        public const decimal Zero = 0;                                                      // Existing

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }

        // Implicitly implemented interfaces
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked +(TSelf, TSelf)                                       // ? Explicit
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)                                                     // * Existing
        //   * TSelf operator checked --(TSelf)                                             // ? Explicit
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked /(TSelf, TSelf)                                       // ? Explicit
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)                                                         // * Existing
        //   * TSelf Floor(TSelf)                                                           // * Existing
        //   * int GetExponentByteCount()                                                   // ? Explicit
        //   * long GetExponentShortestBitLength()                                          // ? Explicit
        //   * int GetSignificandByteCount()                                                // ? Explicit
        //   * long GetSignificandShortestBitLength()                                       // ? Explicit
        //   * TSelf Round(TSelf)                                                           // * Existing
        //   * TSelf Round(TSelf, int)                                                      // * Existing
        //   * TSelf Round(TSelf, MidpointRounding)                                         // * Existing
        //   * TSelf Round(TSelf, int, MidpointRounding)                                    // * Existing
        //   * TSelf Truncate(TSelf)                                                        // * Existing
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // ? Explicit
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[])                                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[], int)                                   // ? Explicit
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[])                                     // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[], int)                                // ? Explicit
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // ? Explicit
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)                                                     // * Existing
        //   * TSelf operator checked ++(TSelf)                                             // ? Explicit
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)                                               // Existing
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked *(TSelf, TSelf)                                       // ? Explicit
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked -(TSelf, TSelf)                                       // ? Explicit
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)                                                      // * Existing
        //   * TSelf operator checked -(TSelf)                                              // ? Explicit
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)                                                      // * Existing
    }

    public struct Double
        : IBinaryFloatingPointIeee754<double>,
          IConvertible,
          IMinMaxValue<double>
    {
        public const double AdditiveIdentity = 0;                                           // ? Expose
        public const double E = Math.E;
        public const double MaxValue = 1.7976931348623157E+308;                             // Existing
        public const double MinValue = -1.7976931348623157E+308;                            // Existing
        public const double MultiplicativeIdentity = 1;                                     // ? Expose
        public const double NegativeOne = -1;                                               // ? Expose
        public const double NegativeZero = -0.0;
        public const double One = 1;                                                        // ? Expose
        public const double Pi = Math.PI;
        public const double Tau = Math.Tau;
        public const double Zero = 0;                                                       // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly implemented interfaces
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IExponentialFunctions
        //   * TSelf Exp(TSelf)
        //   * TSelf ExpM1(TSelf)
        //   * TSelf Exp2(TSelf)
        //   * TSelf Exp2M1(TSelf)
        //   * TSelf Exp10(TSelf)
        //   * TSelf Exp10M1(TSelf)
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * int GetExponentByteCount()                                                   // ? Explicit
        //   * long GetExponentShortestBitLength()                                          // ? Explicit
        //   * int GetSignificandByteCount()                                                // ? Explicit
        //   * long GetSignificandShortestBitLength()                                       // ? Explicit
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // ? Explicit
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[])                                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[], int)                                   // ? Explicit
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[])                                     // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[], int)                                // ? Explicit
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // ? Explicit
        // * IFloatingPointIeee754
        //   * TSelf E { get; }                                                             // ? Explicit
        //   * TSelf Epsilon { get; }                                                       // ? Explicit
        //   * TSelf NaN { get; }                                                           // ? Explicit
        //   * TSelf NegativeInfinity { get; }                                              // ? Explicit
        //   * TSelf NegativeZero { get; }                                                  // ? Explicit
        //   * TSelf Pi { get; }                                                            // ? Explicit
        //   * TSelf PositiveInfinity { get; }                                              // ? Explicit
        //   * TSelf Tau { get; }                                                           // ? Explicit
        //   * TSelf BitDecrement(TSelf)
        //   * TSelf BitIncrement(TSelf)
        //   * TSelf Compound(TSelf, TSelf)                                                 // Approved - NYI
        //   * TSelf FusedMultiplyAdd(TSelf, TSelf, TSelf)
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * TSelf ReciprocalEstimate(TSelf)
        //   * TSelf ReciprocalSqrtEstimate(TSelf)
        //   * TSelf ScaleB(TSelf, int)
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IHyperbolicFunctions
        //   * TSelf Acosh(TSelf)
        //   * TSelf Asinh(TSelf)
        //   * TSelf Atanh(TSelf)
        //   * TSelf Cosh(TSelf)
        //   * TSelf Sinh(TSelf)
        //   * TSelf Tanh(TSelf)
        // * ILogarithmicFunctions
        //   * TSelf Log(TSelf)
        //   * TSelf Log(TSelf, TSelf)
        //   * TSelf LogP1(TSelf)
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MaxNumber(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MinNumber(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * IPowerFunctions
        //   * TSelf Pow(TSelf, TSelf)
        // * IRootFunctions
        //   * TSelf Cbrt(TSelf)
        //   * TSelf Hypot(TSelf, TSelf)                                                    // Approved - NYI
        //   * TSelf Sqrt(TSelf)
        //   * TSelf Root(TSelf, TSelf)                                                     // Approved - NYI
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ITrigonometricFunctions
        //   * TSelf Acos(TSelf)
        //   * TSelf AcosPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Asin(TSelf)
        //   * TSelf AsinPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan(TSelf)
        //   * TSelf AtanPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan2(TSelf, TSelf)
        //   * TSelf Atan2Pi(TSelf, TSelf)                                                  // Approved - NYI
        //   * TSelf Cos(TSelf)
        //   * TSelf CosPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Sin(TSelf)
        //   * (TSelf, TSelf) SinCos(TSelf)
        //   * TSelf SinPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Tan(TSelf)
        //   * TSelf TanPi(TSelf)                                                           // Approved - NYI
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
          ISpanParsable<Guid>
    {
        // Implicitly implemented interfaces
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Half
        : IBinaryFloatingPointIeee754<Half>,
          IMinMaxValue<Half>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)

        // Implicitly implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }                                                      // * Existing
        //   * TSelf MinValue { get; }                                                      // * Existing
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IExponentialFunctions
        //   * TSelf Exp(TSelf)
        //   * TSelf ExpM1(TSelf)
        //   * TSelf Exp2(TSelf)
        //   * TSelf Exp2M1(TSelf)
        //   * TSelf Exp10(TSelf)
        //   * TSelf Exp10M1(TSelf)
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * int GetExponentByteCount()                                                   // ? Explicit
        //   * long GetExponentShortestBitLength()                                          // ? Explicit
        //   * int GetSignificandByteCount()                                                // ? Explicit
        //   * long GetSignificandShortestBitLength()                                       // ? Explicit
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // ? Explicit
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[])                                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[], int)                                   // ? Explicit
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[])                                     // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[], int)                                // ? Explicit
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // ? Explicit
        // * IFloatingPointIeee754
        //   * TSelf E { get; }
        //   * TSelf Epsilon { get; }                                                       // * Existing
        //   * TSelf NaN { get; }                                                           // * Existing
        //   * TSelf NegativeInfinity { get; }                                              // * Existing
        //   * TSelf NegativeZero { get; }
        //   * TSelf Pi { get; }
        //   * TSelf PositiveInfinity { get; }                                              // * Existing
        //   * TSelf Tau { get; }
        //   * TSelf BitDecrement(TSelf)
        //   * TSelf BitIncrement(TSelf)
        //   * TSelf Compound(TSelf, TSelf)                                                 // Approved - NYI
        //   * TSelf FusedMultiplyAdd(TSelf, TSelf, TSelf)
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * TSelf ReciprocalEstimate(TSelf)
        //   * TSelf ReciprocalSqrtEstimate(TSelf)
        //   * TSelf ScaleB(TSelf, int)
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IHyperbolicFunctions
        //   * TSelf Acosh(TSelf)
        //   * TSelf Asinh(TSelf)
        //   * TSelf Atanh(TSelf)
        //   * TSelf Cosh(TSelf)
        //   * TSelf Sinh(TSelf)
        //   * TSelf Tanh(TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * ILogarithmicFunctions
        //   * TSelf Log(TSelf)
        //   * TSelf Log(TSelf, TSelf)
        //   * TSelf LogP1(TSelf)
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MaxNumber(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MinNumber(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * IPowerFunctions
        //   * TSelf Pow(TSelf, TSelf)
        // * IRootFunctions
        //   * TSelf Cbrt(TSelf)
        //   * TSelf Hypot(TSelf, TSelf)                                                    // Approved - NYI
        //   * TSelf Sqrt(TSelf)
        //   * TSelf Root(TSelf, TSelf)                                                     // Approved - NYI
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * ITrigonometricFunctions
        //   * TSelf Acos(TSelf)
        //   * TSelf AcosPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Asin(TSelf)
        //   * TSelf AsinPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan(TSelf)
        //   * TSelf AtanPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan2(TSelf, TSelf)
        //   * TSelf Atan2Pi(TSelf, TSelf)                                                  // Approved - NYI
        //   * TSelf Cos(TSelf)
        //   * TSelf CosPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Sin(TSelf)
        //   * (TSelf, TSelf) SinCos(TSelf)
        //   * TSelf SinPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Tan(TSelf)
        //   * TSelf TanPi(TSelf)                                                           // Approved - NYI
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
    }

    public struct Int16
        : IBinaryInteger<short>,
          IConvertible,
          IMinMaxValue<short>,
          ISignedNumber<short>
    {
        public const short AdditiveIdentity = 0;                                        // ? Expose
        public const short MaxValue = 32767;                                            // Existing
        public const short MinValue = -32768;                                           // Existing
        public const short MultiplicativeIdentity = 1;                                  // ? Expose
        public const short NegativeOne = -1;                                            // ? Expose
        public const short One = 1;                                                     // ? Expose
        public const short Zero = 0;                                                    // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber                                                                  // ? Explicit
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Int32
        : IBinaryInteger<int>,
          IConvertible,
          IMinMaxValue<int>,
          ISignedNumber<int>
    {
        public const int AdditiveIdentity = 0;                                        // ? Expose
        public const int MaxValue = 2147483647;                                       // Existing
        public const int MinValue = -2147483648;                                      // Existing
        public const int MultiplicativeIdentity = 1;                                  // ? Expose
        public const int NegativeOne = -1;                                            // ? Expose
        public const int One = 1;                                                     // ? Expose
        public const int Zero = 0;                                                    // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Int64
        : IBinaryInteger<long>,
          IConvertible,
          IMinMaxValue<long>,
          ISignedNumber<long>
    {
        public const long AdditiveIdentity = 0;                                        // ? Expose
        public const long MaxValue = 9223372036854775807;                              // Existing
        public const long MinValue = -9223372036854775808;                             // Existing
        public const long MultiplicativeIdentity = 1;                                  // ? Expose
        public const long NegativeOne = -1;                                            // ? Expose
        public const long One = 1;                                                     // ? Expose
        public const long Zero = 0;                                                    // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Int128
        : IBinaryInteger<Int128>,
          IMinMaxValue<Int128>,
          ISignedNumber<Int128>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable
        //   * int CompareTo(object?)
        //   * int CompareTo(TSelf)
        // * IEquatable
        //   * bool Equals(TSelf)
        // * IFormattable
        //   * string ToString(string?, IFormatProvider?)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf)
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct IntPtr
        : IBinaryInteger<nint>,
          IMinMaxValue<nint>,
          ISerializable,
          ISignedNumber<nint>
    {
        public static readonly IntPtr Zero = 0;                                       // Existing

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }                                                      // * Existing
        //   * TSelf MinValue { get; }                                                      // * Existing
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct SByte
        : IBinaryInteger<sbyte>,
          IConvertible,
          IMinMaxValue<sbyte>,
          ISignedNumber<sbyte>
    {
        public const sbyte AdditiveIdentity = 0;                                        // ? Expose
        public const sbyte MaxValue = 127;                                              // Existing
        public const sbyte MinValue = -128;                                             // Existing
        public const sbyte MultiplicativeIdentity = 1;                                  // ? Expose
        public const sbyte NegativeOne = -1;                                            // ? Expose
        public const sbyte One = 1;                                                     // ? Expose
        public const sbyte Zero = 0;                                                    // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct Single
        : IBinaryFloatingPointIeee754<float>,
          IConvertible,
          IMinMaxValue<float>
    {
        public const float AdditiveIdentity = 0;                                           // ? Expose
        public const float E = MathF.E;
        public const float MaxValue = 3.40282346638528859e+38f;                            // Existing
        public const float MinValue = -3.40282346638528859e+38f;                           // Existing
        public const float MultiplicativeIdentity = 1;                                     // ? Expose
        public const float NegativeOne = -1;                                               // ? Expose
        public const float NegativeZero = -0.0f;
        public const float One = 1;                                                        // ? Expose
        public const float Pi = Math.PI;
        public const float Tau = Math.Tau;
        public const float Zero = 0;                                                       // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly implemented interfaces
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IExponentialFunctions
        //   * TSelf Exp(TSelf)
        //   * TSelf ExpM1(TSelf)
        //   * TSelf Exp2(TSelf)
        //   * TSelf Exp2M1(TSelf)
        //   * TSelf Exp10(TSelf)
        //   * TSelf Exp10M1(TSelf)
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * int GetExponentByteCount()                                                   // ? Explicit
        //   * long GetExponentShortestBitLength()                                          // ? Explicit
        //   * int GetSignificandByteCount()                                                // ? Explicit
        //   * long GetSignificandShortestBitLength()                                       // ? Explicit
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // ? Explicit
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[])                                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[], int)                                   // ? Explicit
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[])                                     // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[], int)                                // ? Explicit
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // ? Explicit
        // * IFloatingPointIeee754
        //   * TSelf E { get; }                                                             // ? Explicit
        //   * TSelf Epsilon { get; }                                                       // ? Explicit
        //   * TSelf NaN { get; }                                                           // ? Explicit
        //   * TSelf NegativeInfinity { get; }                                              // ? Explicit
        //   * TSelf NegativeZero { get; }                                                  // ? Explicit
        //   * TSelf Pi { get; }                                                            // ? Explicit
        //   * TSelf PositiveInfinity { get; }                                              // ? Explicit
        //   * TSelf Tau { get; }                                                           // ? Explicit
        //   * TSelf BitDecrement(TSelf)
        //   * TSelf BitIncrement(TSelf)
        //   * TSelf Compound(TSelf, TSelf)                                                 // Approved - NYI
        //   * TSelf FusedMultiplyAdd(TSelf, TSelf, TSelf)
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * TSelf ReciprocalEstimate(TSelf)
        //   * TSelf ReciprocalSqrtEstimate(TSelf)
        //   * TSelf ScaleB(TSelf, int)
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IHyperbolicFunctions
        //   * TSelf Acosh(TSelf)
        //   * TSelf Asinh(TSelf)
        //   * TSelf Atanh(TSelf)
        //   * TSelf Cosh(TSelf)
        //   * TSelf Sinh(TSelf)
        //   * TSelf Tanh(TSelf)
        // * ILogarithmicFunctions
        //   * TSelf Log(TSelf)
        //   * TSelf Log(TSelf, TSelf)
        //   * TSelf LogP1(TSelf)
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MaxNumber(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MinNumber(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * IPowerFunctions
        //   * TSelf Pow(TSelf, TSelf)
        // * IRootFunctions
        //   * TSelf Cbrt(TSelf)
        //   * TSelf Hypot(TSelf, TSelf)                                                    // Approved - NYI
        //   * TSelf Sqrt(TSelf)
        //   * TSelf Root(TSelf, TSelf)                                                     // Approved - NYI
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ITrigonometricFunctions
        //   * TSelf Acos(TSelf)
        //   * TSelf AcosPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Asin(TSelf)
        //   * TSelf AsinPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan(TSelf)
        //   * TSelf AtanPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan2(TSelf, TSelf)
        //   * TSelf Atan2Pi(TSelf, TSelf)                                                  // Approved - NYI
        //   * TSelf Cos(TSelf)
        //   * TSelf CosPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Sin(TSelf)
        //   * (TSelf, TSelf) SinCos(TSelf)
        //   * TSelf SinPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Tan(TSelf)
        //   * TSelf TanPi(TSelf)                                                           // Approved - NYI
    }

    public struct TimeOnly
        : IComparisonOperators<TimeOnly, TimeOnly>,
          IMinMaxValue<TimeOnly>,
          ISpanFormattable,
          ISpanParsable<TimeOnly>,
          ISubtractionOperators<TimeOnly, TimeOnly, TimeSpan>
    {
        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
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
          ISpanFormattable,
          ISubtractionOperators<TimeSpan, TimeSpan, TimeSpan>,
          IUnaryNegationOperators<TimeSpan, TimeSpan>
          IUnaryPlusOperators<TimeSpan, TimeSpan>
    {
        public static readonly TimeSpan MaxValue;                                           // Existing
        public static readonly TimeSpan MinValue;                                           // Existing

        // Explicitly Implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }

        // Implicitly Implemented interfaces
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)                                                // * Existing
        //   * bool operator <=(TSelf, TSelf)                                               // * Existing
        //   * bool operator >(TSelf, TSelf)                                                // * Existing
        //   * bool operator >=(TSelf, TSelf)                                               // * Existing
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable                                                               // Existing
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)                                               // * Existing
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)                                                      // * Existing
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)                                                      // * Existing
    }

    public struct UInt16
        : IBinaryInteger<ushort>,
          IConvertible,
          IMinMaxValue<ushort>,
          IUnsignedNumber<ushort>
    {
        public const short AdditiveIdentity = 0;                                             // ? Expose
        public const short MaxValue = 65535;                                                 // Existing
        public const short MinValue = 0;                                                     // Existing
        public const short MultiplicativeIdentity = 1;                                       // ? Expose
        public const short One = 1;                                                          // ? Expose
        public const short Zero = 0;                                                         // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct UInt32
        : IBinaryInteger<uint>,
          IConvertible,
          IMinMaxValue<uint>,
          IUnsignedNumber<uint>
    {
        public const uint AdditiveIdentity = 0;                                             // ? Expose
        public const uint MaxValue = 4294967295;                                            // Existing
        public const uint MinValue = 0;                                                     // Existing
        public const uint MultiplicativeIdentity = 1;                                       // ? Expose
        public const uint One = 1;                                                          // ? Expose
        public const uint Zero = 0;                                                         // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct UInt64
        : IBinaryInteger<ulong>,
          IConvertible,
          IMinMaxValue<ulong>,
          IUnsignedNumber<ulong>
    {
        public const ulong AdditiveIdentity = 0;                                             // ? Expose
        public const ulong MaxValue = 18446744073709551615;                                  // Existing
        public const ulong MinValue = 0;                                                     // Existing
        public const ulong MultiplicativeIdentity = 1;                                       // ? Expose
        public const ulong One = 1;                                                          // ? Expose
        public const ulong Zero = 0;                                                         // ? Expose

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * long GetBitLength()
        //   * int GetByteCount()
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)
        //   * int WriteLittleEndian(byte[])
        //   * int WriteLittleEndian(byte[], int)
        //   * int WriteLittleEndian(Span<byte>)
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct UInt128
        : IBinaryInteger<UInt128>,
          IConvertible,
          IMinMaxValue<UInt128>,
          IUnsignedNumber<UInt128>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)
        //   * bool operator !=(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * long GetBitLength()
        //   * int GetByteCount()
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)
        //   * int WriteLittleEndian(byte[])
        //   * int WriteLittleEndian(byte[], int)
        //   * int WriteLittleEndian(Span<byte>)
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable
        //   * int CompareTo(object?)
        //   * int CompareTo(TSelf)
        // * IEquatable
        //   * bool Equals(TSelf)
        // * IFormattable
        //   * string ToString(string?, IFormatProvider?)
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf)
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct UIntPtr
        : IBinaryInteger<nuint>,
          IMinMaxValue<nuint>,
          ISerializable,
          IUnsignedNumber<nuint>
    {
        public static readonly UIntPtr Zero = 0;                                      // Existing

        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }                                                      // * Existing
        //   * TSelf MinValue { get; }                                                      // * Existing
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable                                                                    // Existing
        //   * int CompareTo(object?)                                                       // * Existing
        //   * int CompareTo(TSelf)                                                         // * Existing
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable                                                                   // Existing
        //   * string ToString(string?, IFormatProvider?)                                   // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }
}

namespace System.Numerics
{
    public struct BigInteger
        : IBinaryInteger<BigInteger>,
          ISignedNumber<BigInteger>,
          ISpanFormattable,
          ISpanParsable<BigInteger>
    {
    }

    public struct Complex
        : IAdditionOperators<Complex, double, Complex>,
          IDivisionOperators<Complex, Complex, Complex>,
          IDivisionOperators<Complex, double, Complex>,
          IFormattable,
          // IExponentialFunctions<Complex>,
          // IHyperbolicFunctions<Complex>,
          // ILogarithmicFunctions<Complex>,
          IMultiplyOperators<Complex, double, Complex>,
          INumberBase<Complex>,
          // IPowerFunctions<Complex>
          // IRootFunctions<Complex>,
          // ITrigonometricFunctions<Complex>,
          ISubtractionOperators<Complex, double, Complex>
    {
    }

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

namespace System.Runtime.InteropServices
{
    public struct CLong
        : IBinaryInteger<CLong>,
          IMinMaxValue<CLong>,
          ISignedNumber<CLong>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetShortestByteCount()                                                   // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable
        //   * int CompareTo(object?)
        //   * int CompareTo(TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable
        //   * string ToString(string?, IFormatProvider?)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf)
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct CULong
        : IBinaryInteger<CULong>,
          IMinMaxValue<CULong>,
          IUnsignedNumber<CULong>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * One { get; }
        //   * Zero { get; }

        // Implicitly Implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * IShiftOperators
        //   * TSelf operator <<(TSelf, TSelf)
        //   * TSelf operator >>(TSelf, TSelf)
        //   * TSelf operator >>>(TSelf, TSelf)
        //   * TSelf operator <<(TSelf, int)
        //   * TSelf operator >>(TSelf, int)
        //   * TSelf operator >>>(TSelf, int)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
        //
        // * IBinaryInteger
        //   * (TSelf, TSelf) DivRem(TSelf, TSelf)
        //   * int GetByteCount()                                                           // ? Explicit
        //   * long GetShortestBitLength()                                                  // ? Explicit
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // ? Explicit
        //   * int WriteLittleEndian(byte[])                                                // ? Explicit
        //   * int WriteLittleEndian(byte[], int)                                           // ? Explicit
        //   * int WriteLittleEndian(Span<byte>)                                            // ? Explicit
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable
        //   * int CompareTo(object?)
        //   * int CompareTo(TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IFormattable
        //   * string ToString(string?, IFormatProvider?)
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // ? Explicit
        //   * TSelf MinNumber(TSelf, TSelf)                                                // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf)
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsFinite(TSelf)                                                         // ? Explicit
        //   * bool IsInfinity(TSelf)                                                       // ? Explicit
        //   * bool IsNaN(TSelf)                                                            // ? Explicit
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * bool IsNegativeInfinity(TSelf)                                               // ? Explicit
        //   * bool IsNormal(TSelf)                                                         // ? Explicit
        //   * bool IsPositive(TSelf)                                                       // ? Explicit
        //   * bool IsPositiveInfinity(TSelf)                                               // ? Explicit
        //   * bool IsSubnormal(TSelf)                                                      // ? Explicit
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
    }

    public struct NFloat
        : IBinaryFloatingPointIeee754<NFloat>,
          IMinMaxValue<NFloat>
    {
        // Explicitly implemented interfaces
        // * IAdditiveIdentity
        //   * TSelf AdditiveIdentity { get; }
        // * IMultiplicativeIdentity
        //   * TSelf MultiplicativeIdentity { get; }
        // * INumberBase
        //   * TSelf One { get; }
        //   * TSelf Zero { get; }
        // * ISignedNumber
        //   * TSelf NegativeOne { get; }

        // Implicitly implemented interfaces
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }
        //   * TSelf MinValue { get; }
        //
        // * IAdditionOperators
        //   * TSelf operator +(TSelf, TSelf)
        //   * TSelf operator checked +(TSelf, TSelf)
        // * IBitwiseOperators
        //   * TSelf operator &(TSelf, TSelf)
        //   * TSelf operator |(TSelf, TSelf)
        //   * TSelf operator ^(TSelf, TSelf)
        //   * TSelf operator ~(TSelf)
        // * IDecrementOperators
        //   * TSelf operator --(TSelf)
        //   * TSelf operator checked --(TSelf)
        // * IDivisionOperators
        //   * TSelf operator /(TSelf, TSelf)
        //   * TSelf operator checked /(TSelf, TSelf)
        // * IIncrementOperators
        //   * TSelf operator ++(TSelf)
        //   * TSelf operator checked ++(TSelf)
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * ISubtractionOperators
        //   * TSelf operator -(TSelf, TSelf)
        //   * TSelf operator checked -(TSelf, TSelf)
        // * IUnaryNegationOperators
        //   * TSelf operator -(TSelf)
        //   * TSelf operator checked -(TSelf)
        // * IUnaryPlusOperators
        //   * TSelf operator +(TSelf)
        //
        // * IBinaryNumber
        //   * bool IsPow2(TSelf)
        //   * TSelf Log2(TSelf)
        // * IComparable
        //   * int CompareTo(object?)
        //   * int CompareTo(TSelf)
        // * IComparisonOperators
        //   * bool operator <(TSelf, TSelf)
        //   * bool operator <=(TSelf, TSelf)
        //   * bool operator >(TSelf, TSelf)
        //   * bool operator >=(TSelf, TSelf)
        // * IEqualityOperators
        //   * bool operator ==(TSelf, TSelf)                                               // * Existing
        //   * bool operator !=(TSelf, TSelf)                                               // * Existing
        // * IEquatable                                                                     // Existing
        //   * bool Equals(TSelf)                                                           // * Existing
        // * IExponentialFunctions
        //   * TSelf Exp(TSelf)
        //   * TSelf ExpM1(TSelf)
        //   * TSelf Exp2(TSelf)
        //   * TSelf Exp2M1(TSelf)
        //   * TSelf Exp10(TSelf)
        //   * TSelf Exp10M1(TSelf)
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * int GetExponentByteCount()                                                   // ? Explicit
        //   * long GetExponentShortestBitLength()                                          // ? Explicit
        //   * int GetSignificandByteCount()                                                // ? Explicit
        //   * long GetSignificandShortestBitLength()                                       // ? Explicit
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // ? Explicit
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[])                                        // ? Explicit
        //   * int WriteExponentLittleEndian(byte[], int)                                   // ? Explicit
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[])                                     // ? Explicit
        //   * int WriteSignificandLittleEndian(byte[], int)                                // ? Explicit
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // ? Explicit
        // * IFloatingPointIeee754
        //   * TSelf E { get; }
        //   * TSelf Epsilon { get; }
        //   * TSelf NaN { get; }
        //   * TSelf NegativeInfinity { get; }
        //   * TSelf NegativeZero { get; }
        //   * TSelf Pi { get; }
        //   * TSelf PositiveInfinity { get; }
        //   * TSelf Tau { get; }
        //   * TSelf BitDecrement(TSelf)
        //   * TSelf BitIncrement(TSelf)
        //   * TSelf Compound(TSelf, TSelf)                                                 // Approved - NYI
        //   * TSelf FusedMultiplyAdd(TSelf, TSelf, TSelf)
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * TSelf ReciprocalEstimate(TSelf)
        //   * TSelf ReciprocalSqrtEstimate(TSelf)
        //   * TSelf ScaleB(TSelf, int)
        // * IFormattable
        //   * string ToString(string?, IFormatProvider?)
        // * IHyperbolicFunctions
        //   * TSelf Acosh(TSelf)
        //   * TSelf Asinh(TSelf)
        //   * TSelf Atanh(TSelf)
        //   * TSelf Cosh(TSelf)
        //   * TSelf Sinh(TSelf)
        //   * TSelf Tanh(TSelf)
        // * ILogarithmicFunctions
        //   * TSelf Log(TSelf)
        //   * TSelf Log(TSelf, TSelf)
        //   * TSelf LogP1(TSelf)
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * bool IsFinite(TSelf)
        //   * bool IsInfinity(TSelf)
        //   * bool IsNaN(TSelf)
        //   * bool IsNegative(TSelf)
        //   * bool IsNegativeInfinity(TSelf)
        //   * bool IsNormal(TSelf)
        //   * bool IsPositive(TSelf)
        //   * bool IsPositiveInfinity(TSelf)
        //   * bool IsSubnormal(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MaxNumber(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)
        //   * TSelf MinNumber(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
        //   * int Sign(TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf)
        // * INumberBase
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool TryConvertFromChecked(TOther, out TSelf)
        //   * bool TryConvertFromSaturating(TOther, out TSelf)
        //   * bool TryConvertFromTruncating(TOther, out TSelf)
        //   * bool TryConvertToChecked(TOther, out TSelf)
        //   * bool TryConvertToSaturating(TOther, out TSelf)
        //   * bool TryConvertToTruncating(TOther, out TSelf)
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * IPowerFunctions
        //   * TSelf Pow(TSelf, TSelf)
        // * IRootFunctions
        //   * TSelf Cbrt(TSelf)
        //   * TSelf Hypot(TSelf, TSelf)                                                    // Approved - NYI
        //   * TSelf Sqrt(TSelf)
        //   * TSelf Root(TSelf, TSelf)                                                     // Approved - NYI
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing
        // * ISpanParsable
        //   * TSelf Parse(ReadOnlySpan<char>, IFormatProvider?)
        //   * bool TryParse(ReadOnlySpan<char>, IFormatProvider?, out TSelf)
        // * ITrigonometricFunctions
        //   * TSelf Acos(TSelf)
        //   * TSelf AcosPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Asin(TSelf)
        //   * TSelf AsinPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan(TSelf)
        //   * TSelf AtanPi(TSelf)                                                          // Approved - NYI
        //   * TSelf Atan2(TSelf, TSelf)
        //   * TSelf Atan2Pi(TSelf, TSelf)                                                  // Approved - NYI
        //   * TSelf Cos(TSelf)
        //   * TSelf CosPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Sin(TSelf)
        //   * (TSelf, TSelf) SinCos(TSelf)
        //   * TSelf SinPi(TSelf)                                                           // Approved - NYI
        //   * TSelf Tan(TSelf)
        //   * TSelf TanPi(TSelf)                                                           // Approved - NYI
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

### Create and Conversion APIs explained

For number creation and conversion, we have three different scenarios to consider.

You have types like `Int32` which can know about other types in the same assembly, such as `Int64`. But which can't know about other types in deriving assemblies, such as `BigInteger`. This scenario is simple as you can directly check for the relevant types and do the conversions. It is possible to utilize things like `internal` to expose details allowing fast conversions and the biggest question ends up being whether to expose the conversions on type `A`, type `B`, or splitting between both.

You have types like `BigInteger` which can know about other types in the same assembly, such as `Complex`, and types in its dependencies, such as `Int32` or `Int64`. It again can't know about other types in deriving assemblies or in completely unrelated assemblies. This scenario is likewise simple with the main difference is you likely don't have access to internals and both conversions need to be exposed on type `B`.

Finally you have types that exist in two parallel assemblies where neither depends on the other. Imagine, for example, two standalone NuGet packages one providing a `Float128` and the other providing a `Rational` number type. This scenario is decently complex because the only way to get conversions is for one assembly to depend on the other (there-by becoming scenario 2) or for some third assembly to depend on both and provide the conversions itself.

#### Scenario 1

The first scenario leads to a design that looks like this.

```csharp
interface INumber<TSelf>
    where TSelf : INumber<TSelf>
{
    public static TSelf Create<TOther>(TOther value)
            where TOther : INumber<TOther>
    {
        if (!TryCreate(value, out TSelf result))
        {
            throw new NotSupportedException();
        }
        return result;
    }

    static abstract bool TryCreate<TOther>(TOther value, out TSelf? result)
        where TOther : INumber<TOther>;
}

struct Int32 : INumber<int>
{
    public static bool TryCreate<TOther>(TOther value, out int result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = (int)(actualValue);
            return true;
        }

        return false;
    }
}

struct Int64 : INumber<long>
{
    public static bool TryCreate<TOther>(TOther value, out long result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = actualValue;
            return true;
        }

        return false;
    }
}
```

It runs into the mentioned issues where the conversion fails if `TOther` is unrecognized, such as `BigInteger`:
```csharp
struct BigInteger : INumber<BigInteger>
{
    public static bool TryCreate<TOther>(TOther value, out BigInteger result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(BigInteger))
        {
            BigInteger actualValue = (BigInteger)(object)(value!);
            result = actualValue;
            return true;
        }

        return false;
    }
}
```

There is also a nuance that this can cause "generic code explosion" and that the size of the methods become quite large, which can negatively impact JIT throughput, inlining, and other behavior.

There is an additional problem where this actually happens 3 times. Once for `checked` (throw on overflow), once for `saturating` (clamp to Min/Max value), and once for `truncating` (take lowest n-bits).

The code is used as follows:
```csharp
public static TResult Sum<T, TResult>(IEnumerable<T> values)
    where T : INumber<T>
    where TResult : INumber<TResult>
{
    TResult result = TResult.Zero;

    foreach (var value in values)
    {
        result += TResult.Create(value);
    }

    return result;
}
```

#### Scenario 2

The second scenario needs to support `TOther` providing a conversion and so the design ends up changing a bit.

```csharp
interface INumber<TSelf>
    where TSelf : INumber<TSelf>
{
    public static TSelf Create<TOther>(TOther value)
            where TOther : INumber<TOther>
    {
        if (!TryCreate(value, out TSelf result))
        {
            throw new NotSupportedException();
        }
        return result;
    }

    public static bool TryCreate<TOther>(TOther value, out TSelf? result)
        where TOther : INumber<TOther>
    {
        return TSelf.TryConvertFrom(value, out result)
            || TOther.TryConvertTo(value, out result);
    }


}

struct Int32 : INumber<int>
{
    protected static bool TryConvertFrom<TOther>(TOther value, out int result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = (int)(actualValue);
            return true;
        }

        result = default;
        return false;
    }

    protected static bool TryConvertTo<TOther>(int value, out TOther? result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualResult = value;
            result = (TOther)(object)(actualResult);
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualResult = value;
            result = (TOther)(object)(actualResult);
            return true;
        }

        result = default;
        return false;
    }
}

struct Int64 : INumber<long>
{
    protected static bool TryConvertFrom<TOther>(TOther value, out long result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = actualValue;
            return true;
        }

        result = default;
        return false;
    }

    protected static bool TryConvertTo<TOther>(long value, out TOther? result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualResult = (int)(value);
            result = (TOther)(object)(actualResult);
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualResult = value;
            result = (TOther)(object)(actualResult);
            return true;
        }

        result = default;
        return false;
    }
}
```

This also then allows creating a `Int32` or `Int64` from a `BigInteger`:
```csharp
struct BigInteger : INumber<BigInteger>
{
    protected static bool TryConvertFrom<TOther>(TOther value, out BigInteger result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)(value!);
            result = actualValue;
            return true;
        }

        if (typeof(TOther) == typeof(BigInteger))
        {
            BigInteger actualValue = (BigInteger)(object)(value!);
            result = actualValue;
            return true;
        }

        result = default;
        return false;
    }

    protected static bool TryConvertTo<TOther>(BigInteger value, out TOther? result)
        where TOther : INumber<TOther>
    {
        if (typeof(TOther) == typeof(int))
        {
            int actualResult = (int)(value);
            result = (TOther)(object)(actualResult);
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            long actualResult = (long)(value);
            result = (TOther)(object)(actualResult);
            return true;
        }

        if (typeof(TOther) == typeof(BigInteger))
        {
            BigInteger actualResult = value;
            result = (TOther)(object)(actualResult);
            return true;
        }

        result = default;
        return false;
    }
}
```

There is still the downside that two types living in different assemblies can't convert between eachother. There is likewise a new downside in that the amount of code you have to write/maintain effectively doubles, with much of it being duplicate.

The same nuance around "generic code explosion" still exists, as does the potential negative impact on JIT throughput, inlining, etc. Additionally, there is a nuance that `TryConvertFrom` and `TryConvertTo` must be "self-contained". That is, they cannot themselves defer to `TOther` for anything or they'll risk introducing a cycle in the logic and causing a `StackOverflowException`.

The same additional problem where this actually happens 3 times. Once for `checked` (throw on overflow), once for `saturating` (clamp to Min/Max value), and once for `truncating` (take lowest n-bits) still exists.

The usage code remains the same.
```csharp
public static TResult Sum<T, TResult>(IEnumerable<T> values)
    where T : INumber<T>
    where TResult : INumber<TResult>
{
    TResult result = TResult.Zero;

    foreach (var value in values)
    {
        result += TResult.Create(value);
    }

    return result;
}
```

#### Scenario 3

This brings us to the third scenario and needing some way to allow conversion between types that know nothing of eachother.

```csharp
interface INumberConverter<T, U>
    where T : INumber<T>
    where U : INumber<U>
{
    static abstract T Convert(U value);
}
```

This would allow the usage code to become:
```csharp
public static TResult Sum<T, TResult, TConverter>(IEnumerable<T> values)
    where T : INumber<T>
    where TResult : INumber<TResult>
    where TConverter : INumberConverter<TResult, T>
{
    TResult result = TResult.Zero;

    foreach (var value in values)
    {
        result += TConverter.Convert(value);
    }

    return result;
}
```

There are upsides and downsides to this last approach. The consumer of the API is able to provide the conversion behavior and decide if it should `throw`, `saturate`, or `truncate` on `overflow`, which may be undesirable to the implementor. We could expose (instead of or in addition to `INumberConverter<T, U>`) `ICheckedNumberConverter<T, U>`, `ISaturatingNumberConverter<T, U>`, and `ITruncatingNumberConverter<T, U>` to allow enforcement of a conversion behavior.

Since C#/.NET don't have higher kinded types or associated types, we can't expose some non-generic variant that defers to the generic variant. We likewise can't easily expose a way to get an instance of the "default" number converter.

This leaves us with the `TryConvertFrom`/`TryConvertTo` pattern for the majority of scenarios, with the ability for that to "fallback" to `IBinaryInteger.TryWriteLittleEndian`, `IFloatingPoint.TryWriteExponentLittleEndian`, and `IFloatingPoint.TryWriteSignificandLittleEndian` for some subset of completely unknown types. It also leaves us with having `INumberConverter<T, U>` on top so that API implementors and consumers have a "path forward" when they are dealing with otherwise completely unrelated types.

#### Why no `IConvertible<T, U>`, `IExplicitOperators<TSelf, TOther>`, or `IImplicitOperators<TSelf, TOther>`

We could expose these, but they have the same general limitations as the `Create` APIs. Additionally, it vastly increases the number of interfaces implemented since there are 11 primitive integer types (`byte`, `char`, `short`, `int`, `long`, `nint`, `sbyte`, `ushort`, `uint`, `ulong`, and `nuint`), 3 primitive  floating-point types (`decimal`, `double`, and `float`), and some 6 other ABI primitive types (`Half`, `Int128`, `UInt128`, `NFloat`, `CLong`, and `CULong`). Supporting single-direction conversions for these requires at least 14 interfaces. Bi-directional doubles that to 24.

### Pending Concepts

There are several types which may benefit from some interface support. These include, but aren't limited to:
* System.Array
* System.Enum
* System.Index
* System.Range
* System.Tuple
* System.ValueTuple
* System.Numerics.Matrix3x2
* System.Numerics.Matrix4x4
* System.Numerics.Plane
* System.Numerics.Quaternion

Likewise, there are many comments within the defined interfaces above that call out key points that need additional modeling/consideration. Several of these concepts will be determined via user research and outreach while others will be determined via API review and feedback from other area experts. Others may be determined by language or runtime limitations in what is feasible for them to support.

Do we want a way to denote "primitives"? If we have it, should this be language primitives, runtime primitives, or ABI primitives?

Do we want an explicit `IConvertible<TSelf, TOther>` (or `IConvertibleFrom`/`IConvertibleTo`)?

Do we want a way to track "scalars" for vectors and matrices?

## Requirements
