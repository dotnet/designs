# .NET Release Labels

We started out .NET Core with a clean slate and the opportunity to align with modern industry trends and user expectations. We noticed that there was a significant diversity of approaches used by various projects for their releases and we were inspired by them. In retrospect, we picked a good approach but didn't get the labeling right. Fortunately, the labeling is the easiest part to fix. This document proposes a new approach for labeling .NET releases.

## Release timeframes

Today, we have a few release timeframes:

- Pre-release
- Full support
- Maintenance
- End of support

For stable releases (post-GA), we have two support durations:

- Shorter releases, supported for 18 months
- Longer "LTS" releases, supported for 36 months

The shorter releases are released in even years, and the LTS ones in odd years.

## Currently-used labels

Today, we have multiple labels that we apply to releases, as you can see illustrated in the following Visual Studio dialog:

![image](dotnet-release-labels.png)

These labels are attempting to describe the following:

- .NET 5 is a "Current" or shorter-supported release.
- .NET 6 is a "Long-term support" or longer-supported release.

We've heard the feedback that "Current" is very confusing, particularly when it describes a release that isn't the latest one. That's the case in this Visual Studio image. It is easy to agree with that.

## Industry examples

Various projects use various terms to describe release types and support timeframes.

- [Node.js](https://nodejs.org/en/about/releases/) releases have "Current", "Active LTS", and "Maintenance LTS" phases. Even-numbered releases move to these two LTS phases after the "Current" phase. Odd-numbered releases are no longer supported after the "Current" phase.
- [OpenJDK](https://access.redhat.com/articles/1299013) releases are supported for 6 years, at least by Red Hat.
- [Python](https://devguide.python.org/devcycle/#branches) releases have "Maintenance" and "Security" phases. Releases are supported for ~5 years.
- [Red Hat Enterprise Linux (RHEL)](https://access.redhat.com/support/policy/updates/errata/) releases have "Full", "Maintenance", and "Extended life" support phases. Each RHEL release is LTS.
- [Ubuntu](https://wiki.ubuntu.com/Releases) releases have "Current" and "Extended Security" support phases. Release are either regular (with no additional label) or LTS. Every two years, there are 3 regular releases and one LTS release. All currently-supported releases are considered "Current".

## Proposed labels

The industry examples demonstrate that we are using the "LTS" label correctly and the "Current" label incorrectly. The Ubuntu/Canonical model is the closest to our practice.

Labels:

- **Preview** -- Indicates a release is in preview and unsupported. Includes release candidates.
- **Go-Live** -- Indicates a preview or release candidate is supported in production.
- **Latest** -- Indicates that a release is the latest actively supported .NET release (not a Preview or Release Candidate).
- **LTS** -- Indicates a release is supported for the LTS timeframe (3 years) and currently actively supported.
- **Maintenance** -- Indicates that a release is within 6 months of remaining support, with security fixes only.
- **EOL** -- Indicates that the support has ended ("end of life").

Notes:
 - The **Current** label will no longer be used.
 - There will be no specific label for the shorter non-LTS releases.

These labels will show up in multiple places, including the [.NET Download pages](https://dotnet.microsoft.com/download/dotnet), [release notes](https://github.com/dotnet/core/blob/main/releases.md), [releases.json](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json), and Visual Studio.

## In practice

Given this proposal, we will see the following labels used in the following timeframes (with a subset of [releases](https://github.com/dotnet/core/blob/main/releases.md) used as examples).


Now (May 2022):

- .NET Core 3.1 (LTS)
- .NET 5 (EOL)
- .NET 6 (LTS; Latest)
- .NET 7 (Preview)

Just before .NET 7 GA (October, 2022):

- .NET Core 3.1 (Maintenance; LTS)
- .NET 5 (EOL)
- .NET 6 (LTS; Latest)
- .NET 7 (Go-Live; Preview)

At .NET 7 GA (November 2022):

- .NET Core 3.1 (Maintenance; LTS)
- .NET 5 (EOL)
- .NET 6 (LTS)
- .NET 7 (Latest)

Just before .NET 8 GA (October 2023):

- .NET Core 3.1 (EOL)
- .NET 5 (EOL)
- .NET 6 (LTS)
- .NET 7 (Latest)
- .NET 8 (Go-Live; Preview)

At .NET 8 GA (November 2023):

- .NET Core 3.1 (EOL)
- .NET 5 (EOL)
- .NET 6 (LTS)
- .NET 7 (Maintenance)
- .NET 8 (LTS; Latest)

+6 months (May, 2024):

- .NET Core 3.1 (EOL)
- .NET 5 (EOL)
- .NET 6 (Maintenance; LTS)
- .NET 7 (EOL)
- .NET 8 (LTS; Latest)
- .NET 9 (Preview)

At .NET 9 GA (November 2024):

- .NET Core 3.1 (EOL)
- .NET 5 (EOL)
- .NET 6 (EOL)
- .NET 7 (EOL)
- .NET 8 (LTS)
- .NET 9 (Latest)

In some views, we can only show one label. In those cases, we will show the most relevant label, which is the first one shown in each example.

For the [download page](https://dotnet.microsoft.com/en-us/download/dotnet) on the .NET website, the key difference from past practice is the following:

- .NET 7 will show a blank status after .NET 7 GA (but it will have a "Maintenance" status after .NET 8 GA)

A given presentation (like Visual Studio) can print long-form or alternate versions of these labels. That's out of scope of this document.
