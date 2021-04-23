# Statics in Interfaces

**DRAFT**

**Owners** [Immo Landwerth](https://github.com/terrajobst) | [Mads Torgersen](https://github.com/MadsTorgersen)

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

Given the operation is the same for each type, only differing on the actual type in question, Tanner would expect this to be easier to author and maintain. Additionlly, each new type that needs to be supported adds additional branches and complexity to the code. For example, in .NET 6 support for `nint` and `nuint` support is desired.

Static abstracts in interfaces will allow Tanner to define an interface such as:
```csharp
public interface IAddable<TSelf>
    where TSelf : IAddable<TSelf>
{
    static abstract TSelf operator +(TSelf left, TSelf right);
}
```

Such an interface now allows the implementation of `ScalarAdd` to be rewritten as:
```csharp
private static T ScalarAdd<T>(T left, T right)
    where T : struct, IAddable<T>
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
public interface IAddable<TSelf>
    where TSelf : IAddable<TSelf>
{
    static abstract TSelf operator +(TSelf left, TSelf right);
}

public interface ISubtractable<TSelf>
    where TSelf : ISubtractable<TSelf>
{
    static abstract TSelf operator -(TSelf left, TSelf right);
}

public interface IMultipliable<TSelf>
    where TSelf : IMultipliable<TSelf>
{
    static abstract TSelf operator -(TSelf left, TSelf right);
}

public interface IDivisable<TSelf>
    where TSelf : IDivisable<TSelf>
{
    static abstract TSelf operator /(TSelf left, TSelf right);
}

public interface INumber<TSelf>
    : IAddable<TSelf>
    : ISubtractable<TSelf>
    : IMultipliable<TSelf>
    : IDivisable<TSelf>
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
        double sum = enumerable.Sum(d => (d - avg) * (d - avg));
        standardDeviation = Math.Sqrt(sum / count);
    }
    return standardDeviation;
}
```

## Designs

The overall design has been influenced by the existing .NET surface area, the Swift numeric protocols (https://developer.apple.com/documentation/swift/swift_standard_library/numbers_and_basic_values/numeric_protocols), and the Rust op traits (https://doc.rust-lang.org/std/ops/index.html). It is broken up into a few layers describing a hierarchy of interfaces and what types implement them. The interfaces aim to be reusable by a variety of types and so do not provide guarantees around concepts like associativity, commutativity or distributivity. Instead, these concepts may become available to tooling in the future via another mechanism, such as attributes on the respective operator implementations.

<!--
@startuml
interface "IAddable<TSelf, TOther, TResult>"
interface "IBinaryFloatingPoint<TSelf>"
interface "IBinaryInteger<TSelf>"
interface "IBinaryNumber<TSelf>"
interface "IComparable"
interface "IComparable<T>"
interface "IComparableOperators<TSelf, TOther>"
interface "IConvertible"
interface "IDecrementable<TSelf>"
interface "IDeserializationCallback"
interface "IDivisable<TSelf, TOther, TResult>"
interface "IEquatable<T>"
interface "IEquatableOperators<TSelf, TOther>"
interface "IFloatingPoint<TSelf>"
interface "IFixedWidth<TSelf>"
interface "IIncrementable<TSelf>"
interface "IFormattable"
interface "IMultipliable<TSelf, TOther, TResult>"
interface "INegatable<TSelf, TResult>"
interface "INumber<TSelf>"
interface "IParseable"
interface "IRemainder<TSelf, TOther, TResult>"
interface "ISerializable"
interface "ISignedNumber<TSelf>"
interface "ISpanFormattable"
interface "ISpanParseable"
interface "ISubtractable<TSelf, TOther, TResult>"
interface "IUnsignedNumber<TSelf>"

"IBinaryNumber<TSelf>"                  <|-- "IBinaryFloatingPoint<TSelf>"
"IFloatingPoint<TSelf>"                 <|-- "IBinaryFloatingPoint<TSelf>"
"IBinaryNumber<TSelf>"                  <|-- "IBinaryInteger<TSelf>"
"INumber<TSelf>"                        <|-- "IBinaryNumber<TSelf>"
"IComparable"                           <|-- "IComparableOperators<TSelf, TOther>"
"IComparable<T>"                        <|-- "IComparableOperators<TSelf, TOther>"
"IEquatableOperators<TSelf, TOther>"    <|-- "IComparableOperators<TSelf, TOther>"
"IEquatable<T>"                         <|-- "IEquatableOperators<TSelf, TOther>"
"ISignedNumber<TSelf>"                  <|-- "IFloatingPoint<TSelf>"
"IAddable<TSelf, TOther, TResult>"      <|-- "INumber<TSelf>"
"IComparableOperators<TSelf, TOther>"   <|-- "INumber<TSelf>"
"IDecrementable<TSelf>"                 <|-- "INumber<TSelf>"
"IDivisable<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"IIncrementable<TSelf>"                 <|-- "INumber<TSelf>"
"IMultipliable<TSelf, TOther, TResult>" <|-- "INumber<TSelf>"
"IRemainder<TSelf, TOther, TResult>"    <|-- "INumber<TSelf>"
"ISpanFormattable"                      <|-- "INumber<TSelf>"
"ISpanParseable<TSelf>"                 <|-- "INumber<TSelf>"
"ISubtractable<TSelf, TOther, TResult>" <|-- "INumber<TSelf>"
"INegatable<TSelf, TResult>"            <|-- "ISignedNumber<TSelf>"
"INumber<TSelf>"                        <|-- "ISignedNumber<TSelf>"
"IFormattable"                          <|-- "ISpanFormattable"
"IParseable"                            <|-- "ISpanParseable"
"INumber<TSelf>"                        <|-- "IUnsignedNumber<TSelf>"

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

"IBinaryInteger<TSelf>"                 <|-- "Byte"
"IConvertible"                          <|-- "Byte"
"IFixedWidth<TSelf>"                    <|-- "Byte"
"IUnsignedNumber<TSelf>"                <|-- "Byte"
"ValueType"                             <|-- "Byte"
"IBinaryInteger<TSelf>"                 <|-- "Char"
"IConvertible"                          <|-- "Char"
"IFixedWidth<TSelf>"                    <|-- "Char"
"IUnsignedNumber<TSelf>"                <|-- "Char"
"ValueType"                             <|-- "Char"
"IComparableOperators<TSelf, TOther>"   <|-- "DateOnly"
"IFixedWidth<TSelf>"                    <|-- "DateOnly"
"ISpanFormattable"                      <|-- "DateOnly"
"ISpanParseable<DateOnly>"              <|-- "DateOnly"
"ValueType"                             <|-- "DateOnly"
"IAddable<TSelf, TOther, TResult>"      <|-- "DateTime"
"IComparableOperators<TSelf, TOther>"   <|-- "DateTime"
"IConvertible"                          <|-- "DateTime"
"IFixedWidth<Tself>"                    <|-- "DateTime"
"ISerializable"                         <|-- "DateTime"
"ISpanFormattable"                      <|-- "DateTime"
"ISpanParseable<TSelf>"                 <|-- "DateTime"
"ISubtractable<TSelf, TOther, TResult>" <|-- "DateTime"
"ValueType"                             <|-- "DateTime"
"IAddable<TSelf, TOther, TResult>"      <|-- "DateTimeOffset"
"IComparableOperators<TSelf, TOther>"   <|-- "DateTimeOffset"
"IDeserializationCallback"              <|-- "DateTimeOffset"
"IFixedWidth<TSelf>"                    <|-- "DateTimeOffset"
"ISerializable"                         <|-- "DateTimeOffset"
"ISpanFormattable"                      <|-- "DateTimeOffset"
"ISpanParseable<TSelf>"                 <|-- "DateTimeOffset"
"ISubtractable<TSelf, TOther, TResult>" <|-- "DateTimeOffset"
"ValueType"                             <|-- "DateTimeOffset"
"IConvertible"                          <|-- "Decimal"
"IDeserializationCallback"              <|-- "Decimal"
"IFixedWidth<decimal>"                  <|-- "Decimal"
"ISerializable"                         <|-- "Decimal"
"ISignedNumber<TSelf>"                  <|-- "Decimal"
"ValueType"                             <|-- "Decimal"
"IBinaryFloatingPoint<TSelf>"           <|-- "Double"
"IConvertible"                          <|-- "Double"
"IFixedWidth<TSelf>"                    <|-- "Double"
"ValueType"                             <|-- "Double"
"IComparable"                           <|-- "Enum"
"IConvertible"                          <|-- "Enum"
"IFormattable"                          <|-- "Enum"
"ValueType"                             <|-- "Enum"
"IComparableOperators<TSelf, TOther>"   <|-- "Guid"
"ISpanFormattable"                      <|-- "Guid"
"ISpanParseable<TSelf>"                 <|-- "Guid"
"ValueType"                             <|-- "Guid"
"IBinaryFloatingPoint<TSelf>"           <|-- "Half"
"IFixedWidth<TSelf>"                    <|-- "Half"
"ValueType"                             <|-- "Half"
"IBinaryInteger<TSelf>"                 <|-- "Int16"
"IConvertible"                          <|-- "Int16"
"IFixedWidth<TSelf>"                    <|-- "Int16"
"ISignedNumber<TSelf>"                  <|-- "Int16"
"ValueType"                             <|-- "Int16"
"IBinaryInteger<TSelf>"                 <|-- "Int32"
"IConvertible"                          <|-- "Int32"
"IFixedWidth<TSelf>"                    <|-- "Int32"
"ISignedNumber<TSelf>"                  <|-- "Int32"
"ValueType"                             <|-- "Int32"
"IBinaryInteger<TSelf>"                 <|-- "Int64"
"IConvertible"                          <|-- "Int64"
"IFixedWidth<TSelf>"                    <|-- "Int64"
"ISignedNumber<TSelf>"                  <|-- "Int64"
"ValueType"                             <|-- "Int64"
"IBinaryInteger<TSelf>"                 <|-- "IntPtr"
"IFixedWidth<TSelf>"                    <|-- "IntPtr"
"ISignedNumber<TSelf>"                  <|-- "IntPtr"
"ISerializable"                         <|-- "IntPtr"
"ValueType"                             <|-- "IntPtr"
"IBinaryInteger<TSelf>"                 <|-- "SByte"
"IConvertible"                          <|-- "SByte"
"IFixedWidth<TSelf>"                    <|-- "SByte"
"ISignedNumber<TSelf>"                  <|-- "SByte"
"ValueType"                             <|-- "SByte"
"IBinaryFloatingPoint<TSelf>"           <|-- "Single"
"IConvertible"                          <|-- "Single"
"IFixedWidth<TSelf>"                    <|-- "Single"
"ValueType"                             <|-- "Single"
"IComparableOperators<TSelf, TOther>"   <|-- "TimeOnly"
"IFixedWidth<TSelf>"                    <|-- "TimeOnly"
"ISpanFormattable"                      <|-- "TimeOnly"
"ISpanParseable<TSelf>"                 <|-- "TimeOnly"
"ISubtractable<TSelf, TOther, TResult>" <|-- "TimeOnly"
"ValueType"                             <|-- "TimeOnly"
"IAddable<TSelf, TOther, TResult>"      <|-- "TimeSpan"
"IComparableOperators<TSelf, TOther>"   <|-- "TimeSpan"
"IDivisable<TSelf, TOther, TResult>"    <|-- "TimeSpan"
"IFixedWidth<Tself>"                    <|-- "TimeSpan"
"IMultipliable<TSelf, TOther, TResult>" <|-- "TimeSpan"
"INegatable<TSelf, TResult>"            <|-- "TimeSpan"
"ISpanFormattable"                      <|-- "TimeSpan"
"ISubtractable<TSelf, TOther, TResult>" <|-- "TimeSpan"
"ValueType"                             <|-- "TimeSpan"
"IBinaryInteger<TSelf>"                 <|-- "UInt16"
"IConvertible"                          <|-- "UInt16"
"IFixedWidth<TSelf>"                    <|-- "UInt16"
"IUnsignedNumber<TSelf>"                <|-- "UInt16"
"ValueType"                             <|-- "UInt16"
"IBinaryInteger<TSelf>"                 <|-- "UInt32"
"IConvertible"                          <|-- "UInt32"
"IFixedWidth<TSelf>"                    <|-- "UInt32"
"IUnsignedNumber<TSelf>"                <|-- "UInt32"
"ValueType"                             <|-- "UInt32"
"IBinaryInteger<TSelf>"                 <|-- "UInt64"
"IConvertible"                          <|-- "UInt64"
"IFixedWidth<TSelf>"                    <|-- "UInt64"
"IUnsignedNumber<TSelf>"                <|-- "UInt64"
"ValueType"                             <|-- "UInt64"
"IBinaryInteger<TSelf>"                 <|-- "UIntPtr"
"IFixedWidth<TSelf>"                    <|-- "UIntPtr"
"ISerializable"                         <|-- "UIntPtr"
"IUnsignedNumber<TSelf>"                <|-- "UIntPtr"
"ValueType"                             <|-- "UIntPtr"
"Object"                                <|-- "ValueType"
@enduml
-->

![UML](http://www.plantuml.com/plantuml/svg/h5LDSzem4BtdLp2ScqC_9YScquQ4reS69eRslBONs5QMgRJAb4v_VCEqaP7inIeJBinA-zw-NLbFxos3OLUh2zACPWqbZiRPXwg2Gk5acQDQlnejvxn5y_J_WDOflXu7oJUamUndgW4clLaqfFali3SlqumRD2SoxbrT20dJfjw1EKYJrYTB4JBVeG5kZ0tRwkJhHpHCdHgtM1giKNCxcUiumw8XKFGBc1ez1QKAABz7IVH8DdsuTpyS_Aiex2JsDNm-C_g9rLUgUDkxdUcX_cUDgO6vUpoVdMBQAKfl-nutS5H8J9C_bGKOYrqf3rW3wGfDcexy-K0xH3bjD5Od1EGxqC54uar1OUuADb1o-h1MslQ9kUY_KAFER_BxydBW7WlVptbKwQf4rnXd8bmcYUOJs8b7Y1mfHX8xqOjG3b_qyob5aPuooMZwhuF0A7dHyBCJaCYdAGiLqOAzn_f5zB2hdq9d-lpQYUugeeSDKQuTcfnyZIIqTy4p-auqIX4jlp1nRS7i1pmebKEOdi0Hlod-dOlHCju3_hi3wPqf5LWx6j_i6SVFmNGBnWUtm3ZNQXyiBAd6pi9ylRR8xJAML8DguyOLgaNTQLVxuAEZoWUVGArza6b-VNKSlNrp75szEuuMtEgU5z-mRDNaGOSvwSsHbeFwmtZzmYeikmhhhiB2gwZo5r0Eb_iTFerNU0E5K_fJcHsaH0m4OHYzcdSGRn1twaQylmpXWmfDEhojyfZflokTgfxpEyu-zhckCci7-6XkYc9hMjo-PAUX32e-o20Z6MGWWunaaD4tNC-ShYcdftjkFTVxJvzwup2JdFODdJIUPyxgQhgv3x_F-v2xJY7mUR9_ENqlv_IDdFDzp_c-ppUCx_xKzyF-vZkdzObSjDT3sgjYrLuRQQ-GeRrEXkQdjiR3DZfstU7Z_6inRHXjRGp7hnW76jkEzmkhPOFyKzYJf6nXARiFgFU4LMMWvy_8YZ9smjK-m-cGWJCX8-l9MDqr0K3zPE2xPQm_RI2HuWdHYa9qG8GeXv05qImcGOGeY1054Q8WGXP4OwW44Q8WGXH4Y888MH1DuBQebAqgfM_Tb8IHqZJUUPeFKO8X2Z14IOJiDE-oXP98MHGfMrQAfyfNX-fJUiwdz9o-jNFQm-SipPnFzzt2wJxaz2u_fzDzJk_lStg3dTDwoUc-vtJVStfGVc1BbVAUAcNtVYdB8-LfU5-LiZHIdeQtLYbFmvkhbAMHyZIylqfv6jvXnRnPYdaWtcGDrYytMnERFxQyVVuSVzzV_m00)

### Base Interfaces

The base interfaces each describe one or more core operations that are used to build up the core interfaces listed later. In many cases, such as for addition, subtraction, multiplication, and division, there is only a single operator per interface. This split is important in order for them to be reusable against downstream types, like `DateTime` where addition does not imply subtraction or `Matrix` where multiplication does not imply division. These interfaces are not expected to be used by many users and instead primarily by developers looking at defining their own abstractions, as such we are not concerned with adding simplifying interfaces like `IAddable<TSelf> : IAddable<TSelf, TSelf>`. Likewise, every interface added introduces increased size and startup overhead, so we need to strike a balance between expressiveness, exxtensibility, and cost.

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

    public interface IEquatableOperators<TSelf, TOther> : IEquatable<TOther>
        where TSelf : IEquatableOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IEquatable<TSelf, TOther>

        // This should not be `IEquatableOperators<TSelf>` as there are types like `Complex` where
        // TOther can be `double` and represent an optimization, both in size and perf

        static abstract bool operator ==(TSelf lhs, TOther rhs);

        static abstract bool operator !=(TSelf lhs, TOther rhs);
    }

    public interface IComparableOperators<TSelf, TOther> : IComparable, IComparable<TOther>, IEquatableOperators<TSelf, TOther>
        where TSelf : IComparableOperators<TSelf, TOther>
    {
        // Given this takes two generic parameters, we could simply call it IComparable<TSelf, TOther>

        // This inherits from IEquatableOperators<TSelf, TOther> as even though IComparable<T> does not
        // inherit from IEquatable<T>, the <= and >= operators as well as CompareTo itself imply equality

        static abstract bool operator <(TSelf lhs, TOther rhs);

        static abstract bool operator <=(TSelf lhs, TOther rhs);

        static abstract bool operator >(TSelf lhs, TOther rhs);

        static abstract bool operator >=(TSelf lhs, TOther rhs);
    }

    public interface IAddable<TSelf, TOther, TResult>
        where TSelf : IAddable<TSelf, TOther, TResult>
    {
        // An additive identity is not exposed here as it may be problematic for cases like
        // DateTime that implements both IAddable<DateTime, DateTime> and IAddable<DateTime, TimeSpan>
        // This would require overloading by return type and may be problematic for some scenarios where TOther might not have an additive identity

        // We can't assume TOther + TSelf is valid as not everything is commutative

        // We can't expose TSelf - TOther as some types only support one of the operations

        static abstract TResult operator +(TSelf lhs, TOther rhs);

        static abstract TResult checked operator +(TSelf lhs, TOther rhs);
    }

    public interface ISubtractable<TSelf, TOther, TResult>
        where TSelf : ISubtractable<TSelf, TOther, TResult>
    {
        static abstract TResult operator -(TSelf lhs, TOther rhs);

        static abstract TResult checked operator -(TSelf lhs, TOther rhs);
    }

    public interface IIncrementable<TSelf>
        where TSelf : IIncrementable<TSelf>
    {
        // We don't inherit IAddable as TOther isn't well-defined for incrementable types
        // Incrementing twice is not necessarily the same as self + 2 and not all languages
        // use incrementing for numbers, they are sometimes used by iterators for example

        // We can't expose TSelf-- as some types only support one of the operations

        // We don't support TResult as C# requires TSelf to be the return type

        static abstract TSelf operator ++(TSelf value);

        static abstract TSelf checked operator ++(TSelf value);
    }

    public interface IDecrementable<TSelf>
        where TSelf : IDecrementable<TSelf>
    {
        static abstract TSelf operator --(TSelf value);

        static abstract TSelf checked operator --(TSelf value);
    }

    public interface IMultipliable<TSelf, TOther, TResult>
        where TSelf : IMultipliable<TSelf, TOther, TResult>
    {
        // A multiplicative identity is not exposed here for the same reasons as IAddable<TSelf, TOther>

        // We can't assume TOther * TSelf is valid as not everything is commutative

        // We can't expose TSelf / TOther as some types, such as matrices, don't support dividing with respect to TOther

        // We can't inherit from IAddable<TSelf, TOther> as some types, such as matrices, support Matrix * scalar but not Matrix + scalar

        static abstract TResult operator *(TSelf lhs, TOther rhs);

        static abstract TResult checked operator *(TSelf lhs, TOther rhs);
    }

    public interface IDivisable<TSelf, TOther, TResult>
        where TSelf : IDivisable<TSelf, TOther, TResult>
    {
        static abstract TResult operator /(TSelf lhs, TOther rhs);

        static abstract TResult checked operator /(TSelf lhs, TOther rhs);
    }

    public interface IRemainder<TSelf, TOther, TResult>
        where TSelf : IRemainder<TSelf, TOther, TResult>
    {
        // The ECMA name is op_Modulus and so the behavior isn't actually "remainder" with regards to negative values

        // Likewise the name IRemainder doesn't fit with the other names, so a better one is needed

        static abstract TResult operator %(TSelf lhs, TOther rhs);

        static abstract TResult checked operator %(TSelf lhs, TOther rhs);
    }

    public interface INegatable<TSelf, TResult>
        where TSelf : INegatable<TSelf, TResult>
    {
        // Should unary plus be on its own type?

        static abstract TResult operator +(TSelf value);

        static abstract TResult checked operator +(TSelf value);

        static abstract TResult operator -(TSelf value);

        static abstract TResult checked operator -(TSelf value);
    }

    public interface IFixedWidth<TSelf>
        where TSelf : IFixedWidth<TSelf>
    {
        // MinValue and MaxValue are more general purpose than just "numbers", so they live on this common base type
        // Fixed-Width isn't the best name however, so we should think of alternatives

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
        : IAddable<TSelf, TSelf>,
          IComparableOperators<TSelf, TSelf>,   // implies IEquatableOperators<TSelf, TSelf>
          IDecrementable<TSelf>,
          IDivisable<TSelf, TSelf>,
          IIncrementable<TSelf>,
          IMultipliable<TSelf, TSelf>,
          IRemainder<TSelf, TSelf>,
          ISpanFormattable,                     // implies IFormattable
          ISpanParseable<TSelf>,                // implies IParseable<TSelf>
          ISubtractable<TSelf, TSelf>
        where TSelf : INumber<TSelf>
    {
        // For the Create methods, there is some concern over users implementing them. It is not necessarily trivial to take an arbitrary TOther
        // and convert it into an arbitrary TSelf. If you know the type of TOther, you may be able to optimize in a few ways. Otherwise, you
        // may have to fail and throw in the worst case.

        static abstract TSelf Create<TOther>(TOther value)
            where TOther : INumber<TOther>;

        static abstract TSelf? CreateExactly<TOther>(TOther value)
            where TOther : INumber<TOther>;

        static abstract TSelf CreateSaturating<TOther>(TOther value)
            where TOther : INumber<TOther>;

        static abstract TSelf CreateTruncating<TOther>(TOther value)
            where TOther : INumber<TOther>;

        // There is an open question on whether properties like IsSigned, IsBinary, IsFixedWidth, Base/Radix, and others are beneficial
        // We could expose them for trivial checks or users could be required to check for the corresponding correct interfaces

        static abstract TSelf One { get; }

        static abstract TSelf Zero { get; }

        // Abs mirrors Math.Abs and returns the same type. This can fail for MinValue of signed integer types
        // Swift has an associated type that can be used here, which would require an additional type parameter in .NET
        // However that would hinder the reusability of these interfaces in constraints

        static abstract TSelf Abs(TSelf value);

        static abstract TSelf Clamp(TSelf value, TSelf min, TSelf max);

        static abstract (TSelf Quotient, TSelf Remainder) DivRem(TSelf left, TSelf right);

        static abstract TSelf Max(TSelf x, TSelf y);

        static abstract TSelf Min(TSelf x, TSelf y);

        // Math only exposes this Sign for signed types, but it is well-defined for unsigned types
        // it can simply never return -1 and only 0 or 1 instead

        static abstract int Sign(TSelf value);
    }

    public interface ISignedNumber<TSelf>
        : INegatable<TSelf, TResult>,
          INumber<TSelf>
        where TSelf : ISignedNumber<TSelf>
    {
        // There is an open question on if we actually need ISignedNumber<TSelf> or if just checking for INegatable<TSelf, TResult> is enough
        // Likewise, it might be that negation on unsigned numbers is fine, its conceptually just `0 - x` which means it always overflows for unsigned
    }

    public interface IUnsignedNumber<TSelf>
        : INumber<TSelf>
        where TSelf : IUnsignedNumber<TSelf>
    {
        // It's not possible to check for lack of an interface in a constraint, so IUnsignedNumber<TSelf> is likely required
    }

    public interface IBinaryNumber<TSelf>
        : INumber<TSelf>
        where TSelf : IBinaryNumber<TSelf>
    {
        // The bitwise operators should probably be defined on some common base interface, like IBitwiseOperators
        // Likewise, having these on IBinaryNumber<TSelf> means they will be available to floating-point types
        // The operations are well-defined [for floats] but many languages don't directly expose them, we already support
        // them in SIMD contexts today, as do most languages with SIMD support, so there is not great concern here.

        static abstract TSelf operator &(TSelf lhs, TSelf rhs);

        static abstract TSelf operator |(TSelf lhs, TSelf rhs);

        static abstract TSelf operator ^(TSelf lhs, TSelf rhs);

        static abstract TSelf operator ~(TSelf value);

        // The shifting operators should probably be defined on some common base interface, like IShiftable
        // There is an open question on if we need language support for "arithmetic" vs "logical" right shift"
        // ECMA 335 already defines `op_SignedRightShift` and `op_UnsignedRightShift`

        // Likewise, forcing int here is inconvenient from the perspective of generic code where the amount will
        // be "in range" (e.g. 0-63 for int64) but where it may be the result of other logic and not be an int32

        static abstract TSelf operator <<(TSelf value, int shiftAmount);

        static abstract TSelf operator >>(TSelf value, int shiftAmount);

        static abstract bool IsPow2();

        static abstract TSelf Log2();

        // We have a "well-defined" behavior for the next few methods in BigInteger. There are some cases where we would likely benefit
        // returning nint/nuint to better support big data and its possible we would want to break compat here for a better experience

        long GetBitLength();

        int GetByteCount(bool isUnsigned);

        byte[] ToByteArray()
        byte[] ToByteArray(bool isUnsigned = false, bool isBigEndian = false);

        bool TryWriteBytes(Span<byte> destination, out int bytesWritten);
        bool TryWriteBytes(Span<byte> destination, out int bytesWritten, bool isUnsigned, bool isBigEndian);
    }

    public interface IBinaryInteger<TSelf> : IBinaryNumber<TSelf>
        where TSelf : IBinaryInteger<TSelf>
    {
        // We might want to support "big multiplication" for fixed width types
        // This would return a tuple (TSelf high, TSelf low) or similar, to match what we do for System.Math

        // Returning int is currently what BitOperations does, however this can be cumbersome or prohibitive
        // in various algorithms where returning TSelf is better.

        static abstract int LeadingZeroCount(TSelf value);

        static abstract int PopCount(TSelf value);

        static abstract int TrailingZeroCount(TSelf value);

        // Much like with shift, the rotateAmount should likely support being TSelf and potentially other types

        static abstract TSelf RotateLeft(TSelf value, int rotateAmount);

        static abstract TSelf RotateRight(TSelf value, int rotateAmount);
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

        static abstract int ILogB(TSelf x);

        static abstract TSelf Log(TSelf x);

        static abstract TSelf Log(TSelf x, TSelf newBase);

        static abstract TSelf Log2(TSelf x);

        static abstract TSelf Log10(TSelf x);

        static abstract TSelf MaxMagnitude(TSelf x, TSelf y);

        static abstract TSelf MinMagnitude(TSelf x, TSelf y);

        static abstract TSelf Pow(TSelf x, TSelf y);

        static abstract TSelf Round(TSelf x);

        static abstract TSelf Round(TSelf x, int digits);

        static abstract TSelf Round(TSelf x, MidpointRounding mode);

        static abstract TSelf Round(TSelf x, int digits, MidpointRounding mode);

        // IEEE defines n to be an integral type, but not the size

        static abstract TSelf ScaleB(TSelf x, int n);

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
```

### Interface Implementors

Various types in the System namespace would implement the above interfaces as follows.

```csharp
namespace System
{
    public struct Byte
        : IBinaryInteger<byte>,
          IConvertible,
          IFixedWidth<byte>,
          IUnsignedNumber<byte>
    {
    }

    public struct Char
        : IBinaryInteger<char>,
          IConvertible,
          IFixedWidth<char>,
          IUnsignedNumber<char>
    {
    }

    public struct DateOnly
        : IComparableOperators<DateOnly, DateOnly>,
          IFixedWidth<DateOnly>,
          ISpanFormattable,
          ISpanParseable<DateOnly>
    {
    }

    public struct DateTime
        : IAddable<DateTime, TimeSpan, DateTime>,
          IComparableOperators<DateTime, DateTime>,
          IConvertible,
          IFixedWidth<DateTime>,
          ISerializable,
          ISpanFormattable,
          ISpanParseable<DateTime>,
          ISubtractable<DateTime, TimeSpan, DateTime>,
          ISubtractable<DateTime, DateTime, TimeSpan>
    {
    }

    public struct DateTimeOffset
        : IAddable<DateTimeOffset, TimeSpan, DateTimeOffset>,
          IComparableOperators<DateTimeOffset, DateTimeOffset>,
          IDeserializationCallback,
          IFixedWidth<DateTimeOffset>,
          ISerializable,
          ISpanFormattable,
          ISpanParseable<DateTimeOffset>,
          ISubtractable<DateTimeOffset, TimeSpan, DateTimeOffset>,
          ISubtractable<DateTimeOffset, DateTimeOffset, TimeSpan>
    {
        // TODO: DateTimeOffset defines an implicit conversion to DateTime, should that be modeled in the interfaces?
    }

    public struct Decimal
        : IConvertible,
          IDeserializationCallback,
          IFixedWidth<decimal>,
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
          IFixedWidth<double>
    {
    }

    public struct Enum
        : IComparable,
          IConvertible,
          IFormattable
    {
    }

    public struct Guid
        : IComparableOperators<Guid, Guid>,
          ISpanFormattable,
          ISpanParseable<Guid>
    {
    }

    public struct Half
        : IBinaryFloatingPoint<Half>,
          IFixedWidth<Half>
    {
    }

    public struct Int16
        : IBinaryInteger<short>,
          IConvertible,
          IFixedWidth<short>,
          ISignedNumber<short>
    {
    }

    public struct Int32
        : IBinaryInteger<int>,
          IConvertible,
          IFixedWidth<int>,
          ISignedNumber<int>
    {
    }

    public struct Int64
        : IBinaryInteger<long>,
          IConvertible,
          IFixedWidth<long>,
          ISignedNumber<long>
    {
    }

    public struct IntPtr
        : IBinaryInteger<nint>,
          IFixedWidth<nint>,
          ISerializable,
          ISignedNumber<nint>
    {
    }

    public struct SByte
        : IBinaryInteger<sbyte>,
          IConvertible,
          IFixedWidth<sbyte>,
          ISignedNumber<sbyte>
    {
    }

    public struct Single
        : IBinaryFloatingPoint<float>,
          IConvertible,
          IFixedWidth<float>
    {
    }

    public struct TimeOnly
        : IComparableOperators<TimeOnly, TimeOnly>,
          IFixedWidth<TimeOnly>,
          ISpanFormattable,
          ISpanParseable<TimeOnly>,
          ISubtractable<TimeOnly, TimeOnly, TimeSpan>
    {
    }

    public struct TimeSpan
        : IAddable<TimeSpan, TimeSpan, TimeSpan>,
          IComparableOperators<TimeSpan, TimeSpan>,
          IDivisable<TimeSpan, double, TimeSpan>,
          IDivisable<TimeSpan, TimeSpan, double>,
          IFixedWidth<TimeSpan>,
          IMultipliable<TimeSpan, double, TimeSpan>,
          INegatable<TimeSpan, TResult>,
          ISpanFormattable,
          ISubtractable<TimeSpan, TimeSpan, TimeSpan>
    {
    }

    public struct UInt16
        : IBinaryInteger<ushort>,
          IConvertible,
          IFixedWidth<ushort>,
          IUnsignedNumber<ushort>
    {
    }

    public struct UInt32
        : IBinaryInteger<uint>,
          IConvertible,
          IFixedWidth<uint>,
          IUnsignedNumber<uint>
    {
    }

    public struct UInt64
        : IBinaryInteger<ulong>,
          IConvertible,
          IFixedWidth<ulong>,
          IUnsignedNumber<ulong>
    {
    }

    public struct UIntPtr
        : IBinaryInteger<nuint>,
          IFixedWidth<nuint>,
          ISerializable,
          IUnsignedNumber<nuint>
    {
    }
}
```

### Pending Concepts

There are several types which may benefit from some interface support. These include, but aren't limited to:
* System.Array
* System.DateOnly
* System.Enum
* System.Index
* System.Range
* System.TimeOnly
* System.Tuple
* System.ValueTuple
* System.Numerics.BigInteger
* System.Numerics.Complex
* System.Numerics.Matrix3x2
* System.Numerics.Matrix4x4
* System.Numerics.Plane
* System.Numerics.Quaternion
* System.Numerics.Vector<T>
* System.Numerics.Vector2
* System.Numerics.Vector3
* System.Numerics.Vector4
* System.Runtime.Intrinsics.Vector64<T>
* System.Runtime.Intrinsics.Vector128<T>
* System.Runtime.Intrinsics.Vector256<T>

Likewise, there are many comments within the defined interfaces above that call out key points that need additional modeling/consideration. Several of these concepts will be determined via user research and outreach while others will be determined via API review and feedback from other area experts. Others may be determined by language or runtime limitations in what is feasible for them to support.

## Requirements
