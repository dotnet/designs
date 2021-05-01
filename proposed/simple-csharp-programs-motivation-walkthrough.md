# Simple C# Programs

This speclet defines "Simple C# Programs" and covers what they can do from the user's standpoint. It does **not** get into detailed design points, a deep dive of a particular mechanisms, etc. The goal is to drive consensus on the user experience and then walk back towards the right design and implementation.

It is also designed to be incremental. Not everything written here must be implemented. For example, we could ship a version without support for packages/frameworks.

## Motivation

C# has lots of great things going for it, but it falls short in a key area: getting started with the least possible friction.

Lots of developers in the world have experience with languages like JavaScript, Python, and Ruby. In these languages, you typically get started in one of these three ways:

1. Launch a REPL and start typing code and getting results
2. Create a file with the right extension and run the code in that file
3. Use build tooling to create an application and then run that application

If you look at online guides for the above languages, they tend to start people off with (1) and (2) and rarely with (3).

In the C# world, we start people off with (3) with the base tooling, the .NET CLI.

This is problematic, because these people **do not expect that they need to do (3) to get started**. The tools don't support their instincts and there is very little information online to support them.

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

Given the following code in `Hello.cs`:

```csharp
WriteLine("Hello World");
```

You can just run it:

```bash
$ dotnet run
Hello World
```

Or with a shebang,

```csharp
#!/usr/bin/shared/dotnet
WriteLine("Hello World");
```

and running:

```bash
$ ./hello.cs
Hello World
```

The `using`s that are normally in a C# project template are implicitly available at design-time. This is accomplished with [Global Using Directives](https://github.com/dotnet/csharplang/blob/main/proposals/GlobalUsingDirective.md) that the build tooling will have available.

The shebang is covered in [Shebang (#!) support](https://github.com/dotnet/csharplang/issues/3507).

### Building instead of running

You can also build it with `dotnet build`:

```bash
$ dotnet build
$ ls
Hello.exe
$ ./Hello.exe
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

Or via a shebang,

```csharp
#r! /usr/bin/shared/dotnet
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

You can also reference a framework/sdk (nomenclature up for debate) via a shebang:

```csharp
#!/usr/bin/share/dotnet-aspnet

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

The syntax for coding against a framework/sdk could also be more similar to the `#r "nuget:"` syntax shown earlier instead of being a shebang.

## `dotnet` command support

Only `dotnet run` and `dotnet build` are supported with Simple C# Programs. All other `dotnet` commands will error out, indicating that you need a project.

## Grow-up story

There is eventually a need for a grow-up story. What's interesting is that this can, in theory, go one of two ways:

1. You "graduate" to using projects, like every other .NET developer
2. You continue to create loose files, but you instead organize them in meaningful folders

The first option is well-understood today, but could involve a special `dotnet` command that _adds_ a project rather than wipes out your existing directory, and converts special directives in your file:

```bash
$ dotnet add project
    Added project 'MyFolder.csproj' to the current workspace
```

This lets you "grow up" into a project by:

* Creating a project file and placing it in the current directory, with a name that matches
* Potentially removing shebangs / special directives and converting things to an appropriate SDK attribute or `PackageReference`
* Provides a nice message saying what it did

After this, you need to run your program with `dotnet run` and friends, but by virtue of being a project, you now have the full `dotnet` toolset at your disposal.

Shebangs wouldn't technically need to be removed. You should still be able to run a single file the same was as before. Having a project file in the current directory shouldn't change that.

The second option has potentially wild implications across our entire build tools and IDE tooling stack. It would require us to fundamentally rethink how .NET applications can be built.

For the purposes of this design, we will require that users create a project-based codebase if they wish to be grown up.

## Design points / pivots

The following is a non-exhuastive list of user experience considerations that go beyond the basic walkthrough above.

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

### Coding against a framework alternative syntax

The walkthrough proposes the shebang `#!/usr/bin/share/dotnet-aspnet`, but the .NET CLI doesn't have a command to "pull in the ASP.NET Core references", so it wouldn't map to something like that.

An alternative could be to extend `#r` like so:

* `#r "framework:aspnet"`
* `#r "sdk:aspnet"`
* etc.

This could cleanly separate the "tell the compiler which references to pull in" world from the "short-cut having to use `dotnet`" world like so:

```csharp
#!/usr/bin/share/dotnet
#r "framework:aspnet"

var app = WebApplication.CreateBuilder(args);
app.MapGet("/", () => "Hello World");
await app.RunAsync();
```

### Shebangs and their semantics

The shebangs in the walkthrough all point at `/usr/bin/share/dotnet`. Substitute a path for your appropriate installation of .NET on your operating system of choice.

What this means is that it identifies the .NET environment and implies a default set of references available in the file. However, it does not bring in other things like ASP.NET or other frameworkreferences. It is a contextual, "this is how I run" sort of directive.

It could be extended/changed in a few ways:

* `/usr/bin/share/dotnet run` makes it explicit that the command is to run the file
* `/usr/bin/share/dotnet-aspnet` is an alternative to `#!aspnet` that brings ASP.NET Core into scope for the file (substitute a different framework/sdk here)

In effect, the shebang's semantics could span a spectrum of "I need the path and command to literally execute this file" towards "I add context that affects the references available to this file".

We'll need to decide if they can affect the set of references available in the file or not.

### Build output

The walkthrough states that the build output is a single executable file in the current working directory. This is based on the hypothesis that this is the least surprising place to put the executable.

* Is this hypothesis based on reality?
* Is this still true if you pull in a package?
* Is this still true if you pull in a framework? (e.g., ASP.NET Core is hundreds of assemblies, where do they go?)

### Nuget reference syntax and naming

The current walkthrough proposed `"#r "nuget:..."` for packages.

Syntax could change:

* Use a `#!r` shebang-style syntax
* Use a custom directive like `#nuget`

Naming could change:

* `package` instead of `nuget`

However, that seems unlikely given that the current syntax is already shipping in .NET Interactive.

### A note on .NET Interactive

.NET Interactive _may_ be able to service these goals, but since it is currently focused on notebooks-based programming it doesn't really address the same scenario like JavaScript, Python, and Ruby. It is highly relevant for people who are used to notebook environments, and since notebooks are breaking into educational spaces, it may also play a big role in getting people started there.

But if a JS/Python/Ruby programmer is looking to try out C# for the first time, the way that they know how to start with a language is not well supported by our tooling.

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