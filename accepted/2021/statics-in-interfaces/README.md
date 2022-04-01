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
class "Complex"
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

"IBinaryInteger<TSelf>"                         <|-- "BigInteger"
"ISignedNumber<TSelf>"                          <|-- "BigInteger"
"ValueType"                                     <|-- "BigInteger"

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

![UML](http://www.plantuml.com/plantuml/svg/h9ZFKziw4CVl_YioEJyEGCXx1uPvvGSbCwN9u8JxsjaaQcKfbTQ0E_tZ6-ZWO5dPo4gvCBjUVQJL3trN-TyI62eBcGX5Q1QGOwy_-ZIV2n9QZTTeWBInvzCKvUAVtdHCApIR_mzeIqaNVK-p9npDKP6WgcxbZRuK2anrApMGKCk95ef6YFZlsU1FEs_uI14kqJ0HNyiYsnb0py9YnwokTB460vd4NVxpgRU9i3jdi1Mldj2GQFLnZRd10XKvd0itpA8H8CLF8A7L5KYPGVxT2H9FmdPVr9iV9KX15PE94qtUERbvMMk5YWJ8srBbh-Fq9zFO4Kn4m9wvrQO0elRQtLLhD9cM8kV7Cb91y_z5By48MXNxUFT2tSFB8yWI_T_rl9IbxRX4znkNM4kH0t_wF6sgZbsrJvVpwHAiFxK968kGoHrVzJCQVkuUj2O-8dq_1AtrJbTYGHrJJSLIuNnFLkaQb6-NrH_vjDCo8mDvn5qxDMAfbIwGpBvDDrCs99NPs-s9I_2EfXv5kyr4bZOm_X5pqgQEINEGO3OnYV_sxktyE_zrT1Ic8Vnr4K-E7xHVoO8tQGWiwI1sBitDPyCAJR2tbvKqz9JdrB10HNKb-I-Y0sGm5fqueXau8m6wdEo-mrYoNu0JNi8P6dE4M2NknJ5vEOvZEfzTM2JJBtg7CTigUVWuUQs9OIP1PwCDTMuJha1kvSfHWajZZZ9IXu-bGmd8Ky9AmZrZ2RquU8fJ8t4Cdj8kfZfJ0eiSbf9qUI0x6AV-2dWproanf0Y5uaZXRmEUrB6-PKa1bMPGTd6xsb5I1yXiRWRelXmIpnif5EGsZCoJVon4xNkcw8_O_0mkekJbS14AnU0Yf8h1HMWMXuiGBmuNeM8SBbRE0d3LuNCN8N0SBkBIv70naiVZabo2jRt-PKNOVpUkLc1gune8nqfM3STK5DXoZ1SBYxHrOouAa5jRbvdSjhfHPL4Rdqinhuqxa8lQ62aw7dwqJay-MiF1Hsj2fZR7sJVCgJRJHeQfK4kvDLz7_xhejMFMx7RMx7VMx7ZczFpsS-2qMc_vxxD_uNY6WxRl-EI_jlFaREXnElQfOmz-bse7DxN3G1X6zsc7FQ87H0Um0tRqpm8T1Inoh_LD0SSl_j-GZCVt76aOA6prMQhXpnpx95lstz7ejht-QDsrmjzvuCMciForjB3O4XORmc93M6pyddgRr4tpzJNDrnC8NyFZS_VbPLlRBRusH7ojUBuN0B_S3uH_lnGGNmk8huq1NmkG7nQWh_si3AqoBKQhNDwSddJhMNfXMgMNvxfABrAhz59TL-a5QvLfsLgbldtqfAbIy3e_nck5uQcqyFm_W1yxbschr_xdpmOTagFhpkbqr-MW4xRDXeaKcxRCjXxFOIwykTZmlcBRXjbKsWwieJJPyD1iFROuM8UwXEkMuNIR2PV0u9QOuKDC34w9mIKnk2G6jyGCQ69YS4eCBed19J4u9cQK57TDXstJhRsWMsfqZkqvJwpW53cS4eTJP73yPfc0fiZedXaTzat7xkHdrjidK7oVGF6ro-7hUOoFyVDzWcNONaS7V1qQyFNIZUy5nBTxCBvUVF6zWFbUW7ovo-9h0VFvO_CIMsUwzyvq6_lJCJYwnzRxPph6HlVOUWjDzzXw4qstsEWUM--ZwHvRxwJntWpZCJayHuSVRi6tCF_x0lku_Ed0pHMOnzaABiZx4kxJPskuVfyjCdk6Xypz6E3fxVFrw-Fz6m00)

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

        static abstract TResult operator checked +(TSelf left, TOther right);
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

        static abstract bool operator <=(TSelf left, TOther right);

        static abstract bool operator >(TSelf left, TOther right);

        static abstract bool operator >=(TSelf left, TOther right);
    }

    public interface IDecrementOperators<TSelf>
        where TSelf : IDecrementOperators<TSelf>
    {
        static abstract TSelf operator --(TSelf value);

        static abstract TSelf operator checked --(TSelf value);
    }

    public interface IDivisionOperators<TSelf, TOther, TResult>
        where TSelf : IDivisionOperators<TSelf, TOther, TResult>
    {
        static abstract TResult operator /(TSelf left, TOther right);

        static abstract TResult operator checked /(TSelf left, TOther right);
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

    public interface IIncrementOperators<TSelf>
        where TSelf : IIncrementOperators<TSelf>
    {
        // We don't inherit IAdditionOperators as TOther isn't well-defined for incrementable types
        // Incrementing twice is not necessarily the same as self + 2 and not all languages
        // use incrementing for numbers, they are sometimes used by iterators for example

        // We can't expose TSelf-- as some types only support one of the operations

        // We don't support TResult as C# requires TSelf to be the return type

        static abstract TSelf operator ++(TSelf value);

        static abstract TSelf operator checked ++(TSelf value);
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

        static abstract TResult operator checked *(TSelf left, TOther right);
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

        static abstract TResult operator checked -(TSelf left, TOther right);
    }

    public interface IUnaryNegationOperators<TSelf, TResult>
        where TSelf : IUnaryNegationOperators<TSelf, TResult>
    {
        static abstract TResult operator -(TSelf value);

        static abstract TResult operator checked -(TSelf value);
    }

    public interface IUnaryPlusOperators<TSelf, TResult>
        where TSelf : IUnaryPlusOperators<TSelf, TResult>
    {
        static abstract TResult operator +(TSelf value);
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
        where TSelf : IExponentialFunctions<TSelf>
    {
        static abstract TSelf Exp(TSelf x);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf ExpM1(TSelf x);

        static abstract TSelf Exp2(TSelf x);

        static abstract TSelf Exp2M1(TSelf x);

        static abstract TSelf Exp10(TSelf x);

        static abstract TSelf Exp10M1(TSelf x);
    }

    public interface IHyperbolicFunctions<TSelf>
        where TSelf : IHyperbolicFunctions<TSelf>
    {
        static abstract TSelf Acosh(TSelf x);

        static abstract TSelf Asinh(TSelf x);

        static abstract TSelf Atanh(TSelf x);

        static abstract TSelf Cosh(TSelf x);

        static abstract TSelf Sinh(TSelf x);

        static abstract TSelf Tanh(TSelf x);
    }

    public interface ILogarithmicFunctions<TSelf>
        where TSelf : ILogarithmicFunctions<TSelf>
    {
        static abstract TSelf Log(TSelf x);

        static abstract TSelf Log(TSelf x, TSelf newBase);

        static abstract TSelf Log2(TSelf x);

        static abstract TSelf Log10(TSelf x);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf LogP1(TSelf x);

        static abstract TSelf Log2P1(TSelf x);

        static abstract TSelf Log10P1(TSelf x);
    }

    public interface IPowerFunctions<TSelf>
        where TSelf : IPowerFunctions<TSelf>
    {
        static abstract TSelf Pow(TSelf x, TSelf y);
    }

    public interface IRootFunctions<TSelf>
        where TSelf : IRootFunctions<TSelf>
    {
        static abstract TSelf Cbrt(TSelf x);

        static abstract TSelf Sqrt(TSelf x);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf Hypot(TSelf x, TSelf y);

        static abstract TSelf Root(TSelf x, TSelf n);
    }

    public interface ITrigonometricFunctions<TSelf>
        where TSelf : ITrigonometricFunctions<TSelf>
    {
        static abstract TSelf Acos(TSelf x);

        static abstract TSelf Asin(TSelf x);

        static abstract TSelf Atan(TSelf x);

        static abstract TSelf Atan2(TSelf y, TSelf x);

        static abstract TSelf Cos(TSelf x);

        static abstract TSelf Sin(TSelf x);

        static abstract TSelf Tan(TSelf x);

        // The following methods are approved but not yet implemented in the libraries

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
          IShiftOperators<TSelf, TSelf, TSelf>
        where TSelf : IBinaryInteger<TSelf>
    {
        // We might want to support "big multiplication" for fixed width types
        // This would return a tuple (TSelf high, TSelf low) or similar, to match what we do for System.Math

        // Returning int is currently what BitOperations does, however this can be cumbersome or prohibitive
        // in various algorithms where returning TSelf is better.

        static abstract (TSelf Quotient, TSelf Remainder) DivRem(TSelf left, TSelf right);

        static abstract TSelf LeadingZeroCount(TSelf value);

        static abstract TSelf PopCount(TSelf value);

        static abstract TSelf RotateLeft(TSelf value, TSelf rotateAmount);

        static abstract TSelf RotateRight(TSelf value, TSelf rotateAmount);

        static abstract TSelf TrailingZeroCount(TSelf value);

        // These methods allow getting the underlying bytes that represent the binary integer

        long GetBitLength();

        int GetByteCount();

        bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten);

        int WriteLittleEndian(byte[] destination);

        int WriteLittleEndian(byte[] destination, int startIndex);

        int WriteLittleEndian(Span<byte> destination);
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

        // The following methods are exposed on the floating-point types today

        static abstract bool IsFinite(TSelf value);

        static abstract bool IsInfinity(TSelf value);

        static abstract bool IsNaN(TSelf value);

        static abstract bool IsNegativeInfinity(TSelf value);

        static abstract bool IsNormal(TSelf value);

        static abstract bool IsPositiveInfinity(TSelf value);

        static abstract bool IsSubnormal(TSelf value);

        // The following methods are approved but not yet implemented in the libraries

        static abstract TSelf Compound(TSelf x, TSelf n);

        static abstract TSelf MaxMagnitudeNumber(TSelf x, TSelf y);

        static abstract TSelf MaxNumber(TSelf x, TSelf y);

        static abstract TSelf MinMagnitudeNumber(TSelf x, TSelf y);

        static abstract TSelf MinNumber(TSelf x, TSelf y);

        // These methods allow getting the underlying bytes that represent the IEEE 754 floating-point

        long GetExponentBitLength();

        int GetExponentByteCount();

        long GetSignificandBitLength();

        int GetSignificandByteCount();

        bool TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten);

        bool TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten);

        int WriteExponentLittleEndian(byte[] destination);

        int WriteExponentLittleEndian(byte[] destination, int startIndex);

        int WriteExponentLittleEndian(Span<byte> destination);

        int WriteSignificandLittleEndian(byte[] destination);

        int WriteSignificandLittleEndian(byte[] destination, int startIndex);

        int WriteSignificandLittleEndian(Span<byte> destination);

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

    public interface INumber<TSelf>
        : IComparisonOperators<TSelf, TSelf>,   // implies IEqualityOperators<TSelf, TSelf>
          IModulusOperators<TSelf, TSelf, TSelf>,
          INumberBase<TSelf>,
          ISpanParsable<TSelf>                  // implies IParsable<TSelf>
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

        // Abs mirrors Math.Abs and returns the same type. This can fail for MinValue of signed integer types
        // Swift has an associated type that can be used here, which would require an additional type parameter in .NET
        // However that would hinder the reusability of these interfaces in constraints

        static abstract TSelf Abs(TSelf value);

        static abstract TSelf Clamp(TSelf value, TSelf min, TSelf max);

        static abstract TSelf CopySign(TSelf value, TSelf sign);

        // IsEven, IsOdd, IsZero

        static abstract bool IsNegative(TSelf value);

        static abstract TSelf Max(TSelf x, TSelf y);

        static abstract TSelf MaxMagnitude(TSelf x, TSelf y);

        static abstract TSelf Min(TSelf x, TSelf y);

        static abstract TSelf MinMagnitude(TSelf x, TSelf y);

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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
        // * IBinaryNumber                                                                  // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
        // * IBinaryNumber                                                                  // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // ? Explicit
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // ? Explicit
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // ? Explicit
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // ? Explicit
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
        public const decimal NegativeOne = -1                                               // ? Expose
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
        //   * TSelf Round(TSelf)                                                           // * Existing
        //   * TSelf Round(TSelf, int)                                                      // * Existing
        //   * TSelf Round(TSelf, MidpointRounding)                                         // * Existing
        //   * TSelf Round(TSelf, int, MidpointRounding)                                    // * Existing
        //   * TSelf Truncate(TSelf)                                                        // * Existing
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        public const double NegativeOne = -1                                                // ? Expose
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
        //   * TSelf ExpM1(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp2(TSelf)                                                            // Approved - NYI
        //   * TSelf Exp2M1(TSelf)                                                          // Approved - NYI
        //   * TSelf Exp10(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp10M1(TSelf)                                                         // Approved - NYI
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
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
        //   * long GetExponentBitLength()                                                  // Approved - NYI
        //   * int GetExponentByteCount()                                                   // Approved - NYI
        //   * long GetSignificandBitLength()                                               // Approved - NYI
        //   * int GetSignificandByteCount()                                                // Approved - NYI
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MinNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf ScaleB(TSelf, int)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // Approved - NYI
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[])                                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[], int)                                   // Approved - NYI
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[])                                     // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[], int)                                // Approved - NYI
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // Approved - NYI
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
        //   * TSelf LogP1(TSelf)                                                           // Approved - NYI
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)                                                          // Approved - NYI
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)                                                         // Approved - NYI
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * TSelf ExpM1(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp2(TSelf)                                                            // Approved - NYI
        //   * TSelf Exp2M1(TSelf)                                                          // Approved - NYI
        //   * TSelf Exp10(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp10M1(TSelf)                                                         // Approved - NYI
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
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
        //   * long GetExponentBitLength()                                                  // Approved - NYI
        //   * int GetExponentByteCount()                                                   // Approved - NYI
        //   * long GetSignificandBitLength()                                               // Approved - NYI
        //   * int GetSignificandByteCount()                                                // Approved - NYI
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MinNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf ScaleB(TSelf, int)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // Approved - NYI
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[])                                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[], int)                                   // Approved - NYI
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[])                                     // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[], int)                                // Approved - NYI
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // Approved - NYI
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
        //   * TSelf LogP1(TSelf)                                                           // Approved - NYI
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)                                                          // Approved - NYI
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)                                                         // Approved - NYI
        // * IModulusOperators
        //   * TSelf operator %(TSelf, TSelf)
        // * IMultiplyOperators
        //   * TSelf operator *(TSelf, TSelf)
        //   * TSelf operator checked *(TSelf, TSelf)
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
        // * IBinaryNumber                                                                  // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        public const float NegativeOne = -1                                                // ? Expose
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
        //   * TSelf ExpM1(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp2(TSelf)                                                            // Approved - NYI
        //   * TSelf Exp2M1(TSelf)                                                          // Approved - NYI
        //   * TSelf Exp10(TSelf)                                                           // Approved - NYI
        //   * TSelf Exp10M1(TSelf)                                                         // Approved - NYI
        // * IFloatingPoint
        //   * TSelf Ceiling(TSelf)
        //   * TSelf Floor(TSelf)
        //   * TSelf Round(TSelf)
        //   * TSelf Round(TSelf, int)
        //   * TSelf Round(TSelf, MidpointRounding)
        //   * TSelf Round(TSelf, int, MidpointRounding)
        //   * TSelf Truncate(TSelf)
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
        //   * long GetExponentBitLength()                                                  // Approved - NYI
        //   * int GetExponentByteCount()                                                   // Approved - NYI
        //   * long GetSignificandBitLength()                                               // Approved - NYI
        //   * int GetSignificandByteCount()                                                // Approved - NYI
        //   * TSelf Ieee754Remainder(TSelf, TSelf)
        //   * int ILogB(TSelf)
        //   * bool IsFinite(TSelf)                                                         // * Existing
        //   * bool IsInfinity(TSelf)                                                       // * Existing
        //   * bool IsNaN(TSelf)                                                            // * Existing
        //   * bool IsNegativeInfinity(TSelf)                                               // * Existing
        //   * bool IsNormal(TSelf)                                                         // * Existing
        //   * bool IsPositiveInfinity(TSelf)                                               // * Existing
        //   * bool IsSubnormal(TSelf)                                                      // * Existing
        //   * TSelf MaxMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MaxNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf MinMagnitudeNumber(TSelf, TSelf)                                       // Approved - NYI
        //   * TSelf MinNumber(TSelf, TSelf)                                                // Approved - NYI
        //   * TSelf ScaleB(TSelf, int)
        //   * bool TryWriteExponentLittleEndian(byte[], out int)                           // Approved - NYI
        //   * bool TryWriteSignificandLittleEndian(byte[], out int)                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[])                                        // Approved - NYI
        //   * int WriteExponentLittleEndian(byte[], int)                                   // Approved - NYI
        //   * int WriteExponentLittleEndian(Span<byte>)                                    // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[])                                     // Approved - NYI
        //   * int WriteSignificandLittleEndian(byte[], int)                                // Approved - NYI
        //   * int WriteSignificandLittleEndian(Span<byte>)                                 // Approved - NYI
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
        //   * TSelf LogP1(TSelf)                                                           // Approved - NYI
        //   * TSelf Log2(TSelf)
        //   * TSelf Log2P1(TSelf)                                                          // Approved - NYI
        //   * TSelf Log10(TSelf)
        //   * TSelf Log10P1(TSelf)                                                         // Approved - NYI
        // * INumber
        //   * TSelf Abs(TSelf)
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // * Existing
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
        // * IParsable
        //   * TSelf Parse(string, IFormatProvider?)                                        // * Existing
        //   * bool TryParse(string?, IFormatProvider?, out TSelf)
        // * ISpanFormattable
        //   * bool TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?)    // * Existing - Optional Args
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
        //   * long GetBitLength()                                                          // Approved - NYI
        //   * int GetByteCount()                                                           // Approved - NYI
        //   * TSelf LeadingZeroCount(TSelf)
        //   * TSelf PopCount(TSelf)
        //   * TSelf RotateLeft(TSelf, int)
        //   * TSelf RotateRight(TSelf, int)
        //   * TSelf TrailingZeroCount(TSelf)
        //   * bool TryWriteLittleEndian(byte[], out int)                                   // Approved - NYI
        //   * int WriteLittleEndian(byte[])                                                // Approved - NYI
        //   * int WriteLittleEndian(byte[], int)                                           // Approved - NYI
        //   * int WriteLittleEndian(Span<byte>)                                            // Approved - NYI
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
        // * IMinMaxValue
        //   * TSelf MaxValue { get; }                                                      // * Existing
        //   * TSelf MinValue { get; }                                                      // * Existing
        // * INumber
        //   * TSelf Abs(TSelf)                                                             // ? Explicit
        //   * TSelf Clamp(TSelf, TSelf, TSelf)
        //   * TSelf CopySign(TSelf, TSelf)                                                 // ? Explicit
        //   * TSelf CreateChecked(TOther)
        //   * TSelf CreateSaturating(TOther)
        //   * TSelf CreateTruncating(TOther)
        //   * bool IsNegative(TSelf)                                                       // ? Explicit
        //   * TSelf Max(TSelf, TSelf)
        //   * TSelf MaxMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Min(TSelf, TSelf)
        //   * TSelf MinMagnitude(TSelf, TSelf)                                             // ? Explicit
        //   * TSelf Parse(string, NumberStyles, IFormatProvider?)                          // * Existing
        //   * TSelf Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)              // * Existing - Optional Args
        //   * int Sign(TSelf)
        //   * bool TryCreate(TOther, out TSelf)
        //   * bool TryParse(string?, NumberStyles, IFormatProvider?, out TSelf)            // * Existing
        //   * bool TryParse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?, out TSelf) // * Existing
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
* System.Numerics.Matrix3x2
* System.Numerics.Matrix4x4
* System.Numerics.Plane
* System.Numerics.Quaternion

Likewise, there are many comments within the defined interfaces above that call out key points that need additional modeling/consideration. Several of these concepts will be determined via user research and outreach while others will be determined via API review and feedback from other area experts. Others may be determined by language or runtime limitations in what is feasible for them to support.

Do we want a way to denote "primitives"? If we have it, should this be language primitives, runtime primitives, or ABI primitives?

Do we want an explicit `IConvertible<TSelf, TOther>` (or `IConvertibleFrom`/`IConvertibleTo`)?

Do we want a way to track "scalars" for vectors and matrices?

## Requirements
