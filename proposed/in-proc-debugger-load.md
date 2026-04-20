# In-proc debugger load hook

**Owner** [Aaron Robinson](https://github.com/AaronRobinsonMSFT) | [Steve Pfister](https://github.com/steveisok)

Mobile and other non-desktop .NET hosts need a supported way to run the debugger in-proc. On these platforms, the traditional model of launching a separate debugger process is not viable, so the host needs a supported way to load the native debugger component before the first managed instruction executes. This proposal introduces a narrow, host-controlled in-proc debugger load hook for that purpose.

The hook is intended for Android and iOS debugging scenarios and similar host-owned diagnostics integrations. It is deliberately not a general-purpose native extensibility point.

## Scenarios and User Experience

### Android and iOS debugging

Tooling that launches a mobile debug session needs a supported way to load a native debugger support library before the first managed code executes.

With the proposed design, tooling sets `DOTNET_INPROC_DEBUGGER` for supported debug launches. The host resolves the specified library and loads it during startup so the native component can perform its debugger-specific initialization before managed entrypoint execution.

```text
DOTNET_INPROC_DEBUGGER=libdebugger.so
```

Conceptually, one possible host flow is:

```c
coreclr_initialize(...);
dlopen(resolve_debugger_hook(getenv("DOTNET_INPROC_DEBUGGER")), RTLD_NOW);
coreclr_execute_assembly(...);
```

The important requirement is that the load occurs before `coreclr_execute_assembly(...)` starts managed code. The exact ordering relative to `coreclr_initialize(...)` is a host implementation detail.

### Standard app launch

If no debugger load hook is configured, application startup is unchanged. There is no additional runtime cost, API surface, or user-visible behavior.

### WASM and other non-desktop targets

Hosts that need early debugger loading can opt into this mechanism. Hosts that do not implement it remain unchanged. The goal is to provide a clear debugger hook where needed, not to require a uniform implementation across every runtime and host.

## Requirements

### Goals

- Provide a supported, host-controlled mechanism for loading a native debugger or diagnostics component during startup.
- Preserve existing Android and iOS debugger bootstrapping scenarios.
- Keep the replacement mechanism narrowly scoped to in-process debugger and diagnostics scenarios.
- Allow hosts to define platform-appropriate path semantics, such as bundle-relative resolution on iOS.
- Avoid introducing a new, broad CoreCLR-level unmanaged startup extensibility surface.
- Make failures explicit when a requested debugger library cannot be loaded.
- Keep the initial design limited to loading exactly one library.
- Enable cleanup of legacy profiler-based bootstrapping on non-desktop targets.

### Non-Goals

- Providing general-purpose native startup extensibility for arbitrary third-party code.
- Continuing or expanding profiler support on non-desktop or WASM targets.
- Standardizing support for multiple libraries in the initial design.
- Guaranteeing availability of this hook on every .NET runtime or host.

## Stakeholders and Reviewers

- `dotnet/runtime` maintainers for mobile, browser, and other non-desktop hosts
- Debugger and diagnostics tooling teams that rely on early native component loading
- IDE and launch tooling teams that need predictable, supported mobile debugging startup behavior

## Design

### Background

Some current mobile debugging flows reuse profiler-related startup plumbing as a practical way to load a native binary during startup. That was a convenient implementation detail, not the intended contract. The actual motivation is that mobile environments cannot depend on the usual out-of-proc debugger architecture (`mscordbi`/DAC plus an external debugger process). For .NET 11, the debugger needs to run in-proc instead, and the host needs a supported way to load that native component before managed code starts.

### Proposed model

The replacement should be a host-owned, debugger-specific load hook:

1. A host opts into recognizing `DOTNET_INPROC_DEBUGGER`.
2. Tooling sets that variable only for supported debug launches.
3. The host resolves the value to a single native library location using host-defined rules.
4. The host loads the library before managed entrypoint execution.
5. If the library cannot be loaded, the host reports a clear error and fails the debug launch rather than silently continuing.

This keeps the behavior narrow and directly aligned with the only scenario we need to preserve: early loading of an in-proc debugger component.

### What the hook is for

The hook is intended to support debugger and diagnostics components that must be present before the app's managed entrypoint runs. Typical uses include:

- loading the native side of an Android or iOS in-proc debugger,
- performing debugger startup work that has to happen before managed code begins, and
- keeping launch-time debugger behavior under host and tooling control rather than exposing a new runtime-wide extension point.

### Naming and scope

This proposal uses `DOTNET_INPROC_DEBUGGER` as the environment variable name. Alternative names were considered, but this one is explicit about the intended purpose and avoids presenting the feature as a general-purpose native extension point.

### Host-specific behavior

The host owns path interpretation:

- **iOS** may prefer paths relative to the app bundle instead of requiring an absolute path.
- **Android** may support app-local or absolute paths as appropriate for the launch environment.
- **Other hosts** are not required to implement this mechanism.

This design intentionally leaves the exact path grammar and loading mechanics to the host so we do not over-specify a cross-platform contract that only a small number of hosts need.

### Why not add `DOTNET_STARTUP_HOOKS_UNMANAGED`?

A CoreCLR-level `DOTNET_STARTUP_HOOKS_UNMANAGED` mechanism would be more general, but it would also:

- enlarge the runtime support matrix,
- create a new extensibility surface that we are not prepared to support broadly, and
- solve a much larger problem than the one at hand.

If future evidence shows that multiple runtimes or hosts need the same capability, the design can be revisited. The current need is narrower and is better addressed in the host.

### Security and supportability

This proposal does not meaningfully change the trust model because environment variables are already treated as trusted process input. This feature should be treated as public because tooling will intentionally depend on it, but it should still be documented and supported as a narrowly scoped debugger hook on participating hosts rather than a general plugin model for arbitrary native code.

## Q & A

### Why is this hook needed?

Because mobile debugging on these platforms cannot rely on the traditional separate debugger process model. This hook makes the in-proc requirement explicit and gives hosts a purpose-built mechanism to load the native debugger before the first managed instruction executes.

### Why not make this a general native extension mechanism?

Because the motivating scenario is narrow and well understood. A general unmanaged startup hook would be a much larger commitment in runtime surface area, compatibility, and support.

### How does this relate to ICorProfiler?

A small amount of current startup behavior reuses profiler plumbing to achieve early debugger loading. This proposal replaces that incidental dependency with a dedicated debugger hook so the feature being supported matches the scenario we actually care about.

### Is the hook public and documented?

Yes. It should be publicly documented at an appropriate level so maintainers and tooling teams can rely on it intentionally. This design doc, plus a pointer from the relevant public docs, should be sufficient while still keeping the feature clearly scoped to debugger and diagnostics scenarios.
