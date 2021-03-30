# Areas of investigation

**DRAFT**

**PM** [Immo Landwerth](https://github.com/terrajobst)

## [Validation of UTF-8 Strings](validation.md)

- better optimizations, for example when enumerating runes
- it's consistent with other ecosystems but not all of them, i.e. in golang validity of the encoding is a soft requirement
- better safety and reliability

## Reduce memory consumption on a densely-packed machine

- i.e. String/UTF-8 String literals in read-only memory (read-only section of PE file) would allow to share strings between processes

## Performant managed/native interop for UTF-8 strings

- i.e. zero-terminated UTF-8 Strings would allow to pass it directly to many native APIs

## To be discussed

- How can we avoid confusion: "Should I use UTF-8 String or System.String?"
- Should framework duplicate APIs? (create UTF-8 and UTF-16 version)
- Should System.String/char be re-defined and become UTF-8 underneath?
- Should UTF-8 literals be prefixed i.e. u8"foo" or just "bar" which compiler would figure out what type that is? (target typing)
