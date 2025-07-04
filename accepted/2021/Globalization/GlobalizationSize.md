# Globalization and Size on Disk

There are different discussions around the globalization support in the .NET mainly around the level of the globalization functionality and the size on disk that such features consume. There are some random thoughts around what we can do to address the raised issues.
The goal of this document is to list such issues and try to get all parties on the same page agreeing on the problem. The document tries to suggest different ways to address such issues which may help to form the final plan.

### Background

.NET supports different globalization functionality like date and number formatting, calendars, locales data, string collation, string casing, string normalization, Internationalizing Domain Names,...etc.
Almost all of such functionality needs globalization data to exist and code to handle such data to perform the expected operation.

Since .NET Framework v4.0, the framework mainly depends on the underlying operating system to perform such operations. Before v4.0 the framework was used to carry the globalization data and have code to handle such data (e.g. collations algorithms). Carrying the globalization data was a big burden on the framework because of the maintainability and servicing while the OS's and standards (e.g. Unicode/ICU) already providing all what we need.

In .NET Core, we continued with the same strategy. When running on Windows we depended on NLS Win32 APIs and when running on other platforms (e.g. Linux, Mac OS,...etc.) we depended on the [ICU](https://github.com/unicode-org/icu) library.

In .NET 5.0 we went one step further to [support using ICU library](https://docs.microsoft.com/en-us/dotnet/standard/globalization-localization/globalization-icu) when running on Windows and we made this behavior the default one. Also, we have supported the [ICU app-local](https://docs.microsoft.com/en-us/dotnet/standard/globalization-localization/globalization-icu#app-local-icu) feature which allows applications to use a different ICU version than the global system one if needed.

In .NET Core, we have supported the [Globalization Invariant Mode](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md) is the mode that the application which doesn't care much about globalization can be configured with mode. The benefit of enabling this mode is avoiding any dependencies on the system or the ICU. The Invariant mode is mainly introduced to support smaller container images (e.g. Alpine Linux image). Another benefit of the Invariant Mode is guaranteeing consistent behavior of the app when running on different OS's or platforms.

### Concerned Scenarios

Although globalization support is working fine for desktop and server applications, it was concerning in other platforms mainly because it required installing ICU library. The ICU module sizes are concerning and wouldn't be acceptable especially when running on mobile platforms.

```
ICU module sizes from MS-ICU Linux x64 package:

libicudata.so.68.2.0.6    28,320,992
libicui18n.so.68.2.0.6    4,220,936
libicuuc.so.68.2.0.6      2,479,488

Thanks to @filipnavara and @CoffeeFlux providing the following info:

iOS arm64:
icudt.dat 1,713,152
icudt_CJK.dat 1,006,224
icudt_EFIGS.dat 602,096
icudt_no_CJK.dat 1,271,296
libicudata.a 720
libicui18n.a 3,536,472
libicuuc.a 2,286,376

Android arm64:
icudt.dat 1,512,896
icudt_CJK.dat 966,080
icudt_EFIGS.dat 559,568
icudt_no_CJK.dat 1,082,128
libicudata.a 1,116
libicui18n.a 7,105,274
libicuuc.a 4,267,870

Browser WASM:
icudt.dat 1,512,896
icudt_CJK.dat 966,080
icudt_EFIGS.dat 559,568
icudt_no_CJK.dat 1,082,128
libicui18n.a 4,077,896
libicuuc.a 2,510,674

The native ICU bits end up being about 380KB compressed on wasm.

```

- **Mobile Platforms**:
  - **Android**: Although Android OS comes with the ICU libraries which .NET can use, the OEMs can choose to not include such libraries in their device images.
  - **iOS/MacCatalyst**: don't come with an ICU library that .NET can use. .NET had to provide an ICU package to be used in such platforms.
- **WebAssembly**: The clients have to be a small size and need to run inside the browser which will not allow accessing any libraries outside the browser. That means WebAssembly clients need to include an ICU package for globalization support.
- Users of the **Alpine Linux containers** which enable `Globalization Invariant Mode` ask for better globalization support without increasing the image size much.

### Current Status

In .NET 5.0/6.0 we have introduced the ICU app-local feature which allowed the applications to publish any ICU package as part of the app. To solve the size issue of mobile apps and WebAssembly clients, Xamarin team has worked to create size-trimmed ICU packages targeting different mobile platforms and WebAssembly clients. Although this was helpful to move forward, it still takes a lot of effort and needs more to be done. It became clear trimming ICU4C is not easy. It is not as simple as just trimming the data but the ICU code size was big and needs trimming too. Trimming ICU code is not a simple task. The code that thought is isolated and trimmed, caused some problems later as it was used for other parts. Looks full code analysis needed to ensure whatever we trim would be safe and not needed for other parts.

One of the suggested thoughts is to try to enhance the Globalization Invariant mode and try to make it providing better globalization support. We already tracking doing some of this work in .NET 6.0  in the [issue](https://github.com/dotnet/runtime/issues/43774). Looking at the scenarios we want to enhance/support, I am not convinced enhancing the globalization mode is the path we need to pursue. The reason is, to address all concerned scenarios, we need to offer a range of different trimmed data and functionality options. For example, we need to offer packages for one or a group of cultures. Something like European-only cultures, CJK cultures, Bidi Cultures, full list of cultures...etc. Also, we need to provide functionality options e.g. Cultural collation support, Normalization support, IDN support, casing support...etc. If we can do that, the users can have the freedom to select the level of the functionality they want and would be clear the size cost for every choice. The users will have full control over the functionality level and the size cost. To support that, would make sense we follow the same ICU app-local idea and not just try to add more functionality to the Invariant mode. It would be better too to keep the definition of Globalization Invariant Mode clear and not confusing. This mode is for the applications that don't need globalization support and not for anything else.

### Apple OS's

Although we cannot access ICU directly on iOS or MacCatalyst, we may explore the path of using the OS Cocoa APIs directly. If this idea succeed we may consider it on MacOS too and not only for iOS or MacCatalyst.

@filipnavara has done the initial available API scan and here is the info:

```
GlobalizationNative_NormalizeString
- NSString decomposedStringWithCanonicalMapping (form D)
- NSString decomposedStringWithCompatibilityMapping (form KD)
- NSString precomposedStringWithCanonicalMapping (form C)
- NSString precomposedStringWithCompatibilityMapping (form KC)

GlobalizationNative_IsNormalized
- ?? (could be mapped to GlobalizationNative_NormalizeString but inefficient)

GlobalizationNative_WindowsIdToIanaId [Not supported on app-local iOS/WASM ICU]
GlobalizationNative_IanaIdToWindowsId [Not supported on app-local iOS/WASM ICU]

GlobalizationNative_GetTimeZoneDisplayName [Not supported on app-local iOS/WASM ICU]
- NSTimeZone name

GlobalizationNative_GetLocaleInfoString
- NSLocale
  - LocaleString_LocalizedDisplayName => localizedStringForLocaleIdentifier
  - LocaleString_EnglishDisplayName => localeIdentifier / localizedStringForLocaleIdentifier?
  - LocaleString_NativeDisplayName => localeIdentifier / localizedStringForLocaleIdentifier?
  - LocaleString_LocalizedLanguageName => localizedStringForLanguageCode
  - LocaleString_EnglishLanguageName => localizedStringForLanguageCode
  - LocaleString_NativeLanguageName => localizedStringForLanguageCode
  - LocaleString_EnglishCountryName => localizedStringForCountryCode
  - LocaleString_NativeCountryName => localizedStringForCountryCode
  - LocaleString_DecimalSeparator => decimalSeparator
  - LocaleString_ThousandSeparator => groupingSeparator
  - LocaleString_Digits => ?
  - LocaleString_MonetarySymbol => currencySymbol
  - LocaleString_CurrencyEnglishName => localizedStringForCurrencyCode
  - LocaleString_CurrencyNativeName => localizedStringForCurrencyCode
  - LocaleString_Iso4217MonetarySymbol => currencySymbol (?)
  - LocaleString_MonetaryDecimalSeparator => ?
  - LocaleString_MonetaryThousandSeparator => ?
  - LocaleString_AMDesignator => calendarIdentifier -> NSCalendar AMSymbol
  - LocaleString_PMDesignator => calendarIdentifier -> NSCalendar PMSymbol
  - LocaleString_PositiveSign => ?
  - LocaleString_NegativeSign => ?
  - LocaleString_Iso639LanguageTwoLetterName => languageCode
  - LocaleString_Iso639LanguageThreeLetterName => ?
  - LocaleString_Iso3166CountryName => countryCode (?)
  - LocaleString_Iso3166CountryName2 => ?
  - LocaleString_NaNSymbol => ?
  - LocaleString_PositiveInfinitySymbol => ?
  - LocaleString_ParentName => ?
  - LocaleString_PercentSymbol => ?
  - LocaleString_PerMilleSymbol => ?

GlobalizationNative_GetLocaleTimeFormat
- NSDateFormatter?

GlobalizationNative_GetLocaleInfoInt
- NSLocale

GlobalizationNative_GetLocaleInfoGroupingSizes
- ??

GlobalizationNative_GetLocales
GlobalizationNative_GetLocaleName
GlobalizationNative_GetDefaultLocaleName
GlobalizationNative_IsPredefinedLocale
- NSLocale

GlobalizationNative_ToAscii
GlobalizationNative_ToUnicode
- No equivalent API for IDN/Punycode?

GlobalizationNative_GetSortHandle
- return reference to NSLocale

GlobalizationNative_CloseSortHandle
- release reference to NSLocale

GlobalizationNative_GetSortVersion
- ??

GlobalizationNative_CompareString
- [NSString compare:options:range:locale:](https://developer.apple.com/documentation/foundation/nsstring/1414561-compare?language=objc)

GlobalizationNative_IndexOf
- [NSString rangeOfString:options:range:locale:](https://developer.apple.com/documentation/foundation/nsstring/1417348-rangeofstring?language=objc)

GlobalizationNative_LastIndexOf
- same as GlobalizationNative_IndexOf w/ NSBackwardsSearch

GlobalizationNative_StartsWith
- can be implemented trough GlobalizationNative_CompareString?

GlobalizationNative_EndsWith
- can be implemented trough GlobalizationNative_CompareString?

GlobalizationNative_GetSortKey
- wcsxfrm_l?

GlobalizationNative_ChangeCase
- NSString uppercaseStringWithLocale
- NSString lowercaseStringWithLocale

GlobalizationNative_ChangeCaseInvariant
- NSString uppercaseString
- NSString lowercaseString

GlobalizationNative_ChangeCaseTurkish
- Implemented in S.G.N using u_tolower/u_toupper, so easy to replicate

GlobalizationNative_InitOrdinalCasingPage
-  Implemented in S.G.N using u_toupper, so easy to replicate

GlobalizationNative_GetCalendars
GlobalizationNative_GetCalendarInfo
GlobalizationNative_EnumCalendarInfo
GlobalizationNative_GetLatestJapaneseEra
GlobalizationNative_GetJapaneseEraStartDate
- NSCalendar (TBD: details)
```

### ICU4X

It is interesting enough, there is a newly launched open-source project called [ICU4X](https://github.com/unicode-org/icu4x#icu4x----) for introducing globalization support libraries (similar to ICU). ICU4X has the exact goals we need to achieve with our concerned scenarios.

```
ICU4X will provide an ECMA-402-compatible API surface in the target client-side platforms, including the web platform, iOS, Android, WearOS, WatchOS, Flutter, and Fuchsia, supported in programming languages including Rust, JavaScript, Objective-C, Java, Dart, and C++.

The design goals of ICU4X are:
- Small and modular code
- Pluggable locale data
- Availability and ease of use in multiple programming languages
- Written by i18n experts to encourage best practices
```

That is exactly what we need (or what we are trying to achieve).
Eric Erhardt already did some investigation and arranged a meeting with the project committee to learn more about the project and the status of the project. It was very helpful getting in touch and learning more about this project. It is a promising project managed by experts and using the CLDR data which means it still sticking with the standards.

The catch here is ICU4X still in the early stages and still not providing all functionality needed by .NET for globalization support. The project committee is willing to get and prioritize requests from different parties. We already communicated in the meeting what functionality .NET currently using from ICU4C and want to see that supported in ICU4X. For example, collation support is one of the topics we brought to the committee's attention.

### Plan Suggestion

ICU4X is the promising path we should pursue to address all concerned scenarios. We can wait a little bit more to get the needed missing functionality implemented in the project and integrate it to .NET as we did with ICU4C.

- We need to look more at what is currently supported functionality and what is missing so we can have the full list of features we need to get.
- We need to be in touch with the ICU4X committee communicating our requests and understanding when such features can be available.
- Need to look if we can associate some resources to help with that project especially with the missing features we need in the .NET.
- Need to look at the scope of work to integrate ICU4X to .NET runtime.
- Need to look if we can have a process creating different NuGet packages from the ICU4X repo with data and code customization.

.NET 7.0 would be the best to start invest in that and try to start consuming at least the available parts of ICU4X.

### Alternative Plans

Other ideas would be similar to what the ICU4X is trying to do but maybe with scoped level. Here are the options we can try if we didn't go with ICU4X:

- Invest more in ICU4C trimming. That will need spending more resources doing code analysis and figuring out how we may trim the ICU code to the level that can satisfy the size requirements.
- Write a code wrapper around the CLDR or ICU data. So we'll not use ICU code to access the data. This can work for locale data but I don't think that will be a good option for other functionality like collation as the code is more complicated and not easy to re-implement.

Any option we choose here will need to have an automated process to extract the needed data and code and pack it in a NuGet package. Also, whatever plan we consider, would be considered for the next release as this is not trivial work to do for .NET 6.0.
