# .NET Core GC Support for Docker Limits

**Owner** [Rich Lander](https://github.com/richlander)

.NET Core has support for [control groups](https://en.wikipedia.org/wiki/Cgroups) (cgroups), which is the basis of [Docker limits](https://docs.docker.com/config/containers/resource_constraints/). We found that the algorithm we use to honor cgroups works well for larger memory size limits (for example, >500MB), but that it is not possible to configure a .NET Core application to run indefinitely at lower memory levels. This document proposes an approach to support low memory size limits, for example <100MB.

Note: Windows has a concept similar to cgroups called [job objects](https://docs.microsoft.com/windows/desktop/ProcThread/job-objects). .NET 6+ correctly honors job objects in the same way as cgroups. This document will focus on cgroups throughout.

It is critical to provide effective and well defined experiences when .NET Core applications are run within memory-limited cgroups. An application should run indefinitely given a sensible configuration for that application. We considered relying on orchestators to manage failing applications (that can no longer satisfy the configuration of a cgroup), but believe this to be antithetical as a primary solution for building reliable systems. We also expect that there are scenarios where orchestrators will be unavailable or primitive or hardware will be constrained, and therefore not tolerant of frequently failing applications. As a result, we need a better tuned algorithm for cgroup support to the end of running reliable software within constrained environments.

See [implementing hard limit for GC heap dotnet/coreclr #22180](https://github.com/dotnet/coreclr/pull/22180).

See [Validate container improvements with .NET 6](https://github.com/dotnet/runtime/issues/53149).

## GC Heap Hard Limit

The following configuration knobs will be exposed to enable developers to configure their applications:

* `GCHeapHardLimit` - specifies a hard limit for the GC heap as an absolute value
* `GCHeapHardLimitPercent` - specifies a hard limit for the GC heap as a percentage of physical memory that the process is allowed to use

If both are specified, `GCHeapHardLimit` is used.

The `GCHeapHardLimit` will be calculated using the following formular if it is not specified and the process is running inside a container (or cgroup or job object) with a memory limit specified:

```console
max (20mb, 75% of the memory limit on the container)
```

The GC will more aggressive perform GCs as the GC heap grows closer to the `GCHeapHardLimit` with the goal of making more memory available so that the application can continue to safely function. The GC will avoid continuously performing full blocking GCs if they are not considered productive.

The GC will throw an `OutOfMemoryException` for allocations that would cause the committed heap size to exceed the `GCHeapHardLimit` memory size, even after a full compacting GC.

## GC Heap Minimum Size

Using Server GC, there are multiple GC heaps created, up to one per core. This model doesn't scale well when a small memory limit is set on a machine with many cores.

The minimum _reserved_ segment size per heap: `16 MiB`

Example -- CPU unconstrained:

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

Example -- CPU constrained:

```bash
docker run --rm -m 256mb --cpus 2 mcr.microsoft.com/dotnet/samples
```

* 48 core machine
* cgroup has a 256MiB memory limit
* cgroup has 2 CPU/core limit
* 192MB `GCHeapHardLimit`
* Server GC will create 2 GC heaps, with 16 MiB reserved memory
* Only 2 cores can be used by the application

There are other scenarios, like using `--cpuset-cpus` but they all follow from these two examples.

If [`DOTNET_PROCESSOR_COUNT`](https://github.com/dotnet/runtime/issues/48094) is set, including if it differs from `--cpus`, then the GC will use the ENV value for determining the maximum number of heaps to create.

Note: .NET Framework has the same behavior but `COMPlus_RUNNING_IN_CONTAINER` must be set. Also processor count is affected (in the same way) by `COMPlus_PROCESSOR_COUNT`.

## Previous behavior

Previously, the **maximum GC heap size** matched the cgroup limit.
