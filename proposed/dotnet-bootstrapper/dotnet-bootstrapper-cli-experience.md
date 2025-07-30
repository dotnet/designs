# dnUp Prototype CLI

Historical Context:

The officially supported `DNVM` and `DNX` project was replaced by the `.NET CLI` as `dotnet` in `2017`.

As of 2025, `DNVM` is a personal project owned by @agocke.

We are proposing an official `dnUp` prototype (name subject to change) that officially provides similar functionality, like `nvm` or `rustup` but for `.NET`. The framing and product document is hosted at https://hackmd.io/oyAjpxTNQKa60ipv-MmYSQ and being ported to GitHub soon.

If the prototype is successful, we would like to take those learnings and add the CLI to `dotnet` itself after further scrutiny as defined the product document.

## 🗃️ Installation Experience

```
dnup install
dnup install <channel>
```

### MVP:
`dnup`, `dnup install` → `First run || !dnup mode`:
Launches an interactive experience if the machine is not in a `dnUp` mode, and the `-y` flags are not added. That interactive experience is also launched in a terminal when the `dnUp` executable is first run. The interactive experience (defined by the product document) will set the PATH, and enable the `dnUp` mode, as specified by `dotnet-bootstrapper-path.md`. As part of that, it will run the standard `install` command experience, described below:

`dnup install` → In `dnup` mode:
Installs latest 'released' non-preview SDK, or the 'latest' acceptable SDK based on `global.json` if specified.

The SDK installed will be based on the zip installs provided in https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json.

This tooling will not support admin installs. We should make an effort to reduce the security context/elevation privileges for this tool, such as by spawning it as a standard user process.

* `global.json` lookup will follow the semantics of existing products.
  That is, to look at the `cwd`, and then look up until the `repo root` directory.

| Command | Description |
|---------|-------------|
| `dnup install 9.0` | Installs latest 9.0 SDK. Equivalent to `rollForward: latestFeature` in `global.json` |
| `dnup install latest` | Installs latest STS/LTS SDK that's not a preview SDK |
| `dnup install preview` | Installs latest preview SDK |
| `dnup install 9.0.102` | Installs fully specified version. Equivalent to `rollForward: disable` |

### Installation Location:

**Priority order:**
1. `-i --install-path` The custom directory specified by the user.
2. `global.json path value`. The `global.json` contains a `paths` key with several paths to .NET SDKs. Utilize the first one available to install, but scan every directory to check if the install already exists.
3. `Environment Variable`. `DOTNET_HOME/installs`. `DOTNET_HOME` is an `environment variable` which defaults to a user folder that is shared across repositories and is set when the program hits its 'first run.' If the environment variable is unspecified, the default of `DOTNET_HOME` is still utilized.

#### Defaults of `DOTNET_HOME`:
`Root Directory` + `Application Directory`

##### `Root Directory` Specification Version A (Simplified):

| OS | Path Resolution Order |
|----|----------------------|
| **Windows** | `%LOCALAPPDATA%`
| **Mac** | `$HOME`
| **Linux** | `$HOME`

##### `Application Directory`:
| OS | Directory |
|----|-----------|
| **Windows** | `/dnup` |
| **Mac** | `/dnup` |
| **Linux** | `/dnup` |

##### :star: `Root Directory` Specification Version B (Nuanced):

| OS | Path Resolution Order |
|----|----------------------|
| **Windows** | `%DOTNET_CLI_HOME%` → `%LOCALAPPDATA%` → `%USERPROFILE%` |
| **Mac** | `$DOTNET_CLI_HOME` → `$HOME` → `/Users/$USER` → `/Users` |
| **Linux** | `$DOTNET_CLI_HOME` → `$XDG_RUNTIME_DIR` → `$HOME` → `/home/$USER` → `/home` |

##### `Application Directory`:
| OS | Directory |
|----|-----------|
| **Windows** | `/dnup` |
| **Mac** | `/Library/Application\ Support/dnup/` |
| **Linux** | `/.local/dnup` |

### Additional Features
| Command | Description |
|---------|-------------|
| `dnup install 9` | Installs latest 9.X SDK, `rollForward: latestMinor` |
| `dnup install 9.0.1xx` | Installs latest 9.0.100 SDK. Equivalent to `rollForward: latestPatch` in `global.json` |

We should make an effort to align these with the GitHub actions/setup-dotnet conventions.

### Nice to Have:
| Command | Description |
|---------|-------------|
| `dnup install lts` | Installs latest LTS SDK. Updates are impacted as such |
| `dnup install sts` | Similar to the above but for STS |

### Future Options
| Option | Description |
|--------|-------------|
| `-f --force` | Install even when the SDK already exists |
| `-i --install-path` | Install at the specified directory |
| `-v --verbose` | Show logging messages in stdout |
| `-a --architecture` | Install for another architecture besides the detected system architecture |
| `-h --help` | Show help |
| `-u --url` | Use a custom `cdn` `releases.json` for locked down systems |

For installations that do not match the system architecture, we can mimic the behavior of the global installers, which would be to create an 'x64' folder at the root of the install folder that contains the 'x64' binaries, if the machine is 'arm64'. The same would be true for 'x86' on 'x64.'

`dnup sdk install <channel>` - noun first equivalent.
The SDK has recently prioritized `noun` first equivalent command options:
`https://github.com/dotnet/sdk/issues/9650`

### Additional Possibilities:
```
dnup runtime install <channel>
dnup runtime install --aspnetcore
dnup runtime install --windowsdesktop
```

### Implementation Detail

For every install made through `dnup`, the `global.json` `sdk`, `path`, `rollForward` value is recorded in a shared `dnup-manifest.json` file, stored at the root of `DOTNET_HOME`.

The `timestamp` of the last modified date of `global.json` is also stored, as we can prevent file reads each time by checking this.

The `dnup-manifest.json` also stores each `install` along with the `path` and `dependents` of that install, as `global.json` files or as a `dnup` dependent (fully specified static version).

#### Example dnup-manifest.json:

```json
{
  "globalJsonReferences": [
    {
      "filePath": "C:\\projects\\myproject\\global.json",
      "timestamp": "2025-04-15T14:30:22.1234567Z",
      "sdk": {
        "version": "9.0.100",
        "rollForward": "latestFeature",
        "allowPrerelease": false,
        "paths": ["C:\\Program Files\\dotnet\\sdk"]
      },
    },
  ],
  "installations": [
    {
      "version": "9.0.100",
      "mode": "sdk",
      "architecture": "x64",
      "path": "C:\\Users\\username\\dnup\\installs\\9.0.100",
      "channel": "unspecified",
      "dependents": [
        {
          "type": "globalJson",
          "path": "C:\\projects\\myproject\\global.json"
        }
      ]
    },
    {
      "version": "9.0.200-preview.1",
      "mode": "sdk",
      "architecture": "x64",
      "path": "C:\\Users\\username\\dnup\\installs\\9.0.200-preview.1",
      "channel": "preview",
      "dependents": [
        {
          "type": "dnvm",
          "path": "C:\\Users\\User\\AppData\\Local\\dnup\\installs\\9.0.200-preview.1\\dotnet.exe"
        }
      ]
    }
  ]
}
```

This manifest tracks:
1. All referenced `global.json` files with their SDK requirements and timestamps
2. All SDK installations with their paths and dependent references
3. The source/channel of each installation
4. Dependencies linking installations to `global.json` files or direct dnup commands

### Install Terminology

`Channel` is an overloaded term and previous products have been blocked from using the term `channel`.

Channel is already used by Visual Studio.
It is also used by the existing dnup to describe an SDK as 'latest' or 'sts.'
Utilizing the name `channel` may be tricky.

In the broadest interpretation, a `channel` may end up meaning several things:
- `Support Phase` and or `Release Type` (`lts` / `sts` / `preview`)
- `Version` (`9.0`, `9.0.2xx`)
- Something Else (`latest`)

Other names:
- Release
- Ring

### Related Offerings

#### DNVM (https://github.com/dn-vm/dnvm/tree/main/src/dnvm)

`DNVM install` Only supports a fully specified SDK version.
`DNVM track <channel>` supports installing an SDK based on the channel.

**Channels:**
`lts`, `sts`, `preview`, `latest`.

Installs created with a fully specified version are not update-able.
Update terminology is not aware of the `global.json`.

`dnup restore` is the command that is aware of `global.json`, to the extent of installing based on the `rollForward` value and `sdk` of the file.
`dnup track` must be used to install utilizing a `channel`.

`dnup update` updates all tracked SDKS.

#### NVM

Not available on Windows, though `nvm-windows` is a product.
`nvm use <version>` enables usage of a specific version of `node.js`.
Installs at `NVM_DIR` which is `$HOME/.nvm`.
`nvm install --lts`.

https://github.com/nvm-sh/nvm

#### RustUp
`rustup install <toolchain>` installs based on `stable`, `nightly` or a fully specified version.
`rustup update` updates based on `sts`.
`rustup self update` updates itself.
Modified the `PATH`.
Installs are in `~/.cargo/bin`.

More information on other internal docs.

## ♻️ Update Experience

### MVP
`dnup update` Updates all SDKS / Runtimes managed by `dnup`. Uninstalls the older SDKs that are not installed as fully specified versions. Older SDKs are defined by ones that are no longer needed as specified by the summation of all known `global.json` files.

For each possible update, `-y`, `-n` prompt is given.

#### Options
| Option | Description |
|--------|-------------|
| `-y --yes` | Auto accept |
| `-w --what-if` | Show what would be updated, basically like auto 'no' |
| `-h --help` | Show help |
| `-u --url` | Use a custom `cdn` `releases.json` for locked down systems |

#### Nice to Have
| Command | Description |
|---------|-------------|
| `dnup update version` | Only update the specified SDK managed by `dnup` |
| `dnup self update` | Updates the `dnup` product |

#### Notes
`dnup track` is not provided.
`dnup untrack` is not provided.

## 🗑️ Uninstall Experience

`dnup uninstall <channel>` - Uninstalls the specified SDK. Uninstall is simply a file deletion. No updates to the `PATH` are made, except in the case that this is the final remaining installation, in which case `DOTNET_HOME` is removed from the `PATH` to ensure the machine state remains unchanged.

`dnup prune` - Prunes older SDKs, akin to `dnvm prune`.

The channels supported above work here.
Example: `dnup uninstall latest`.
`dnup uninstall 8.0` would uninstall all `8.0` SDKs that are not installed via a `fully specified` mechanism.

`dnup uninstall latest` will prompt with `-y` or `-n` if `global.json` dependencies are still on the machine for this SDK.

## Additional Thoughts

`dnup --info` should include dnup's architecture and version.

Self-updating: This will be discussed in another document. In this context, dnup will become an AOT app or something runs along side the host.
