# Add ability to sign extractable binaries included in a singlefile app.

There is a known issue around singlefile apps when the user needs to sign included artefacts or otherwise modify them. A typical solution for regular build artefacts is to sign binaries after the build is completed.
A problem arises for files that are included in a singlefile bundle. The fact that included files are not signed is observable for extractable files, but the user cannot easily address that.

Requiring that constituent files are signed prior to building a singlefile app could be highly inconvenient to existing build pipelines.
It is particularly problematic for a file that exists only in a transient form before its inclusion in a single file bundle. This includes the app main binary itself (as in `hello.dll` for a hello app) and composite r2r image, when composite r2r is used. The build does not currently present a documented opportunity for the user to observe and modify files before inclusion into the bundle.

In addition to signing a similar difficulties may affect other scenarios that rely on post-build modification of build artefacts. For example obfuscation and instrumentation.

## Current state (as of .NET 6)

The problem of signing applies primarily to files that are extracted to the file system as a part of execution. For the assemblies that are not extracted the problem is generally not a concern. 
With transitioning to superhost technology on all platforms in 6.0 a need to extract files is greatly reduced as native binaries that belong to the dotnet platform are no longer extracted and in fact cannot be extracted, since they are monolithically linked with the app. 

The files that still can be extracted:
-	Native dependencies included via a nuget references. (this is, technically, the only truly supported scenario to include a native dependency)
It could be expected that such files are signed prior to the build.

-	Dependencies that are forced to be extracted by the user.  
There are several possible reasons for the user to force extraction of all files and there is a way to force extraction of all dependencies, including managed assemblies that otherwise could run in-place.
Note that these files include the app dll itself as well as `.r2r.dll` in a case of composite publishing. Both would not present themselves to the user in the process of building.
This is the case where extraction is a very observable feature and it is plausible the extraction as a feature will be carried forward in the future versions.
I believe this is the case that causes the most inconveniences.

The abscense of documented solutions in this area forces users to implement solutions that rely on internal implementation details.  
For example: [`CollectBundleFilesToSign` target](https://github.com/dotnet/diagnostics/blob/18a8e986ddf28ee3c950b5acf00abc652d87be74/src/Tools/Directory.Build.targets).  

## Proposal for .NET 6

Provide a build-time hook to implement signing logic. The use pattern is expected to be similar to `CollectBundleFilesToSign`, but without relying on undocumented behavior.  
We will provide:
-	A target `BeforeBundle` that will be called between `_ComputeFilesToBundle` and `GenerateBundle`
-	An ItemGroup `FilesToBundle` containing all files that will be passed to `GenerateSingleFileBundle`
-	A Property `AppHostFile` that will specify the apphost.  
The reason for this is that the host will be in the list of files to bundle, but generally need not be signed.

It is possible that the signing tool will need to copy files in the process of signing. That could happen if the file is a shared item not owned by the build, for example the file comes from a nuget cache. In such case it is expected that the tool will modify the path of the corresponding `FilesToBundle` item to point to the signed copy.

## Alternative approaches considered:

-	Unbundle/Rebundle support

Provide a way to unbundle/rebundle a singlefile app.  

While very feasible in principle, a good UX for such solution remained elusive. 
The main problem here is that bundling process is inherently lossy and to perform bundling for the second time user would need to restate what was lost. Alternatively the lost information could be inferred form the bundle (which is nontrivial unless it is stored in the bundle by the build) and stored on a side to be subsequently used when bundling. 
In either case it leads to unnecessarily complicated, unintuitive and fragile user experience.

