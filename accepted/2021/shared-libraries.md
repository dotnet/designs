# Enabling shared libraries

Sharing (or single instancing) libraries across multiple apps in a production deployment is one of the most obvious missing capabilities of the .NET Core project. It is a key capability of .NET Framework, with the [global assembly cache](https://docs.microsoft.com/en-us/dotnet/framework/app-domains/gac). The benefit of sharing libraries is to avoid the size (over the wire and on disk) and memory cost of duplicate libraries and to enable servicing/updating them as a single unit.

In particular, the [.NET SDK includes multiple copies of assemblies](https://github.com/dotnet/sdk/issues/16895) and could benefit from a shared library solution. The intent of this design is to provide a generally useful solution while also targeting the SDK needs in particular.

## Design goals

This topic is very complex. It interacts with multiple other systems, such as NuGet and frameworks (like `Microsoft.NETCore.App`). Before diving into complexity, we'll define a set of design goals, including top-level interactions with related concepts.

- Primarily oriented on `PackageReference` and `ProjectReference`. The other (non-mutually-exclusive) existing option is frameworks.
- Consumer can opt to share libraries across apps without requiring the package or project author to participate.
- Compatibility metadata (a new post .NET 6 feature) should be consulted and honored if available.
- Both rid-specific and portable assets/layouts should be supported.
- Low coupling between shared libraries and apps, in terms of the production layout. Desire for non-NuGet coupling. Support for the portable scenario might make that more challenging.
- Shared libraries can be updated without updating apps and vice versa. Doing so must be done according to specific guidance and also accepting risk of breakage.
- Primary mode is maximum sharing. Anything else is an advanced gesture.
- Affordance for optimizing shared assemblies. Primary opportunities would be R2R and PGO and not trimming.

The following are anti-goals.

- No formal machine- or user-global store (like GAC).
- No support for reasoning about equivalence of NuGet packages and frameworks, like `System.Reflection.Metadata`. There is an easy workaround for managing this already.

## First-level thinking

Conceptually, this feature could be modeled as a meta project that references all the app projects with a special kind of build (or other MSBuild target) that identifies and copies all the duplicate compatible libraries (`PackageReference` and `ProjectReference`) across app graphs. The final libraries would need to be recorded in some form of lock file that would then be used as input to the application build to avoid copying the same libraries into the bin directory, and to register their presence in some store (location TBD).

It might be a surprise to see `PackageReference` and `ProjectReference` offered parity behavior for this proposal. We're done a poor job to date by thinking of them as distinct. In fact, we have no substitutability story (there's another design coming for that soon). We can offer significantly more value by including projects not just packages.

There is likely a strong tie-in to the NuGet coherence project that Mikayla and I have been working on. There are at least two tie-ins:

- The compatibility metadata would be useful to inform sharing candidates.
- Shared libraries are effectively the opposite end of the spectrum as Java shading. Perhaps we should consider this end of the spectrum as a formal part of the NuGet coherence project.

## .NET SDK

The .NET SDK is more complicated than the typical app. There are a number of complications to consider:

- While the .NET SDK includes apps, it also includes components (like ASP.NET Core) that are distributed separately from the SDK. 
- In essence, it is a transparent layer cake with the apps being the top layer (or strawberries on top) that can take a dependency on shared libraries from each of the layers. One can imagine each layer taking a dependency on one or more of the layers below it with a > dozen arcs between framework layers and app consumers .
- The inner and bottom layers are frameworks. It's possible we'll need to support frameworks as an input (as opposed to just `PackageReference` and `ProjectReference`) but only for the SDK.
- There is also framework <-> framework duplication. This may not be possible to solve (due to the need to distribute frameworks separately, both from the SDK and each other), and that's probably OK.
- Components are inserted into the SDK today as portable components when they should be TFM- and RID-specific. This is perhaps the biggest issue.

## Final thoughts

This doc was solely written to develop and collect an initial set of thoughts. It isn't a spec. It is also intended to spur further thoughts in others to help us chart a course.
