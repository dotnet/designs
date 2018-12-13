# Proposal for .NET Core GC Support for Docker Limits

.NET Core has support for [control groups](https://en.wikipedia.org/wiki/Cgroups) (cgroups), which is the basis of [Docker limits](https://docs.docker.com/config/containers/resource_constraints/). We found that the algorithm we use to honor cgroups works well for larger memory size limits (for example, >500MB), but that it is not possible to configure a .NET Core application to run indefinitely at lower memory levels. This document proposes an approach to support low memory size limits, <100MB.

Note: Windows has a concept similar to cgroups called [job objects](https://docs.microsoft.com/windows/desktop/ProcThread/job-objects). .NET Core should honor job objects in the same way as cgroups, as appropriate. This document will focus on cgroups throughout.

It is critical to provide effective and well defined experiences when .NET Core applications are run within memory-limited cgroups. An application should run indefinitely given a sensible configuration for that application. We considered relying on orchestators to manage failing applications (that can no longer satisfy the configuration of a cgroup), but believe this to be antithetical to building reliable systems. We also expect that there are scenarios where orchestrators will be unavailable or primitive or hardware will be constrainted, and therefore not tolerant of frequently failing applications. As a result, we need a better tuned algorithm for cgroup support to the end of running reliable software within constrained environments.

## Archetypal Experiences

We identified two archetypal experiences (with arbitrary metrics provided) that we want to enable:

* Maximum RPS that can be maintained indefinitely with 64MB of memory allocated
* Minimum memory size required to indefinitely maintain 100 requests per second (RPS)

These experiences fix a single metric and expect the application to otherwise function indefinitely. They each offer a specific characteristic that we expect will align with a specific kind of workload.

A cloud hoster might want to fix the memory allocated to an application to improve hosting density and profitability. They will look to our documentation to understand the maximum RPS that they can promise their customers in this configuration, as demonstrated by a sample application like Music Store.

An IoT developer might want to determine the minimum amount of memory that can be used to maintain a given RPS metric, typically a very low level <100 RPS.

## GC Configuration

We will expose the following configuration knobs (final naming TBD) to enable developers to define their own policies, with the following default values (final values TBD).

* **Native memory budget**: 20MB
* **Minimum GC heap size**: 20MB
* **Maximum GC heap size**: 90% of (**cgroup limit** - **native memory budget**)
* **Safe GC heap size**: 80% of (**cgroup limit** - **native memory budget**)

Small cgroup example:

* **cgroup limit**: 60MB
* Defaults for other values
* **Maximum GC heap size**: 36MB
* **Safe GC heap size**: 32MB

Big cgroup example:

* **cgroup limit**: 1000MB
* Defaults for other values
* **Maximum GC heap size**: 882MB
* **Safe GC heap size**: 784MB

Error cases:

* **cgroup limit** - **native memory budget** - **minimum GC heap size** < 0
* **maximum GC heap size** < **minimum GC heap size**
* **maximum GC heap size** < **safe GC heap size**

Note: This means that the minimum cgroup size by default is 40MB.

Today, the **maximum GC heap size** matches the cgroup limit. We found that the maximum GC heap size needs to be lower than the cgroup limit in order to account for native component memory requirements and to enable the GC to successfully maintain the managed heap at a sustainable level for a process.

The heap sizes can be specified as a percentage of cgroup limit or as an absolute value. We expect a given application to be run in cgroups of varying sizes, making a percentage value attractive.  **native memory budget** represents the amount of native memory that is expected to be used by native components in the process and should be unavailable to the GC to use. Specifying `0` for any of the configuration knobs disables the associated policy from being enforced for that knob.

The GC will throw an `OutOfMemoryException` when an allocation exceeds the specified **maximum GC heap size**.

The GC will more aggressive perform GCs after the GC heap grows beyond the **safe GC heap limit** with the goal of returning the heap under that limit. The GC will avoid continuously performing full blocking GCs if they are not considered productive.

## Conclusion

We believe that running .NET Core applications in cgroups set to low memory limits is critically important, and that the two archetypal scenarios are descriptive of real-world needs. We would appreciate feedback to determine if we're on the right track, and if we have thought broadly enough about scenarios that should be supported.