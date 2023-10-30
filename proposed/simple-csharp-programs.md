# Simple C# Programs

This speclet defines "Simple C# Programs" and covers what they can do from the user's standpoint. It does **not** get into detailed design points, a deep dive of a particular mechanism, etc. The goal is to drive consensus on the user experience and then walk back towards the right design and implementation.

It is also designed to be incremental. Not everything written here must be implemented. For example, we could ship a version without support for packages/frameworks.

## Motivation

C# has lots of great things going for it, but it falls short in a key area: getting started with the least possible friction.

Lots of developers in the world have experience with languages like JavaScript, Python, and Ruby. In these languages, you typically get started in one of these three ways:

1. Launch a REPL and start typing code and getting results
2. Create a file with the right extension and run the code in that file
3. Use build tooling to create an application and then run that application

If you look at online guides for the above languages, they tend to start people off with (1) and (2) and rarely with (3).

In the C# world, we start people off with (3) with the base tooling, the .NET CLI.

This is problematic because these people **do not expect that they need to do (3) to get started**. The tools don't support their instincts and there is very little information online to support them.

Of course, REPLs are another great way to get started. But they are outside the scope of this document.

## What is a Simple C# Program

A Simple C# Program is this:

* A C# file
* A set of global `using`s inferred by the build environment
* A tool that can run that file / produce a working program
* A way to incorporate libraries and frameworks/sdks into that file and run it
* A way to "graduate" to using a project file

A Simple C# Program is **not** a replacement for project-based C# code or .NET Interactive.

## Simple C# is just C# code

Simple C# Programs are not a dialect of the C# language, like the interactive dialect, CSX. Any code in a Simple C# Program can be built and run in a project-based program.

## User experience walkthrough

Given the following code in a singular `hello.cs` in a directory:

```csharp
WriteLine("Hello World");
```

You can just run it:

```bash
$ dotnet run
Hello World
```

Or with a `#!` directive,

```csharp
#!/path/to/dotnet
WriteLine("Hello World");
```

and running:

```bash
$ ./hello.cs
Hello World
```

When running via `dotnet run` or the `#!` directive, artifacts may be created on disk in the temp folder. They could also be generated in memory.

When running via powershell or CMD on Windows, the invocation may be different, since marking files as executable is different. An alternative on Windows is to also create a file association that allows them to be run when double-clicked.

The `using`s that are normally in a C# project template are implicitly available at design-time. This is accomplished with [Global Using Directives](https://github.com/dotnet/csharplang/blob/main/proposals/GlobalUsingDirective.md) that the build tooling will have available.

The `#!` directive is covered in [Hashbang (#!) support](https://github.com/dotnet/csharplang/issues/3507).

### Building instead of running

You can also build it with `dotnet build`:

```bash
$ dotnet build
$ ls
hello.exe
hello.cs
$ ./hello.exe
Hello World
```

When built, it creates a single executable that it drops in the current working directory.

### Referencing a package

You can also pull in a package (syntax perhaps subject to change):

```csharp
#r "nuget: Newtonsoft.Json"

record Person(string Name);

var json = "{\"name\": \"dotnet bot\"}";
var person = JsonConvert.DeserializeObject<Person>(json);

Console.WriteLine(person.Name);
```

And it'll run after downloading the package:

```bash
$ dotnet run
dotnet bot
```

Or via a `#!` directive,

```csharp
#!/path/to/dotnet
#r "nuget: Newtonsoft.Json"

record Person(string Name);

var json = "{\"name\": \"dotnet bot\"}";
var person = JsonConvert.DeserializeObject<Person>(json);

Console.WriteLine(person.Name);
```

and running:

```bash
$ ./hello.cs
dotnet bot
```

The `#r "nuget:..."` syntax is already supported by C# today in .NET Interactive. It is a shared implementation between C# and F#. The syntax and mechanism could be re-used here.

### Coding against a framework/sdk like ASP.NET Core

You can also reference a framework/sdk (nomenclature up for debate) via a `#!` directive:

```csharp
#!/path/to/dotnet-aspnet

var app = WebApplication.CreateBuilder(args);
app.MapGet("/", () => "Hello World");
await app.RunAsync();
```

The `dotnet-aspnet` gesture indicates two things to the build/run mechanism:

* ASP.NET Core gets loaded and referenced
* Applicable implicit ASP.NET Core `using`s are brought into scope

And you can run it:

```bash
$ dotnet run
Listening on localhost://12345
```

The syntax for coding against a framework/sdk could also be more similar to the `#r "nuget:"` syntax shown earlier instead of being a `#!` directive.

### Arguments also work

The following should be possible:

```bash
$ dotnet run arg1 arg2
```

or

```bash
$ ./hello.cs arg1 arg2`
```

### Running or building a specific file

If you have multiple C# files in the same directory, `dotnet run` and `dotnet build` won't work by default. You'll need to specify the file you wish to run:

```bash
$ dotnet run hello.cs
Hello World
```

or

```bash
$ dotnet build hello.cs
```

`dotnet build` could in theory work here, as it could just build an executable for each file. But symmetry with `dotnet run` is probably preferred.

Multiple arguments should also work, but you'll still have to pass the file name:

```bash
$ dotnet run hello.cs arg1 arg2
```

### TFM chosen at build time

For both `dotnet run` and `dotnet build`, a TFM to build against must be selected.

It will be the latest TFM available in the installed SDK. So if you have the .NET 7 SDK, it will be built and run against `net7`.

There is no ability to select a different TFM.

## `dotnet` command support

Only `dotnet run` and `dotnet build` are supported with Simple C# Programs. All other `dotnet` commands will error out, indicating that you need a project.

## Synthesized project file

Behind the scenes, a likely implementation will involve a synethesized project file. This would then have all of the information to make the `dotnet` commands work, and open us up to allowing more `dotnet` commands in the future.

A synthesized project file could live either in memory or in the temp folder.

## Tooling support in VSCode

One of the most popular editors for people learning C# and .NET, or people who are just doing some lightweight work, is VSCode. There would have to be work in the tooling for C# and VSCode to support loading a loose file and have full support for:

* IntelliSense, navigation, etc.
* Debugging when you have an appropriate `launch.json`

After all, one of the benefits of C# and .NET is great tooling. We'd need to make sure that stays true for VSCode.

Visual Studio tooling experiences are much more difficult to consider. For now, Visual Studio is out of scope.

## Grow-up story

There is eventually a need for a grow-up story. This will involve dropping a project file in the user's current directory.

This could involve a special `dotnet` command that simply drops a new project file (or the one synthesized in a temp folder, if a temp folder is picked). It would be named based on the current directory.

```bash
$ dotnet add project
    Added project 'MyFolder.csproj' to the current workspace
```

This lets you "grow up" into a project by:

* Creating a project file and placing it in the current directory, with a name that matches
* Potentially removing `#!` directives / special directives and converting things to an appropriate SDK attribute or `PackageReference`
* Provides a nice message saying what it did

After this, you need to run your program with `dotnet run` and friends, but by virtue of being a project, you now have the full `dotnet` toolset at your disposal.

`#!` directives wouldn't technically need to be removed. You should still be able to run a single file the same was as before. Having a project file in the current directory shouldn't change that.

## Proposed order of implementation

A way to approach this is to break it up into phases. Only phase 1 has to be implemented first. The rest could come afterwards. Some could never be implemented at all. This is merely a proposed ordering.

### Phase 1 - basic scenario only

A first phase of implementation would be to support everything up until `#r "nuget:...` and coding against a framework/sdk. That would mean:

* Core C# language changes (global `using`s and `#!` directive support)
* Build tooling additions to support global `using`s
* Build tooling additions to erect the right guardrails (e.g., not using `dotnet publish` here).
* `dotnet add project` command support

This would mean that you could write anything that you'd be able to write in today's console app project. You can also "graduate" to a project file to get access to the full range of `dotnet` commands, packages, etc.

### Phase 2 - tooling support in VSCode

One of the most popular tools for people learning C# and .NET, or just doing lightweight work, is VSCode. It would have to support Simple C# Programs with the full range of tooling support:

* IntelliSense, navigation, etc.
* Debugging (assuming you have the right `launch.json` set up)

To support this, the C# experience in VSCode would have to know what to load/run at design-time to power these tools. Since there's no project file to evaluate, it would have to take a different approach.

### Phase 2 - package and framework support

This phase would bring in support for referencing packages and coding against a framewor/sdk:

* `#r "nuget:..."` or similar directive that resolves packages. Also works in tooling, so you get IntelliSense for the package
* `#!/usr/bin/share/dotnet-aspnet` or `#r "framework:..."` support, allowing you to run against ASP.NET Core (`server.cs`)

This would require some design work to arrive at the best syntax and semantics. It is also expected that tooling works when these are incorporated.

### Phase 3 - multiple files

This phase would bring in support for pulling in multiple files for a single program. Imagine a `server.cs` with a `network-utils.cs` file, where `network-utils.cs` is used by `server.cs`.

Some guardrails would have to be built into place.

### Phase 4 - support in Visual Studio

This is not designed, but eventually support for Simple C# Programs would have to make its way into Visual Studio. This would perhaps be a complicated task though, since .NET tooling in Visual Studio more or less assumes you are in a project/solution-based workspace. It could be that we never support Simple C# Programs in Visual Studio.

## Design points / pivots

The following is a non-exhuastive list of user experience considerations that go beyond the basic walkthrough above.

### Where do the artifacts go when building and running?

When using `dotnet run` or a `#!` directive in a Simple C# Program, there's a question of where build artifacts go, because it has to compile the program. There are two primary options:

1. Compile and execute in memory
2. Place artifacts in the temp directory on your machine

There is precedent for (2) in Golang, which means it's probably a viable option. But there is a concern about cleaning the temp folder as well, lest it get too large. If everything is compiled and ran in memory, then this isn't a problem. However, given that `#r "nuget"` in F# and .NET Interactive today works by restoring against a project file in the temp folder, the temp folder approach may be the route taken. Other build-time artifacts could be placed there too.

### Multiple files

We will need to decide if Simple C# Programs support more than one file. In other languages, like JavaScript and F#, you can have several loose files in your directory (or other directories) and include them into your program. For example:

**utils.js**
```js
const chalk = require('chalk')
const currentTime = (new Date().toLocaleTimeString([], { hour12: false })).split(':').join('-')

// Pretty print console logs with Chalk
exports.log = {
  dim: (text) => console.log(chalk.dim(text)),
  success: (text) => console.log(chalk.green(text))
}

// Generates a fileName with current time. e.g. fake-names-19:29:17.json
exports.getFileName = () => `fake-names-${currentTime}.json`
```

**program.js**

```js
const { log, getFileName } = require('../lib/utils')

// use stuff from `utils`
```

Similarly, in F# there is the `#load "path-to-file.fsx"` directive.

This leads to some questions:

* Are multiple file supported? That is, can I define a `server.cs` file that is my web service, and a `data.cs` that handles some data manipulation that is called from code inside of `server.cs`?
* If multiple files are supported, what are the guardrails? How can people know if they made a mistake and fix it?

It's also quite likely that some developers will have several programs all as single C# files in the same directory. That means that `dotnet run` would have to understand this and give an appropriate error message about specifying a specific C# file. However, this means inspecting the files to see if they are being used via `#load` directive (or something similar).

Consider the folowing two subfolders.

Directory One:

```
reboot-server.cs
check-server-status.cs
```

Directory Two:

```
reboot-server.cs
utils.cs
```

In Directory Two, the `utils.cs` file is used by `reboot-server.cs`. A developer may expect to be able to just run `dotnet run` in that directory and have the program execute. But if they are in Directory One, they would have to be explicit about which file they are running.

### Coding against a framework alternative syntax

The walkthrough proposes the `#!` directive `#!/path/to/dotnet-aspnet`, but the .NET CLI doesn't have a command to "pull in the ASP.NET Core references", so it wouldn't map to something like that.

An alternative could be to extend `#r` like so:

* `#r "framework:aspnet"`
* `#r "sdk:aspnet"`
* etc.

This could cleanly separate the "tell the compiler which references to pull in" world from the "short-cut having to use `dotnet`" world like so:

```csharp
#!/path/to/dotnet
#r "framework:aspnet"

var app = WebApplication.CreateBuilder(args);
app.MapGet("/", () => "Hello World");
await app.RunAsync();
```

### `#!` and its semantics

The `#!` directives in the walkthrough all point at `/path/to/dotnet`. This path may be different depending on your development OS (e.g., Fedora).

What this means is that it identifies the .NET environment and implies a default set of references available in the file. However, it does not bring in other things like ASP.NET or other frameworkreferences. It is a contextual, "this is how I run" sort of directive.

It could be extended/changed in a few ways:

* `/path/to/dotnet run` makes it explicit that the command is to run the file
* `/path/to/dotnet-aspnet` is an alternative to `#!aspnet` that brings ASP.NET Core into scope for the file (substitute a different framework/sdk here)

In effect, the `#!` directive's semantics could span a spectrum of "I need the path and command to literally execute this file" towards "I add context that affects the references available to this file".

We'll need to decide if they can affect the set of references available in the file or not.

### Using `#!` to point to `dotnet` or point to `env`

An alternative to `#!/path/to/dotnet` is `#!/usr/bin/env dotnet`. That would mean that any UNIX program could run this file:

```csharp
#!/usr/bin/env dotnet
WriteLine("Hello, world!");
```

However, the downside is that you can't pass any more arguments to the `dotnet` process. It's a matter of portability vs. utility.

### Build output

The walkthrough states that the build output is a single executable file in the current working directory. This is based on the hypothesis that this is the least surprising place to put the executable.

* Is this hypothesis based on reality?
* Is this still true if you pull in a package?

### Nuget reference syntax and naming

The current walkthrough proposed `"#r "nuget:..."` for packages.

Syntax could change:

* Use a `#!r` -style syntax, similar to the `#!` directive
* Use a custom directive like `#nuget`

Naming could change:

* `package` instead of `nuget`

However, that seems unlikely given that the current syntax is already shipping in .NET Interactive.

### Should there be support for a user-specified TFM?

So far, the assumption is that a Simple C# Program will simply execute against the TFM in the SDK it's built from. If you run from a .NET 7 SDK, it targets .NET 7. There's no way to configure that unless you use a project file.

Another alternative is to use a hashbang like `dotnet-script` to support a specific TFM, like so:

```csharp
#!net5
using System;

Console.WriteLine("Hello, world!");
```

This might get tricky though, as it raises some questions:

* Does the directive refer to a target runtime only?
* Can you use features that aren't shipped with the SDK that matches a lower target runtime?

This is, frankly, too complex and probably shouldn't be done. But it's worth listing.

## Prior art

This isn't the first proposal that deals with trying to cut down the number of concepts involved in writing a small C# program. There's some prior art to consider in this space.

### A note on .NET Interactive

.NET Interactive _may_ be able to service these goals, it may also play a big role in getting people started.

But if a JS/Python/Ruby programmer is looking to try out C# for the first time, the way that they know how to start with a language may be not well supported by our tooling.

### A note on F#

With some very real ["Simpsons already did it"](https://knowyourmeme.com/memes/the-simpsons-did-it) energy, we here recognize that F# has already gone down this path with F# scripting and F# Interactive:

**hello.fsx**:

```fsharp
printfn "Hello, World!"
```

and run it:

```bash
$ dotnet fsi hello.fsx
Hello, World!
```

However, the model is based on FSX and F# Interactive, which is not a realistic approach for C# to do. The CSX and C# dialects are subtly different, there is legacy behavior to reconcile, and more.

A "Simple F# Programs" proposal could also be implemented. However, it would require the F# language to support `#!` in non-scripts and global `open`s.

### A note on `dotnet-script`

This proposal is very similar to [`dotnet-script`](https://github.com/filipw/dotnet-script). Some of these similarities are:

* Support for `#!` to make executing scripts easier
* Support for `#r "nuget:..."` in C# scripts

 However, there are some notable differences:

* `dotnet-script` is based on the C# scripting model (CSX) which has subtle differences from C# proper
* `dotnet-script` evaluates code with the C# scripting model (`Microsoft.CodeAnalysis.CSharp.Scripting`)
* `dotnet-script` is a global tool, not an evolution of `dotnet run` and `dotnet build itself`
* `dotnet-script` also comes with its own (awesome) scaffolding
* `dotnet-script` supports specifying the target framework
* `dotnet-script` is also a REPL

Of these differences, the first and second are the most prominent. "Simple C# Programs" are not C# script programs. The C# scripting dialect lets you do several things that C# proper cannot, such as shadowing and method bodies being declarable outside class declarations. C# scripts also cannot declare namespaces.

This proposal does not aim to bring the C# scripting model into `dotnet build`/`dotnet run`. Hence, we can't "just use `dotnet-script`".
