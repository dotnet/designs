# Requirements

**DRAFT**

**PM** [Immo Landwerth](https://github.com/terrajobst)

## [UTF-8 Strings should be pre-validated](validation.md)

- allow us to do better optimizations, for example when enumerating runes
- it's consistent with other ecosystems
- better safety and reliability

## String/UTF-8 String literals should be in read-only memory (read-only section of PE file)

- allows to share strings between processes

## UTF-8 Strings should be zero-terminated

- allows to pass strings directly to native APIs which frequently expect zero-terminated string

## To be discussed

- How can we avoid confusion: "Should I use UTF-8 String or System.String?"
- Should framework duplicate APIs? (create UTF-8 and UTF-16 version)
- Should System.String/char be re-defined and become UTF-8 underneath?
- Should UTF-8 literals be prefixed i.e. u8"foo" or just "bar" which compiler would figure out what type that is? (target typing)
