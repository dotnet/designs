# Extracting Bundled Files to Disk

When running the single-file, the host will need to extract some bundled files resources out to the disk before loading them. This is particularly true for native DLLs because of operating system API restrictions -- for example, Windows doesn't support a `LoadLibrary()` API that reads in from a stream. 

We now explore the options with respect to the location and life-time of the extracted files.

## Time of Extraction

* **Startup**: The host spills the necessary files at startup, and communicates the location to the runtime.
* **Lazy:** The runtime provides callbacks to extract a bundled file to disk when it is actually necessary to be loaded. 

Extraction at startup is easier to implement, as the runtime needs no knowledge of the extraction. 

Lazy extraction saves startup cost, particularly if many ready-to-run images are bundled. The lazy extraction feature is likely useful only when files are extracted on every run.

Mono extracts native libraries at startup. Costura extracts and explicitly loads the native-libraries at startup.

## Temporary Extraction

The first option is to extract out the necessary files to a temporary location, and clean them out on exit. The requirements for this path are:

* Extract to a location within the temp-directory, so that if some files are not removed (ex: because an app crashed before cleanup), they are removed when the temp-directory is purged.
* Be short, to reduce the risk of running over path length limit.
* Be randomized, in order to support concurrent launches of the same app.

The proposed extraction path is `%TEMP\.net\<random-code>` / `$TMPDIR/.net/<random-code>` . 

#### Pros

* Simpler implementation: fewer complexities with respect to app-updates and concurrency.
* User need not worry about explicit cleanup.
* Some customers don't prefer a click-and-run model, rather than an app-install model. This option better suits them.

#### Cons

* Higher startup cost, because of extraction on every run -- especially for self-contained apps.

Mono and Costura extract native dependencies to temporary files on every run.

## Persistent Extraction

The requirements for persistent extraction are:

* Reuse: Extract on first-run, reuse on subsequent runs.
* Fault-tolerance: Recover from failure after partial extraction on first run.
* Concurrency: Handle concurrent launches on first start.
* Upgrade: Each version of the app extracts to a unique location, supporting side-by-side use of multiple versions.
* Uninstall: Users can identify and delete extracted files when the app is no longer needed.
* Access control: Processes running with elevated access can extract to admin-only-writable locations.

At first startup, the host 

* Takes a disk lock (in order to handle concurrent launches).
* Extracts appropriate files to an *install-location* on disk, and verifies them.
* If all files are extracted successfully, host writes an *end-marker*. The end-marker will include name and version information for the app's main assembly in plain text, in order to help users in associating install-locations with app versions.
* If there are failures, host attempts to clean up all contents of the *install-location*
* Releases the disk lock. 

Subsequent runs reuse the extracted files after confirming their successful extraction. However, if the contents of install-location are corrupted post-extraction, they will need to be explicitly cleaned.

#### Install Location

By default, the host extracts dependencies to `BASE_PATH/APP_NAME/ID/PERM/`

- `BASE_PATH` is  `%HOMEPATH%\.dotnet\Apps ` /  `$HOME/.dotnet/Apps`
- `APP_NAME` is simple the name of the app (the host executable containing embedded data).
- `ID` is a hash-code based on the host binary to distinguish app version and architecture specifications.
- `PERM` is `user` for normal runs, and `admin` for elevated runs with appropriate write-permissions.

A different extraction location may be specified via the runtime configuration settings:

```json
{
    "runtimeoptions": {
        "bundledFileExtractionLocation": "<path>"
    }
}
```

#### Cleanup

The cleanup of extracted files in the install-location will be manual in this version. We can consider adding `dotnet CLI` commands for cleanup in future. However, future versions are expected to spill fewer artifacts to disk, making the cleanup commands a lower priority feature.

#### Pros

- Saves startup cost, particularly for self-contained apps, or apps containing several native binary dependencies.
- No need for implementing lazy-extraction.

#### Cons

- Complex implementation.
- Unsuitable for customer scenarios that cannot tolerate persistent disk state after the app terminates.
- Cleanup is manual.

## Proposed Solution

The above implementation options suit different customer scenarios. So, instead of picking an extraction policy, we can implement both strategies, and let app-developers choose a strategy as part of the app's configuration.

For .Net Core 3.0, we propose that we start with the simple implementation that extracts necessary files to temporary locations on every run at startup. Based on customer feedback, we can implement other options discussed above.