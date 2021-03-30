# Improve UTF-8 support

**DRAFT**

**PM** [Immo Landwerth](https://github.com/terrajobst)

UTF-8 is currently the most popular encoding on the web with [more than 95% of all websites using it](https://w3techs.com/technologies/cross/character_encoding/ranking). Additionally it's used as a default encoding on Linux. When .NET was created there was many character encodings used widely and UTF-8 was not dominant at the time - .NET has chose to use UTF-16. As UTF-8 became more popular it become more obvious that UTF-8 has many memory advantages over other encodings including UTF-16 and therefore its great adoption.

## Benefits of using UTF-8 over UTF-16

- Reduces heap allocations - the amount of memory needed for storing text may be reduced up to 2x compared to UTF-16 encoding
- Improves quality, readability, and maintainability of code - performance oriented apps frequently store UTF-8 strings as byte arrays or workaround the platform limitations in other ways
- Improves integration with subsystems that are UTF-8 or mostly UTF-8 - for example on Linux most of the APIs are UTF-8 strings are by convention - they currently need to transcode from/to UTF-16 - sometimes multiple times - on every API call
- Competitivenes with other platforms that offer UTF-8 primitives - most of the popular languages currently offer some sort of UTF-8 string support while .NET only allows transcoding to/from byte array
- Reduced CPU time for transcoding between UTF-16 and UTF-8 - currently many apps get data as UTF-8 and/or need to produce UTF-8 document output - while transcoding is very fast and in a typical complex app it will not show up very high on the perf metrics it can still add up to couple of percent of CPU cycles

## Drawbacks of using UTF-8

- When you perform heavy text processing or utilize globalization-related features heavily (e.g., a GUI application localized to your native language), UTF-16 is often faster than UTF-8 due to technical details of its implementation
- For string which consist primarily of CJK characters, UTF-8 is less memory-efficient than UTF-16
- WinRT and some Windows APIs only take UTF-16 strings, the integration with them will be worse
- Possible dropping quality, readability and maintainability of code needing strictly UTF-16

These conditions aren't expected to occur frequently in web applications or other application types where TTFB and memory utilization are critical metrics.

## [Scenarios](scenarios/README.md)

## [Areas of investigation](areas-of-investigation/README.md)

## [Designs](designs/README.md)
