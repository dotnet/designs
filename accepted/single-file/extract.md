# Extracting Bundled Files to Disk

When running the single-file, the host will need to extract some bundled files resources out to the disk before loading them. This is particularly true for native DLLs because of operating system API restrictions -- for example, Windows doesn't support a `LoadLibrary()` API that reads in from a stream. 

We now explore the options with respect to the location and life-time of the extracted files.

## Extraction Options

### Extraction Time

* **Startup**: The host spills the necessary files at startup, and communicates the location to the runtime.
* **Lazy:** The runtime provides callbacks to extract a bundled file to disk when it is actually necessary to be loaded. 

Extraction at startup is easier to implement, as the runtime needs no knowledge of the extraction. 

Lazy extraction saves startup cost, particularly if many ready-to-run images are bundled. The lazy extraction feature is only useful when files are extracted on every run.

### Extraction Lifetime

#### Temporary Extraction

Extract out the necessary files to a random temporary location, and attempt to clean them out on exit. 

##### Pros

* Simpler implementation: fewer complexities with respect to app-updates and concurrency.
* User need not worry about explicit cleanup.
* Suits customers who prefer click-and-run model for their apps, rather than app-install model. 

##### Cons

* Higher startup cost, because of extraction on every run -- especially for self-contained apps.
* Cleanup may fail if the application crashes.

#### Persistent Extraction

Extract files to a specific location on first run, and re-use the extraction on subsequent runs.

##### Pros

- Saves startup cost, particularly for self-contained apps, or apps containing several native binary dependencies.
- No need for implementing lazy-extraction.

##### Cons

- Complex implementation.
- Unsuitable for customer scenarios that cannot tolerate persistent disk state after the app terminates.
- Cleanup is manual.

### Current Tools

* **Extraction Time**: Mono extracts native libraries at startup. Costura extracts and explicitly loads the native-libraries at startup.
* **Lifetime**:  Mono and Costura extract native dependencies to temporary files on every run.

## Proposed Solution

For .Net Core 3.0, we propose to implement the persistent-extraction scheme. Based on customer feedback, we can implement other options discussed above.

### Extraction Requirements

- *Reuse*: Extract on first-run, reuse on subsequent runs.
- *Fault-tolerance*: Recover from failure after partial extraction on first run.
- *Concurrency*: Handle concurrent launches on first start.
- *Upgrade*: Each version of the app extracts to a unique location, supporting side-by-side use of multiple versions.
- *Cleanup*: Users can identify and delete extracted files when the app is no longer needed.
- *Access control*: Have an option for elevated runs to extract files to admin-only-writable locations.

### Extraction Location

The desirable characteristics of the extraction directory are:

- Should be configurable
- Should be semi-permeant: Not a directory that is lost frequently (ex: on machine reboot), but considered recyclable storage, since any subsequent run can re-extract.
- Be short, to reduce the risk of running over path length limit.

For a single-file app, the extraction directory is `<base>/<app>/<bundle-id>`

* `<base>` is 

  * `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable, if set.
  * If not, defaults to 
    * `%TEMP%\.net ` on Windows
    * `$TMPDIR\.net` if `$TMPDIR` is set (Posix conforming OSes including Mac)
    * Otherwise `/var/tmp/.net` (Ubuntu)  if the directory exists.
    * Otherwise  `/tmp/.net` 

* `<app>` is the name of the single-exe binary

* `<bundle-id>` is a unique Bundle-identifier. 

  Each single-file bundle contains a path-compatible cryptographically strong identifier embedded within it. This identifier is used as part of the extraction path in order to ensure that files from multiple versions of an app do not interfere with each other.

#### Cleanup

The cleanup of extracted files in the install-location will be manual in this version. 

We can consider providing helper scripts to cleanup the extracted files for an executable. However, future versions are expected to spill fewer artifacts to disk, making the cleanup commands a lower priority feature.

### Extraction Mechanism

During startup, the Apphost:

- Checks if pre-extracted files are available for the app, and if so, reuses them.
  - In this version, the extracted files are assumed to be correct by construction. That is, if  `<base>/<app>/<bundle-id>`  exists, the files there are reused.
  - If the contents of the extraction directory are corrupted post-extraction, they will need to be explicitly cleaned.
  - This situation is no different from having a managed app with one of the dependent DLLs corrupted.
- Extracts appropriate files to a temporary location `<base>/<app>/<proc-id>` . This ensures that concurrent launches of the app do not interfere with each other.
- After successful extraction of all necessary files to `<base>/<app>/<proc-id>`, the app host commits the extraction to the actual location `<base>/<app>/<bundle-id>` .
  - If `<base>/<app>/<bundle-id>` already exists by now, another process may have completed the extraction, so the host removes the directory `<base>/<app>/<proc-id>`, and continues execution with files in `<base>/<app>/<bundle-id>` .
  - If not,  `<base>/<app>/<proc-id>` is renamed to `<base>/<app>/<bundle-id>` 
- The `<base>/<app>/<proc-id>`  is now considered app-root, and execution continues.

### Extraction Configurations

The following environment variables influence the extraction mechanism:

* `DOTNET_BUNDLE_EXTRACT_BASE_DIR`  The base directory within which the files embedded in a single-file app are extracted, as explained in sections above. This directory can be set up an admin-only writable location if necessary.
* `DOTNET_BUNDLE_EXTRACT_ALL`:  Apps may choose to have all of their dependencies extracted to disk at runtime (instead of loading certain files directly from the bundle) by setting this variable.