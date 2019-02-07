# Single-file Staging 

Publishing apps as a single file is a popular feature-request in .Net Core. Ideally, we want a single-file solution that:

* Is compatible with all .Net Core applications
* Bundles MSIL, R2R, native code and custom (data) files
* Doesn't require installation or cleanup steps
* Runs directly from the bundle, without extracting components to disk
* Reduces publish-size
* Improves startup cost
* Works cohesively with debuggers, profilers, Watson dump etc. 

This document explores a few options to realize the single-file publish feature, with different feature-set vs development cost trade-offs. The document can also be considered a development staging plan, where each stage spills fewer items onto temporary files, while paying an incremental development cost.

## 1. Self-Extractor

The first stage is to develop a pack and extract tool.
While this stage is technically simplistic, the interface and tooling will match the final solution, giving partner teams and potential customers a chance to prepare for adopting the technology.

### 1.1 Description

This stage implements:

* A  packaging tool (bundler) that embeds the application and all of its dependencies (essentially the contents of the publish directory) into a host executable. 
	* This version will not support compression of the bundled assemblies.
* When the executable runs, it will extract all of those files into a temporary directory, and then run as though the app was published to that temporary directory.

The mechanism will support:

* Reuse: Extract on first-run, reuse on subsequent runs.
* Upgrade: Each version of the app extracts to a unique location, supporting side-by-side use of multiple versions.
* Uninstall: Users can identify and delete extracted files when the app is no longer needed.
* Access control: Processes running with elevated access can extract to admin-only-writable locations.

### 1.2 Scenarios

Best suited for:

* Environments requiring maximal compatibility -- need to embed different kinds of files (IL, native, etc) into one, without losing functionality such as debuggability.

Limitations:

* Unsuitable for environments that require that the app does not perform disk-writes at startup

Advantages:

* Low cost of development
* Bundler tool can be used as-is for most further stages
* Provides ability to develop test infrastructure and prototypes

## 2. Run from Bundle: MSIL

### 2.1 Description

This stage improves on Stage 1 in that 

* MSIL files bundled into the single-file will load and execute directly from the executable.
* Native libraries will still need to 
	* Remain in the publish directory unmerged, or
	* Extracted to the disk like the previous self-extractor stage
* Debugging support is unaffected. 

### 2.2 Scenarios

Best suited for:

* Framework dependent purely managed apps that are not ready-to-run compiled.

Limitations:

* Unsuitable for environments that 
	* Have native dependencies (ex: published `--self-contained`, or depend on custom native libraries), and 
	* Cannot tolerate native libraries to remain unbundled or extracted to disk
* The app may need to be aware that its dependencies will be embedded into the single-file, when using certain LoadLibrary APIs.

## 3. Run from Bundle:  R2R

### 3.1 Description

In this stage

* MSIL and Ready-to-run files bundled into the single-file will load and execute directly from the executable.
* Native library support is the same as the previous stage.
* Debugging support is unaffected. 

### 3.2 Scenarios

Best suited for:

* Framework dependent managed apps that may be ready-to-run compiled.

Limitations:

* Unsuitable for environments that have native dependencies that must be bundled and cannot be extracted.

## 4. Run from Bundle: .Net Core libraries 

### 4.1 Description

In this stage

* .Net Core native libraries are statically linked to the single-file host executable.
* Handling of custom native libraries is the same as the previous stage.
* A large portion of the work involved in this stage is to make debuggers and tools compatible with the statically linked runtime.

### 4.2 Scenarios

Best suited for:

* Self-contained managed apps

Limitations:

* Environments that have dependencies on custom native libries that must be bundled and cannot be extracted.
* Watson dumps may not be supported.

## 5. Run from Bundle: Native libraries

### 5.1 Description

This stage improves on the previous one by providing the ability to statically link custom native code along into the host executable. This involves:

- Publishing the runtime as a library.
- Provide tools and guidance to statically link user's native-code with the runtime library to obtain a custom host executable
- Tooling to embed managed dependencies into this custom executable.

 Debugging support is the same as the previous stage.

Native library dependencies that cannot be statically linked will need to be extracted to the disk -- because some operating-systems do not support loading native libraries from memory.

### 5.2 Scenarios

Best suited for:

* Self-contained managed apps with custom native dependencies (that can be linked).

Limitations:

* Watson dumps may not be supported.
