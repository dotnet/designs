# Accessing Bundled Content

This document outlines the process by which the host and runtime components access files embedded within a single-file bundle directly (that is, without extracting out to temporary files on disk).  

### AppHost

The AppHost bundle-handling code implements the following service, which will be registered for use by other components.

```c++
// If the requested file is available in the bundle, the function provides 
//   - A pointer to a buffer with the contents of the file in memory 
//   - The size of the file.
// The function returns true if the file is found, false otherwise.
static bool read_bundled_file(const char* name, 
                              const void** buffer, 
                              size_t* size);
```

### HostFxr

The `Apphost` provides the a bundle-reader for `HostFxr` via the updated API:

```C++
SHARED_API int hostfxr_main_startupinfo(const int argc, 
                                        const pal::char_t* argv[], 
                                        const pal::char_t* host_path, 
                                        const pal::char_t* dotnet_root,
                                        const pal::char_t* app_path,
                                        bool (*bundle_reader)(const char* name, 
                                                              const void** buffer, 
                                                              size_t* size));
```

The `HostFxr` uses this reader to read `app.runtimeconfig.json` and any other files it may need from the bundle. If the `bundle_reader` is invalid or fails to find the requested file, it falls back to look for an actual file on disk.

### HostPolicy

The `HostFxr` registers the `bundle_reader` with `HostPolicy` via the updated API:

```C++
struct corehost_initialize_request_t
{
    size_t version;
    strarr_t config_keys;
    strarr_t config_values;
    bool (*bundle_reader)(const char* name, const void** buffer, size_t* size);
};

SHARED_API int __cdecl corehost_initialize(
    const corehost_initialize_request_t *init_request, 
    int32_t options,
    /*out*/ corehost_context_contract *context_contract);
```

Host policy uses this reader to attempt to process the `app.deps.json` file and check for the existence of other component files within the bundle.

### Runtime Components

The `HostPolicy` registers the `bundle-reader` with the runtime via a new API: 

```C++
// Register a bundle-reader with CoreCLR
//
// Parameters:
//  reader - The bundle-reader to use for this application
// Returns:
//  S_OK

CORECLR_HOSTING_API(coreclr_register_bundle_reader,
                    bool (*bundle_reader)(const char* name, 
                                          const void** buffer, 
                                          size_t* size));
```

`HostPolicy` communicates the location of files residing within the bundle with a special bundle-marker (`:`) in the CoreCLR properties such as `TRUSTED_PLATFORM_ASSEMBLIES`.

For example, if the bundle contains the following files:

- `il.dll` is a pure managed assembly that is loaded directly from the bundle
- `r2r.dll` is a ready-to-run assembly that is bundled and extract out to disk
- `dep.dll` is an assembly that is not bundled with the app,

The TPA will be set to `:/il.dll`; `/var/tmp/.net/app/e53xf3/r2r.dll`; `/usr/local/nuget/dep.dll`

If the runtime wants to load `il.dll`, it reads the contents of the file via the `bundle_reader` and loads the file using `PELoader`.