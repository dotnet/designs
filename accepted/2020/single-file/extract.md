# Extracting Bundled Files to Disk

When running the single-file, the [host components](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md) may need to extract some bundled files out to the disk. This is particularly true for bundled native DLLs because of operating system API restrictions -- for example, Windows doesn't support a `LoadLibrary()` API that reads in from a memory stream. 

## Extraction Requirements

- *Fault-tolerance*: Recover from failure after partial extraction on first run.
- *Concurrency*: Handle concurrent launches on first start.
- *Upgrade*: Each version of the app extracts to a unique location, supporting side-by-side use of multiple versions.
- *Manual Cleanup*: Users can identify and delete extracted files when the app is no longer needed.
- *Amortization*: It is desirable that the cost of extraction is amortized over several runs. For example, by extracting on first-run, and reusing the same extracted files on subsequent runs.

## Extraction Options

* ***Extraction Time***
  * **Startup**: The host spills the necessary files at startup, and communicates the location to the runtime.
    * Simple implementation
    * High startup cost
    * Current tools: Mono extracts native libraries at startup. Costura extracts and explicitly loads the native-libraries at startup.
  * **Lazy:** The runtime provides callbacks to extract a bundled file to disk when it is actually necessary to be loaded.
    * Lazy extraction is risky because the time when a particular file is needed is not always known to the runtime (ex: dependencies of native DLLs loaded by the runtime).
    * Low startup cost
* ***Lifetime***
  * **Extract Always**: Extract out the necessary files to a random temporary location on each run
    * Simple implementation, since the app need not keep track of previous extractions
    * Higher startup cost, because of extraction on every run.
    * Current tools: Mono and Costura extract native dependencies to temporary files on every run.

  * **Reuse extraction**: Extract files to a specific location on first run, and re-use the extraction on subsequent runs.
    * Amortizes the startup cost over several runs.
    * Complex implementation: During startup, apps need to look for existing extractions, verify if they are usable, and extract missing pieces if necessary.
    * Unsuitable for customer scenarios that cannot tolerate persistent disk state after the app terminates.
* ***Location***
  * **Temporary Directory**
    * The extracted files are cleaned up automatically by the OS 
    * If extracted files are reused, the app should tolerate missing files
  * **Persistent directory**
    * Robust solution for reusing extracted files
    * Solution feels like an app-install, rather than a click-and-run executable. 

## Proposed Solution

### Extraction Mechanism

During startup, the host performs the following steps:

* Check if the [extraction directory](Extraction Location) exists for this app:
  * If not, perform a new extraction:
    * Extract appropriate files to a temporary location `<base>/<app>/<proc-id>` . This ensures that concurrent launches of the app do not interfere with each other.
    * After successful extraction of all necessary files to `<base>/<app>/<proc-id>`, the commit the extraction to the actual location `<base>/<app>/<bundle-id>`.
      - If `<base>/<app>/<bundle-id>` already exists by now, another process may have completed the extraction, so remove the directory `<base>/<app>/<proc-id>`. Verify and reuse the extraction at `<base>/<app>/<bundle-id>`.
      - If not,  `<base>/<app>/<proc-id>` is renamed to `<base>/<app>/<bundle-id>` 
      - The rename should be performed with appropriate retries to accommodate file-locking by antivirus software.
  * If it does, verify if all expected files are intact in the extraction location.
    * If all files are intact, the continue execution reusing the extracted files.
    * If not, extract out the missing components: For each missing file, extract it within `<base>/<app>/<proc-id>`, and rename it to the corresponding location within `<base>/<app>/<bundle-id>` , as described above.

### Extraction Location

The desirable characteristics of the extraction directory are:

- Should be configurable
- Should be semi-permanent: Not a directory that is lost frequently (ex: on machine reboot), but considered recyclable storage, since any subsequent run can re-extract.
- Be short, to reduce the risk of running over path length limit.

For a single-file app, the extraction directory is 

```bash
<base>/<app>/<bundle-id>
```

where:

* `<base>` is 
  * `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable, if set.
  * If not, defaults to: 
    * Windows: `%TEMP%\.net`
    * Unix:
      * `${TMPDIR}/.net/${UID}` if `${TMPDIR}` is set; otherwise,
      * `/var/tmp/.net/${UID}` if `/var/tmp` exists, and is writable; otherwise,
      * `/tmp/.net/${UID}` if `/tmp` exists, and is writable; otherwise fail.

* `<app>` is the name of the single-exe binary
* `<bundle-id>` is the unique [Bundle-identifier](bundler.md#bundle identifier). 

#### Unix: An Alternate Proposal 

On Unix-like systems, where multiple users may use a single system, an approach that removes the possibility of name collisions and other users creating files to prevent an application to start (by a malicious user creating a predictable directory name) is used instead:

* The directory to extract the bundle is created with `mkdtemp()`, using the `$TMPDIR` environment variable (if set), `/var/tmp/` (if exists, because it survives reboots), falling back to `/tmp` (does not survive reboots, it's often a ramdisk) in the template (e.g. `/var/tmp/dotnet-<app>-XXXXXX`);
  * To facilitate reuse of extraction, the extraction directory must be predictable. To achieve this, a symbolic link to the directory created by `mkdtemp()` is created in a predictable location:
    * If `${XDG_CACHE_HOME}` is set, the symlink is created under `${XDG_CACHE_HOME}/.cache/dotnet/<app>/<bundle-id>` (See [the XDG spec for information](https://specifications.freedesktop.org/basedir-spec/basedir-spec-latest.html)); otherwise,
    * Otherwise, the symlink is created under `~/.cache/dotnet/<app>/<bundle-id>`
  * In order to check for reuse on startup:
    * If the symbolic link exists and isn't stale (points to a directory owned by the user, with correct permissions (`0700`), etc.), that's what it is used;
    * If the symbolic link does not exist (or exists and is stale), it is removed, a new directory is created with `mkdtemp()`, and the link is re-created.

#### Cleanup

The cleanup of extracted files in the install-location will be manual, or through OS cleanup routines that remove files within the temporary directory.

## Extraction Implementation

In .NET Core3.0, the AppHost itself performed the extraction of bundled components. This was necessary because, the [HostFxr](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md#host-fxr) and [HostPolicy](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md#host-policy) DLLs themselves may be bundled (in self-contained builds), and need to be extracted out before extraction. In .net 5.0, this problem is solved by alleviating the need to extract host components, as explained in the [Host Builds](design.md#host-builds) section. 

In .net 5.0, the bundle-extraction functionality will be implemented in the HostPolicy component. The advantages of this approach are: 

* The AppHost is designed to be a minimal wrapper that finds and invokes the framework. It needs to be compatible with several .net versions, and is not easily serviceable. Therefore, moving the extraction out of the AppHost better suits its architecture.
* For Framework dependent apps, moves functionality into the framework, thus reducing AppHost size.
* For Framework dependent apps, makes extraction code easily serviceable.