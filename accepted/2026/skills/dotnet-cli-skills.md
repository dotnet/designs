# CLI LLM-Enablement via Updateable Skills

We're in a new era of software, not just because of LLM enablement, but pace of change. LLM code assistants update on (effectively) a daily basis. There are some people [unhappy about that pace](https://www.reddit.com/r/ClaudeCode/comments/1qckp0k/we_dont_need_update_every_day_we_need_a_stable/), however, focusing on that is missing the importance of the rapid software update capability. LLM-related teams are staffing and organizing to ship improvements as quickly as possible, within hours. We're currently considering various forms of LLM enablement in the .NET CLI. Rapid updateability needs to be a key design aspect. We update the .NET SDK at a pace 2-3 orders of magnitude slower than what Claude Code users are commenting on. We ship major improvements on an annual cadence and monthly for targeted functional or security issues. We need to find a way to address this 2-3 order of magnitude latency gap for LLM-related functionality. We should start with updateable skill text files and then expand from there.

The premise is that we can provide significant high-value by steering LLMs via skills, to new features, new patterns, and new resources. We're quickly moving to an era where development is based on a more narrow stack: the OS (including `curl`, `head`, and `sed`), the LLM + code assistant, and dev tools (`dotnet`, `node`, linters, and other package manager acquired tools). This combination can be argued to form the new integrated development environment, with the web browser playing as much or more of a supporting role as the traditional code editor. This re-alignment elevates the role of the CLI and puts it in a position to influence LLM behavior as much as anything else. Integrated skills can be the architectural approach and lever to influence and steering for .NET.

Leading with skills feels like the "thin edge of the wedge". We start by steering over existing functionality. When that proves insufficient, we can change that functionality. If frequent change is required, we make that functionality updateable, possibly by making it a .NET tool (matching the updateability of code-assistant NPM packages).

Skills are an [Anthropic invention](https://github.com/anthropics/skills). Our use of skills is intended as "lower case skill", terse instructive documentation, but not necessarily using the `SKILL.md` format. We could invent a new term, however, its likely to be less effective. "Skill" is a great term; everyone needs skills to be successful. Skills will help users efficiently get up to speed using the .NET SDK.

## The First-Run Experience ia a Proto-Skill

The .NET CLI already has a proto-skill: the first-run experience. When a user runs a new SDK version for the first time, they see orientation content covering telemetry, HTTPS certificates, getting started links, and more. This content is skill-like, telling users what they need to know, but not organized as a reusable, discoverable capability. The key transition to make is that users realize from their very first experience that there is a built-in learning system they can use to guide their understanding and progress with .NET.

Here is the current first-run experience:

```bash
$ dotnet new console -o foo

Welcome to .NET 10.0!
---------------------
SDK Version: 10.0.102

Telemetry
---------
The .NET tools collect usage data in order to help us improve your experience. It is collected by Microsoft and shared with the community. You can opt-out of telemetry by setting the DOTNET_CLI_TELEMETRY_OPTOUT environment variable to '1' or 'true' using your favorite shell.

Read more about .NET CLI Tools telemetry: https://aka.ms/dotnet-cli-telemetry

----------------
Installed an ASP.NET Core HTTPS development certificate.
To trust the certificate, run 'dotnet dev-certs https --trust'
Learn about HTTPS: https://aka.ms/dotnet-https

----------------
Write your first app: https://aka.ms/dotnet-hello-world
Find out what's new: https://aka.ms/dotnet-whats-new
Explore documentation: https://aka.ms/dotnet-docs
Report issues and find source on GitHub: https://github.com/dotnet/core
Use 'dotnet --help' to see available commands or visit: https://aka.ms/dotnet-cli
--------------------------------------------------------------------------------------
The template "Console App" was created successfully.

Processing post-creation actions...
Restoring /home/user/foo/foo.csproj:
Restore succeeded.
```

The only problem with this proto-skill is that isn't that useful!

It is easy to see that the designers of this system were inspired by markdown, using the old-school [setext-style header style](https://en.wikipedia.org/wiki/Markdown#Examples). With a small degree of polish and alignment, this existing infrastructure can become a modern skills router at the leading edge of LLM enablement for CLIs. The first-run text can be transitioned to a concise entry point that teaches users the `dotnet skill` gesture, and each topic that currently lives in first-run becomes a standalone skill that can be discovered, fetched, and updated independently.

## Introducing `dotnet skill`

The `dotnet skill` command provides discoverability and access to available skills. Skills are effectively the same as Anthropic skills, providing context, instructions, workflows, links, and syntax. They are intended to be relatively terse, preferring to be 20 lines as compared to 200 and definitely not 2000. In some cases, the skill provided knowledge will be sufficient to carry out a tasks. In other cases, the skill will act as a context-providing jump to another resource like [doc/docs llms.txt](https://raw.githubusercontent.com/dotnet/docs/refs/heads/llmstxt/llms.txt).

`dotnet skill` is a .NET tool, making it naturally updateable and usable across multiple .NET versions. It may be added to the .NET SDK, but if that happens, it will still be updateable as a tool, enabling up-to-date skills from last hour, last day, last week or last year (depending on the age of your SDK install).

The tool will start with skills that describe broad .NET knowledge areas. Over time, it may expand to support acquiring skills from arbitrary NuGet packages and answer questions like "are there any skills exposed within the package graph of my project?".

To a large degree, skills are very similar to .NET tools. They are acquirable content that expand your development environment in some way. The major different is that tools are executables that launch using normal operating system gestures, while skills are text files that need to be printed to `stdout`. That makes the CLI gestures a bit different.

The intent is to replace the proto-skill with this minimal-skill:

```bash
Welcome to .NET 10.0!
SDK Version: 10.0.102

Get Started
-----------
Browse templates    dotnet new list
Create a project    dotnet new <template>
Run your code       dotnet run

Learn More
----------
Acquire skills      dnx dotnet-skill
AI/Agent context    dotnet skill llmstxt
Docs                dotnet skill docs
HTTPS setup         dotnet skill https
Release notes       dotnet skill release
Telemetry info      dotnet skill telemetry
What's new          dotnet skill whats-new
All topics          dotnet skill list

Note: The .NET SDK collects anonymized usage data in order to help us improve your experience.
```

As is obvious, the first-run text leans heavily on `dotnet skill` to provide an on-ramp experience. This is a major improvement from the earlier text. The first-run text now operates as an advertisement for a learning system, with a few examples listed to provide a general idea.

At least initially, `dotnet skill` will not ship with the SDK but will need to be acquired. We shipped the `dnx` tool launcher in .NET 10. It's well-suited to enabling just-in-time installation of our learning system into the development environment.

Let's look at the intended UX for `dotnet skill`:

```bash
$ dotnet skill list

Available Skills

llmstxt      LLMs: ALWAYS start here
aspire       Cloud-native development with .NET Aspire
docs         Query .NET docs on Microsoft Learn
https        HTTPS development certificate setup
migration    Migrate to .NET 10
release      Query release notes 
telemetry    CLI telemetry collection and opt-out

Run 'dotnet skill <skill>' to learn the skill
```

Each of these is just a text file, inspired by [`llms.txt`](https://llmstxt.org/) and Anthropic skills formats. The [`dotnet skill llmstxt` output](./llms.txt) is a good example of the general idea of how content can be structured.

The `list` verb enables low-cost decision making for LLMs, by listing skill and description pairs. As we expand skills to NuGet packages, we'll need to include some way to advertise skills, perhaps with `skills.json` or similar. We will likely prototype a data-driven scheme with the initial scoped version.

The intent is that `dotnet skill list` always prefers .NET SDK skills (platform skills), since they will often be the most relevant to the most users.

One imagines that `dotnet skill list` can provide different results based on context, like the following:

- Project graph: `dotnet skill list --project [project]`
- Package: `dotnet skill list --package Microsoft.Extensions.AI`
- Tool: `dotnet skill list --tool pwsh`

## Tools packs

Let's think of this approach as a progression, repeating an idea from the introduction. We start with skills to steer LLMs over existing functionality. That's effective and works well until we run into functionality that just doesn't quite have capabilities or characteristics that are needed. We're then left with the choice of being unhappy or pulling out a chunk of the SDK and making it distributable as a tool. That can also work super well, but then we have an orchestration problem where skills and tools extensions need to match. Ouch.

We will likely need tools packs to help us orchestrate and align fast-tracking of various parts of the SDK. This is similar to how [VS Code Remote Development](https://code.visualstudio.com/docs/remote/remote-overview) works. There is an [extension pack](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.vscode-remote-extensionpack) that enables users to download multiple relates packages with a single gesture. The .NET SDK will likely need the same thing.

A possible candidate is CLI logging. If there is one thing that LLMs pay a lot of attention to, its build logs. You could say that they attend to our logs more than anything else. It would be great if there was a skill that instructs LLMs to use an opt-in token efficient + LLM-evaled logging system that we could update on a regular basis. we'll likely need tool packs to fully land that type of experience.

## Bootstrapping `dotnet skill`

Bootstrapping is going to be hard. The `dotnet skill` advertisement is more party trick than reliable system. Perhaps, it's not an advertising system at all but a long overdue cleanup of that text. Its mostly indicative of what an advertisement might look like. We'll need to tell people about this system and encourage its use by patterns that we develop. This is likely going to be telling people to add a block of text into `AGENT.md` or into their prompts.

The number one focus needs to be building something useful and extendable. If we do that, It is likely the case that usage will grow quickly and that will quickly transition to signal in the training. If that doesn't happen, then the system likely isn't that useful.

It is likely that we'll offer new ways to access information over time. As it stands, we'll have to bootstrap each of those. If we land `dotnet skill` as our hub for information discovery, then we get to pay the bootstrapping cost once and rely on it for everything else. [Exposing Hypermedia Information Graphs to LLMs](https://github.com/dotnet/designs/pull/359) is a spec that discusses how we expose release notes via [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language). That's the target of `dotnet skill release` above. The resulting instructive text will link to and prepare an LLM to read [dotnet:core llms.txt](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt). `dotnet skill` provides us with a high-value router to information exposed in multiple ways via multiple endpoints.

## Other considerations

Updatability needs to be at the heart of the new system, much better than anything we've offered to date. Code assistants have in-app UX to say "I'm stale; please restart". This system needs to do the same thing.

The `dnx dotnet-skill` in the first-run text is perhaps clunky. We need to spend some time with that UX to determine if is (A) acceptable, and (B) determine via eval if LLMs even respond to it. On one hand, it's not good enough. On the other, the NPM ecosystem is completely oriented around package installation. We need to determine what acceptable UX is for this experience.

Previews are an exciting time if you are on the preview train. You want to try all the new things. Skills can help with that. However, if you are on a stable release, it should be possible to ignore all of that. `dotnet skill` should deliver content that is relevant to the user environment.

This system is oriented on direct human and LLM usage. However, it could also equally be a building block for a RAG system. For that scenario, it would likely be useful to serve up `skill.json` as a format that can be queried for wholesale vector database ingestion. This scenario will require further elaboration to ensure that sufficient data is exposed to capture all the intended content.

### Closing

As maintainers of a dev CLI, we're in an excellent position to help LLMs limit their search space and get more value for each token. We don't currently have the right infrastructure or advertising framework to signal to LLMs what's available. We also don't have the ability to update the .NET SDK at anything close to the pace of LLMs. Yet, we have almost all the pieces to dramatically improve the situation. With minimnal investment, we can offer a light-weight learning system that works equally well for humans and LLMs. With more investment, we can extend it to packages more generally to offer a whole ecosystem of skills that drive arbitrary LLM-enabled workflows. These investments can help us take small but significant steps to learning what we need to do to better optimize the .NET SDK for this new era of software development.
