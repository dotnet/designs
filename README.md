# .NET Design Proposals

This repo contains [design proposals](meta/proposals.md) for the .NET platform. For a complete listing, see [INDEX.md](INDEX.md).
This repo focuses on designs for the runtime, framework, and the SDK. For others, see:

* [C# Language Designs and Suggestions (dotnet/csharplang)](https://github.com/dotnet/csharplang)
* [VB Language Designs and Suggestions (dotnet/vblang)](https://github.com/dotnet/vblang)
* [F# Language Designs (fsharp/fslang-design)](https://github.com/fsharp/fslang-design)
* [F# Language Suggestions (fsharp/fslang-suggestions)](https://github.com/fsharp/fslang-suggestions)

## Creating a proposal

Unless you're driving the design of a feature, you're not expected to contribute
to this repo. Specifically, if you merely want to request new features, you
should file an issue for the [corresponding repo](https://github.com/dotnet/core/blob/master/Documentation/core-repos.md).

To create a proposal:

1. Create a new branch off of `main`
2. Create a document in the `accepted` folder and use [meta/template.md](meta/template.md) as your
   starting point.
3. Run `./update-index > INDEX.md` to update the [INDEX](INDEX.md).  On Windows use `python3 update-index > INDEX.md`
4. Create a pull request against `main`
5. Once broad agreement is reached, merge the PR

## License

This project is licensed with the [Creative Commons BY 4.0 License](LICENSE).

## .NET Foundation

This is a [.NET Foundation project](https://dotnetfoundation.org/projects).
