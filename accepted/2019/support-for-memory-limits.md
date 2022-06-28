# .NET GC Support for Docker Limits

**Owner** [Rich Lander](https://github.com/richlander)

.NET has support for [control groups (cgroups)](https://en.wikipedia.org/wiki/Cgroups) , which is the basis of [Docker resource limits](https://docs.docker.com/config/containers/resource_constraints/). .NET has supported cgroups since .NET Core 2.1.

Windows has a concept similar to cgroups called [job objects](https://docs.microsoft.com/windows/desktop/ProcThread/job-objects). .NET 6+ correctly honors job objects in the same way as cgroups. This document will focus on cgroups throughout.

It is critical to provide effective and well-defined capabilities for .NET applications within memory-limited cgroups. An application should run indefinitely given a sensible configuration for that application. It is important that .NET developers have good controls to optimize their container hosted applications. Our goal is that certain classes of .NET applications can be run with <100 MiB memory constraints.

Related:

- [implementing hard limit for GC heap dotnet/coreclr #22180](https://github.com/dotnet/coreclr/pull/22180).
- [Validate container improvements with .NET 6](https://github.com/dotnet/runtime/issues/53149).

## Cgroup constraints

cgroups control two main resources: memory and cores. Both are relevant to the .NET GC.

Memory constraints defines the maximum memory available to the cgroup. This memory is used by the guest operating system, the .NET runtime, the GC heap, and potentially other users. If a cgroup has `100 MiB` available, the app will have less than that. The cgroup will be terminated (AKA `OOMKilled`) when the memory limit is reached.

Core constraints determine how many GC heaps should be created, at maximum. The maximum heap value matches `Environment.ProcessorCount`. There are three primary ways that this value can be set (using the `docker` CLI to demonstrate):

- Not specified -- `Environment.ProcessorCount` will match the total number of machine cores.
- Via `--cpus` -- `Environment.ProcessorCount` uses that (decimal) value (rounded up to the next integer).
- Via `--cpu-sets` -- `Environment.ProcessorCount` matches the count of specified CPUs.
- `DOTNET_PROCESSOR_COUNT` -- `Environment.ProcessorCount` uses this value. If other values are also specified, they are ignored.

In the general case, there will be one heap per core. If the GC creates too many heaps, that can over-eagerly use up the memory limit, at least in part. There are controls to avoid that.

## GC Heap Hard Limit

The *GC Heap Hard Limit* is the maximum managed heap size. It only applies when running within a cgroup. By default, it is lower than the cgroup memory constraint (AKA "the cgroup hard limit").

The following configuration knobs are exposed to configure applications:

* `GCHeapHardLimit` - specifies a hard limit for the GC heap as an absolute value, in bytes (hex value).
* `GCHeapHardLimitPercent` - specifies a hard limit for the GC heap as a percentage of the cgroup hard limit (hex value).

If both are specified, `GCHeapHardLimit` is used.

By default, the `GCHeapHardLimit` will be calculated using the following formula:

```console
max (20mb, 75% of the memory limit on the container)
```

The GC will more aggressive perform GCs as the GC heap grows closer to the `GCHeapHardLimit` with the goal of making more memory available so that the application can continue to safely function. The GC will avoid continuously performing full blocking GCs if they are not considered productive.

The GC will throw an `OutOfMemoryException` for allocations that would cause the committed heap size to exceed the `GCHeapHardLimit` memory size, even after a full compacting GC.

## GC Heap Count

Using Server GC, there are multiple GC heaps created, up to one per core. This model doesn't scale well when a small memory limit is set on a machine with many cores.

The heap count can be set two ways:

- Manually via `DOTNET_GCHeapCount`.
- Automatically by the GC, relying on:
  - Number of observed or configured cores.
  - A minimum _reserved_ memory size per heap of `16 MiB`.

If [`DOTNET_PROCESSOR_COUNT`](https://github.com/dotnet/runtime/issues/48094) is set, including if it differs from `--cpus`, then the GC will use the ENV value for determining the maximum number of heaps to create.

Note: [.NET Framework 4.8 and 4.8.1](https://github.com/microsoft/dotnet-framework-docker/discussions/935) have the same behavior but `COMPlus_RUNNING_IN_CONTAINER` must be set. Also processor count is affected (in the same way) by `COMPlus_PROCESSOR_COUNT`.

Let's look at some examples.

### Memory constrained; CPU unconstrained

```bash
docker run --rm -m 256mb mcr.microsoft.com/dotnet/samples
```

* 48 core machine
* cgroup has a 256MiB memory limit
* cgroup has no CPU/core limit
* 192MB `GCHeapHardLimit`
* Server GC will create 12 GC heaps, with 16 MiB reserved memory
* All 48 cores can be used by the application, per [container policy](https://docs.docker.com/config/containers/resource_constraints/#cpu)

`heaps = (256 * .75) / 16`
`heaps = 12`

### Memory and CPU constrained

```bash
docker run --rm -m 256mb --cpus 2 mcr.microsoft.com/dotnet/samples
```

* 48 core machine
* cgroup has a 256MiB memory limit
* cgroup has 2 CPU/core limit
* 192MB `GCHeapHardLimit`
* Server GC will create 2 GC heaps, with 16 MiB reserved memory
* Only 2 cores can be used by the application

### Memory and CPU constrained (with CPU affinity):

```bash
docker run --rm -m 256mb --cpuset-cpus 0,2,3 mcr.microsoft.com/dotnet/samples
```

* 48 core machine
* cgroup has a 256MiB memory limit
* cgroup has 3 CPU/core limit
* 192MB `GCHeapHardLimit`
* Server GC will create 3 GC heaps, with 16 MiB reserved memory
* Only 3 cores can be used by the application

### Memory and CPU constrained (overriden by `DOTNET_PROCESSOR_COUNT`):

```bash
docker run --rm -m 256mb --cpus 2 -e DOTNET_PROCESSOR_COUNT=4 mcr.microsoft.com/dotnet/samples
```

* 48 core machine
* cgroup has a 256MiB memory limit
* cgroup has 2 CPU/core limit
* 192MB `GCHeapHardLimit`
* Server GC will create 4 GC heaps, with 16 MiB reserved memory
* Only 2 cores can be used by the application
