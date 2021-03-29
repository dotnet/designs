# Source Link

**Dev** [Tomáš Matoušek](https://github.com/tmat)

Source Link is a developer productivity feature that allows unique information about an assembly's original source code to be embedded in its PDB during compilation. The [Roslyn](https://github.com/dotnet/roslyn/wiki/Roslyn%20Overview) compiler flag for Source Link is `/sourcelink:<file>`.

## Why Source Link?
Most debugging is done against locally built source code on a developer's machine. In this scenario, matching the binary to the source is not difficult. However, there are many debugging scenarios where the original source code is not immediately available. Two good examples of this are debugging crash dumps or third party libraries. In these scenarios, it can be very difficult for a developer to acquire the exact source code that was built to produce the binary they are debugging. Source Link solves this problem by embedding unique information about the source code (such as a git commit hash) in the PDB. Diagnostic tools, such as debuggers, can use this unique information to retrieve the original source code from hosted services (such as [GitHub](https://github.com)).

## Source Link File Specification
The `/sourcelink:<file>` compiler flag will embed a JSON configuration file in the PDB. This configuration file would be generated as part of the build process. The JSON configuration file contains a simple mapping of local file path to URL where the source file can be retrieved via http or https. A debugger would retrieve the original file path of the current location from the PDB, look this path up in the Source Link map, and use the resulting URL to download the source file.

## MSBuild Example
There is an open source tool available for generating Source Link configuration at build time. This supports GitHub and VSTS out of the box. See https://github.com/dotnet/sourcelink#using-sourcelink for the most up to date information.

Generating and embedding a Source Link file can also be done manually with an MSBuild Target.

### GitHub
GitHub allows files to be downloaded from the domain raw.githubusercontent.com. For example, the README for the [.NET Core](https://github.com/dotnet/core) repo can be downloaded directly for commit `1ae869434444693bd463bf972af5b9a1b1a889d0` with the following URL:
```
https://raw.githubusercontent.com/dotnet/core/1ae869434444693bd463bf972af5b9a1b1a889d0/README.md. 
```
Using this URL scheme, it is possible to generate Source Link for GitHub repositories. 

> Note that the following code is just a sample that doesn't handle all corner cases and is not as efficient as it could be. Use [Microsoft.SourceLink.GitHub](https://github.com/dotnet/sourcelink#using-sourcelink) package to get a robust Source Link support.

```xml
...
  <!-- Enable the /sourcelink compiler flag -->
  <PropertyGroup>
    <SourceLink>$(IntermediateOutputPath)source_link.json</SourceLink>
  </PropertyGroup>
  ...
  <Target Name="GenerateSourceLink" BeforeTargets="CoreCompile">
    <PropertyGroup>
      <!-- Determine the root of the repository and ensure its \'s are escaped -->
      <SrcRootDirectory>$([System.IO.Directory]::GetParent($(MSBuildThisFileDirectory.TrimEnd("\"))))</SrcRootDirectory>
      <SourceLinkRoot>$(SrcRootDirectory.Replace("\", "\\"))</SourceLinkRoot>
    </PropertyGroup>

    <!-- Get the GitHub url for the current git repo -->
    <Exec Command="git config --get remote.origin.url" ConsoleToMsBuild="true">
      <Output TaskParameter="ConsoleOutput" PropertyName="RemoteUri" />
    </Exec>

    <!-- Get the current commit from git -->
    <Exec Command="git rev-parse HEAD" ConsoleToMsBuild="true">
      <Output TaskParameter="ConsoleOutput" PropertyName="LatestCommit" />
    </Exec>

    <!-- Get the current commit from git -->
    <Exec Command="git merge-base --fork-point refs/remotes/origin/master HEAD" ConsoleToMsBuild="true">
      <Output TaskParameter="ConsoleOutput" PropertyName="LatestCommit" />
    </Exec>

    <!-- Write out the source file for this project to point at raw.githubusercontent.com -->
    <WriteLinesToFile File="$(SourceLink)" Overwrite="true"
                      Lines='{"documents": { "$(SourceLinkRoot)\\*" : "$(RemoteUri.Replace(".git", "").Replace("github.com", "raw.githubusercontent.com"))/$(LatestCommit)/*" }}' />
  </Target>
...
```

### VSTS
VSTS git repos allow files to be downloaded directly using the [VSTS REST API](https://docs.microsoft.com/en-us/rest/api/vsts/git/items/get?view=vsts). Using the API, it is possible to create a URL that can be used for Source Link. For example, the README of the repo at https://myaccount.visualstudio.com/MyProject/_git/MyRepo/ can be downloaded directly at commit `a194757aa9808e18fe900d5a3b4dcde6df14c094` with the following URL:
```
https://myaccount.visualstudio.com/MyProject/_apis/git/repositories/MyRepo/items?scopePath=/README.md&versionDescriptor.version=a194757aa9808e18fe900d5a3b4dcde6df14c094&versionDescriptor.versionType=commit&api-version=4.1-preview
```
Using this URL scheme, it is possible to generate Source Link for VSTS git repositories. NOTE: VSTS does not allow anonymous access to repositories, so all requests must be [authenticated](https://docs.microsoft.com/en-us/vsts/integrate/get-started/authentication/authentication-guidance?view=vsts). See [tooling](#tooling) for more info. 

> Note that the following code is just a sample that doesn't handle all corner cases and is not as efficient as it could be. Use [Microsoft.SourceLink.Vsts.Git](https://github.com/dotnet/sourcelink#using-sourcelink) package to get a robust Source Link support.

```xml
...
  <!-- Enable the /sourcelink compiler flag -->
  <PropertyGroup>
    <SourceLink>$(IntermediateOutputPath)source_link.json</SourceLink>
  </PropertyGroup>
...
  <Target Name="GenerateSourceLink" BeforeTargets="CoreCompile">
    <PropertyGroup>
      <!-- Determine the root of the repository and ensure its \'s are escaped -->
      <SrcRootDirectory>$([System.IO.Directory]::GetParent($(MSBuildThisFileDirectory.TrimEnd("\"))))</SrcRootDirectory>
      <SourceLinkRoot>$(SrcRootDirectory.Replace("\", "\\"))</SourceLinkRoot>

      <!-- Define our VSTS repo's Account and Project -->
      <VstsAccount>MyAccount</VstsAccount>
      <VstsProject>MyProject</VstsProject>
      <VstsRepo>MyRepo</VstsRepo>
    </PropertyGroup>

    <!-- Get the current commit from git -->
    <Exec Command="git rev-parse HEAD" ConsoleToMsBuild="true">
      <Output TaskParameter="ConsoleOutput" PropertyName="LatestCommit" />
    </Exec>

    <!-- Get the current commit from git-->
    <Exec Command="git merge-base --fork-point refs/remotes/origin/master HEAD" ConsoleToMsBuild="true">
      <Output TaskParameter="ConsoleOutput" PropertyName="LatestCommit" />
    </Exec>

    <!-- Write out the source file for this project to point at VSTS REST API -->
    <WriteLinesToFile File="$(SourceLink)" Overwrite="true"
                      Lines='{"documents": { "$(SourceLinkRoot)\\*" : "https://$(VstsAccount).visualstudio.com/$(VstsProject)/_apis/git/repositories/$(VstsRepo)/items?scopePath=/*&amp;versionDescriptor.version=$(LatestCommit)&amp;versionDescriptor.versionType=commit&amp;api-version=4.1-preview" }}' />
  </Target>
...
```

## Source Link JSON Schema
```json
{
    "$schema": "http://json-schema.org/draft-04/schema#",
    "title": "SourceLink",
    "description": "A mapping of source file paths to URLs",
    "type": "object",
    "properties": {
        "documents": {
            "type": "object",
            "minProperties": 1,
            "additionalProperties": {
                "type": "string"
            },
            "description": "Each document is defined by a file path and a URL. Original source file paths are compared 
                             case-insensitively to documents and the resulting URL is used to download source. The document 
                             may contain an asterisk to represent a wildcard in order to match anything in the asterisk's 
                             location. The rules for the asterisk are as follows:
                             1. The only acceptable wildcard is one and only one '*', which if present will be replaced by a relative path.
                             2. If the file path does not contain a *, the URL cannot contain a * and if the file path contains a * the URL must contain a *.
                             3. If the file path contains a *, it must be the final character.
                             4. If the URL contains a *, it may be anywhere in the URL."
        }
    },
    "required": ["documents"]
}
```

## Examples
The simplest Source Link JSON would map every file in the project directory to a URL with a matching relative path. The following example shows what the Source Link JSON would look like for the .NET [Code Formatter](https://github.com/dotnet/codeformatter). In this example, Code Formatter has been cloned to `C:\src\CodeFormatter\` and is being built at commit `bcc51178e1a82fb2edaf47285f6e577989a7333f`.
```json
{
    "documents": {
        "C:\\src\\CodeFormatter\\*": "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/*"
    }
}
```
In this example, the file `C:\src\CodeFormatter\src\Microsoft.DotNet.CodeFormatting\FormattingEngine.cs` would be retrieved from `https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/Microsoft.DotNet.CodeFormatting/FormattingEngine.cs`

If the project was built on Linux and was cloned to `/usr/local/src/CodeFormatter/`, the Source Link JSON would be:
```json
{
    "documents": {
        "/usr/local/src/CodeFormatter/*": "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/*"
    }
}
```

Source Link configuration can also contain absolute mappings from file paths to URLs without an asterisk. Using the Code Formatter example from above, every source file could be mapped explicitly:
```js
{
    "documents": {
        "C:\\src\\CodeFormatter\\src\\CodeFormatter\\Program.cs":               "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/CodeFormatter/Program.cs",
        "C:\\src\\CodeFormatter\\src\\CodeFormatter\\CommandLinerParser.cs":    "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/CodeFormatter/CommandLineParser.cs",
        "C:\\src\\CodeFormatter\\src\\DeadRegions\\Program.cs":                 "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/DeadRegions/Program.cs",
        "C:\\src\\CodeFormatter\\src\\DeadRegions\\Options.cs":                 "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/DeadRegions/Options.cs",
        "C:\\src\\CodeFormatter\\src\\DeadRegions\\OptionParser.cs":            "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/src/DeadRegions/OptionParser.cs",
        // additional file listings...
    }
}
```

Source Link JSON may contain multiple relative and/or absolute mappings in any order. They will be resolved in order from most specific to least specific. Here is an example of a valid Source Link JSON for an arbitrary project structure:
```json
{
    "documents": {
        "C:\\src\\*":                   "http://MyDefaultDomain.com/src/*",
        "C:\\src\\foo\\*":              "http://MyFooDomain.com/src/*",
        "C:\\src\\foo\\specific.txt":   "http://MySpecificFoodDomain.com/src/specific.txt",
        "C:\\src\\bar\\*":              "http://MyBarDomain.com/src/*"
    }
}
```
In this example:
- All files under directory `bar` will map to a relative URL beginning with `http://MyBarDomain.com/src/`.
- All files under directory `foo` will map to a relative URL beginning with `http://MyFooDomain.com/src/` **EXCEPT** `foo/specific.txt` which will map to `http://MySpecificFoodDomain.com/src/specific.txt`.
- All other files anywhere under the `src` directory will map to a relative url beginning with `http://MyDefaultDomain.com/src/`.

## Tooling<a name="#tooling">
- [Visual Studio 2017](https://www.visualstudio.com/downloads/)
    - First debugger to support Source Link.
    - Supports Source Link in [Portable PDBs](https://github.com/dotnet/core/blob/master/Documentation/diagnostics/portable_pdb.md) -- default PDB format for .NET Core projects.
    - VS 2017 Update 3 supports Source Link in Windows PDBs -- default PDB format for .NET Framework projects.
    - The current implementation uses a case insensitive string comparison between the file path and the Source Link entry. Using multiple entries that differ only in casing is not supported; the first entry will be used and the second entry that differs only by case will be ignored. Here is an example of an unsupported Source Link JSON:
        ```json
        {
            "documents": {
                "/FOO/*": "http://.../X/*",
                "/foo/*": "http://.../Y/*"
            }
        }
        ```
    - Starting in [VS 2017 version 15.7 Preview 3](https://blogs.msdn.microsoft.com/visualstudio/2018/04/09/visual-studio-2017-version-15-7-preview-3/), the debugger can automatically authenticate Source Link requests for VSTS and GitHub repositories.
- [C# Extension for Visual Studio Code](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp)
    - Supports Source Link as of C# extension version 1.15
- [JetBrains dotPeek](https://www.jetbrains.com/dotpeek) and [JetBrains ReSharper](https://www.jetbrains.com/resharper)
    - First .NET decompiler to support Source Link.
    - Supports Source Link in [Portable PDBs](https://github.com/dotnet/core/blob/master/Documentation/diagnostics/portable_pdb.md) -- default PDB format for .NET Core projects.
    - Can navigate to sources referenced in `source_link.json` or embedded in the Portable PDB.
    - Can present `source_link.json` contents to the user.
