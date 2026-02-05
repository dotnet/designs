# Improving the _string_ developer experience

**Owners** [Tarek Mahmoud Sayed](https://github.com/tarekgh) | [Santiago Fernandez Madero](https://github.com/safern) | [Levi Broderick](https://github.com/GrabYourPitchforks)

## Problem summary

APIs on `string` are not consistently ordinal or linguistic, and this leads to confusion among our developer community and bugs in applications. https://github.com/dotnet/runtime/issues/43956 describes this pain in more detail, and it aggregates issues where developers experienced breaks when upgrading from .NET Core 3.1 to .NET 5 due to runtime changes exposing latent bugs in their own applications.

That GitHub issue describes various options we were considering, including relying solely on Roslyn analyzers or providing runtime compatibility switches. None of these issues is very palatable because they either force the developers to think about globalization concerns or they make it difficult for library authors to reason about what behaviors their components will exhibit at runtime.

This draft proposes an alternate solution which attempts to provide the best of all worlds: steering new developers toward success, letting people with existing code bases know when they have issues, and allowing existing compiled DLLs to work largely as expected.

## Characteristics of an ideal solution

An ideal solution to this problem should have two qualities.

1. `string` and related APIs should default to ordinal behavior unless the caller makes an explicit request for a different behavior.

2. Developers should not be exposed to any globalization-related concepts unless they explicitly seek globalization support.

The first characteristic is fairly straightforward. APIs like `string.Compare`, `string.Contains`, `string.Equals`, `string.IndexOf`, and others should all behave in an ordinal fashion. Developers shouldn't have to rely on Roslyn analyzers or docs.microsoft.com to know what behavior a given API has.

The second characteristic requires a bit more explanation. Popular programming languages like Java, JavaScript, Go, Python, C/C++, and others treat string APIs as ordinal. .NET is unique in that we build globalization concepts directly into many basic `string` APIs. Unfortunately, evidence shows that .NET's behavior in this regard is largely unexpected and confusing to our developer audience.

[Java 16's `String` class](https://docs.oracle.com/en/java/javase/16/docs/api/java.base/java/lang/String.html) (which is the closest to .NET's `string` class) is a good example of this principle. Per their own documentation:

> Unless otherwise noted, methods for comparing Strings do not take locale into account.

And in fact, the _only_ two Java `String` APIs that are culture-aware are case-conversion routines.

```java
String s = getString();

// parameterless overloads use current locale by default
String s1 = s.toUpperCase();
String s2 = s.toLowerCase();

// or you can pass a Locale instance if you don't want to use the current locale
// Java's Locale.ROOT is roughly equivalent to .NET's CultureInfo.InvariantCulture
String s3 = s.toUpperCase(Locale.ROOT);
String s4 = s.toLowerCase(Locale.ROOT);
```

And even though these are the only two linguistic-by-default APIs on Java's `String` type, problematic usage of them is still so widespread that CERT's coding rules for Java [forbid calling the parameterless overloads](https://wiki.sei.cmu.edu/confluence/display/java/STR02-J.+Specify+an+appropriate+locale+when+comparing+locale-dependent+data). This CERT rule STR02-J is roughly equivalent to .NET's [CA1310 rule](https://docs.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1310), which flags certain overloads of `string` APIs as problematic.

Put another way, the second characteristic mentioned above might be paraphrased as "developers shouldn't be exposed to globalization-related concerns unless they have imported the _System.Globalization_ namespace into their file."

## Proposed solution

To solve this problem for the .NET ecosystem at large, we propose to take the following actions on `string`.

1. Obsolete (as warning) all linguistic-by-default methods on `string` with a new __SYSLIBxxxx__ identifier.
2. Mark all newly-obsoleted methods as `[EditorBrowsable(EditorBrowsableState.Never)]`.
3. Mark all methods which take a `CultureInfo` parameter as `[EditorBrowsable(EditorBrowsableState.Never)]`, but do not obsolete them.
4. Add simple ordinal overloads for all obsoleted methods. (These overloads _should not_ take `StringComparison` as a parameter. More on this later.)
5. Add simple "ignore case" ordinal overloads for all methods which currently don't have them.
6. Create fixers to migrate call sites from the now-obsoleted methods onto newer methods.

And we propose to take the following actions on `string`-like APIs.

1. Mark all linguistic-related APIs as `[EditorBrowsable(EditorBrowsableState.Never)]`, but do not obsolete them. (Exception: do not mark APIs if they're in the _System.Globalization_ namespace.)
2. Create analyzers to detect `Comparer<string>.Default`, `new SortedSet<string>()`, and similar code patterns that implicitly rely on the current culture.

### List of API obsoletions and additions

In the diff below, a `-` indicates hiding; and a `+` indicates addition. A subset of `-` APIs are obsoleted, using the pattern described above.

```diff
 public sealed partial class String : System.Collections.Generic.IEnumerable<char>, System.Collections.IEnumerable, System.ICloneable, System.IComparable, System.IComparable<string?>, System.IConvertible, System.IEquatable<string?>
 {
     public static readonly string Empty;
     [System.CLSCompliantAttribute(false)]
     public unsafe String(char* value) { }
     [System.CLSCompliantAttribute(false)]
     public unsafe String(char* value, int startIndex, int length) { }
     public String(char c, int count) { }
     public String(char[]? value) { }
     public String(char[] value, int startIndex, int length) { }
     public String(System.ReadOnlySpan<char> value) { }
     [System.CLSCompliantAttribute(false)]
     public unsafe String(sbyte* value) { }
     [System.CLSCompliantAttribute(false)]
     public unsafe String(sbyte* value, int startIndex, int length) { }
     [System.CLSCompliantAttribute(false)]
     public unsafe String(sbyte* value, int startIndex, int length, System.Text.Encoding enc) { }
     [System.Runtime.CompilerServices.IndexerName("Chars")]
     public char this[int index] { get { throw null; } }
     public int Length { get { throw null; } }
     public object Clone() { throw null; }
-    public static int Compare(System.String? strA, int indexA, System.String? strB, int indexB, int length) { throw null; }
-    public static int Compare(System.String? strA, int indexA, System.String? strB, int indexB, int length, bool ignoreCase) { throw null; }
-    public static int Compare(System.String? strA, int indexA, System.String? strB, int indexB, int length, bool ignoreCase, System.Globalization.CultureInfo? culture) { throw null; }
-    public static int Compare(System.String? strA, int indexA, System.String? strB, int indexB, int length, System.Globalization.CultureInfo? culture, System.Globalization.CompareOptions options) { throw null; }
     public static int Compare(System.String? strA, int indexA, System.String? strB, int indexB, int length, System.StringComparison comparisonType) { throw null; }
-    public static int Compare(System.String? strA, System.String? strB) { throw null; }
-    public static int Compare(System.String? strA, System.String? strB, bool ignoreCase) { throw null; }
-    public static int Compare(System.String? strA, System.String? strB, bool ignoreCase, System.Globalization.CultureInfo? culture) { throw null; }
-    public static int Compare(System.String? strA, System.String? strB, System.Globalization.CultureInfo? culture, System.Globalization.CompareOptions options) { throw null; }
+    public static int CompareString(System.String? strA, System.String? strB) { throw null; }
+    public static int CompareStringIgnoreCase(System.String? strA, System.String? strB) { throw null; }
     public static int Compare(System.String? strA, System.String? strB, System.StringComparison comparisonType) { throw null; }
     public static int CompareOrdinal(System.String? strA, int indexA, System.String? strB, int indexB, int length) { throw null; }
     public static int CompareOrdinal(System.String? strA, System.String? strB) { throw null; }
-    public int CompareTo(object? value) { throw null; }
-    public int CompareTo(System.String? strB) { throw null; }
+    public int CompareToString(System.String? strB) { throw null; }
+    public int CompareToStringIgnoreCase(System.String? strB) { throw null; }
     public static System.String Concat(System.Collections.Generic.IEnumerable<string?> values) { throw null; }
     public static System.String Concat(object? arg0) { throw null; }
     public static System.String Concat(object? arg0, object? arg1) { throw null; }
     public static System.String Concat(object? arg0, object? arg1, object? arg2) { throw null; }
     public static System.String Concat(params object?[] args) { throw null; }
     public static System.String Concat(System.ReadOnlySpan<char> str0, System.ReadOnlySpan<char> str1) { throw null; }
     public static System.String Concat(System.ReadOnlySpan<char> str0, System.ReadOnlySpan<char> str1, System.ReadOnlySpan<char> str2) { throw null; }
     public static System.String Concat(System.ReadOnlySpan<char> str0, System.ReadOnlySpan<char> str1, System.ReadOnlySpan<char> str2, System.ReadOnlySpan<char> str3) { throw null; }
     public static System.String Concat(System.String? str0, System.String? str1) { throw null; }
     public static System.String Concat(System.String? str0, System.String? str1, System.String? str2) { throw null; }
     public static System.String Concat(System.String? str0, System.String? str1, System.String? str2, System.String? str3) { throw null; }
     public static System.String Concat(params string?[] values) { throw null; }
     public static System.String Concat<T>(System.Collections.Generic.IEnumerable<T> values) { throw null; }
     public bool Contains(char value) { throw null; }
     public bool Contains(char value, System.StringComparison comparisonType) { throw null; }
     public bool Contains(System.String value) { throw null; }
     public bool Contains(System.String value, System.StringComparison comparisonType) { throw null; }
     [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
     [System.ObsoleteAttribute("This API should not be used to create mutable strings. See https://go.microsoft.com/fwlink/?linkid=2084035 for alternatives.")]
     public static System.String Copy(System.String str) { throw null; }
     public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count) { }
     public static System.String Create<TState>(int length, TState state, System.Buffers.SpanAction<char, TState> action) { throw null; }
     public bool EndsWith(char value) { throw null; }
+    public bool EndsWithIgnoreCase(char value) { throw null; }
-    public bool EndsWith(System.String value) { throw null; }
-    public bool EndsWith(System.String value, bool ignoreCase, System.Globalization.CultureInfo? culture) { throw null; }
     public bool EndsWith(System.String value, System.StringComparison comparisonType) { throw null; }
+    public bool EndsWithString(System.String value) { throw null; }
+    public bool EndsWithStringIgnoreCase(System.String value) { throw null; }
     public System.Text.StringRuneEnumerator EnumerateRunes() { throw null; }
     public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] object? obj) { throw null; }
     public bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.String? value) { throw null; }
+    public bool EqualsIgnoreCase([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.String? value) { throw null; }
     public static bool Equals(System.String? a, System.String? b) { throw null; }
     public static bool Equals(System.String? a, System.String? b, System.StringComparison comparisonType) { throw null; }
+    public static bool EqualsIgnoreCase(System.String? a, System.String? b) { throw null; }
     public bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.String? value, System.StringComparison comparisonType) { throw null; }
     public static System.String Format(System.IFormatProvider? provider, System.String format, object? arg0) { throw null; }
     public static System.String Format(System.IFormatProvider? provider, System.String format, object? arg0, object? arg1) { throw null; }
     public static System.String Format(System.IFormatProvider? provider, System.String format, object? arg0, object? arg1, object? arg2) { throw null; }
     public static System.String Format(System.IFormatProvider? provider, System.String format, params object?[] args) { throw null; }
     public static System.String Format(System.String format, object? arg0) { throw null; }
     public static System.String Format(System.String format, object? arg0, object? arg1) { throw null; }
     public static System.String Format(System.String format, object? arg0, object? arg1, object? arg2) { throw null; }
     public static System.String Format(System.String format, params object?[] args) { throw null; }
     public System.CharEnumerator GetEnumerator() { throw null; }
     public override int GetHashCode() { throw null; }
     public static int GetHashCode(System.ReadOnlySpan<char> value) { throw null; }
     public static int GetHashCode(System.ReadOnlySpan<char> value, System.StringComparison comparisonType) { throw null; }
     public int GetHashCode(System.StringComparison comparisonType) { throw null; }
     [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
     public ref readonly char GetPinnableReference() { throw null; }
     public System.TypeCode GetTypeCode() { throw null; }
     public int IndexOf(char value) { throw null; }
     public int IndexOf(char value, int startIndex) { throw null; }
     public int IndexOf(char value, int startIndex, int count) { throw null; }
     public int IndexOf(char value, System.StringComparison comparisonType) { throw null; }
+    public int IndexOfIgnoreCase(char value) { throw null; }
-    public int IndexOf(System.String value) { throw null; }
-    public int IndexOf(System.String value, int startIndex) { throw null; }
-    public int IndexOf(System.String value, int startIndex, int count) { throw null; }
     public int IndexOf(System.String value, int startIndex, int count, System.StringComparison comparisonType) { throw null; }
     public int IndexOf(System.String value, int startIndex, System.StringComparison comparisonType) { throw null; }
     public int IndexOf(System.String value, System.StringComparison comparisonType) { throw null; }
+    public int IndexOfString(System.String value) { throw null; }
+    public int IndexOfStringIgnoreCase(System.String value) { throw null; }
     public int IndexOfAny(char[] anyOf) { throw null; }
     public int IndexOfAny(char[] anyOf, int startIndex) { throw null; }
     public int IndexOfAny(char[] anyOf, int startIndex, int count) { throw null; }
     public System.String Insert(int startIndex, System.String value) { throw null; }
     public static System.String Intern(System.String str) { throw null; }
     public static System.String? IsInterned(System.String str) { throw null; }
     public bool IsNormalized() { throw null; }
     public bool IsNormalized(System.Text.NormalizationForm normalizationForm) { throw null; }
     public static bool IsNullOrEmpty([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(false)] System.String? value) { throw null; }
     public static bool IsNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(false)] System.String? value) { throw null; }
     public static System.String Join(char separator, params object?[] values) { throw null; }
     public static System.String Join(char separator, params string?[] value) { throw null; }
     public static System.String Join(char separator, string?[] value, int startIndex, int count) { throw null; }
     public static System.String Join(System.String? separator, System.Collections.Generic.IEnumerable<string?> values) { throw null; }
     public static System.String Join(System.String? separator, params object?[] values) { throw null; }
     public static System.String Join(System.String? separator, params string?[] value) { throw null; }
     public static System.String Join(System.String? separator, string?[] value, int startIndex, int count) { throw null; }
     public static System.String Join<T>(char separator, System.Collections.Generic.IEnumerable<T> values) { throw null; }
     public static System.String Join<T>(System.String? separator, System.Collections.Generic.IEnumerable<T> values) { throw null; }
     public int LastIndexOf(char value) { throw null; }
     public int LastIndexOf(char value, int startIndex) { throw null; }
     public int LastIndexOf(char value, int startIndex, int count) { throw null; }
-    public int LastIndexOf(System.String value) { throw null; }
-    public int LastIndexOf(System.String value, int startIndex) { throw null; }
-    public int LastIndexOf(System.String value, int startIndex, int count) { throw null; }
     public int LastIndexOf(System.String value, int startIndex, int count, System.StringComparison comparisonType) { throw null; }
     public int LastIndexOf(System.String value, int startIndex, System.StringComparison comparisonType) { throw null; }
     public int LastIndexOf(System.String value, System.StringComparison comparisonType) { throw null; }
+    public int LastIndexOfString(System.String value) { throw null; }
+    public int LastIndexOfStringIgnoreCase(System.String value) { throw null; }
     public int LastIndexOfAny(char[] anyOf) { throw null; }
     public int LastIndexOfAny(char[] anyOf, int startIndex) { throw null; }
     public int LastIndexOfAny(char[] anyOf, int startIndex, int count) { throw null; }
     public System.String Normalize() { throw null; }
     public System.String Normalize(System.Text.NormalizationForm normalizationForm) { throw null; }
     public static bool operator ==(System.String? a, System.String? b) { throw null; }
     public static implicit operator System.ReadOnlySpan<char> (System.String? value) { throw null; }
     public static bool operator !=(System.String? a, System.String? b) { throw null; }
     public System.String PadLeft(int totalWidth) { throw null; }
     public System.String PadLeft(int totalWidth, char paddingChar) { throw null; }
     public System.String PadRight(int totalWidth) { throw null; }
     public System.String PadRight(int totalWidth, char paddingChar) { throw null; }
     public System.String Remove(int startIndex) { throw null; }
     public System.String Remove(int startIndex, int count) { throw null; }
     public System.String Replace(char oldChar, char newChar) { throw null; }
     public System.String Replace(System.String oldValue, System.String? newValue) { throw null; }
-    public System.String Replace(System.String oldValue, System.String? newValue, bool ignoreCase, System.Globalization.CultureInfo? culture) { throw null; }
     public System.String Replace(System.String oldValue, System.String? newValue, System.StringComparison comparisonType) { throw null; }
     public string[] Split(char separator, int count, System.StringSplitOptions options = System.StringSplitOptions.None) { throw null; }
     public string[] Split(char separator, System.StringSplitOptions options = System.StringSplitOptions.None) { throw null; }
     public string[] Split(params char[]? separator) { throw null; }
     public string[] Split(char[]? separator, int count) { throw null; }
     public string[] Split(char[]? separator, int count, System.StringSplitOptions options) { throw null; }
     public string[] Split(char[]? separator, System.StringSplitOptions options) { throw null; }
     public string[] Split(System.String? separator, int count, System.StringSplitOptions options = System.StringSplitOptions.None) { throw null; }
     public string[] Split(System.String? separator, System.StringSplitOptions options = System.StringSplitOptions.None) { throw null; }
     public string[] Split(string[]? separator, int count, System.StringSplitOptions options) { throw null; }
     public string[] Split(string[]? separator, System.StringSplitOptions options) { throw null; }
     public bool StartsWith(char value) { throw null; }
+    public bool StartsWithIgnoreCase(char value) { throw null; }
-    public bool StartsWith(System.String value) { throw null; }
-    public bool StartsWith(System.String value, bool ignoreCase, System.Globalization.CultureInfo? culture) { throw null; }
+    public bool StartsWithString(System.String value) { throw null; }
+    public bool StartsWithStringIgnoreCase(System.String value) { throw null; }
     public bool StartsWith(System.String value, System.StringComparison comparisonType) { throw null; }
     public System.String Substring(int startIndex) { throw null; }
     public System.String Substring(int startIndex, int length) { throw null; }
     System.Collections.Generic.IEnumerator<char> System.Collections.Generic.IEnumerable<char>.GetEnumerator() { throw null; }
     System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { throw null; }
     bool System.IConvertible.ToBoolean(System.IFormatProvider? provider) { throw null; }
     byte System.IConvertible.ToByte(System.IFormatProvider? provider) { throw null; }
     char System.IConvertible.ToChar(System.IFormatProvider? provider) { throw null; }
     System.DateTime System.IConvertible.ToDateTime(System.IFormatProvider? provider) { throw null; }
     decimal System.IConvertible.ToDecimal(System.IFormatProvider? provider) { throw null; }
     double System.IConvertible.ToDouble(System.IFormatProvider? provider) { throw null; }
     short System.IConvertible.ToInt16(System.IFormatProvider? provider) { throw null; }
     int System.IConvertible.ToInt32(System.IFormatProvider? provider) { throw null; }
     long System.IConvertible.ToInt64(System.IFormatProvider? provider) { throw null; }
     sbyte System.IConvertible.ToSByte(System.IFormatProvider? provider) { throw null; }
     float System.IConvertible.ToSingle(System.IFormatProvider? provider) { throw null; }
     object System.IConvertible.ToType(System.Type type, System.IFormatProvider? provider) { throw null; }
     ushort System.IConvertible.ToUInt16(System.IFormatProvider? provider) { throw null; }
     uint System.IConvertible.ToUInt32(System.IFormatProvider? provider) { throw null; }
     ulong System.IConvertible.ToUInt64(System.IFormatProvider? provider) { throw null; }
     public char[] ToCharArray() { throw null; }
     public char[] ToCharArray(int startIndex, int length) { throw null; }
-    public System.String ToLower() { throw null; }
-    public System.String ToLower(System.Globalization.CultureInfo? culture) { throw null; }
-    public System.String ToLowerInvariant() { throw null; }
+    public System.String ToLowerCase() { throw null; } // aliases ToLowerInvariant
     public override System.String ToString() { throw null; }
     public System.String ToString(System.IFormatProvider? provider) { throw null; }
-    public System.String ToUpper() { throw null; }
-    public System.String ToUpper(System.Globalization.CultureInfo? culture) { throw null; }
-    public System.String ToUpperInvariant() { throw null; }
+    public System.String ToUpperCase() { throw null; } // aliases ToUpperInvariant
     public System.String Trim() { throw null; }
     public System.String Trim(char trimChar) { throw null; }
     public System.String Trim(params char[]? trimChars) { throw null; }
     public System.String TrimEnd() { throw null; }
     public System.String TrimEnd(char trimChar) { throw null; }
     public System.String TrimEnd(params char[]? trimChars) { throw null; }
     public System.String TrimStart() { throw null; }
     public System.String TrimStart(char trimChar) { throw null; }
     public System.String TrimStart(params char[]? trimChars) { throw null; }
 }
 public abstract partial class StringComparer : System.Collections.Generic.IComparer<string?>, System.Collections.Generic. IEqualityComparer<string?>, System.Collections.IComparer, System.Collections.IEqualityComparer
 {
     protected StringComparer() { }
-    public static System.StringComparer CurrentCulture { get { throw null; } }
-    public static System.StringComparer CurrentCultureIgnoreCase { get { throw null; } }
-    public static System.StringComparer InvariantCulture { get { throw null; } }
-    public static System.StringComparer InvariantCultureIgnoreCase { get { throw null; } }
-    public static System.StringComparer Ordinal { get { throw null; } }
-    public static System.StringComparer OrdinalIgnoreCase { get { throw null; } }
+    public static System.StringComparer Normal { get { throw null; } } // aliases Ordinal
+    public static System.StringComparer IgnoreCase { get { throw null; } } // aliases OrdinalIgnoreCase
     public int Compare(object? x, object? y) { throw null; }
     public abstract int Compare(string? x, string? y);
-    public static System.StringComparer Create(System.Globalization.CultureInfo culture, bool ignoreCase) { throw null; }
-    public static System.StringComparer Create(System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options) { throw null; }
     public new bool Equals(object? x, object? y) { throw null; }
     public abstract bool Equals(string? x, string? y);
     public static System.StringComparer FromComparison(System.StringComparison comparisonType) { throw null; }
     public int GetHashCode(object obj) { throw null; }
     public abstract int GetHashCode(string obj);
 }
 public enum StringComparison
 {
-    CurrentCulture = 0,
-    CurrentCultureIgnoreCase = 1,
-    InvariantCulture = 2,
-    InvariantCultureIgnoreCase = 3,
-    Ordinal = 4,
-    OrdinalIgnoreCase = 5,
+    Normal = 4, // aliases Ordinal
+    IgnoreCase = 5, // aliases OrdinalIgnoreCase
 }
```

## A note on `StringComparison` and `StringComparer`

One notable aspect of this proposal is the complete restructuring of the `StringComparison` and `StringComparer` types. It is important to note that all members of these types will be preserved. The proposal here is to hide all existing fields / static properties and to create new "simpler" names _Normal_ and _IgnoreCase_. Both of these additions alias their ordinal siblings.

> Note: the following paragraphs are speculative. They're based on reasonable lines of thought, but we have not performed the work to draw a conclusive link.

While searching through internal and public sources, we discovered significant usage of the members `StringComparison.InvariantCulture` and `StringComparison.InvariantCultureIgnoreCase`. The usage numbers for these APIs is much higher than would be reasonably expected given their scenario of performing linguistic (somewhat "fuzzy") text matching. This may be explained as an interesting byproduct of having drilled [__CA1305__](https://docs.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1305) (specify `CultureInfo.InvariantCulture` when calling `ToString(IFormatProvider, ...)`) and ["prefer `string.ToUpperInvariant` over `string.ToUpper`"](https://docs.microsoft.com/dotnet/api/system.string.toupper) into developers' muscle memory over the past decades. This can form a positive association that makes developers believe that __"invariant"__ is always an appropriate default to use.

So when rules like [__CA1307__](https://docs.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1307) (pass an explicit `StringComparison` value) are enforced, the developer is now forced to choose between ordinal, invariant, or current-culture. It is not reasonable to expect the average developer to understand the difference between ordinal and linguistic behavior. But it _is_ reasonable for an average developer to think to themselves, "I was told _invariant_ was correct in all other situations where I encountered it. It must also be the appropriate default here.". (It would further be reasonable that this decision may be made via muscle memory, with the developer never consciously realizing that they were presented a choice.)

> This portends a critical usability problem with `StringComparison` and `StringComparer`. The type uses terminology ("invariant") that the developer has become > accustomed to believe is always correct, but in most instances of `StringComparison` and `StringComparer` this misleads developers into making the wrong decision.
> 
> __We must conduct user studies if we wish to validate this conclusion.__

To combat this, this proposal document hides the _Invariant_-named members from `StringComparison` and `StringComparer`. The only two members which are unhidden are _Normal_ (ordinal) and _IgnoreCase_ (ordinal, case-insensitive). For developers who really need linguistic, non-culture-specific behavior, they can continue to use `StringComparison.InvariantCulture` unimpeded, as the field is not deprecated. Or, even better, we should direct them to use APIs like `System.Globalization.TextInfo` and `System.Globalization.CompareInfo`, which provide much finer control over the behavior and can more correctly fulfill the scenario the developer had in mind. (For example, when searching for a substring match in a Word document, you may wish to ignore accents or punctuation, so a substring search for "naive" will also match "naïve".)

## Discussion on this proposal

__Existing compiled assemblies should see no behavioral change on the new runtime.__ Because `string.IndexOf(string)` remains linguistic across runtimes, an existing compiled call to this API will not suddenly see a behavioral change if an old NuGet package is installed into a project targeting a new runtime. On the other hand, if an existing compiled assembly did not intend the call to be linguistic, the latent bug will remain and will be unaddressed by any newer runtime.

__Source code retargeted to the new runtime may see warnings.__ Per the API changes outlined earlier, these warnings would _only_ be for API calls which are linguistic by default but which do not take a `CultureInfo` parameter. The developer may choose to address these warnings, perhaps by using an inbox fixer which can rewrite these as ordinal equivalents. Or they can suppress the warnings in their .csproj file by targeting the specific __SYSLIBxxxx__ warning code we apply to this family.

__Greenfield code shouldn't see the old APIs and shouldn't see any warnings.__ This targets newly-created projects and developers new to .NET. Assuming these developers are guided by Intellisense, they will never fall into the pit of failure that previous generations of .NET developers encountered. If they copy \& paste code from StackOverflow which uses the obsoleted APIs, they may start to see warnings, which the fixers can help address.

Additionally, greenfield code should not be exposed to the concept of `StringComparison` for common scenarios. Developers should be directed to the _-IgnoreCase_ method groups where appropriate. `StringComparison` should be reserved for advanced or less-common scenarios.

This proposal would require changing the Roslyn analyzer rules __CA1307__ and __CA1310__ (and perhaps others) to account for the API changes. We don't want analyzers to fire for code targeting the new API surface.

### Analyzer and fixer behavior

When we obsolete the `string` APIs, we should introduce a fixer that offers to rewrite the calls in terms of the ordinal equivalents. For example, the fixer may offer to change calls from `string.IndexOf(string)` (linguistic) to `string.IndexOfString(string)` (ordinal). The fixer should try to remain consistent with the greenfield code description above and should try to avoid introducing the concept of `StringComparison` into code which doesn't already reference it.

Certain APIs implicitly capture the current culture before performing linguistic textual operations. The analyzer and fixer should make an effort to catch these calls and suggest changing them to ordinal (perhaps linguistic but culture-agnostic?) equivalents where possible. The following is a non-exhaustive list of such APIs and conversions.

* `Array.Sort<string>(string[])`
* `SortedSet<string>..ctor()`
* `Comparer<string>.Default`
* `SortedDictionary<string, ...>..ctor()`
* `Tuple<..., string, ...>.CompareTo`
* any conversion from `string` to `IComparable` or `IComparable<string>`
* any conversion from `T<..., string, ...>` to `IComparable` or `IComparable<>`

> As proposed, this fixer intentionally changes application behavior in an effort to mitigate potential bugs. This may have implications for how we surface the suggestions proposed by the fixer or what gestures developers must make to accept them.

## Drawbacks to this proposal

This proposal values maintaining compatibility for the existing .NET ecosystem, steering developers toward newer APIs rather than modifying the behavior of older APIs. This means that if existing .NET ecosystem components are currently broken, they will remain broken.

Obsoleting existing common APIs may also invalidate existing C# tutorials and introductory courses. For example, in Microsoft's own C# "Hello world" tutorial, [Step 4](https://docs.microsoft.com/dotnet/csharp/tour-of-csharp/tutorials/hello-world?tutorial-step=4) and [Step 6](https://docs.microsoft.com/dotnet/csharp/tour-of-csharp/tutorials/hello-world?tutorial-step=6) both contain calls to APIs this proposal intends to obsolete. It would be a poor experience for a novice developer to encounter a warning so early into their .NET journey. (This code sample focuses on Microsoft's own tutorial pages, but we should be mindful that these types of samples are probably very widespread.)

```cs
/* Sample code from Step 4, containing calls to proposed obsolete methods */

Console.WriteLine(sayHello.ToUpper());
Console.WriteLine(sayHello.ToLower());

/* Sample code from Step 6, containing calls to proposed obsolete methods */

string songLyrics = "You say goodbye, and I say hello";
Console.WriteLine(songLyrics.StartsWith("You"));
Console.WriteLine(songLyrics.StartsWith("goodbye"));

Console.WriteLine(songLyrics.EndsWith("hello"));
Console.WriteLine(songLyrics.EndsWith("goodbye"));
```

As a minor concern, developers working in an interactive debugger (think VS's Immediate window) will not see any obsoletion messages that we put on our public API surface. If they enter `theStr.IndexOf("foo")` into the Immediate window, that will evaluate in a linguistic, current-culture context. This is not strictly a behavioral change from previous runtimes, but it could be slightly confusing to developers who have the old patterns engrained into their muscle memory and who rely on our warnings and fixers for guidance.

Finally, we cannot rely on analyzers and fixers alone to detect when a linguistic, current-culture call is about to be made. If a `string` instance is passed around as `object` and some consumer has code which reads `if (obj is IComparable comparable) { comparable.CompareTo(...); }`, there's not much we can do about this. If it's too unpalatable to change the underlying behavior of `string` itself, then the best we can do is direct people to use a modern API surface and generic collections, avoiding non-generic methods like `Array.Sort(Array)`.

## Future work and ecosystem implications

This work has implications for GUI stacks, such as WPF and WinForms. GUI scenarios often involve performing linguistic, current-culture collation of data, such as alphabetically sorting entries in a dropdown list. It should be expected that WinForms, WPF, and similar code bases will be disproportionately hit with warnings. They have a few options available to them. In descending order of preference:

* Change the call to instead use an API on `System.Globalization.TextInfo` or `System.Globalization.CompareInfo`.
* Change the call to explicitly pass `StringComparison.CurrentCulture[IgnoreCase]`.
* Suppress the warning code project-wide.
