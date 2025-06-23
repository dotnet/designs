# DNVM / Dotnet Bootstrapper End-to-End CLI Experience

Initial Context:

The officially supported `DNVM` and `DNX` project was replaced by the `.NET CLI` as `dotnet` in `2017`.

As of 2025, `DNVM` is an unofficial product, but internally developed and leveraged.

We are proposing an official `DNVM` product (name subject to change).
This is an offering similar to `nvm` or `rustup` but for `.NET`.
There have already been many internal documents/discussions. This is a 'strawman proposal' for us to dig at further. For all mentions of the term `dnvm`, this is subject to change to an applicable product name.

The `dnvm` product will be AOT bundled alongside the apphost to enable shipment via an external standalone executable, that is also callable from within the .NET SDK. The implementation details of this will be settled and discussed within another document, but it's necessary to remark on this here to explain the usage of 'dotnet' as a root command.

## 🗃️ Installation Experience

```
dotnet install
dotnet install <channel>
```

### MVP:
`dotnet install` → installs latest STS/LTS SDK, or the 'latest' acceptable SDK based on `global.json` if specified. The installation modifies the user-level `PATH`. (This is complicated and will get its own document.)
* `global.json` lookup will follow the semantics of existing products.
  That is, to look at the `cwd`, and then look up until the `repo root` directory.

| Command | Description |
|---------|-------------|
| `dotnet install 9.0` | Installs latest 9.0 SDK. Equivalent to `rollForward: latestFeature` in `global.json` |
| `dotnet install latest` | Installs latest STS/LTS SDK |
| `dotnet install preview` | Installs latest preview SDK |
| `dotnet install 9.0.102` | Installs fully specified version. Equivalent to `rollForward: disable` |

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
| **Windows** | `/dnvm` |
| **Mac** | `/dnvm` |
| **Linux** | `/dnvm` |

##### :star: `Root Directory` Specification Version B (Nuanced):

| OS | Path Resolution Order |
|----|----------------------|
| **Windows** | `%DOTNET_CLI_HOME%` → `%LOCALAPPDATA%` → `%USERPROFILE%` |
| **Mac** | `$DOTNET_CLI_HOME` → `$HOME` → `/Users/$USER` → `/Users` |
| **Linux** | `$DOTNET_CLI_HOME` → `$XDG_RUNTIME_DIR` → `$HOME` → `/home/$USER` → `/home` |

##### `Application Directory`:
| OS | Directory |
|----|-----------|
| **Windows** | `/dnvm` |
| **Mac** | `/Library/Application\ Support/dnvm/` |
| **Linux** | `/.local/dnvm` |

### Additional Features
| Command | Description |
|---------|-------------|
| `dotnet install 9` | Installs latest 9.X SDK |
| `dotnet install 9.0.1xx` | Installs latest 9.0.100 SDK. Equivalent to `rollForward: latestFeature` in `global.json` |

### Nice to Have:
| Command | Description |
|---------|-------------|
| `dotnet install lts` | Installs latest LTS SDK. Updates are impacted as such |
| `dotnet install sts` | Similar to the above but for STS |

### Future Options
| Option | Description |
|--------|-------------|
| `-f --force` | Install even when the SDK already exists |
| `-i --install-path` | Install at the specified directory |
| `-v --verbose` | Show logging messages in stdout |
| `-a --architecture` | Install for another architecture besides the detected system architecture |
| `-h --help` | Show help |
| `-u --url` | Use a custom `cdn` `releases.json` for locked down systems |

`dotnet sdk install <channel>` - noun first equivalent.
The SDK has recently prioritized `noun` first equivalent command options:
`https://github.com/dotnet/sdk/issues/9650`

### Additional Possibilities:
```
dotnet runtime install <channel>
dotnet runtime install --aspnetcore
dotnet runtime install --windowsdesktop
```

### Implementation Detail

For every install made through `dnvm`, the `global.json` `sdk`, `path`, `rollForward` value is recorded in a shared `dnvm-manifest.json` file, stored at the root of `DOTNET_HOME`.

The `timestamp` of the last modified date of `global.json` is also stored, as we can prevent file reads each time by checking this.

The `dnvm-manifest.json` also stores each `install` along with the `path` and `dependents` of that install, as `global.json` files or as a `dnvm` dependent (fully specified static version).

#### Example dnvm-manifest.json:

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
      "path": "C:\\Users\\username\\dnvm\\installs\\9.0.100",
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
      "path": "C:\\Users\\username\\dnvm\\installs\\9.0.200-preview.1",
      "channel": "preview",
      "dependents": [
        {
          "type": "dnvm",
          "path": "C:\\Users\\User\\AppData\\Local\\dnvm\\installs\\9.0.200-preview.1\\dotnet.exe"
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
4. Dependencies linking installations to `global.json` files or direct DNVM commands

### Install Terminology

`Channel` is an overloaded term and previous products have been blocked from using the term `channel`.

Channel is already used by Visual Studio.
It is also used by the existing DNVM to describe an SDK as 'latest' or 'sts.'
Utilizing the name `channel` may be tricky.

In the broadest interpretation, a `channel` may end up meaning several things:
- `Support Phase` and or `Release Type` (`lts` / `sts` / `preview`)
- `Version` (`9.0`, `9.0.2xx`)
- Something Else (`latest`)

Other names:
- Release
- Ring

### Competitor Offerings

#### DNVM (https://github.com/dn-vm/dnvm/tree/main/src/dnvm)

`dnvm install` Only supports a fully specified SDK version.
`dnvm track <channel>` supports installing an SDK based on the channel.

**Channels:**
`lts`, `sts`, `preview`, `latest`.

Installs created with a fully specified version are not update-able.
Update terminology is not aware of the `global.json`.

`dnvm restore` is the command that is aware of `global.json`, to the extent of installing based on the `rollForward` value and `sdk` of the file.
`dnvm track` must be used to install utilizing a `channel`.

`dnvm update` updates all tracked SDKS.

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
`dotnet update` Updates all SDKS / Runtimes managed by `dnvm`. Uninstalls the older SDKs that are not installed as fully specified versions. Older SDKs are defined by ones that are no longer needed as specified by the summation of all known `global.json` files.

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
| `dotnet update version` | Only update the specified SDK managed by `dnvm` |
| `dotnet self update` | Updates the `dnvm` product |

#### Notes
`dotnet track` is not provided.
`dotnet untrack` is not provided.

## 🗑️ Uninstall Experience

`dotnet uninstall <channel>` - Uninstalls the specified SDK. Uninstall is simply a file deletion. No updates to the `PATH` are made.

`dotnet prune` - Prunes older SDKs, akin to `dnvm prune`.

The channels supported above work here.
Example: `dotnet uninstall latest`.
`dotnet uninstall 8.0` would uninstall all `8.0` SDKs that are not installed via a `fully specified` mechanism.

`dotnet uninstall latest` will prompt with `-y` or `-n` if `global.json` dependencies are still on the machine for this SDK.
