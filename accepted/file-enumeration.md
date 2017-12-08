# Extensible File Enumeration

**PM** [Immo Landwerth](https://github.com/terrajobst) | **Dev** [Jeremy Kuhne](https://github.com/jeremykuhne)

Enumerating files in .NET provides limited configurability. You can specify a simple DOS style pattern and whether or not to look recursively. More complicated filtering requires post filtering all results which can introduce a significant performance drain.

Recursive enumeration is also problematic in that there is no way to handle error states such as access issues or cycles created by links.

These restrictions have a significant impact on file system intensive applications, a key example being MSBuild. This document proposes a new set of primitive file and directory traversal APIs that are optimized for providing more flexibility while keeping the overhead to a minimum so that enumeration becomes both more powerful as well as more performant.

## Scenarios and User Experience

### Getting full paths

To get full paths for files, one would currently have to do the following:

```
public static IEnumerable<string> GetFileFullPaths(string directory,
    string expression = "*",
    bool recursive = false)
{
    return new DirectoryInfo(directory)
        .GetFiles(expression, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
        .Select(r => r.FullName);
}

```

While not complicated to write, it allocates far more than it needs to. A `FileInfo` will be allocated for every result, even though it strictly isn't needed. A new, lower allocating, full-path wrapper could be written as follows:

``` C#
public static IEnumerable<string> GetFileFullPaths(string directory,
    string expression = "*",
    bool recursive = false)
{
    return new FindEnumerable<string, string>(
        directory,
        (ref FindData<string> findData) => FindTransforms.AsUserFullPath(ref findData),
        (ref FindData<string> findData) =>
        {
            return !FindPredicates.IsDirectory(ref findData)
                && DosMatcher.MatchPattern(findData.State, findData.FileName, ignoreCase: true);
        },
        state = DosMatcher.TranslateExpression(expression),
        recursive = recursive);
}

```

While using anonymous delegates isn't strictly necessary, they are cached, so all examples are written with them. The second argument above, for example, could simply be `FindTransforms.AsUserFullPath`, but it wouldn't take advantage of delegate caching.


### Getting full paths of a given extension

Again, here is the current way to do this:

``` C#
public static IEnumerable<string> GetFilePathsWithExtensions(string directory, bool recursive, params string[] extensions)
{
    return new DirectoryInfo(directory)
        .GetFiles("*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
        .Where(f => extensions.Any(e => f.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        .Select(r => r.FullName);
}
```

Again, not complicated to write, but this can do an enormous amount of extra allocations. You have to create full strings and FileInfo for every single item in the file system. We can cut this down significantly with the extension point:

``` C#
public static IEnumerable<string> GetFileFullPathsWithExtension(string directory,
    bool recursive, params string[] extensions)
{
    return new FindEnumerable<string, string[]>(
        directory,
        (ref FindData<string[]> findData) => FindTransforms.AsUserFullPath(ref findData),
        (ref FindData<string[]> findData) =>
        {
            return !FindPredicates.IsDirectory(ref findData)
                && findData.State.Any(s => findData.FileName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        },
        state = extensions,
        recursive = recursive);
}
```

The number of allocation reductions with the above solution is significant.

- No `FileInfo` allocations
- No fullpath string allocations for paths that don't match
- No filename allocations for paths that don't match (as the filename will still be in the native buffer at this point)

## Requirements


### Goals

The key goal is to provide an advanced API that keeps allocations to a minimum by allowing access to enumeration internals. We're willing to sacrifice some usability to achieve this- but will keep the complexity in-line with similar `Span<T>` based APIs, such as `String.Create()`.
 
1. Keep allocations to a minimum
	- Per result allocations are the priority
	- Initial allocations should also be minimized, but not at the cost of per-result
2. Considers filter/transform method call overhead for large sets
	- Delegates appear to be a better solution than using Interfaces
3. Filtering can be done on existing FileSystemInfo exposed data
	- Name
	- Attributes
	- Time stamps
	- File size
4. Transforms can provide results in any type
	- Like Linq Select(), but keeps FileData on the stack
5. Filters and transforms will be provided
	- Criteria considered for inclusion
		- Expected to be commonly used
		- Can be optimized by being part of the platform
			- Via access to internals
			- Can abstract away expected future improvements (e.g. adding Span<T> support to Regex)
	- Expected initial transforms
		- To file/directory name
		- To full path
		- To File/Directory/FileSystemInfo
	- Expected initial filters
		- (Pri0) DOS style filters (Legacy- `*/?` with DOS semantics, e.g. `*.` is all files without an extension)   
		- (Pri1) Simple Regex filter (e.g. IsMatch())
		- (Pri2) Simpler globbing (`*/?` without DOS style variants)
		- (Pri2) Set of extensions (`*.c`, `*.cpp`, `*.cc`, `*.cxx`, etc.)
6. Recursive behavior is configurable
	- On/Off via flag
	- Predicate based on FileData
7. Behavior flags
	1. Recurse
	2. Ignore inaccessible files (e.g. no rights)
	3. Hint to use larger buffers (for remote access)
	4. Optimization to skip locking for single-thread enumeration 

### Non-Goals

1. This will not replace existing APIs- this is an advanced way for people to write performance solutions
2. We will not provide a way to plug in custom platform APIs to provide the FileData
2. We will not expose platform specific data in this release
3. We will not expose raw errors for filtering

## Design

### Proposed API surface

``` C#
namespace System.IO
{
    /// <summary>
    /// Delegate for filtering out find results.
    /// </summary>
    public delegate bool FindPredicate<TState>(ref FindData findData, TState state);

    /// <summary>
    /// Delegate for transforming raw find data into a result.
    /// </summary>
    public delegate TResult FindTransform<TResult, TState>(ref FindData findData, TState state);

    [Flags]
    public enum FindOptions
	{
        None = 0x0,

        // Enumerate subdirectories
        Recurse = 0x1,

        // Skip files/directories when access is denied (e.g. AccessDeniedException/SecurityException)
        IgnoreInaccessable = 0x2,

        // Hint to use larger buffers for getting data (notably to help address remote enumeration perf)
        UseLargeBuffer = 0x4,

        // Allow .NET to skip locking if you know the enumerator won't be used on multiple threads
        // (Enumerating is inherently not thread-safe, but .NET needs to still lock by default to
        //  avoid access violations with native access should someone actually try to use the
        //  the same enumerator on multiple threads.)
        NoLocking = 0x8

        // Future: Add flags for tracking cycles, etc. 
	}

    public class FindEnumerable<TResult, TState> : CriticalFinalizerObject, IEnumerable<TResult>, IEnumerator<TResult>
    {
        public FindEnumerable(
            string directory,
            FindTransform<TResult, TState> transform,
            FindPredicate<TState> predicate,
            // Only used if FindOptions.Recurse is set. Default is to always recurse.
            FindPredicate<TState> recursePredicate = null,
            TState state = default,
            FindOptions options = FindOptions.None)
    }

    public static class Directory
    {
        public static IEnumerable<TResult> Enumerate<TResult, TState>(
            string path,
            FindTransform<TResult, TState> transform,
            FindPredicate<TState> predicate,
            FindPredicate<TState> recursePredicate = null,
            TState state = default,
            FindOptions options = FindOptions.None);
    }

    public class DirectoryInfo
    {
        public static IEnumerable<TResult> Enumerate<TResult, TState>(
            FindTransform<TResult, TState> transform,
            FindPredicate<TState> predicate,
            FindPredicate<TState> recursePredicate = null,
            TState state = default,
            FindOptions options = FindOptions.None);
    }

    /// <summary>
    /// Used for processing and filtering find results.
    /// </summary>
    public ref struct FindData
    {
        // This will have private members that hold the native data and
        // will lazily fill in data for properties where such data is not
        // immediately available in the current platform's native results.

        // The full path to the directory the current result is in
        public string Directory { get; }

        // The full path to the starting directory for enumeration
        public string OriginalDirectory { get; }

        // The path to the starting directory as passed to the enumerable constructor
        public string OriginalUserDirectory { get; }

        // Note: using a span allows us to reduce unneeded allocations
        public ReadOnlySpan<char> FileName { get; }
        public FileAttributes Attributes { get; }
        public long Length { get; }

        public DateTime CreationTimeUtc { get; }
        public DateTime LastAccessTimeUtc { get; }
        public DateTime LastWriteTimeUtc { get; }
    }
}
```

### Transforms & Predicates

We'll provide common predicates transforms for building searches.

``` C#
namespace System.IO
{
    internal static partial class FindPredicates
    {
        internal static bool NotDotOrDotDot(ref FindData findData)
        internal static bool IsDirectory(ref FindData findData)
    }

    public static partial class FindTransforms
    {
        public static DirectoryInfo AsDirectoryInfo(ref FindData findData)
        public static FileInfo AsFileInfo(ref FindData findData)
        public static FileSystemInfo AsFileSystemInfo(ref FindData findData)
        public static string AsFileName(ref FindData findData)
        public static string AsFullPath(ref FindData findData)
    }
}

```

### DosMatcher

We currently have an implementation of the algorithm used for matching files on Windows in FileSystemWatcher. Providing this publicly will allow consistently matching names cross platform according to Windows rules if such behavior is desired.

``` C#
namespace System.IO
{
    public static class DosMatcher
    {
        /// <summary>
        /// Change '*' and '?' to '&lt;', '&gt;' and '"' to match Win32 behavior. For compatibility, Windows
        /// changes some wildcards to provide a closer match to historical DOS 8.3 filename matching.
        /// </summary>
        public unsafe static string TranslateExpression(string expression)

        /// <summary>
        /// Return true if the given expression matches the given name.
        /// </summary>
        public unsafe static bool MatchPattern(string expression, ReadOnlySpan<char> name, bool ignoreCase = true)
    }
}
```

### Existing API summary

``` C#
namespace System.IO
{
    public static class Directory
    {
        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption);
        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
        public static IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption);
        public static string[] GetDirectories(string path, string searchPattern, SearchOption searchOption);
        public static string[] GetFiles(string path, string searchPattern, SearchOption searchOption);
        public static string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption);
    }

    public sealed class DirectoryInfo : FileSystemInfo
    {
        public IEnumerable<DirectoryInfo> EnumerateDirectories(string searchPattern, SearchOption searchOption);
        public IEnumerable<FileInfo> EnumerateFiles(string searchPattern, SearchOption searchOption);
        public IEnumerable<FileSystemInfo> EnumerateFileSystemInfos(string searchPattern, SearchOption searchOption);
        public DirectoryInfo[] GetDirectories(string searchPattern, SearchOption searchOption);
        public FileInfo[] GetFiles(string searchPattern, SearchOption searchOption);
        public FileSystemInfo[] GetFileSystemInfos(string searchPattern, SearchOption searchOption); 
    }

    public enum SearchOption
    {
        AllDirectories,
        TopDirectoryOnly
    }
}
```


## Q & A

#### Why aren't you providing a filter that does _`X`_?

We want to only provide pre-made filters that have broad applicability. Based on feedback we can and will consider adding new filters in the future.

#### Why did you put data in the struct that is expensive to get on Unix?

While Windows gives all of the data you see in `FindData` in a single enumeration call, this isn't true for Unix. We're trying to match the current `System.IO.*Info` class data, not the intersection of OS call data. We will lazily get the extra data (time stamps, for example) on Unix to avoid unnecessarily taxing solutions that don't need it.

#### Why doesn’t the filename data type match the OS format?

We believe that it is easier and more predictable to write cross-plat solutions based on `char` rather than have to deal directly with encoding. The current plan is that we'll optimize here by not converting from UTF-8 until/if needed and we'll also minimize/eliminate GC impact by keeping the converted data on the stack or in a cached array.

