# Capability APIs

**PM** [Immo Landwerth](https://github.com/terrajobst)

Thoughts I shared on Twitter:

* This morning it happened. I had an epiphany under the shower to make
  platform-checks much better via a generalized approach for capability APIs.
  Basically, instead of doing actual platform checks, the analyzer would push you
  call capability APIs.
* The nice thing is that this also works for features that aren't OS specific,
  but more of a combination of runtime and environment, such as whether you can
  JIT.
* Imagine in web assembly where we discover certain features wouldn't work. It's
  hard to annotate an API with "works everywhere but here" in a fashion that
  doesn't break consumers when the environment changes.

## Scenarios and User Experience

## Requirements

### Goals

### Non-Goals

## Design

### Open Questions

#### How can we reduce the noise?

Basically, we should make sure that people building applications don't have to
perform capability checks if the answer is statically known. For example,
registry will work on Windows, so folks building a WinForms/WPF app shouldn't
have to suppress them. One option is add typical capabilities to the assembly,
but that's messy. Another option is tag capabilities with platform annotations
that basically say "on this platform, you don't have to ask for capability
checks". Then it's only a matter of flowing the platform information from the
build into the analyzer.

#### Should this analyzer be-opt in?

This might be useful for .NET Standard library developers and much less useful
for people building .NET Core apps. We might also want different defaults.

#### Should we provide an assert-style attribute?

This would behave the same was tagging the assemblies, modules, types, or
members with the `[RequiredXxxCapability]` but without requiring callers to
check it. It's basically a suppression for a specific capability.

#### Should different capabilities have different diagnostic ids?

This probably doesn't work in Roslyn as the diagnostic set needs to be known
statically.

### Attributes

```C#
namespace System.Runtime.InteropServices
{
    public abstract class CapabilityAttribute : Attribute
    {
        public bool Assert { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CapabilityCheckAttribute : Attribute
    {
        public CapabilityCheckAttribute(Type type, string propertyName)
        {
            Type = type;
            PropertyName = propertyName;
        }

        public Type Type { get; }
        public string PropertyName { get; }
    }
}
```

### Example: Registry

```C#
//[RequiresRegistryCapability]
static string GetLoggingPath()
{
    if (Registry.IsSupported)
    {
        var key = new RegistryKey();
        var value = key.GetValue("foo");

        /*
        using (var key = Registry.CurrentUser.OpenSubKey(@"SoftwareFabrikamAssetManagement"))
        {
            if (key?.GetValue("LoggingDirectoryPath") is string configuredPath)
                return configuredPath;
        }
        */s
    }

    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(appDataPath, "Fabrikam", "AssetManagement", "Logging");
}
```

```C#
namespace Microsoft.Win32
{
    [CapabilityCheck(typeof(Registry), nameof(Registry.IsSupported))]
    [AlwaysAvailabeOn(OSPlatform.Windows)]
    [AlwaysAvailabeOn(OSPlatform.Linux)]
    [AlwaysAvailabeOn(OSPlatform.FreeBSD)]
    [AlwaysUnsupportedOn(WebAssembly)]
    public sealed class RequiresRegistryCapabilityAttribute : CapabilityAttribute
    {
    }

    [RequiresRegistryCapability]
    public partial class Registry
    {
        public static bool IsSupported { get; }

        public static readonly RegistryKey CurrentUser;
    }

    [RequiresRegistryCapability]
    public partial class RegistryKey : IDisposable
    {
        public RegistryKey OpenSubKey(string path) => throw null;
        public object GetValue(string name) => throw null;
        public void Dispose() => throw null;
    }
}
```

### Example: ACLs

```C#
void Main()
{
    if (File.Exits(path))
    {
        using (var x = File.OpenRead(path))
        {
            if (ObjectSecurity.IsSupported)
            {
                var acl = x.GetAccessControl();
                acl.GiveMePermissions();
            }
        }
    }
}
```

```C#
namespace System.Security.AccessControl
{
    [CapabilityCheck(typeof(ObjectSecurity), nameof(ObjectSecurity.IsSupported))]
    public sealed class RequiresAccessControlCapabilityAttribute : CapabilityAttribute
    {
    }

    [RequiresAccessControlCapability]
    public partial class ObjectSecurity
    {
        public static bool IsSupported { get; }
    }

    [RequiresAccessControlCapability]
    public partial class FileSecurity
    {
    }

    [RequiresAccessControlCapability]
    public partial class MutexSecurity
    {
    }
}

namespace System.IO
{
    public partial class File
    {
        [RequiresAccessControlCapability]
        public static FileSecurity GetAccessControl(string path);
    }

    public partial class FileInfo
    {
        [RequiresAccessControlCapability]
        public FileSecurity GetAccessControl();
    }
}

namespace System.Threading
{
    public partial class Mutex
    {
        [RequiresAccessControlCapability]
        public Mutex(bool initiallyOwned, string name, out bool createdNew, MutexSecurity mutexSecurity) => throw null;
    }
}
```

### Example: JIT

```C#
namespace System.Reflection.Emit
{
    [CapabilityCheck(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
    [CapabilityCheck(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeCompiled))]
    public partial class RequiresDynamicCodeCapability : CapabilityAttribute
    {
    }

    [RequiresDynamicCodeCapability]
    public partial class DynamicMethod : MethodInfo
    {
    }

    [RequiresDynamicCodeCapability]
    public partial class AssemblyBuilder : Assembly
    {
    }

    // Would also mark
    // - Other *Builder types
    // - AppDomain.DefineDynamicAssembly
    // - ILGenerator
}
```

## Q & A
