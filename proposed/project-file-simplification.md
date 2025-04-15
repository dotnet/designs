# Project file simplification and improved change management

// Contents of Type should match template name as much as possible

## Summary
[summary]: #summary

This proposal is to simplify the project file while giving the user better control over breaking changes and new warnings.

The approach is versioned default properties specified by a `Base` attribute in the project file, a `Directory.Build.props`, or an `Import` file. This attribute specifies a version of the file containing the default properties. The project file is further simplified by `Type` attribute on the `Project` element. The `Type` is not versioned, although versioned default properties can be added as part of the `Base` evaluation. The `Type` attribute replaces the current `SDK` and the `Type` file contains both the `SDK` name and properties that apply to the application `Type`.

An example of a new project file is:

```XML
<Project Base="10.0.100" 
         Type="Console"> 
</Project> 
```

If there is a `Directory.Build.props` or `Import` file, it can contain the `Base` attribute, although it cannot contain the `Type` attribute. This is because the `Type` attribute specifies the SDK instructions for build evaluation.

There would be no change to existing projects. They will continue to work as today.

## Motivation
[motivation]: #motivation

The fundamental benefit of this effort is to speed up and remove friction for users that wish to adopt tooling improvements quickly, while also allowing users to delay incorporating change until it is not disruptive. To do this, we need to allow developers a friction free experience on upgrade, which means that with no more work than previously they have control over the upgrade process.

All breaking changes and new warning are introduced because we believe the positive impact outweighs the pain of the change. If a change is not breaking and does not add new warnings, we just add the feature and they are available when the user upgrades. If the change breaks everyone, we are unlikely to make the change. This proposal affects the occasional features that may break a few projects some of the time.

The break may be a new warning. We treat new warnings as breaking changes because they can be disruptive to projects that rely on `TreatWarningsAsErrors` to guard against regressions in their own code. We want to make it easy for users to incorporate changes and warnings as quickly and smoothly as possible to achieve these benefits. An example of a new feature that created new warnings and clearly benefits almost all projects is the `NuGetAudit` flag which informs you if you are using a vulnerable package directly or transitively.

We realize that your capacity to accommodate change and possible changes to your code varies widely by team, time of year, business considerations, and whether your already working on the code. These changes also affect all of the projects you are working on or building. Thus, breaks and warning that occur on tooling upgrades are undesirable.

Your approach to upgrading the `TargetFramework` of your project varies from wanting the newest features and performance improvements as quickly as possible to postponing as long as possible to ease the upgrade effort.

### Motivation for `Base` attribute

A new `Base` attribute on the `Project` element of the project file, `Directory.Build.props`, or an `Import` file and will specify defaults for all common project properties. This means the project file will contain two attributes and the things you add, such as packages. In cases where common properties like `Nullable` have been moved to `Directory.Build.props`, or an `Import` file, that file can be simplified.

When a there is a breaking change or a new warning, there is a new MSBuild property (as today). For breaking changes or new warnings, the feature will be turned off if it `false` or the equivalent `disable`. The defaults specified by `Base` will turn on the features we strongly recommend. If you disagree with this for your project, you can reset the value to `false`.

The key element of the `Base` design is that it is versioned and released with each SDK. This allows you to initially experiment with, and then adopt all the breaking changes and new warnings with a single value. The proposed structure of this version is the corresponding SDK version number, such as `10.0.100`, with a possible simplification to just `10`. The `Base` defaults include the `TargetFramework` so the user manages a single version number, in most cases.

However, key to this design is that `Base` is not required to match either your SDK or your `TargetFramework`. For example, if you use an LTS strategy and want to ensure you get the latest warnings to benefit your code and prepare for future upgrade, you might have:

```XML
<Project Base="11.0.100" 
         Type="Console"> 
   <PropertyGroup>
        <TargetFramework>net10<TargetFramework>
   </PropertyGroup>
</Project> 
```

Similarly, you can use `Base` to get the warnings applicable to you if you are running a .NET Framework or .NET Standard project where the `TargetFramework` does not change.

When upgrading to adopt breaking changes and new warnings, the user can just change the `Base` value, or they can use tooling with info on the new behavior.

This design considers both very small and very large scenarios. For small projects and new users, the project file is simple and contains only two attributes that they did not enter. Both of these attributes have names/values they understand.

For large repos with many projects, we embrace `Imports` files and look forward to feedback to drive further refinements.

### Separately controlling `TargetFramework` and tooling behavior

For many users, upgrading their tooling breaking changes/warnings along with their `TargetFramework` keeps life simple:

```XML
<Project Base="10.0.100" 
         Type="Console"> 
</Project> 
```

For users that wish to postpone or avoid breaking changes/warnings, they can use a lower version of tooling defaults:

```XML
<Project Base="10.0.100" 
         Type="Console">
    <PropertyGroup>
        <TargetFramework>net11</TargetFramework>
    </PropertyGroup>
</Project> 
```

For users that are not ready to upgrade their `TargetFramework` but want any new warnings that may improve their application, they can use a higher version of tooling defaults:

```XML
<Project Base="10.0.100" 
         Type="Console">
    <PropertyGroup>
        <TargetFramework>net482</TargetFramework>
    </PropertyGroup>
</Project> 
```

This could be used to provide features like `NuGetAudit` that are independent of `TargetFramework` to more users with less friction.

### Tying behavior to tooling vs. TFM vs. tooling abstraction

## Design
[design]: #design

This design is intended to lay out the goals in concrete terms that align with MSBuild behavior. We expect the design to be reconsidered during implementation.

### Challenges the design needs to solve

#### Users intuit "near" and MSBuild users "last"

For many users, MSBuild is a configuration and they rarely think about it as a tool that runs operations, including complex evaluation. When multiple values are set in different location, casual thinking is likely to expect the one closest to the user to win. MSBuild generally mimics this.

However, MSBuild actually uses the last value set, no matter where that occurred. The order of evaluation of MSBuild is used to prioritize the values that are in or closest to the project file. All `Base` and `Type` values will be considered further from the project file than any values in `Import` files, `Directory.Build.props` or the project file.

#### `Base` values must not overwrite user values

To illustrate precedence, consider this non-trivial, but expected to be a real-world case if a group of projects use `Import` to provide common behavior:

```xml
<!-- .csproj file -->
<Project Type="Console"> 
</Project> 

<!-- Directory.Build.props file -->
<Project>
    <PropertyGroup>
        <TargetFramework>net11</TargetFramework>
    </PropertyGroup>
    <Import>..\MyImportFile.props</Import>
</Project> 

<!-- MyImportFile.props file -->
<Project Base="10.0.100"> 
</Project> 

<!-- 10.0.100 Base file expresses -->
<Project> 
    <PropertyGroup>
        <TargetFramework>net10</TargetFramework>
    </PropertyGroup>
</Project> 
```

In this case, `TargetFramework` should be set to `net11` because it is closer to te user.

We are particularly concerned about precedence being predictable because the results may be subtle or _spooky action at a distance_ because the properties set may affect a very different part of the build. Also, debugging MSBuild via binlogs and the structured log viewer is a skill many of our users do not have because they rarely or never need to debug MSBuild logic.

#### Respecting values set by the user

The properties declared in `Base` should not overwrite other values. We can accomplish this with MSBuild conditionals:

```xml
<!-- 10.0.100 Base file expresses -->
<Project> 
    <PropertyGroup>
        <TargetFramework Condition=" '$(TargetFramework)' == '' ">net10</TargetFramework>
    </PropertyGroup>
</Project> 
```

However, this would result in a more complicated `Base`. See [Simple supporting files](#simple-supporting-files).

#### Simple supporting files

Introducing new default values, mostly or completely controlled by the .NET teams, is likely to lead users to want to understand what is being set. Simple supporting files for `Base` and `Type` will allow users to open the file and understand the source of truth for controlling new features, warnings, and breaking changes. Unless another format is compelling, the simplest seems to be the project or `.props` file format. This means default values would appear in a `<PropertyGroup`. There will likely be restrictions in which XML elements are valid in this file.

It is likely that these default values will need to be conditional, because they should not overwrite user values, and it will be difficult or impossible to guarantee bases are evaluated before any user entries (see [`Base` values must not overwrite user values](#base-values-must-not-overwrite-user-values)). This conditionality should not appear as MSBuild conditions in the base files to override complicating them. An indicator that the property is treated special should be included for clarity.

An example of what this might look like:

```xml
<Project>
   <PropertyGroup>
      <!-- Additional values -->
      <TargetFramework TreatAsDefault=true>net10</TargetFramework>
   </PropertyGroup>
</Project>
```

Short descriptive documentation or links could also be included as comments and an automated process could create PRs in `dotnet/docs` for the docs team to merge.

#### Torn state

A torn state is one where a there are discrepancies between the understanding of the system in different parts of the system. In the case of MSBuild evaluation, this could happen if a property is changed after it is used to make a decision or to set another property.

An example of this would be:

```xml
<!-- .csproj file -->
<Project Type="Console"> 
</Project> 

<!-- Directory.Build.props file -->
<Project>
    <Import>..\MyImportFile.props</Import>
    <PropertyGroup>
        <TargetFramework>net11</TargetFramework>
    </PropertyGroup>
</Project> 

<!-- MyImportFile.props file -->
<Project Base="10.0.100">
    <PropertyGroup>
        <MessageText>The TFM is: $(TargetFramework)</MessageText>
    </PropertyGroup>
</Project> 

<!-- 10.0.100 Base file expresses -->
<Project> 
    <PropertyGroup>
        <TargetFramework TreatAsDefault=true>net10</TargetFramework>
    </PropertyGroup>
</Project> 
```

Here the `TargetFramework` is set by the user in their `Directory.Build.props` to a different value than in the `Base` file. `MessageText` will not reflect the `TargetFramework` that is used for most of the build.

This can happen in MSBuild today. There is nothing fundamentally knew by using `Base`. However, we think it will be more likely to occur. We also believe it will become more valuable to put conditionals or use the property values in `Import` files, although we are still looking for specific scenarios.

Being able to reset values that provide defaults in the `Base` is a core aspect of the feature. This retains the user as the primary decision maker for their project.

We could add a new feature to MSBuild that would catch this scenario (see [Immutability/AssignOnce](#immutabilityassignonce)).

We can give warnings or errors for torn state if we recognize when values have been read. If we have a marking we can raise a warning or error if the property is reset. You can imagine a dictionary of property names for those that have been read, although the implementation is not determined.

This would be a breaking change, because a user may be successful evaluating a project that has a torn state - even if it is fragile. As such we would provide a property that turns on the test for torn state, and it would be off by default. In the `Base` for .NET 10, we would enable the torn state test.

#### User customization

We have received several requests for customization for `Base` files. Our initial response is to clarify our intent in `Base` files and how they differ from `Import` files. `Import` files are a long-standing feature of MSBuild and allow project's to incorporate MSBuild elements from another file. This feature is widely used in large repos and we have no plans for any changes to it.

`Base` files are intended as a mechanism to give user's more control of the changes Microsoft to rolls out.Fundamentally, any user entered properties will overwrite any Microsoft supplied properties - you're in control of your build. With this context, we think that `Import` is a better solution for customizing at a repo level and do not plan to support custom `Base` files at this time. We are interested in hearing about any compelling scenarios.

`Import` and `Base` will work well together and the `Base` attribute can appear in any `Import` file.

#### Multiple `Base` files

We considered whether to allow more than one `Base` files. We have three options:

* Allow multiple `Base` files and evaluate in an order that maintains precedence
* Throw an error if more than one `Base` is encountered during evaluation
* Ignore all but the `Base` that is closest to the project file

We do not have scenarios where multiple `Base` files all need to be evaluated. We do have scenarios where multiple `Base` declarations appear, but only the one closest to the project should be evaluated. This is just an extension of MSBuild principles about recognizing the properties closest to the user. The leading project will use the defaults of the new `TargetFramework` ignoring the earlier .NET version's `Base` file.

Only the first `Base` encountered will be used.

#### Immutability/AssignOnce

We have received a request to allow some specific values to block further change to the values set from the `Base`. We do not see a reason for this in the `Base` files we will create.

We are exploring broader use of the `AssignOnce` attribute on properties that make them immutable. We think this may solve the request to block further property changes in `Import` files.

#### Abbreviating version declaration

The SDK version number changes quarterly, while most breaking changes and warnings are introduced annually. Also, when users think about the version their behavior is tied to, users may think in terms of aligning to the major .NET version. Thus, supporting a short hand of just `10`, rather than `10.0.100` is desirable. If the user wants to use a quarterly `Base` file, they will need to enter the full SDK version number. We anticipate quarterly changes to `Base` files to be rare, except in support of upcoming preview features.

### Changes to common user files

We will add two new attributes to project files. This will be an optional/alternative way to declare the SDK and the property values that were previously in the project file when created from templates. The existing approach to project files will remain unchanged.

In addition, we will provide a mechanism for declaring a default property value. When this file is read a check will be made - if the property has already been set, the existing value will be retained and no action will be performed.

#### `Type` attribute

_The name `Type` is not final._

The `SDK` attribute on the `Project` element can be replaced by a `Type` attribute. It is not legal to have both a `Type` and an `SDK` attribute. If neither exists, the project will behave in the same manner as projects without a `SDK` attribute behave today.

From a user perspective, the `Type` attribute is a friendlier alternative to the `SDK` attribute since its name and contents describe intent. Also, this allows application type specific properties to be moved from the project file into the `Type` system.

It is an error for a `Type` attribute to appear anywhere but a project file `Project` element. For example, it cannot appear in `Directory.Build.props`, The reason for this restriction is that the logic run when the `Type` attribute appears determines the SDK (the tasks and targets) that are used. `Directory.Build.props` is located as part of an SDK task.

There is a file delivered as part of the SDK that provides the `Type` information. The effect of this file will be the same as if it was a props file with an `SDK` attribute on the `Project` element and a series of properties. It is an error for a `Type` file to be missing the SDK, to contain any MSBuild element except `PropertyGroup`. Since we will create and deliver these files, we might not do the work to enforce this restriction. The purpose of this restriction is that SDKs already allow adding information to the build, and anything more complex is probably better done via an SDK.

### Upgrade tooling

While the user can directly change the value specified in the `Base` attribute, this design allows a more sophisticated upgrade experience.

We can determine what the project file would look like with the previous `Base` (or no `Base`) and with the proposed `Base`.See the section [Changing the project file, retaining behavior](#changing-the-project-file-retaining-behavior).

The upgrade assistant should also be explored as part of this process.

#### Including help information in the `Base`

It would be desirable to include extra information as part of each property in the `Base` file. This could be used as part of an upgrade process, used in generating docs, passed on to users, or made available to AI. The desired information would be:

| Information                             |
|-----------------------------------------|
| Description                             |
| URL for more info                       |
| SDK version where first added to `Base` |

This information should be semantically available (possibly through MSBuild metadata) and also be helpful to humans perusing the file to see what's new.

#### Changing the project file, retaining behavior

One approach to the tooling would be to run a process that updates the current project to one that has identical behavior using the new `Base` by placing new properties into the project file. The user could then explore which of these to remove to enable new behavior. The user would start this process with a command like:

```bash
dotnet project upgrade 11.0.100
```

This would update the project file with the requested `Base` and new properties based on a _diff_ between the previous and resulting project properties.

For example, if the 11.0.100 version of `Base` had two new enabled properties `UseFeatureA` and `UseFeatureB`, two new properties would be added to the project file when upgrading from a version 10 to version 11:

```XML
<Project Base="10.0.100" 
         Type="Console"> 
</Project> 
```

```XML
<Project Base="11.0.100" 
         Type="Console">
    <PropertyGroup Label="These properties added on upgrade from 10.0.100 to 11.0.100"
       <UseFeatureA>disable</UseFeatureA>
       <UseFeatureB>disable</UseFeatureB>
    </PropertyGroup>
</Project> 
```

This approach is simple for a project file. It might be extended to complex repos by placing the `PropertyGroup` containing the new properties at the start of whichever file (project file, `Directory.Build.props` or `Import` file) the `Base` attribute appeared in. However, the command would probably be run in the same directory as the file containing the `Base` attribute and specify the file if there were multiple props files.

#### Interactive upgrade

|Command|Result|
|-|-|
|dotnet project upgrade 11.0.100 --whatif|List the properties that would be added, along with any inline comments|
|dotnet project upgrade 11.0.100 --preserve|Actually run the upgrade, resulting in new properties to match previous behavior|
|dotnet project upgrade 11.0.100 --no-preserve|Actually run the upgrade, without new properties|

## Drawbacks
[drawbacks]: #drawbacks

This introduces a new concept to .NET users, and some additional complexity in the overall MSBuild evaluation. Users would see this in areas such as understanding binlogs. We think the trade off is worthwhile because users get small clean project files the ability to control new warnings/breaking changes independent of both SDK version and TargetFramework.

## Alternatives
[alternatives]: #alternatives

We could continue to tie new features to TFM. THe drawback is that users generally upgrade their TFM to keep their projects in support. While this avoids breaking changes/warnings occurring on tooling upgrade, it may either delay adopting desirable new features, or result in breaking changes/warnings happening when the user is just trying to stay in support.

## Open questions
[open]: #open-questions

### What capacity to pin and float should we offer for `Base` versions?

### What about multi-targeting?

### Should we restrict `Type` to the project file?

Stated differently, should we allow `Type` to be in  `Directory.Build.props`. Since `Type` determines the SDK and that contains the targets that run most build operations, this is tricky. Also, it may be best for users to see this in the project file for clarity.
