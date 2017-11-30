# Extensible File Enumeration

**PM** [Immo Landwerth](https://github.com/terrajobst) | **Dev** [Jeremy Kuhne](https://github.com/jeremykuhne)

Enumerating files in .NET provides limited configurability. You can specify a simple DOS style pattern and whether or not to look recursively. More complicated filtering requires post filtering all results which can introduce a significant performance drain.

Recursive enumeration is also problematic in that there is no way to handle error states such as access issues or cycles created by links.

These restrictions have a significant impact on file system intensive applications, a key example being MSBuild. We can address these restrictions and provide performant, configurable enumeration.

## Scenarios and User Experience

1. MSBuild can custom filter filesystem entries with limited allocations and form the results in any desired format.
2. Users can build custom enumerations utilizing completely custom or provided commonly used filters and transforms.

## Requirements

1. Filtering based on common file system data is possible
	a. Name
	b. Attributes
	c. Time stamps
    d. File size
2. Result transforms can be of any type
3. We provide common filters and transforms
4. Recursive behavior is configurable
5. Error handling is configurable

### Goals

1. API is cross platform generic
2. API minimizes allocations while meeting #1

### Non-Goals

1. API will not expose platform specific data
3. Error handling configuration is fully customizable

## Design

### Proposed API surface

``` C#
namespace System.IO
{
    /// <summary>
    /// Delegate for filtering out find results.
    /// </summary>
    internal delegate bool FindPredicate<TState>(ref RawFindData findData, TState state);

    /// <summary>
    /// Delegate for transforming raw find data into a result.
    /// </summary>
    internal delegate TResult FindTransform<TResult, TState>(ref RawFindData findData, TState state);

    [Flags]
    public enum FindOptions
	{
        None = 0x0,

        // Enumerate subdirectories
        Recurse = 0x1,

        // Skip files/directories when access is denied
        IgnoreAccessDenied = 0x2,

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

    public static class DirectoryInfo
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
    public ref struct RawFindData
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
        internal static bool NotDotOrDotDot(ref RawFindData findData)
        internal static bool IsDirectory(ref RawFindData findData)
    }

    public static partial class FindTransforms
    {
        public static DirectoryInfo AsDirectoryInfo(ref RawFindData findData)
        public static FileInfo AsFileInfo(ref RawFindData findData)
        public static FileSystemInfo AsFileSystemInfo(ref RawFindData findData)
        public static string AsFileName(ref RawFindData findData)
        public static string AsFullPath(ref RawFindData findData)
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

### Samples

Getting full path of all files matching a given name pattern (close to what FindFiles does, but returning the full path):

``` C#
public static FindEnumerable<string, string> GetFiles(string directory,
    string expression = "*",
    bool recursive = false)
{
    return new FindEnumerable<string, string>(
        directory,
        (ref RawFindData findData, string expr) => FindTransforms.AsFullPath(ref findData),
        (ref RawFindData findData, string expr) =>
        {
            return FindPredicates.NotDotOrDotDot(ref findData)
                && !FindPredicates.IsDirectory(ref findData)
                && DosMatcher.MatchPattern(expr, findData.FileName, ignoreCase: true);
        },
        state: DosMatcher.TranslateExpression(expression),
        options: recursive ? FindOptions.Recurse : FindOptions.None);
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