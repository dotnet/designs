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
        var documents = Directory.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories)
                                 .Select(p => Document.Parse(p))
                                 .Where(d => d != null)
                                 .Select(d => d!);

        var template = new IndexTemplate(directoryPath, documents);
        var content = template.TransformText();

        // Let's get the line breaks consistent with the OS line breaks:
        content = Regex.Replace(content, "(\r\n)|(\r(?!\n))|(\n(?<!\r))", Environment.NewLine);
        File.WriteAllText(outputPath, content);
    }
}

internal enum DocumentKind
{
    Meta,
    AcceptedDesign,
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
        var titleRegex = new Regex("^# *(?<title>.*?)#?$");
        var ownerRegex = new Regex(@"^\*\*(?:Libraries)? *(PM|Dev) ?\*\*(?<owner>[^|]+)");

        foreach (var line in lines)
        {
            var titleMatch = titleRegex.Match(line);
            var ownerMatch = ownerRegex.Match(line);

            if (titleMatch.Success && title == null)
            {
                title = titleMatch.Groups["title"].Value.Trim();
            }
            else if (ownerMatch.Success)
            {
                var owner = ownerMatch.Groups["owner"].Value.Trim();
                if (owner.Length > 0)
                    owners.Add(owner);
            }
        }

        if (title == null)
        {
            return null;
        }

        // Some designs have sub designs. We could have a marker in the document
        // but for now it's easier to say "if there is no explicit PM/dev marker"
        // we assume it's a sub design and return null.

        if (kind == DocumentKind.AcceptedDesign && !owners.Any())
        {
            return null;
        }

        return new Document(kind.Value, path, year, title, owners);
    }
}

public partial class IndexTemplate
{
    private readonly Document[] _documents;
    private readonly string _directoryPath;

    internal IndexTemplate(string directoryPath, IEnumerable<Document> documents)
    {
        _directoryPath = directoryPath;
        _documents = documents.ToArray();
    }

    private string GetMarkdownLink(Document document)
    {
        var relativePath = Path.GetRelativePath(_directoryPath, document.Path)
                               .Replace("\\", "/"); // In Markdown, we always want slashes
        return $"[{document.Title}]({relativePath})";
    }

    private IEnumerable<Document> GetDocuments(DocumentKind kind)
    {
        return _documents.Where(d => d.Kind == kind)
                         .OrderBy(d => d.Year)
                         .ThenBy(d => d.Title);
    }
}
