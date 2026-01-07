using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Mono.Options;

internal static class Program
{
    private static int Main(string[] args)
    {
        var exeName = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
        var help = false;
        var outputPath = "";
        var directoryPath = "";

        var options = new OptionSet
        {
            $"usage: {exeName} <directory> [OPTIONS]+",
            { "o|out=", "The output {path} where the index should be written to. Default: <directory>/INDEX.md", v => outputPath = v },
            { "h|?|help", null, v => help = true, true },
            new ResponseFileSource()
        };

        try
        {
            var parameters = options.Parse(args).ToArray();

            if (help)
            {
                options.WriteOptionDescriptions(Console.Error);
                return 0;
            }

            if (parameters.Length >= 1)
            {
                directoryPath = parameters[0];
            }
            else
            {
                Console.Error.WriteLine("error: must specify a directory");
                return 1;
            }

            var unprocessed = parameters.Skip(1);

            if (unprocessed.Any())
            {
                foreach (var option in unprocessed)
                {
                    Console.Error.WriteLine($"error: unrecognized argument {option}");
                }

                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }

        if (!Directory.Exists(directoryPath))
        {
            Console.Error.WriteLine($"error: directory '{directoryPath}' does not exist");
            return 1;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = Path.Join(directoryPath, "INDEX.md");
        }

        try
        {
            UpdateIndex(directoryPath, outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void UpdateIndex(string directoryPath, string outputPath)
    {
        var designs = Directory.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories)
                               .Select(p => Document.Parse(p))
                               .Where(d => d != null)
                               .Select(d => d!)
                               .ToArray();

        using var outputWriter = new StreamWriter(outputPath);

        outputWriter.WriteLine("<!--");
        outputWriter.WriteLine("");
        outputWriter.WriteLine("This file is auto-generated. Direct changes to it maybe lost.");
        outputWriter.WriteLine("");
        outputWriter.WriteLine("Use update-index to regenerate it:");
        outputWriter.WriteLine("");
        outputWriter.WriteLine("    ./update-index");
        outputWriter.WriteLine("");
        outputWriter.WriteLine("-->");
        outputWriter.WriteLine();
        outputWriter.WriteLine("# Design Index");
        outputWriter.WriteLine();
        WriteList(DocumentKind.Meta, "Meta");
        outputWriter.WriteLine();
        WriteDetails(DocumentKind.AcceptedDesign, "Accepted Designs");
        outputWriter.WriteLine();
        WriteDetails(DocumentKind.DraftDesign, "Drafts");
        outputWriter.WriteLine();
        WriteDetails(DocumentKind.ProposedDesign, "Proposals");
        outputWriter.WriteLine();

        static string GetMarkdownLink(string relativeTo, string path, string title)
        {
            var relativePath = Path.GetRelativePath(relativeTo, path)
                                    .Replace("\\", "/"); // In Markdown, we always want slashes
            return $"[{title}]({relativePath})";
        }

        void WriteList(DocumentKind kind, string header)
        {
            outputWriter.WriteLine($"## {header}");
            outputWriter.WriteLine();

            foreach (var design in designs.Where(d => d.Kind == kind)
                                          .OrderBy(d => d.Year)
                                          .ThenBy(d => d.Title))
            {
                var link = GetMarkdownLink(directoryPath, design.Path, design.Title);
                outputWriter.WriteLine($"* {link}");
            }
        }

        void WriteDetails(DocumentKind kind, string header)
        {
            outputWriter.WriteLine($"## {header}");
            outputWriter.WriteLine();
            outputWriter.WriteLine($"|Year|Title|Owners|");
            outputWriter.WriteLine($"|----|-----|------|");

            foreach (var design in designs.Where(d => d.Kind == kind)
                                          .OrderBy(d => d.Year)
                                          .ThenBy(d => d.Title))
            {
                var owners = string.Join(", ", design.Owners);
                var link = GetMarkdownLink(directoryPath, design.Path, design.Title);
                outputWriter.WriteLine($"| {design.Year} | {link} | {owners} |");
            }
        }
    }
}

internal enum DocumentKind
{
    Meta,
    AcceptedDesign,
    DraftDesign,
    ProposedDesign
}

internal sealed class Document
{
    public Document(DocumentKind kind, string path, int? year, string title, IEnumerable<string> owners)
    {
        Kind = kind;
        Path = path;
        Year = year;
        Title = title;
        Owners = owners.ToArray();
    }

    public DocumentKind Kind { get; }
    public string Path { get; }
    public int? Year { get; }
    public string Title { get; }
    public IReadOnlyList<string> Owners { get; }

    public static Document? Parse(string path)
    {
        var fileInfo = new FileInfo(path);
        Console.WriteLine($"info: parsing {fileInfo.FullName}");
        if (!fileInfo.Exists)
        {
            return null;
        }

        var directory = fileInfo.Directory;

        var kind = (DocumentKind?)null;
        var year = (int?)null;
        var title = (string?)null;
        var owners = new List<string>();

        while (directory != null)
        {
            if (string.Equals(directory.Name, "meta", StringComparison.OrdinalIgnoreCase))
            {
                kind = DocumentKind.Meta;
                break;
            }
            else if (string.Equals(directory.Name, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                kind = DocumentKind.AcceptedDesign;
                break;
            }
            else if (string.Equals(directory.Name, "proposed", StringComparison.OrdinalIgnoreCase))
            {
                kind = DocumentKind.ProposedDesign;
                break;
            }

            if (int.TryParse(directory.Name, out var number))
            {
                year = number;
            }

            directory = directory.Parent;
        }

        if (kind == null)
        {
            return null;
        }

        var lines = File.ReadAllLines(path);

        // Extract titles, owners, and draft status from content above any subheadings
        var subheadingRegex = new Regex("^#{2,}");
        var reachedSubheading = false;

        var titleRegex = new Regex("^# *(?<title>.*?)#?$");
        var ownerRegex = new Regex(@"^\*\*Owner(s)?\*\*(?<owner>[^|,]+)(\s*[\|,]\s*(?<owner>[^|,]+))*", RegexOptions.IgnoreCase);
        var draftRegex = new Regex(@"^\*\*DRAFT\*\*$", RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            reachedSubheading = reachedSubheading || subheadingRegex.Match(line).Success;

            if (!reachedSubheading)
            {
                var titleMatch = titleRegex.Match(line);
                var ownerMatch = ownerRegex.Match(line);
                var draftMatch = draftRegex.Match(line);

                if (titleMatch.Success && title == null)
                {
                    title = titleMatch.Groups["title"].Value.Trim();
                }
                else if (ownerMatch.Success)
                {
                    foreach (Capture capture in ownerMatch.Groups["owner"].Captures)
                    {
                        var owner = capture.Value.Trim();
                        if (owner.Length > 0)
                            owners.Add(owner);
                    }
                }
                else if (draftMatch.Success)
                {
                    kind = DocumentKind.DraftDesign;
                }
            }
        }


        if (title == null)
        {
            Console.Error.WriteLine($"error: could not find title in {path}");
            return null;
        }

        // Some designs have sub designs. We could have a marker in the document
        // but for now it's easier to say "if there is no explicit PM/dev marker"
        // we assume it's a sub design and return null.

        if (kind == DocumentKind.AcceptedDesign && !owners.Any())
        {
            Console.Error.WriteLine($"error: could not find owners for accepted design in {path}");
            return null;
        }

        var ownersString = owners.Count == 0 ? "no one" : string.Join(", ", owners);
        Console.WriteLine($"info: found {kind.ToString().Green()}{(year is null ? "" : "(" + year?.ToString()?.Green() + ")")} design in {System.IO.Path.GetFileName(path).Yellow()} named '{title.Green()}' and owned by {ownersString.Yellow()}");
        return new Document(kind.Value, path, year, title, owners);
    }
}
public static class StringExtensions
{
    public static string Red(this string str) => $"\u001b[31m{str}\u001b[0m";
    public static string Green(this string str) => $"\u001b[32m{str}\u001b[0m";
    public static string Yellow(this string str) => $"\u001b[33m{str}\u001b[0m";
    public static string Blue(this string str) => $"\u001b[34m{str}\u001b[0m";
    public static string Magenta(this string str) => $"\u001b[35m{str}\u001b[0m";
    public static string Cyan(this string str) => $"\u001b[36m{str}\u001b[0m";
    public static string White(this string str) => $"\u001b[37m{str}\u001b[0m";
    public static string Black(this string str) => $"\u001b[30m{str}\u001b[0m";
    public static string Bold(this string str) => $"\u001b[1m{str}\u001b[0m";
    public static string Underline(this string str) => $"\u001b[4m{str}\u001b[0m";
    public static string Inverse(this string str) => $"\u001b[7m{str}\u001b[0m";
    public static string Italic(this string str) => $"\u001b[3m{str}\u001b[0m";
    public static string Strikethrough(this string str) => $"\u001b[9m{str}\u001b[0m";
}
