# Why validated, immutable UTF-8 strings and spans?

Author: [Levi Broderick](https://github.com/GrabYourPitchforks)

## Consistency with other ecosystems

The following is a brief tour of how strings work in other languages which promote UTF-8 as a first-class construct.

### Swift

In Swift, a [`String`](https://developer.apple.com/documentation/swift/string) is an abstraction over a series of Unicode scalar values (in .NET, `Rune`). Swift strings are _immutable_ (copy on mutation) and _validated_ (enforced by ctors). A string's inner representation is an implementation detail: it can be represented as UTF-8, UTF-16, or UTF-32, and there are APIs to project a string as a sequence of another encoding.

```swift
myString // standard enumeration as grapheme clusters (in .NET, StringInfo's "text elements")
myString.unicodeScalars // returns a series of Unicode scalar values (in .NET, Runes)
myString.utf8 // returns a series of UTF-8 code units (in .NET, bytes)
myString.utf16 // returns a series of UTF-16 code units (in .NET, chars)
```

A [`Substring`](https://developer.apple.com/documentation/swift/substring) is a non-copying, sliced view of a backing `String`. It is not legal to create a substring view over any non-String data structure (like an array) without using unsafe code, as this could subvert string validity checks. The runtime disallows creating a `String` or a `Substring` over ill-formed UTF-\* data, and depending on the API being called the runtime will either return _nil_ (to indicate error) or perform silent fixup.

```swift
let s = "Hello😀" // '😀' char is 4 UTF-8 bytes
print(String(s.utf8.dropFirst(1))); // "ello😀"
print(String(s.utf8.dropLast(1))); // <nil>, since dropping a single byte would result in an ill-formed string
print(Substring(s.utf8.dropLast(1)).utf8.last); // 111 (= 'o'), showing that Substring ctor truncated ill-formed data
```

### Rust

In Rust, a [`std::string::String`](https://doc.rust-lang.org/std/string/struct.String.html) is a mutable, growable (like C++'s `std::vector<>`) array of bytes, which is always guaranteed to contain well-formed UTF-8 data. A `std::string::&str` is a string slice, which may come in mutable or immutable varieties. Mutation will result in a `&str` whose contents continue to be well-formed UTF-8.

```rust
fn main() {
    let hello = "Hello, world!"; // &str pointing to literal data
    println!("{}", &hello[0..5]); // "Hello"
    
    let mut hello2 = String::from(hello); // mutable String
    hello2.make_ascii_uppercase(); // mutate String instance
    println!("{}", &hello2[0..5]); // "HELLO"
    
    let emoji = "😀"; // 4-byte UTF-8 char
    let emojiSlice = &emoji[1..]; // PANIC: index 1 not at a scalar boundary, would make bad UTF-8
}
```

Rust's borrow checker will ensure that nobody can mutate a `String` instance while a `&str` slice into that string exists. This prevents somebody from later mutating the string such that the slice points to an invalid (ill-formed) subsegment of a larger well-formed string.

The Rust language distinguishes between arbitrary byte array slices (`&[u8]`) and slices of UTF-8 data (`&str`). Projecting from a `&str` to a `&[u8]` is free, but projecting from a `&[u8]` to a `&str` (see [`str::from_utf8`](https://doc.rust-lang.org/std/str/fn.from_utf8.html)) incurs validation overhead.

```rust
use std::str;

fn main() {
    let vec = vec![72, 105, 33];
    let mystr = str::from_utf8(&vec).unwrap();
    println!("{}", mystr); // "Hi!"
    
    let vec = vec![255, 105, 33]; // invalid UTF-8
    let mystr = str::from_utf8(&vec).unwrap(); // PANIC
}
```

The conversion from `&[u8]` to `&str` is allocation-free because Rust's borrow checker can ensure that the original vector remains immutable for the duration of the borrowed `&str`'s existence. Allocating equivalents exist for scenarios where the caller must continue to mutate the original vector after creating strings from it.

> Technically, Rust differentiates between _safety invariant_ and _validity invariant_. A safety invariant is a behavior guaranteed by the library when called by safe code. If unsafe code violates a safety invariant, safe code may experience undefined behavior when calling an API. A validity invariant is something known to the compiler and which participates in branch elimination. For example, .NET's `ushort` has a validity invariant of always being in the range `[0 .. 65535]`, so the compiler or JIT can replace `if (my_ushort >= 1000000)` with `if (false)` at compile time, pruning the unreachable code.
> 
> In Rust, [`str` has a safety invariant](https://github.com/rust-lang/rust/issues/71033) - not a validity invariant - of always being valid UTF-8. This means that the compiler treats `str` as a type which contains opaque binary data; but the standard library can (and does!) assume that `str` instances are well-formed, and the standard library may exhibit undefined behavior if this invariant is violated. The following sample shows using unsafe code to violate a safety invariant (ill-formed `str`), then using the library `chars()` method over the corrupt instance to produce a `char` value which violates a validity invariant (`char` greater than `U+10FFFF`, which the compiler assumes cannot happen so replaces the expression with the literal _false_). The result is a `char` instance which is simultaneously greater than `U+10FFFF` and not greater than `U+10FFFF`.
> 
> ```rust
> use std::str;
> 
> fn main() {
>     let vec = vec![244, 159, 128, 128]; // invalid UTF-8
>     let mystr = unsafe { str::from_utf8_unchecked(&vec) };
>     
>     let mut chars = mystr.chars();
>     let firstChar = chars.next().unwrap();
>     println!("firstChar = 0x{:x}", firstChar as u32); // prints 0x11ffff
>     println!("firstChar > 0x10ffff = {}", (firstChar as u32) > 0x10ffff); // prints false
> }
> ```

### Go

In Go, a [`string`](https://blog.golang.org/strings) is an immutable sequence of arbitrary bytes. Strings can be arbitrarily sliced. By convention, Go strings are intended to contain well-formed UTF-8. However, there is no requirement that they do so.

```go
// example taken from https://blog.golang.org/strings
const sample = "\xbd\xb2\x3d\xbc\x20\xe2\x8c\x98"
fmt.Println(sample) // "��=� ⌘"
```

Depending on which string API is being called, ill-formed strings may be fixed up on the fly (using U+FFFD substitution) or may remain ill-formed. Generally speaking, mutation operations which perform inspection of strings beyond a simple "each byte processed in isolation" mechanism will result in the runtime attempting to perform fixup.

```go
func printString(s string) {
    fmt.Println(s)
    fmt.Printf("hex bytes: ")
    for i := 0; i < len(s); i++ {
        fmt.Printf("%x ", s[i])
    }
    fmt.Printf("\n")   
}

func main() {
    printString("Hello\x80\xFFworld!")
    printString(strings.ToUpper("Hello\x80\xFFworld!"))
    printString(strings.ReplaceAll("Hello\x80\xFFworld!", "H", "J"))
}
```

```txt
OUTPUT:
Hello��world!
hex bytes: 48 65 6c 6c 6f 80 ff 77 6f 72 6c 64 21
HELLO��WORLD!
hex bytes: 48 45 4c 4c 4f ef bf bd ef bf bd 57 4f 52 4c 44 21 ; [80 FF] replaced with [EF BF BD] sequences
Jello��world!
hex bytes: 4a 65 6c 6c 6f 80 ff 77 6f 72 6c 64 21 ; [80 FF] not replaced with [EF BF BD]
```

```go
fmt.Println(strings.EqualFold("\x80", "\xFF")) // prints 'true' (OrdinalIgnoreCase-like equality)
```

The Go language differentiates between string slices and byte array slices as distinct types with unique behaviors. Strings are a read-only view of an immutable backing buffer. (Contrast this against .NET, where `ReadOnlySpan<T>` is a read-only view of a potentially mutable backing buffer, like `T[]`.) Arrays are a read+write view of a mutable backing buffer. Projecting between Go `string` and `[]byte` slices is an _O(n)_-complexity copying operation due to the immutability constraints which must be upheld.

### A brief segment on runes

Go popularized the concept of a _rune_ type, which can unambiguously represent any Unicode code point. In practice, their _rune_ is an alias for _int32_. This means that the type itself offers no unique functionality; its primary usage is intended to be a marker type in APIs to indicate that the parameter has special meaning. An analog on the .NET side would be `IntPtr` vs. `nint`, which are both the same type under the covers but which convey somewhat different semantics when used as parameters in a public API.

Other languages, including .NET, take a stricter view. Their primitive Unicode data type is the [scalar](https://www.unicode.org/glossary/#unicode_scalar_value), which is a UTF-\*-agnostic way to represent any Unicode value.

| Language | "Rune" type | Specific meaning |
|---|---|---|
| Swift | [`Unicode.Scalar`](https://developer.apple.com/documentation/swift/unicode/scalar) | A Unicode scalar value (ctor validates) |
| Rust | [`char`](https://doc.rust-lang.org/std/primitive.char.html) | A Unicode scalar value (ctor validates) |
| Go | [`rune`](https://golang.org/pkg/builtin/#rune) | A typedef for _int32_ (non-validating), only used as a convention |
| .NET | [`System.Text.Rune`](https://docs.microsoft.com/dotnet/api/system.text.rune) | A Unicode scalar value (ctor validates) |

## Performance

Since well-formed UTF-8 data must follow a particular pattern, APIs which operate on bytes as if they were UTF-8 must validate this pattern while reading and must enforce some policy if they encounter data which violates this pattern. Consider an API which checks that two UTF-8 strings are equivalent under a case-insensitive comparer. The typical way do to this is to treat each string as a sequence of scalars, then to compare those scalars against each other.

If an API is reading a byte stream which is not known to be valid UTF-8, consider the checks that must be applied whenever it sees a byte _b_ which matches the pattern `0b_1110_xxxx`:

 1. Is there more data in the stream? If not, _violation_.
 2. Read the next byte and store into _c_. If _c_ is not in the range `[80..BF]`, _violation_.
 3. If _b_ is `E0` and `c < A0`, _violation_.
 4. If _b_ is `ED` and `c > 9F`, _violation_.
 5. Is there more data in the stream? If not, _violation_.
 6. Read the next byte and store into _d_. If _d_ is not in the range `[80..BF]`, _violation_.
 7. Bit-twiddle _b_, _c_, and _d_ to reconstruct the original scalar value.

These checks must occur for _every single value_ that's being read from the underlying stream. And if you have a switch statement or a dictionary, these checks occur _multiple times per string_, as the query string may be compared against multiple candidates until an appropriate match is found.

Contrast this against an API which can assume it's operating against data known to be well-formed UTF-8. When it sees a byte _b_ which matches the pattern `0b_1110_xxxx`, its logic is much simpler and branch-free:

 1. Read the next byte and store it into _c_. (The byte is known to exist and to be a valid first continuation byte with respect to _b_.)
 2. Read the next byte and store it into _d_. (The byte is known to exist and to be in the range `[80..BF]`.)
 3. Bit-twiddle _b_, _c_, and _d_ to reconstruct the original scalar value.

In fact, on x86, this is only 6 instructions total.

```asm
; assume ecx := 'b', a zero-extended byte which is known to have the pattern 0b1110_xxxx
; assume rcx := pointer to where 'b' was obtained from
shl ecx, 12
movzx eax, byte ptr [rcx + 1] ; eax := temp register
shl eax, 6
add ecx, eax
movzx eax, byte ptr [rcx + 2]
lea ecx, [ecx + eax - E2080h] ; ecx := reconstructed scalar value
```

### Enumerating scalar values

As mentioned above, any string comparison operation (aside from a simple binary comparison) begins by enumerating the scalar values from the UTF-\* input stream. For non-ASCII text in particular, avoiding validation during enumeration has a measurable impact.

> In this and other below tables, the _Corpus_ column corresponds to a file from [Project Gutenberg](https://www.gutenberg.org/).
> 
> * __11.txt__ - entirely ASCII
> * __11-0.txt__ - mostly ASCII with some non-ASCII characters (e.g., smart quotes, em-dashes, accents)
> * __25249-0.txt__ - Chinese text (mostly 3-byte UTF-8 sequences)
> * __30774-0.txt__ - Cyrillic text (mostly 2-byte UTF-8 sequences, with ASCII spacing and punctuation, and some 3-byte punctuation)
> * __39251-0.txt__ - Greek text (mostly 2-byte sequences, with ASCII spacing and punctuation, and some 3-byte punctuation)

The table below shows the performance of iterating over all scalar values in a UTF-8 sequence. The baseline measurement uses the existing `Rune.DecodeUtf8` API, which takes a `ROS<byte>` as input and must perform validation. The variable measurement uses an equivalent code which assumes the input is well-formed and thus skips validation.

|                   Method |      Corpus |     Mean |   Error |  StdDev | Ratio | Code Size |
|------------------------- |------------ |---------:|--------:|--------:|------:|----------:|
|   **IterateRunesValidating** |    **11-0.txt** | **320.8 μs** | **3.96 μs** | **3.70 μs** |  **1.00** |     **429 B** |
| IterateRunesAssumeValid  |    11-0.txt | 327.3 μs | 1.99 μs | 1.56 μs |  1.02 |     219 B |
|                          |             |          |         |         |       |           |
|   **IterateRunesValidating** |      **11.txt** | **284.0 μs** | **2.14 μs** | **2.00 μs** |  **1.00** |     **429 B** |
| IterateRunesAssumeValid  |      11.txt | 266.0 μs | 2.45 μs | 2.04 μs |  0.94 |     219 B |
|                          |             |          |         |         |       |           |
|   **IterateRunesValidating** | **25249-0.txt** | **287.9 μs** | **2.68 μs** | **2.24 μs** |  **1.00** |     **429 B** |
| IterateRunesAssumeValid  | 25249-0.txt | 203.9 μs | 1.22 μs | 1.02 μs |  0.71 |     219 B |
|                          |             |          |         |         |       |           |
|   **IterateRunesValidating** | **30774-0.txt** | **217.9 μs** | **1.52 μs** | **1.35 μs** |  **1.00** |     **429 B** |
| IterateRunesAssumeValid  | 30774-0.txt | 182.5 μs | 1.16 μs | 1.09 μs |  0.84 |     219 B |
|                          |             |          |         |         |       |           |
|   **IterateRunesValidating** | **39251-0.txt** | **310.1 μs** | **2.42 μs** | **2.02 μs** |  **1.00** |     **429 B** |
| IterateRunesAssumeValid  | 39251-0.txt | 267.1 μs | 1.05 μs | 0.98 μs |  0.86 |     219 B |

### Transcoding UTF-8 to UTF-16

Transcoding between UTF-\* representations is normally a two-step process: (1) count the number of code units (chars or bytes) that would result from the transcoding; then (2) perform the conversion into a destination buffer of appropriate capacity. Input validation is performed in both steps. (For cases where the destination is known to be large enough to contain any possible worst-case output, the counting step can be skipped.)

The table below shows the performance of counting how many UTF-16 chars would result from an input UTF-8 sequence. The baseline measurement uses the existing `Encoding.UTF8.GetCharCount` API, which takes a `ROS<byte>` as input and must perform validation. The variable measurement uses an equivalent code which assumes the input is well-formed and thus skips validation.

|                              Method |      Corpus |      Mean |     Error |    StdDev | Ratio |
|------------------------------------ |------------ |----------:|----------:|----------:|------:|
|            **EncodingUtf8GetCharCount** |    **11-0.txt** | **23.711 μs** | **0.3899 μs** | **0.3456 μs** |  **1.00** |
| EncodingUtf8GetCharCountAssumeValid |    11-0.txt |  9.876 μs | 0.0427 μs | 0.0334 μs |  0.42 |
|                                     |             |           |           |           |       |
|            **EncodingUtf8GetCharCount** |      **11.txt** |  **3.116 μs** | **0.0164 μs** | **0.0145 μs** |  **1.00** |
| EncodingUtf8GetCharCountAssumeValid |      11.txt |  3.133 μs | 0.0151 μs | 0.0126 μs |  1.01 |
|                                     |             |           |           |           |       |
|            **EncodingUtf8GetCharCount** | **25249-0.txt** | **39.604 μs** | **0.6143 μs** | **0.5746 μs** |  **1.00** |
| EncodingUtf8GetCharCountAssumeValid | 25249-0.txt | 10.318 μs | 0.0657 μs | 0.0583 μs |  0.26 |
|                                     |             |           |           |           |       |
|            **EncodingUtf8GetCharCount** | **30774-0.txt** | **50.301 μs** | **0.4709 μs** | **0.4405 μs** |  **1.00** |
| EncodingUtf8GetCharCountAssumeValid | 30774-0.txt |  5.818 μs | 0.0495 μs | 0.0413 μs |  0.12 |
|                                     |             |           |           |           |       |
|            **EncodingUtf8GetCharCount** | **39251-0.txt** | **91.980 μs** | **1.0060 μs** | **0.9410 μs** |  **1.00** |
| EncodingUtf8GetCharCountAssumeValid | 39251-0.txt |  7.533 μs | 0.0996 μs | 0.0932 μs |  0.08 |

### Performance impact of upfront validation

The design of a validating Utf8String type should confer __zero performance overhead in the common cases__. There should be three common ways of creating a Utf8String instance.

1. When creating _from a string or a char sequence_, the UTF-16 -\> UTF-8 transcoding process is guaranteed to produce well-formed output. Any such ctor would skip additional validation.

2. When creating _from a Utf8StringBuilder or similar builder object_, no validation need take place. Any builder object can guarantee as an implementation detail that its buffers are already well-formed UTF-8.

3. When creating _from arbitrary i/o bytes_, validation takes place. Applications that interpret raw i/o streams as structured data (whether that's UTF-8, an object model, or something else) really should be performing data validation anyway as part of reliability and security hardening. The validating ctor performs this check on behalf of the caller.

   > If an application knows the raw byte data is provided by a trusted source, or if it has already separately performed validation (e.g., Kestrel having already examined the request line), the caller can invoke one of Utf8String or Utf8Span's non-validating ctors around the arbitrary byte buffer. This dual safe/unsafe API surface matches the API shape of Swift and Rust.

Concatenating multiple Utf8String instances together is a non-validating operation since the concatenation of valid subsequences is already guaranteed to be a valid sequence. The only overhead is the normal _O(n)_ memcpy operation.

Substringing Utf8String instances (or slicing Utf8Span instances) is an _O(1)_ operation to check validity. Assuming the underlying buffer is valid, the only checks that need take place are ensuring that the start and end of the subsequence didn't occur in the middle of a multi-byte sequence. The following shows the validation code for `Utf8String.Substring` and `Utf8Span.Slice`.

```cs
public Utf8String Substring(int offset, int length)
{
    // normal out-of-bounds logic
    if (NotInBounds(offset, length)) { /* throw */ }

    // takes advantage of Utf8String being null-terminated
    if ((sbyte)Unsafe.Add(ref this._firstByte, offset) < -64
        || (sbyte)Unsafe.Add(ref this._firstByte, offset + length) < -64)
    {
        /* throw - attempting to create an illegal slice */
    }

    return SubstringCore(offset, length); // all checks passed!
}

public Utf8Span Slice(int offset, int length)
{
    // normal out-of-bounds logic
    if (NotInBounds(offset, length)) { /* throw */ }

    // since spans aren't guaranteed null-terminated, we need
    // to check that we're not going to buffer overrun before
    // we check for the presence of a continuation byte.
    if (offset < this.Length
        && ((sbyte)Unsafe.Add(ref this._ptr, offset) < -64
            || (offset + length < this.Length && (sbyte)Unsafe.Add(ref this._ptr, offset + length) < -64)))
    {
        /* throw - attempting to create an illegal slice */
    }

    return SliceCore(offset, length); // all checks passed!
}
```

### Raw measurements of .NET 5's validation routines

Recently, there has been ample discussion in the community regarding vectorizing UTF-8 validation logic into SIMD-enabled, branchless logic. See [\[1\]](https://lemire.me/blog/2018/05/16/validating-utf-8-strings-using-as-little-as-0-7-cycles-per-byte/), [\[2\]](https://lemire.me/blog/2018/10/19/validating-utf-8-bytes-using-only-0-45-cycles-per-byte-avx-edition/), and [\[3\]](https://lemire.me/blog/2020/10/20/ridiculously-fast-unicode-utf-8-validation/). .NET 5's built-in UTF-8 validation routine offers very competitive performance. Below is a table showing raw performance numbers of `Encoding.UTF8.GetCharCount(byte[])` for two different inputs: _twitter.json_, a 617 KB test file [from the _simdjson_ repo](https://github.com/simdjson/simdjson/blob/ad37651726f5a12a387fbd1fc626a16c2cd62174/jsonexamples/twitter.json); and a 617 KB file containing all-ASCII text.

|              Method |     Mean |    Error |   StdDev |
|-------------------- |---------:|---------:|---------:|
|       ValidateAscii | 12.23 μs | 0.077 μs | 0.072 μs |
| ValidateTwitterJson | 43.65 μs | 0.201 μs | 0.178 μs |

The _twitter.json_ test file is unique in that it contains data from many different languages, many of which use different average byte counts for each encoded symbol, all in a single file. In practice, standalone string instances will usually be confined to a single language, which means that we can potentially leverage optimizations resulting from anticipating repeated patterns within a single string instance.

The table below compares the relative throughput of .NET 5's validation implementation on my test box (AMD 3950X, 3.50 GHz) against the numbers provided by [the Keiser \& Lemire research paper (PDF)](https://arxiv.org/pdf/2010.03090.pdf).

| Implementation | Processor               | Throughput (_twitter.json_) | Throughput (ASCII) |
|--------------- |------------------------ |----------------------------:| ------------------:|
|         .NET 5 | AMD 3950X, 3.50 GHz     |                 14.5 GB / s |        51.7 GB / s |
|    K\&L lookup | AMD Zen 2, 3.40 GHz     |                   28 GB / s |          66 GB / s |
|    K\&L lookup | Intel Skylake, 3.70 GHz |                   24 GB / s |          59 GB / s |

.NET 5's UTF-8 validation implementation SIMD-optimizes the ASCII paths but does not SIMD-optimize the non-ASCII paths. Additionally, .NET 5's validation routine does not take advantage of the 256-bit AVX2 instruction set, as there was concern that this could downclock the CPU and impact other critical application scenarios. Finally, .NET 5's validation routine also computes the number of UTF-16 code units that would result from the validated UTF-8 string, which is technically beyond the scope of the Keiser \& Lemire research benchmarks. If the .NET code paths were split so that validation could be performed independently of analysis, we'd expect to win back several percentage points of performance.

If profiling data demonstrates that UTF-8 validation performance is impactful, we can investigate full SIMD vectorization of the .NET UTF-8 validation logic.

## Safety and reliability

### String splitting and data loss

In .NET, it is not common for developers to have to worry about ill-formed string instances. When data comes in via i/o, it must go through one of the __System.Text.Encoding__ APIs, and these APIs guarantee that the resulting string instance will contain well-formed UTF-16 data, regardless of whether the input bytes were themselves well-formed. Similarly, when writing strings back out via i/o, the __Encoding.GetBytes__ method will guarantee that the resulting byte stream is well-formed for whatever UTF-\* is in use.

If a .NET application does end up with an ill-formed string in memory, it's almost certainly caused by passing incorrect parameters to `string.Substring`, where the original string was well-formed but a substring boundary occurs in the middle of a surrogate pair. Perhaps the most naïve way is to truncate overlong input to a constant length, usually to append "..." or a newline for writing to a file.

```cs
// This sample shows incorrect code (may split in the middle of a surrogate pair)
public void WriteWithNewlines(string value, Stream output)
{
    while (value.Length > 72)
    {
        output.Write(Encoding.UTF8.GetBytes(value.Substring(0, 72) + Environment.NewLine));
        value = value.Substring(72);
    }
    output.Write(Encoding.UTF8.GetBytes(value));
}
```

Since the System.Text.Encoding APIs will always generate well-formed output, the resulting i/o stream contains well-formed UTF-8 bytes. However, if any splits happened in the middle of a surrogate pair, some data will be lost during the conversion.

(There are other ways to generate ill-formed strings, but these usually involve manipulating chars directly, which isn't very common outside of serialization scenarios.)

In UTF-16, it is extremely uncommon to encounter surrogate pairs in typical enterprise scenarios. The most common usage of UTF-16 surrogate pairs is for emoji characters and dingbats. Rarely-used languages may also require use of UTF-16 surrogate pairs. But the Unicode consortium takes care to ensure that the vast majority of the world's written languages fit cleanly into the Basic Multilingual Plane (BMP), which means no surrogate pairs are needed. Assuming most enterprise apps don't need to deal with emoji or rarely-used languages, it's likely the majority of developers have never encountered this surrogate-splitting problem before. (Devs who write public-facing web apps which allow arbitrary user content are more likely to have encountered this.)
 
In UTF-8, it is much more common to encounter multi-byte sequences. Any language that includes characters beyond basic unaccented \[a-z\] will spill over into multi-byte sequences. This implies that if UTF-8 strings become mainstream, it will be common (particularly in non-English scenarios) for developers who perform truncation in the manner shown above to encounter data loss.

Requiring UTF-8 strings to contain only well-formed data (and not arbitrary slices within multi-byte sequences) can help prevent data loss by throwing an exception whenever an illegal split is made. These exceptions can be debugged by the developer to help pinpoint the exact error location, saving them time from investigating silent after-the-fact data loss.

### Preventing bad data from violating app-wide invariants

In addition to data loss mentioned above, allowing ill-formed UTF-8 could break invariants that most applications depend on. Consider two different Utf8String instances _a_ and _b_, put into a dictionary.

```cs
Debug.Assert(a != b);
var dict = new Dictionary<Utf8String, ...>();
dict[a] = /* ... */;
dict[b] = /* ... */;
Debug.Assert(dict.Count == 2);
```

The application needs to interoperate with another component which uses _string_ instead of _Utf8String_, so it converts these instances to strings and shoves them into a new dictionary instance.

```cs
Debug.Assert(a != b);
var dictStr = new Dictionary<string, ...>();
dictStr[a.ToString()] = dict[a];
dictStr[b.ToString()] = dict[b];
Debug.Assert(dict.Count == 2); // May fail if a.ToString() and b.ToString() result in the same string!
```

Now, two different components within the same application may look at the same data but disagree on what exactly that data means. This can also occur across applications within the same ecosystem: a web application might believe that two usernames are distinct, but the backend database may believe they're identical and combine them into a single entry. In such a distributed system, this can occur if somebody projects a malformed Utf8String as a ROS\<byte\> and attempts to send it as-is across the wire.

> This isn't merely theoretical. As of this writing, MITRE's CVE database contains 218 entries tracking security vulnerabilities caused by applications mishandling UTF-\* data. (See the two search queries [\[1\]](https://cve.mitre.org/cgi-bin/cvekey.cgi?keyword=utf) and [\[2\]](https://cve.mitre.org/cgi-bin/cvekey.cgi?keyword=utf8) and remove duplicates.) See also [Unicode Technical Report \#36: Unicode Security Considerations](https://www.unicode.org/reports/tr36/#UTF-8_Exploit).
> 
> In fact, UTF-8 vulnerabilities in applications are so prevalent that MITRE even [has a category classification for it](https://capec.mitre.org/data/definitions/80.html) in their database of common attack patterns.

## Treating the UTF-8 buffer as raw bytes

That _Utf8String_ and _Utf8Span_ are well-formed should not preclude developers from arbitrarily slicing the underlying buffer when the data is projected as raw byte data. In those cases, the data is referenced by a `ReadOnlyMemory<byte>` or a `ReadOnlySpan<byte>`, and those types don't try to perform any validation under the covers.

For example:

```cs
Utf8String str = new Utf8String("€12,95"); // '€' = three UTF-8 bytes ([ E2 82 AC ])
Console.WriteLine(str.Length); // = 8 bytes ([ E2 82 AC 31 32 2C 39 35])
Utf8String slicedStr = str.Substring(1); // throws an exception since would illegally slice [ E2 82 AC ]
Utf8Span slicedSpan = str.AsSpan().Slice(1); // throws an exception since would illegally slice [ E2 82 AC ]
ReadOnlySpan<byte> slicedBytes = str.AsByteSpan().Slice(1); // OK
Console.WriteLine(slicedBytes.Length); // = 7 bytes ([ 82 AC 31 32 2C 39 35 ])
```
