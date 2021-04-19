# Statics in Interfaces

**DRAFT**

**Owners** [Immo Landwerth](https://github.com/terrajobst) | [Mads Torgersen](https://github.com/MadsTorgersen)

C# is looking at enabling static abstract members in interfaces (https://github.com/dotnet/csharplang/issues/4436). From the libraries perspective, this is an opportunity to enable "generic math" which could define a new baseline for how developers write algorithms in .NET.

## Scenarios

### Allow easier maintainence for generic code

Tanner is building `Vector<T>`, an important numeric helper type for SIMD acceleration. Today, this type support 10 underlying primitive types for `T` and they can use generic specialization in an internal helper to ensure the implementation is performant for each:

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

Jeff is trying to compute standard deviation of a list of numbers. They are surprised to find that such a function isn't built into .NET. After searching the web, Jeff finds a blog post with the following method (https://dotnetcodr.com/2017/06/29/calculate-standard-deviation-of-integers-with-c-net-3/):
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

### Base Interfaces

The base interfaces each describe one or more core operations that are used to build up the core interfaces listed later. In many cases, such as for addition, subtraction, multiplication, and division, there is only a single operator per interface. This split is important in order for them to be reusable against downstream types, like `DateTime` where addition does not imply subtraction or `Matrix` where multiplication does not imply division. These interfaces are not expected to be used by many users and instead primarily by developers looking at defining their own abstractions, as such we are not concerned with adding simplifying interfaces like `IAddable<TSelf> : IAddable<TSelf, TSelf>`. Likewise, there is not a concern with exposing `IAddable<TSelf, TOther, TResult>` and instead we will require `TSelf` and `TResult` be the same. This requirement greatly simplifies implementation and consumption of the types.

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

    public interface IComparableOperators<TSelf, TOther> : IComparable<TOther>, IEquatableOperators<TSelf, TOther>
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

    public interface IAddable<TSelf, TOther>
        where TSelf : IAddable<TSelf, TOther>
    {
        // An additive identity is not exposed here as it may be problematic for cases like
        // DateTime that implements both IAddable<DateTime, DateTime> and IAddable<DateTime, TimeSpan>
        // This would require overloading by return type and may be problematic for some scenarios where TOther might not have an additive identity

        // We can't assume TOther + TSelf is valid as not everything is commutative

        // We can't expose TSelf - TOther as some types only support one of the operations

        static abstract TSelf operator +(TSelf value);

        static abstract TSelf operator +(TSelf lhs, TOther rhs);

        // There is an open question on if we should support "checked" versions of the operators
        // Many algorithms, such as in LINQ, depend on checked behavior for overflow
        // Supporting this would require well-defined ECMA-335 names and language work to ensure the right things happen
    }

    public interface ISubtractable<TSelf, TOther>
        where TSelf : ISubtractable<TSelf, TOther>
    {
        static abstract TSelf operator -(TSelf lhs, TOther rhs);
    }

    public interface IIncrementable<TSelf>
        where TSelf : IIncrementable<TSelf>
    {
        // We don't inherit IAddable as TOther isn't well-defined for incrementable types
        // Incrementing twice is not necessarily the same as self + 2 and not all languages
        // use incrementing for numbers, they are sometimes used by iterators for example

        // We can't expose TSelf-- as some types only support one of the operations

        static abstract TSelf operator ++(TSelf value);
    }

    public interface IDecrementable<TSelf>
        where TSelf : IDecrementable<TSelf>
    {
        static abstract TSelf operator --(TSelf value);
    }

    public interface IMultipliable<TSelf, TOther>
        where TSelf : IMultipliable<TSelf, TOther>
    {
        // An multiplicative identity is not exposed here for the same reasons as IAddable<TSelf, TOther>

        // We can't assume TOther * TSelf is valid as not everything is commutative

        // We can't expose TSelf / TOther as some types, such as matrices, don't support dividing with respect to TOther

        // We can't inherit from IAddable<TSelf, TOther> as some types, such as matrices, support Matrix * scalar but not Matrix + scalar

        static abstract TSelf operator *(TSelf lhs, TOther rhs);
    }

    public interface IDivisable<TSelf, TOther>
        where TSelf : IDivisable<TSelf, TOther>
    {
        static abstract TSelf operator /(TSelf lhs, TOther rhs);
    }

    public interface IRemainder<TSelf, TOther>
        where TSelf : IRemainder<TSelf, TOther>
    {
        // The ECMA name is op_Modulus and so the behavior isn't actually "remainder" with regards to negative values

        // Likewise the name IRemainder doesn't fit with the other names, so a better one is needed

        static abstract TSelf operator %(TSelf lhs, TOther rhs);
    }

    public interface INegatable<TSelf>
        where TSelf : INegatable<TSelf>
    {
        static abstract TSelf operator -(TSelf value);
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

        // MinValue and MaxValue are more general purpose than just "numbers", they should probably live on some common base type
        // Likewise, the concept is a bad fit for types like BigInteger which aren't fixed sized
        // There may also be concern around the primitive types which already define these names as constants

        static abstract TSelf MinValue { get; }

        static abstract TSelf MaxValue { get; }

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
        : INumber<TSelf>,
          INegatable<TSelf>
        where TSelf : ISignedNumber<TSelf>
    {
        // There is an open question on if we actually need ISignedNumber<TSelf> or if just checking for INegatable<TSelf> is enough
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

    public interface ISignedBinaryNumber<TSelf>
        : IBinaryNumber<TSelf>,
          ISignedNumber<TSelf>
        where TSelf : ISignedBinaryNumber<TSelf>
    {
    }

    public interface IUnsignedBinaryNumber<TSelf>
        : IBinaryNumber<TSelf>,
          IUnsignedNumber<TSelf>
        where TSelf : IUnsignedBinaryNumber<TSelf>
    {
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

    public interface ISignedBinaryInteger<TSelf>
        : IBinaryInteger<TSelf>,
          ISignedBinaryNumber<TSelf>
        where TSelf : ISignedBinaryInteger<TSelf>
    {
    }

    public interface IUnsignedBinaryInteger<TSelf>
        : IBinaryInteger<TSelf>,
          IUnsignedBinaryNumber<TSelf>
        where TSelf : IUnsignedBinaryInteger<TSelf>
    {
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
        : IFloatingPoint<TSelf>,
          IBinaryNumber<TSelf>
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
    public struct Byte : IUnsignedBinaryInteger<byte> { }

    public struct Char : IUnsignedBinaryInteger<char> { }

    public struct DateTime
        : IAddable<DateTime, TimeSpan>,
          IComparableOperators<DateTime, DateTime> // implies IEquatableOperators<DateTime, DateTime>
    {
        // MinValue and MaxValue should likely come from a base interface

        static DateTime MinValue { get; }

        static DateTime MaxValue { get; }
    }

    public struct DateTimeOffset
        : IAddable<DateTimeOffset, DateTimeOffset>,
          IAddable<DateTimeOffset, TimeSpan>,
          IComparableOperators<DateTimeOffset, DateTimeOffset> // implies IEquatableOperators<DateTimeOffset, DateTimeOffset>
    {
        // MinValue and MaxValue should likely come from a base interface

        static DateTimeOffset MinValue { get; }

        static DateTimeOffset MaxValue { get; }

        // TODO: DateTimeOffset defines an implicit conversion to DateTime, should that be modeled in the interfaces?
    }

    public struct Decimal
        : ISignedNumber<decimal>
    {
        // Decimal defines a few additional operations like Ceiling, Floor, Round, and Truncate
        // The rest of the IEEE operations are missing.

        // Decimal and some other types likewise define "friendly" names for operators, should we expose them?
    }

    public struct Double : IBinaryFloatingPoint<double> { }

    public struct Guid
        : IComparableOperators<Guid, Guid> // implies IEquatableOperators<Guid, Guid>
    {
    }

    public struct Half : IBinaryFloatingPoint<Half> { }

    public struct Int16 : ISignedBinaryInteger<short> { }

    public struct Int32 : ISignedBinaryInteger<int> { }

    public struct Int64 : ISignedBinaryInteger<long> { }

    public struct IntPtr : ISignedBinaryInteger<nint> { /* Size */ }

    public struct SByte : ISignedBinaryInteger<sbyte> { }

    public struct Single : IBinaryFloatingPoint<float> { }

    public struct TimeSpan
        : IAddable<TimeSpan, TimeSpan>,
          IComparableOperators<TimeSpan, TimeSpan>, // implies IEquatableOperators<TimeSpan, TimeSpan>
          IDivisable<TimeSpan, TimeSpan>,
          IDivisable<TimeSpan, double>,
          IMultipliable<TimeSpan, TimeSpan>,
          IMultipliable<TimeSpan, double>,
          INegatable<TimeSpan>
    {
    }

    public struct UInt16 : IUnsignedBinaryInteger<ushort> { }

    public struct UInt32 : IUnsignedBinaryInteger<uint> { }

    public struct UInt64 : IUnsignedBinaryInteger<ulong> { }

    public struct UIntPtr : IUnsignedBinaryInteger<nuint> { }
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
