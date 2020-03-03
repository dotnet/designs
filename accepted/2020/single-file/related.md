# Related Work

Single-file packaging for .NET apps is currently supported by third-party tools such as [Warp](https://github.com/dgiagio/warp) and [Costura](https://github.com/Fody/Costura). We now consider the pros and cons of implementing this feature within .Net Core.

#### Advantages

* A standardized experience available for all apps
* Integration with dotnet CLI
* A more transparent experience: for example, external tools may utilize public APIs such `AssemblyLoadContext.Resolving` or `AssemblyLoadContext.ResolvingUnmanagedDll` events to load files from a bundle. However, this may conflict with the app's own use of these APIs, which requires a cooperative resolution. Such a situation is avoided by providing inbuilt support for single-file apps.

#### Limitations

* Inbox implementation of the feature adds complexity for customers with respect to the list of deployment options to choose from.
* Independent tools can evolve faster on their own schedule.
* Independent tools can provide richer set of features catering to match a specific set of customers.

We believe that the advantages outweigh the disadvantages in with respect to implementing this feature inbox.

## References

* [Mono/MkBundle](https://github.com/mono/mono/blob/master/mcs/tools/mkbundle) 
* [Fody.Costura](https://github.com/Fody/Costura)
* [Warp](https://github.com/dgiagio/warp)
* [dotnet-warp](https://github.com/Hubert-Rybak/dotnet-warp)
* [BoxedApp](https://docs.boxedapp.com/index.html)
* [LibZ](https://github.com/MiloszKrajewski/LibZ)

