# Globalization Support in .NET 5

## Overview
Globalization in the .NET Core space has been handled by taking a dependency on the [icu4c](http://site.icu-project.org/home) library and p-invoking into it from managed code.  This model has been successful for desktop and server modes mostly because they can afford to take on the disk space ICU requires. However, in the size restricted workloads, this is quite a different story as there are differences in platform support and tolerance of taking on a significantly sized dependency.

The purpose of this document is to describe what globalization support, if any, Android, iOS/tvOS, and WebAssembly have and what options there are should ICU not suffice.

## .NET Core ICU dependencies
All of the existing ICU dependencies in .NET Core today are located inside System.Private.CoreLib. .NET Core does not install ICU and relies on the unmanaged implementation to be available on the system.

The existing implementation requires that the ICU version is [50](https://github.com/dotnet/runtime/blob/4480bdbe66c55caceb64fc3477010435ba70c4ec/src/libraries/Native/Unix/System.Globalization.Native/pal_icushim.c#L60) or higher.

The implementation files which reference globalization interop are:

* [System.Private.CoreLib/src/System/Globalization/IdnMapping.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/dnMapping.Icu.cs)
* [System.Private.CoreLib/src/System/Globalization/CalendarData.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/CalendarData.Icu.cs)
* [System.Private.CoreLib/src/System/Globalization/CultureData.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/CultureData.Icu.cs)
* [System.Private.CoreLib/src/System/Globalization/TextInfo.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/TextInfo.Icu.cs)
* [System.Private.CoreLib/src/System/Globalization/CompareInfo.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/CompareInfo.Icu.cs)
* [System.Private.CoreLib/src/System/Globalization/JapaneseCalendar.Icu.cs](https://github.com/dotnet/runtime/tree/87f391001eca716a8db896f5c3855d33fe30aca8/src/libraries/System.Private.CoreLib/src/System/Globalization/JapaneseCalendar.Icu.cs)
* [https://github.com/dotnet/runtime/blob/88cc71b26ba1eb430aaeeebddcd5675b3009f2ee/src/libraries/System.Private.CoreLib/src/System/TimeZoneInfo.Unix.cs#L127](https://github.com/dotnet/runtime/blob/88cc71b26ba1eb430aaeeebddcd5675b3009f2ee/src/libraries/System.Private.CoreLib/src/System/TimeZoneInfo.Unix.cs#L127)

Existing test coverage checks the implementation which is available. It does not have any expected list of data included therefore does not check whether same set of data is available everywhere.

## .NET Globalization Invariant Mode
There is already support for the globalization [invariant mode](https://github.com/dotnet/runtime/blob/master/docs/design/features/globalization-invariant-mode.md) since .NET Core 2.0. This will be the most size effective globalization mode for .NET 5 which can be set by the developers. We are going to use existing logic and tweak it where necessary to allow IL Linker to remove any globalization code from the final app when running in invariant mode.

### Enhanced Invariant Mode
In order to cast a wider net for applications that can use invariant mode, we will incorporate the simple ICU casing tables into dotnet/runtime.  This allows us to move beyond ASCII when it comes to casing comparisons at a very small (3Kb) size cost. 

See the enhanced invariant mode [proposal](https://github.com/dotnet/runtime/issues/30960) for more detail.

## Size Sensitive Platforms
Existing Xamarin platforms allow customers to choose which set of collation data they would like to add to their apps, if any. This still makes sense for .NET 5, as on some platforms the ICU dependency is not available from the system, and we’ll need to bundle it to the final app. We could make this available to everyone, but it’s most likely we will focus only on platforms which are size-sensitive in the .NET 5 time frame.

### Android
Android has historically relied on ICU for some kind of globalization support.  From basically the beginning, `Locale` , `Character`, and some classes in `java.text` were provided in the platform globalization.  If you needed more than that, you could access `libicuuc` directly or choose to bundle another version in your APK.  That was pretty much the story up until API level 23.

In API level 24 and higher, the platform white lists its version of `libicuuc` and also includes a subset of [ICU4J](https://developer.android.com/guide/topics/resources/internationalization#relation) API’s.  It’s important to note that [ICU4C](https://unicode-org.github.io/icu-docs/apidoc/released/icu4c/) (accessing `libicuuc` directly) is not part of the Android NDK.  What this means is that OEM’s are not required to follow any type of rules and they could limit / alter the data files as they see fit.  The potential for undefined behavior and additional testing on our part is very real. 

Consult this [issue](https://github.com/android/ndk/issues/548#issuecomment-395561629) for detailed commentary on the challenges of making ICU an NDK API.

### What Does This Mean For Us?
The good news is that p-invoking from managed code works.  Assuming `libicuuc` continues to be white listed, this should not be a problem.  It would be wise if we explored using the `ICU4J`  API subset that’s exposed in the event they decide to tighten things up.  

To summarize:

* We can p-invoke into the system `libicuuc`.  Globalization for .NET 5 should work normally
* Android 4.3 (API level 18) started bundling ICU 50 and our minimum version requirement is satisfied. 
* OEM’s can potentially make life difficult by altering locales / globalization features
* We should explore trying to interop with the `ICU4J` API and determine what the implications, if any are.

### iOS/tvOS
There is no built-in support for ICU on Apple mobile platforms. For our purposes of having highly compatible globalization mode across all platforms we support, we need to look into bringing ICU to ios/tvOS as well.

System.Globalization.Invariant mode still needs to work without any ICU dependency. How we are going to slice the actual data will be defined later based on the experience and existing ICU tooling.

We are not the first framework to hit this problem. Swift framework is dealing with same problems, and they seem to prefer a solution which drops the ICU dependency, but that still does not look like final [decision](https://forums.swift.org/t/icu-usage-in-swift/20473).

### WebAssembly
The intention is to set invariant globalization mode as the default mode for everyone for WebAssembly, but at the same time allow a fallback to full globalization support when needed. This will come with additional cost, but on the web platform we have a little bit more flexibility how to deploy and share the data.

The ICU library consists from code and data. We need to explore if there is a reasonable way to package the ICU data and store them in the cloud as publicly available data blobs. This could give us flexibility to support the full range of ICU data on demand if sliced into small data sets. We’d still need to ship the ICU implementation, but that also could be bound dynamically using dynamic linking. 

From our earlier ICU work, we do know that libicuuc and libicui18n are dynamically linked to libicudata. We would need to download and make libicudata available before loading ICU. 

## ICU Packaging
Due to lack of ICU on all platform we intend to support for .NET5 we need to look into how to package it for following RIDs

* ios-x64
* ios-arm
* ios-arm64
* tvos-x64
* tvos-arm64
* webassembly (browser mode)

A default build of ICU normally results in over 16 MB of data, and a substantial amount of object code.

The packages have to be split to implementation and data part and ideally the data parts will be also sliced in the way that the developers can select only relevant data for their app if they for example don’t target Chinese market.

### Data Library Customizer (TBD)
There is a [tool](http://www.icu-project.org/docs/demo/datacustom_help.html) built by ICU which allow customizing what kind of data will be included in the final data set. We need to explore if that something we could leverage either for the data islands or even as something using during build process.

Additional documentation about the tool is [here](https://github.com/unicode-org/icu/blob/master/docs/userguide/icu_data/buildtool.md)

## App Local ICU for Windows
Windows teams is working on packaging ICU for .NET needs. It will be available as NuGet package and we could explore to use a similar mechanism for iOS/tvOS packaging (although Windows will contain the whole data file). 

## .NET 5 Work
The public tracking issue is available at [https://github.com/dotnet/runtime/issues/33652](https://github.com/dotnet/runtime/issues/33652)
