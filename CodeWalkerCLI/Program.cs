using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace CodeWalkerCLI;

internal static class Program
{
    private static int Main(string[] args)
    {
        var parse = CliOptions.Parse(args);
        if (!parse.Success)
        {
            WriteResponse(CommandResult.Error(parse.ErrorMessage!), parse.Xml);
            return 1;
        }

        var commandContext = new CommandContext(parse.GtaFolder);
        var root = CommandRegistry.Create();

        var result = root.Execute(commandContext, parse.CommandArgs);
        WriteResponse(result, parse.Xml);
        return result.Success ? 0 : 1;
    }

    private static void WriteResponse(CommandResult result, bool xml)
    {
        var payload = new CliResponse
        {
            Success = result.Success,
            Message = result.Message,
            Data = result.Data,
            ExitCode = result.Success ? 0 : 1
        };

        if (xml)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonOptions));
            var root = new XElement("response");
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                root.Add(ToXml(prop.Name, prop.Value));
            }
            Console.WriteLine(new XDocument(root));
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static XElement ToXml(string name, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => new XElement(name, value.EnumerateObject().Select(p => ToXml(p.Name, p.Value))),
            JsonValueKind.Array => new XElement(name, value.EnumerateArray().Select(v => ToXml("item", v))),
            JsonValueKind.String => new XElement(name, value.GetString()),
            JsonValueKind.Number => new XElement(name, value.ToString()),
            JsonValueKind.True => new XElement(name, true),
            JsonValueKind.False => new XElement(name, false),
            _ => new XElement(name)
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class CommandContext
{
    public CommandContext(string? gtaFolder)
    {
        GtaFolder = gtaFolder;
    }

    public string? GtaFolder { get; }

    public string RequireGtaFolder()
    {
        if (string.IsNullOrWhiteSpace(GtaFolder))
        {
            throw new InvalidOperationException("Missing GTA folder. Use --gtafolder <path>.");
        }

        if (!Directory.Exists(GtaFolder))
        {
            throw new DirectoryNotFoundException($"GTA folder not found: {GtaFolder}");
        }

        return GtaFolder;
    }

    public RpfManager CreateRpfManager()
    {
        var folder = RequireGtaFolder();
        var coreDllPath = Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll");
        if (!File.Exists(coreDllPath))
        {
            throw new FileNotFoundException("CodeWalker.Core.dll was not found in the executable directory.", coreDllPath);
        }

        GTA5Keys.LoadFromPath(folder);
        var manager = new RpfManager();
        var errors = new List<string>();
        manager.Init(folder, false, _ => { }, e => errors.Add(e), buildIndex: false);

        if (!manager.IsInited)
        {
            throw new InvalidOperationException("Failed to initialize RPF manager.");
        }

        return manager;
    }
}

internal readonly record struct CommandResult(bool Success, string Message, object? Data = null)
{
    public static CommandResult Ok(string message, object? data = null) => new(true, message, data);
    public static CommandResult Error(string message, object? data = null) => new(false, message, data);
}

internal sealed class CliResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public int ExitCode { get; set; }
}

internal interface ICliCommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    CommandResult Execute(CommandContext context, IReadOnlyList<string> args);
}

internal static class CommandRegistry
{
    public static ICliCommand Create() => new RootCommand(new ICliCommand[]
    {
        new InfoCommand(),
        new VersionCommand(),
        new RpfCommand(),
        new FileCommand(),
        new SearchCommand()
    });
}

internal sealed class RootCommand : ICliCommand
{
    private readonly Dictionary<string, ICliCommand> _commands;

    public RootCommand(IEnumerable<ICliCommand> commands)
    {
        _commands = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "root";
    public string Description => "CodeWalkerCLI root command";
    public string Usage => "CodeWalkerCLI <command> [options]";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        try
        {
            if (args.Count == 0)
            {
                return _commands["info"].Execute(context, args);
            }

            if (!_commands.TryGetValue(args[0], out var command))
            {
                return CommandResult.Error($"Unknown command: {args[0]}", new { usage = Usage, availableCommands = _commands.Keys.OrderBy(k => k) });
            }

            return command.Execute(context, args.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            return CommandResult.Error(ex.Message);
        }
    }
}

internal sealed class InfoCommand : ICliCommand
{
    public string Name => "info";
    public string Description => "Display application info and available commands.";
    public string Usage => "CodeWalkerCLI info";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        var commands = new[]
        {
            new { name = "info", usage = "CodeWalkerCLI info", description = "Display application info and command list" },
            new { name = "version", usage = "CodeWalkerCLI version", description = "Display CodeWalkerCLI and CodeWalker.Core versions" },
            new { name = "rpf list", usage = "CodeWalkerCLI --gtafolder <path> rpf list", description = "List discovered RPF archives" },
            new { name = "rpf extract", usage = "CodeWalkerCLI --gtafolder <path> rpf extract --archive <archive.rpf> --entry <entryPath> --output <path>", description = "Extract one file from an RPF archive" },
            new { name = "file info", usage = "CodeWalkerCLI file info --path <localFile> OR CodeWalkerCLI --gtafolder <path> file info --path <rpfEntryPath>", description = "Get metadata for local or game files" },
            new { name = "file export", usage = "CodeWalkerCLI --gtafolder <path> file export --path <rpfEntryPath> --output <file>", description = "Export a game file (XML when supported, raw fallback)" },
            new { name = "search", usage = "CodeWalkerCLI --gtafolder <path> search --pattern <text> [--limit <n>]", description = "Search game archive entries" }
        };

        return CommandResult.Ok("CodeWalkerCLI command help", new
        {
            application = "CodeWalkerCLI",
            output = new { @default = "json", alternative = "xml via --xml" },
            globalOptions = new[]
            {
                new { name = "--gtafolder", description = "Path to GTA V installation folder" },
                new { name = "--xml", description = "Output XML instead of JSON" }
            },
            commands
        });
    }
}

internal sealed class VersionCommand : ICliCommand
{
    public string Name => "version";
    public string Description => "Display application versions.";
    public string Usage => "CodeWalkerCLI version";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var coreVersion = typeof(GameFileCache).Assembly.GetName().Version?.ToString() ?? "unknown";
        return CommandResult.Ok("Version information", new
        {
            codeWalkerCli = cliVersion,
            codeWalkerCore = coreVersion
        });
    }
}

internal sealed class RpfCommand : ICliCommand
{
    public string Name => "rpf";
    public string Description => "RPF archive commands.";
    public string Usage => "CodeWalkerCLI --gtafolder <path> rpf <list|extract> ...";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing rpf subcommand.", new { usage = Usage });
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => List(context),
            "extract" => Extract(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown rpf subcommand: {args[0]}", new { usage = Usage })
        };
    }

    private static CommandResult List(CommandContext context)
    {
        var manager = context.CreateRpfManager();
        var archives = manager.AllRpfs.Select(r => new
        {
            r.Name,
            r.Path,
            r.FilePath,
            fileCount = r.TotalFileCount,
            folderCount = r.TotalFolderCount
        }).OrderBy(r => r.Path).ToArray();

        return CommandResult.Ok($"Found {archives.Length} archive(s).", new { archives });
    }

    private static CommandResult Extract(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var archivePath = options.GetRequired("archive");
        var entryPath = options.GetRequired("entry");
        var outputPath = options.GetRequired("output");

        var manager = context.CreateRpfManager();
        var archive = manager.FindRpfFile(archivePath);
        if (archive == null)
        {
            return CommandResult.Error($"Archive not found: {archivePath}");
        }

        var entry = archive.AllEntries?.OfType<RpfFileEntry>()
            .FirstOrDefault(e => string.Equals(e.Path, entryPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            return CommandResult.Error($"Entry not found in archive: {entryPath}");
        }

        var bytes = archive.ExtractFile(entry);
        if (bytes == null || bytes.Length == 0)
        {
            return CommandResult.Error($"Failed to extract entry: {entry.Path}");
        }

        var targetPath = outputPath;
        if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            targetPath = Path.Combine(outputPath, entry.Name);
        }

        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.WriteAllBytes(targetPath, bytes);

        return CommandResult.Ok("Entry extracted.", new
        {
            archive = archive.Path,
            entry = entry.Path,
            bytes = bytes.Length,
            output = targetPath
        });
    }
}

internal sealed class FileCommand : ICliCommand
{
    public string Name => "file";
    public string Description => "Game file commands.";
    public string Usage => "CodeWalkerCLI file <info|export> ...";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing file subcommand.", new { usage = Usage });
        }

        return args[0].ToLowerInvariant() switch
        {
            "info" => Info(context, args.Skip(1).ToArray()),
            "export" => Export(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown file subcommand: {args[0]}", new { usage = Usage })
        };
    }

    private static CommandResult Info(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var path = options.GetRequired("path");

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return CommandResult.Ok("Local file metadata", new
            {
                source = "local",
                info.Name,
                info.FullName,
                info.Extension,
                info.Length,
                info.LastWriteTimeUtc
            });
        }

        var manager = context.CreateRpfManager();
        var entry = manager.GetEntry(path) as RpfFileEntry;
        if (entry == null)
        {
            return CommandResult.Error($"File not found in RPF index: {path}");
        }

        var data = entry.File.ExtractFile(entry);
        var metadata = BuildGameFileMetadata(entry, data);

        return CommandResult.Ok("Game file metadata", metadata);
    }

    private static CommandResult Export(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var path = options.GetRequired("path");
        var outputPath = options.GetRequired("output");

        var manager = context.CreateRpfManager();
        var entry = manager.GetEntry(path) as RpfFileEntry;
        if (entry == null)
        {
            return CommandResult.Error($"File not found in RPF index: {path}");
        }

        var data = entry.File.ExtractFile(entry);
        if (data == null || data.Length == 0)
        {
            return CommandResult.Error($"Failed to load file data for export: {entry.Path}");
        }

        string exportedFormat;
        string? xml = MetaXml.GetXml(entry, data, out _, Path.GetDirectoryName(outputPath) ?? string.Empty);
        if (!string.IsNullOrEmpty(xml))
        {
            var target = outputPath;
            if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                target = Path.Combine(outputPath, entry.Name + ".xml");
            }

            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllText(target, xml);
            outputPath = target;
            exportedFormat = "xml";
        }
        else
        {
            var target = outputPath;
            if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                target = Path.Combine(outputPath, entry.Name);
            }

            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllBytes(target, data);
            outputPath = target;
            exportedFormat = "raw";
        }

        return CommandResult.Ok("File exported.", new
        {
            entry = entry.Path,
            format = exportedFormat,
            output = outputPath
        });
    }

    private static object BuildGameFileMetadata(RpfFileEntry entry, byte[] data)
    {
        var ext = Path.GetExtension(entry.NameLower ?? entry.Name).ToLowerInvariant();
        var details = new Dictionary<string, object?>
        {
            ["entryPath"] = entry.Path,
            ["name"] = entry.Name,
            ["extension"] = ext,
            ["size"] = data?.Length ?? 0,
            ["isEncrypted"] = entry.IsEncrypted,
            ["entryType"] = entry.GetType().Name
        };

        try
        {
            switch (ext)
            {
                case ".ymap":
                    var ymap = RpfFile.GetFile<YmapFile>(entry, data);
                    details["entities"] = ymap?.AllEntities?.Length ?? 0;
                    details["carGenerators"] = ymap?.CarGenerators?.Length ?? 0;
                    break;
                case ".ytyp":
                    var ytyp = RpfFile.GetFile<YtypFile>(entry, data);
                    details["archetypes"] = ytyp?.AllArchetypes?.Length ?? 0;
                    break;
                case ".ybn":
                    var ybn = RpfFile.GetFile<YbnFile>(entry, data);
                    details["hasBounds"] = ybn?.Bounds != null;
                    break;
                case ".ydr":
                    var ydr = RpfFile.GetFile<YdrFile>(entry, data);
                    details["hasDrawable"] = ydr?.Drawable != null;
                    break;
                case ".ydd":
                    var ydd = RpfFile.GetFile<YddFile>(entry, data);
                    details["drawables"] = ydd?.DrawableDict?.Drawables?.data_items?.Length ?? 0;
                    break;
            }
        }
        catch (Exception ex)
        {
            details["parseWarning"] = ex.Message;
        }

        return details;
    }
}

internal sealed class SearchCommand : ICliCommand
{
    public string Name => "search";
    public string Description => "Search for files in game archives.";
    public string Usage => "CodeWalkerCLI --gtafolder <path> search --pattern <text> [--limit <n>]";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var pattern = options.GetRequired("pattern");
        var limitText = options.Get("limit");
        var limit = 100;

        if (!string.IsNullOrWhiteSpace(limitText) && (!int.TryParse(limitText, out limit) || limit <= 0))
        {
            return CommandResult.Error("Invalid --limit value. It must be a positive integer.");
        }

        var manager = context.CreateRpfManager();
        var matches = manager.EntryDict
            .Where(kv => kv.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Take(limit)
            .ToArray();

        return CommandResult.Ok($"Found {matches.Length} matching file(s).", new
        {
            pattern,
            limit,
            matches
        });
    }
}

internal sealed class OptionParser
{
    private readonly Dictionary<string, string> _values;

    private OptionParser(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static OptionParser Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Missing value for option --{key}.");
            }

            values[key] = args[++i];
        }

        return new OptionParser(values);
    }

    public string GetRequired(string key)
    {
        var value = Get(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option --{key}.");
        }

        return value;
    }

    public string? Get(string key)
    {
        _values.TryGetValue(key, out var value);
        return value;
    }
}

internal sealed class CliOptions
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Xml { get; init; }
    public string? GtaFolder { get; init; }
    public string[] CommandArgs { get; init; } = Array.Empty<string>();

    public static CliOptions Parse(string[] args)
    {
        var remaining = new List<string>();
        string? gtaFolder = null;
        var xml = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.Equals("--xml", StringComparison.OrdinalIgnoreCase))
            {
                xml = true;
                continue;
            }

            if (token.Equals("--gtafolder", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return new CliOptions { ErrorMessage = "Missing value for --gtafolder.", Xml = xml };
                }

                gtaFolder = args[++i];
                continue;
            }

            remaining.Add(token);
        }

        return new CliOptions
        {
            Success = true,
            Xml = xml,
            GtaFolder = gtaFolder,
            CommandArgs = remaining.ToArray()
        };
    }
}
