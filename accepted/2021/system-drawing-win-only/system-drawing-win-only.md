# Make `System.Drawing.Common` only supported on Windows

**Owner** [Santiago Fernandez Madero](https://github.com/safern)

In the .NET Core 2.x days we created the `Microsoft.Windows.Compatibility`
package which exposes many Windows-only APIs so that more people can migrate to
.NET Core. As part of this, we also ported `System.Drawing.Common`. This was
mainly meant for .NET Core users that still target Windows, however since Mono
already had a cross-platform implementation, we decided to bring that over. The
cross-platform functionality is provided via the native library `libgdiplus`.
Sadly, `libgdiplus` is a community supported library and at this point
effectively unmaintained.

While `libgdiplus` has helped a lot of customers expand to cross-platform it has
also caused some headaches. `System.Drawing.Common` is very Windows driven as it
is mostly a wrapper around GDI+ and is heavily coupled to Windows Forms, which
keeps evolving and diverging from cross-platform graphics. This has caused some
confusion over why some things don't work well on Unix or don't work at all.

The cross-platform implementation of `System.Drawing.Common` is mainly provided
on the native side, via `libgdiplus`. It's effectively a re-implementation of
the parts of Windows that `System.Drawing.Common` is depending on. As you can
imagine, that makes `libgdiplus` a very non-trivial component. It's around 30k
lines of pretty old C code, virtually untested, and lacks a lot of
functionality. `libgdiplus` also has a lot of external dependencies for all the
image processing and text rendering, such as `cairo`, `pango`, and other native
libraries, which makes maintaining and shipping it even more challenging.

Since `System.Drawing.Common` was really designed to be a thin wrapper over
Windows technologies, its cross-platform implementation is a bit like a square
peg in a round hole; it's subpar because it was only designed with Windows in
mind.

Since the inclusion of the Mono cross-platform implementation we spent lot of
time redirecting issues to `libgdiplus` that never got fixed, or helping people
install `libgdiplus` correctly (because it's not part of .NET Core proper). This
is very different from other external dependencies we have taken, for example,
`icu` or `openssl`, which are high-quality libraries, rather than poorly
maintained re-implementation of Windows components. Thus, we have come to
believe that it's not viable to get `libgdiplus` to the point where its feature
set and quality is on par with the rest of the .NET stack.

We have noticed (such as from analysis of NuGet packages) that
`System.Drawing.Common` is used on cross-platform mostly for image manipulation
like QR code generators and text rendering. We haven't noticed heavy graphics
usage as our cross-platform graphics support is very incomplete. For example,
right now some components crash on macOS because the native APIs we use have
been deprecated and are no longer included with recent macOS releases. We have
never gotten a report about that crash; we discovered it ourselves while making
`System.Drawing.Common` trim compatible.

The usages we see of `System.Drawing.Common` in non-Windows environments are
typically well supported with SkiaSharp and ImageSharp.

Thus, we believe it is not worth investing in making `System.Drawing.Common`
work on non-Windows platforms and instead propose we only keep evolving it in
the context of Windows Forms and GDI+.

## Requirements

### Goals

* Consumers of `System.Drawing.Common` will be informed that moving forward,
  it's only supported on Windows, via the [platform compatibility analyzer]
* Cross-platform consumers can reference older versions of the package to
  continue using `System.Drawing.Common` via `libgdiplus`
* We don't have to support usages of `System.Drawing.Common` on operating
  systems other than Windows. We also will not support teams at Microsoft using
  this component on other platforms either. Instead, we'll point people to other
  libraries.

### Non-Goals

* Closing the gap of `System.Drawing.Common` for non-Windows platforms or
  bundling `libgdiplus` as part of the .NET 6 installer

## Stakeholders and Reviewers

* Libraries team
* ML.NET
* Xamarin team

## Design

In the past we decided to include the following disclaimer in our docs:

> The `System.Drawing.Common` NuGet package works on Windows, Linux, and macOS.
> However, there are some platform differences. On Linux and macOS, the GDI+
> functionality is implemented by the `libgdiplus` library. This library is not
> installed by default in most Linux distributions and doesn't support all the
> functionality of GDI+ on Windows on macOS. There are also platforms where
> `libgdiplus` is not available at all. To use types from the
> `System.Drawing.Common` package on Linux and macOS, you must install
> `libgdiplus` separately. For more information, see Install .NET on Linux or
> Install .NET on macOS.
>
> If you can't use System.Drawing with your application, recommended
> alternatives include ImageSharp, SkiaSharp, and Windows Imaging Components.

However, this disclaimer doesn't feel clear enough to steer people towards using
other graphics libraries.

The proposal is to do the following for .NET 6:

1. **Mark the assembly as only being supported on Windows**. That will cause the
   platform compatibility analyzer to warn when consumed from a project that
   isn't targeting net6.0-windows (or marked as Windows-specific).

2. Throw `PlatformNotSupportedException` when trying to load `libgdiplus`
   pointing to an aka.ms link with the reasons behind it and recommending
   stable, well-maintained open-source library alternatives

3. Provide a [runtime config switch](https://docs.microsoft.com/dotnet/core/run-time-config/) named `System.Drawing.EnableUnixSupport`, that when is set to `true` we will no longer throw a `PlatformNotSupportedException` when loading `libgdiplus`.

4. In .NET 7+ all of `Sytem.Drawing.Common` APIs dependent of `libgdiplus` will throw `PlatformNotSupportedException` and the runtime config switch will no longer work.

The challenge is that `System.Drawing.Common` is used by 1st party consumers
such as ML.NET, which uses it for image manipulation and even exposes an API
that returns a Bitmap. At the same time, https://github.com/dotnet/iot is in the
process of moving to use ImageSharp and so far they had a great migration
experience. We need to engage with 1st party customers to see what impact this
proposal will have on them.

***Note:** This only affect `System.Drawing.Common` and would leave
`System.Drawing.Primitives` as-is. That assembly contains primitive types that
don't depend on GDI+, such `Rectangle`, `Point`, and `Size`. Any types in
`System.Drawing.Common` that don't depend on GDI+, will be moved to
`System.Drawing.Primitives`.*

## Q & A

### Will this prevent applications to move to .NET 6?

No, `System.Drawing.Common` is a standalone package that does not ship as part
of the shared framework. For that reason if an application wants to keep using
`System.Drawing.Common` on `Unix` and move to .NET 6, they can use the `6.0.x` package and set a runtime config switch `System.Drawing.UnixSupport` to `true`.

However application should be aware that we will not provide any support for Unix issues starting from .NET 6 and that we will be removing Unix implementation entirely on .NET 7+.

### How will applications migrate to recommended libraries?

We will provide with the right documentation via a breaking change notice and a
document that will contain guidance and samples.

### Does this mean .NET is keeping drawing/image processing code to Windows users?

Not really. The types in `System.Drawing.Common` don't actually implement
drawing, font processing or image parsing. The .NET APIs are merely wrappers
that call into native Windows APIs (namely GDI and GDI+). The cross-platform
implementation of `System.Drawing.Common` is provided by `libgdiplus` which is a
native re-implementation of the underlying Windows APIs. That's why the
experience on other operating systems is subpar and difficult to maintain -- it
amounts to re-implementing parts of Windows.

`System.Drawing.Common`, just like `System.Windows.Forms`, are very thin
wrappers over Windows technologies and weren't designed for cross-platform use
in mind. This means trying to make these technologies work on other platforms
doesn't always work well. And in case of `System.Drawing.Common` the end result
feels like square peg in a round hole.

### What does the usage data of `System.Drawing.Common` reveal?

In order to assess how `System.Drawing.Common` we looked at the API usage from
libraries published to nuget.org.

We looked at member refs to types that begin with `System.Drawing.` and
aggregated the hits by type and member name. We also pulled in the TFM of the
assemblies in question to help categorize the hits. There's a lot of data (there
are a lot of members * TFMs), so instead of just huge raw tables of nonsense,
we'll provide a summary of what we observed. 

If we look at .NET Core family of TFMs, the top APIs are:

* `System.Drawing.Bitmap` (constructor)
* `System.Drawing.Font` (constructor)
* `System.Drawing.Graphics.DrawImage()`/`DrawString()`/`FillRectangle()`
* `System.Drawing.Point(F)`
* `System.Drawing.Rectangle(F)`
* `System.Drawing.Size(F)`

The hits here are in the 40k range, and quickly fall off as we get to
`System.Drawing.Brush` at ~15k

These usages are consistent with drawing/augmenting images scenarios.
Interestingly, `set_SmoothingMode` has fewer than 1k hits, which suggests that
most people are getting less than stellar results from their current usage, and
they're likely to be much happier moving to our suggested solutions, which have
better quality by default.

.NET Standard TFMs have similar usage, but `System.Drawing.Color` shows up at
the top of the hits, along with operations that indicate color calculations are
a top scenario, and the bulk of other auxillary types like point, rectangle,
size, etc. have reduced usage. Overall, .NET Standard usage is slightly less
than .NET Core, which was a surprise to us, and we're not exactly sure what to
make of it. It could suggest that .NET Core usage overall is beginning to
eclipse .NET Standard. Or, it could mean something else entirely, like .NET
Standard libraries tend to be used in different scenarios that are doing less
image manipulation, and more raw color calculations. Finding out, is a lot more
work, and we don't think it helps us here.

Other interesting takeaways:

* These types are used by Xamarin.iOS and MonoAndroid targets, particularly the
  `Rectangle`/`Size`/`Point` types/APIs, although overall usage is in the 13k
  range, and falls off quickly. This suggests a usage pattern other than drawing
  things on images, but it's not clear that this decision has any effect on that
  part of the ecosystem.

* For comparison, the .NET Framework usage of these types is in the 531k range,
  which very similar usage patterns to .NET Core. If you combine "no TFM"
  assemblies, you get an extra almost 200k of hits, so this dwarfs .NET Core
  usage, but that's no surprise.

[platform compatibility analyzer]: https://docs.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer
