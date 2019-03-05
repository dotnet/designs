# Proposal for .NET Core GC Support for Docker Limits

.NET Core has support for [control groups](https://en.wikipedia.org/wiki/Cgroups) (cgroups), which is the basis of [Docker limits](https://docs.docker.com/config/containers/resource_constraints/). We found that the algorithm we use to honor cgroups works well for larger memory size limits (for example, >500MB), but that it is not possible to configure a .NET Core application to run indefinitely at lower memory levels. This document proposes an approach to support low memory size limits, <100MB.

Note: Windows has a concept similar to cgroups called [job objects](https://docs.microsoft.com/windows/desktop/ProcThread/job-objects). .NET Core should honor job objects in the same way as cgroups, as appropriate. This document will focus on cgroups throughout.

It is critical to provide effective and well defined experiences when .NET Core applications are run within memory-limited cgroups. An application should run indefinitely given a sensible configuration for that application. We considered relying on orchestators to manage failing applications (that can no longer satisfy the configuration of a cgroup), but believe this to be antithetical as a primary solution for building reliable systems. We also expect that there are scenarios where orchestrators will be unavailable or primitive or hardware will be constrainted, and therefore not tolerant of frequently failing applications. As a result, we need a better tuned algorithm for cgroup support to the end of running reliable software within constrained environments.

See [implementing hard limit for GC heap dotnet/coreclr #22180](https://github.com/dotnet/coreclr/pull/22180).

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

## GC Heap Heap Minimum Size

Using Server GC, there are multiple GC heaps created, up to one per core. This model doesn't scale well when a small memory limit is set on a machine with many cores.

The minimum _reserved_ segment size per heap: 16mb

Example:

* 48 core machine
* cgroup has a 200MB memory limit
* cgroup has no CPU/core limit
* 160MB `GCHeapHardLimit`
* Server GC will create 10 GC heaps
* All 48 cores can be used by the application

Example:

* 48 core machine
* cgroup has a 200MB memory limit
* cgroup has 4 CPU/core limit
* 160MB `GCHeapHardLimit`
* Server GC will create 4 GC heaps
* Only 4  cores can be used by the application

## Previous behavior

Previously, the **maximum GC heap size** matched the cgroup limit.
