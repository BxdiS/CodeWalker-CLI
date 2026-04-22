using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace CodeWalker.CLI;

internal static class Program
{
    private const int TextIndentSize = 2;

    private static int Main(string[] args)
    {
        var parse = CliOptions.Parse(args);
        if (!parse.Success)
        {
            WriteResponse(CommandResult.Error(parse.ErrorMessage!), parse.OutputFormat, parse.Compact);
            return 1;
        }

        var commandContext = new CommandContext(parse.GtaFolder);
        var root = CommandRegistry.Create();

        var result = root.Execute(commandContext, parse.CommandArgs);
        WriteResponse(result, parse.OutputFormat, parse.Compact);
        return result.Success ? 0 : 1;
    }

    private static void WriteResponse(CommandResult result, OutputFormat outputFormat, bool compact)
    {
        var payload = new CliResponse
        {
            Success = result.Success,
            Message = result.Message,
            Data = result.Data,
            ExitCode = result.Success ? 0 : 1
        };

        var jsonOptions = GetJsonOptions(compact);

        if (outputFormat == OutputFormat.Xml)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, jsonOptions));
            var root = new XElement("response");
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                root.Add(ToXml(prop.Name, prop.Value));
            }
            Console.WriteLine(compact ? root.ToString(SaveOptions.DisableFormatting) : new XDocument(root).ToString());
            return;
        }

        if (outputFormat == OutputFormat.Text)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, jsonOptions));
            Console.WriteLine(ToText(doc.RootElement, compact));
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(payload, jsonOptions));
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

    private static string ToText(JsonElement element, bool compact)
    {
        if (compact)
        {
            var lines = new List<string>();
            FlattenText(element, "$", lines);
            return string.Join(Environment.NewLine, lines);
        }

        var sb = new StringBuilder();
        WriteText(element, sb, 0);
        return sb.ToString().TrimEnd();
    }

    private static void FlattenText(JsonElement element, string path, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    FlattenText(prop.Value, $"{path}.{prop.Name}", lines);
                }
                break;
            case JsonValueKind.Array:
                var idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenText(item, $"{path}[{idx}]", lines);
                    idx++;
                }
                break;
            default:
                lines.Add($"{path}={ScalarToString(element)}");
                break;
        }
    }

    private static void WriteText(JsonElement element, StringBuilder sb, int depth)
    {
        var indent = new string(' ', depth * TextIndentSize);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{indent}{prop.Name}:");
                        WriteText(prop.Value, sb, depth + 1);
                    }
                    else
                    {
                        sb.AppendLine($"{indent}{prop.Name}: {ScalarToString(prop.Value)}");
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{indent}-");
                        WriteText(item, sb, depth + 1);
                    }
                    else
                    {
                        sb.AppendLine($"{indent}- {ScalarToString(item)}");
                    }
                }
                break;
            default:
                sb.AppendLine($"{indent}{ScalarToString(element)}");
                break;
        }
    }

    private static string ScalarToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => element.ToString()
    };

    private static JsonSerializerOptions GetJsonOptions(bool compact) => new()
    {
        WriteIndented = !compact,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal enum OutputFormat
{
    Json,
    Xml,
    Text
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
        // Explicit dependency validation: fail fast with a clear error if packaged files are incomplete.
        if (!File.Exists(coreDllPath))
        {
            throw new FileNotFoundException("CodeWalker.Core.dll was not found in the executable directory.", coreDllPath);
        }

        GTA5Keys.LoadFromPath(folder);
        var manager = new RpfManager();
        manager.Init(folder, false, _ => { }, _ => { }, buildIndex: false);

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
    public string Description => "CodeWalker.CLI root command";
    public string Usage => "CodeWalker.CLI <command> [options]";

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
    public string Usage => "CodeWalker.CLI info";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        var commands = new[]
        {
            new { name = "info", usage = "CodeWalker.CLI info", description = "Display application info and command list" },
            new { name = "version", usage = "CodeWalker.CLI version", description = "Display CodeWalker.CLI and CodeWalker.Core versions" },
            new { name = "rpf list", usage = "CodeWalker.CLI --gtafolder <path> rpf list", description = "List discovered RPF archives" },
            new { name = "rpf inspect", usage = "CodeWalker.CLI --gtafolder <path> rpf inspect <stats|files|child|defrag-size|defragment-size (alias)> --archive <archive.rpf> ...", description = "Inspect RPF archive structure and sizes" },
            new { name = "rpf extract", usage = "CodeWalker.CLI --gtafolder <path> rpf extract [file] --archive <archive.rpf> --entry <entryPath> --output <path>", description = "Extract one file from an RPF archive" },
            new { name = "rpf extract scripts", usage = "CodeWalker.CLI --gtafolder <path> rpf extract scripts --archive <archive.rpf> --output <path>", description = "Extract .ysc scripts recursively" },
            new { name = "rpf extract test-all", usage = "CodeWalker.CLI --gtafolder <path> rpf extract test-all --archive <archive.rpf>", description = "Run extraction diagnostics for all archive files" },
            new { name = "rpf create", usage = "CodeWalker.CLI --gtafolder <path> rpf create <root-archive|archive|directory|file> ... --force true", description = "Create archives/directories/files in RPFs" },
            new { name = "rpf edit", usage = "CodeWalker.CLI --gtafolder <path> rpf edit <rename-entry|delete-entry> ... --force true", description = "Mutate archive entries" },
            new { name = "rpf crypto", usage = "CodeWalker.CLI --gtafolder <path> rpf crypto <is-valid|ensure-valid|set> ...", description = "Inspect or update archive encryption" },
            new { name = "rpf maintenance", usage = "CodeWalker.CLI --gtafolder <path> rpf maintenance <defrag-size|defragment-size (alias)|defragment> ...", description = "Defragmentation and size planning" },
            new { name = "rpf util", usage = "CodeWalker.CLI rpf util <compress|decompress|flags-from-size|flags-from-blocks|size-from-flags|version-from-flags|page-flags> ...", description = "Invoke RpfFile utility operations" },
            new { name = "file info", usage = "CodeWalker.CLI file info --path <localFile> OR CodeWalker.CLI --gtafolder <path> file info --path <rpfEntryPath>", description = "Get metadata for local or game files" },
            new { name = "file export", usage = "CodeWalker.CLI --gtafolder <path> file export --path <rpfEntryPath> --output <file>", description = "Export a game file (XML when supported, raw fallback)" },
            new { name = "search", usage = "CodeWalker.CLI --gtafolder <path> search --pattern <text> [--limit <n>]", description = "Search game archive entries" }
        };

        return CommandResult.Ok("CodeWalker.CLI command help", new
        {
            application = "CodeWalker.CLI",
            output = new { @default = "json", alternatives = "xml|text via --format" },
            globalOptions = new[]
            {
                new { name = "--gtafolder", description = "Path to GTA V installation folder" },
                new { name = "--format", description = "Output format: json|xml|text" },
                new { name = "--xml", description = "Backward compatible alias for --format xml" },
                new { name = "--compact", description = "Output compact payload" }
            },
            commands
        });
    }
}

internal sealed class VersionCommand : ICliCommand
{
    public string Name => "version";
    public string Description => "Display application versions.";
    public string Usage => "CodeWalker.CLI version";

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
    private const int MaxLogEntries = 20;

    public string Name => "rpf";
    public string Description => "RPF archive commands.";
    public string Usage => "CodeWalker.CLI --gtafolder <path> rpf <list|inspect|extract|create|edit|crypto|maintenance|util> ...";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing rpf subcommand.", new { usage = Usage });
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => List(context),
            "inspect" => Inspect(context, args.Skip(1).ToArray()),
            "extract" => Extract(context, args.Skip(1).ToArray()),
            "create" => Create(context, args.Skip(1).ToArray()),
            "edit" => Edit(context, args.Skip(1).ToArray()),
            "crypto" => Crypto(context, args.Skip(1).ToArray()),
            "maintenance" => Maintenance(context, args.Skip(1).ToArray()),
            "util" => Util(context, args.Skip(1).ToArray()),
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

    private static CommandResult Inspect(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing inspect subcommand.", new { usage = "CodeWalker.CLI --gtafolder <path> rpf inspect <stats|files|child|defrag-size|defragment-size (alias)> ..." });
        }

        var options = OptionParser.Parse(args.Skip(1).ToArray());
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));

        return args[0].ToLowerInvariant() switch
        {
            "stats" => CommandResult.Ok("Archive stats", new
            {
                archive = archive.Path,
                archive.Name,
                archive.FilePath,
                archive.FileSize,
                archive.Encryption,
                archive.TotalFileCount,
                archive.TotalFolderCount,
                archive.TotalResourceCount,
                archive.TotalBinaryFileCount,
                archive.GrandTotalRpfCount,
                archive.GrandTotalFileCount,
                archive.GrandTotalFolderCount,
                archive.GrandTotalResourceCount,
                archive.GrandTotalBinaryFileCount,
                topParent = archive.GetTopParent()?.Path,
                physicalPath = archive.GetPhysicalFilePath()
            }),
            "files" => InspectFiles(archive, options),
            "child" => InspectChild(archive, options),
            "defrag-size" or "defragment-size" => InspectDefragSize(archive, options),
            _ => CommandResult.Error($"Unknown inspect subcommand: {args[0]}")
        };
    }

    private static CommandResult InspectFiles(RpfFile archive, OptionParser options)
    {
        var folder = options.Get("folder") ?? archive.Path;
        var recurse = options.GetBool("recurse", false);
        var files = archive.GetFiles(folder, recurse)
            .Select(f => new { f.Path, f.Name, type = f.GetType().Name, size = f.GetFileSize() })
            .OrderBy(f => f.Path)
            .ToArray();

        return CommandResult.Ok($"Found {files.Length} file(s).", new { archive = archive.Path, folder, recurse, files });
    }

    private static CommandResult InspectChild(RpfFile archive, OptionParser options)
    {
        var entryPath = options.GetRequired("entry");
        var entry = FindFileEntry(archive, entryPath);
        if (entry == null)
        {
            return CommandResult.Error($"Entry not found in archive: {entryPath}");
        }

        var child = archive.FindChildArchive(entry);
        if (child == null)
        {
            return CommandResult.Error($"Child archive not found for entry: {entry.Path}");
        }

        return CommandResult.Ok("Child archive found.", new { entry = entry.Path, child.Name, child.Path, child.FileSize, child.Encryption });
    }

    private static CommandResult InspectDefragSize(RpfFile archive, OptionParser options)
    {
        var recursive = options.GetBool("recursive", true);
        var size = archive.GetDefragmentedFileSize(recursive);
        return CommandResult.Ok("Defragmented size computed.", new { archive = archive.Path, recursive, size });
    }

    private static CommandResult Extract(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return args[0].ToLowerInvariant() switch
            {
                "file" => ExtractFile(context, args.Skip(1).ToArray()),
                "scripts" => ExtractScripts(context, args.Skip(1).ToArray()),
                "test-all" => ExtractTestAll(context, args.Skip(1).ToArray()),
                _ => CommandResult.Error($"Unknown extract subcommand: {args[0]}")
            };
        }

        return ExtractFile(context, args);
    }

    private static CommandResult ExtractFile(CommandContext context, IReadOnlyList<string> args)
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

        var targetPath = OutputPathResolver.ResolveAndCreate(outputPath, entry.Name);
        File.WriteAllBytes(targetPath, bytes);

        return CommandResult.Ok("Entry extracted.", new
        {
            archive = archive.Path,
            entry = entry.Path,
            bytes = bytes.Length,
            output = targetPath
        });
    }

    private static CommandResult ExtractScripts(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var outputPath = options.GetRequired("output");
        Directory.CreateDirectory(outputPath);

        var log = new List<string>();
        archive.ExtractScripts(outputPath, s => log.Add(s));

        return CommandResult.Ok("Script extraction finished.", new
        {
            archive = archive.Path,
            output = outputPath,
            lastError = archive.LastError,
            log = log.TakeLast(MaxLogEntries).ToArray()
        });
    }

    private static CommandResult ExtractTestAll(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var report = archive.TestExtractAllFiles();

        return CommandResult.Ok("Archive test extraction finished.", new
        {
            archive = archive.Path,
            extractedBytes = archive.ExtractedByteCount,
            report
        });
    }

    private static CommandResult Util(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing util subcommand.", new { usage = "CodeWalker.CLI rpf util <compress|decompress|flags-from-size|flags-from-blocks|size-from-flags|version-from-flags|page-flags> ..." });
        }

        return args[0].ToLowerInvariant() switch
        {
            "compress" => UtilCompress(args.Skip(1).ToArray()),
            "decompress" => UtilDecompress(context, args.Skip(1).ToArray()),
            "flags-from-size" => UtilFlagsFromSize(args.Skip(1).ToArray()),
            "flags-from-blocks" => UtilFlagsFromBlocks(args.Skip(1).ToArray()),
            "size-from-flags" => UtilSizeFromFlags(args.Skip(1).ToArray()),
            "version-from-flags" => UtilVersionFromFlags(args.Skip(1).ToArray()),
            "page-flags" => UtilPageFlags(args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown util subcommand: {args[0]}")
        };
    }

    private static CommandResult UtilCompress(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var input = options.GetRequired("input");
        var output = options.GetRequired("output");
        if (!File.Exists(input))
        {
            return CommandResult.Error($"Input file not found: {input}");
        }

        var source = File.ReadAllBytes(input);
        var compressed = RpfFile.CompressBytes(source);
        File.WriteAllBytes(output, compressed);
        return CommandResult.Ok("Compression complete.", new { input, output, sourceBytes = source.Length, compressedBytes = compressed.Length });
    }

    private static CommandResult UtilDecompress(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var input = options.GetRequired("input");
        var output = options.GetRequired("output");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        if (!File.Exists(input))
        {
            return CommandResult.Error($"Input file not found: {input}");
        }

        var source = File.ReadAllBytes(input);
        var decompressed = archive.DecompressBytes(source);
        if (decompressed == null)
        {
            return CommandResult.Error("Decompression failed.", new { archive = archive.Path, archive.LastError });
        }

        File.WriteAllBytes(output, decompressed);
        return CommandResult.Ok("Decompression complete.", new { archive = archive.Path, input, output, sourceBytes = source.Length, decompressedBytes = decompressed.Length });
    }

    private static CommandResult UtilFlagsFromSize(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var size = ParseIntOption(options.GetRequired("size"), "size");
        var version = ParseUIntOption(options.Get("version") ?? "0", "version");
        var flags = RpfResourceFileEntry.GetFlagsFromSize(size, version);
        return CommandResult.Ok("Flags computed from size.", new { size, version, flags, flagsHex = $"0x{flags:X8}" });
    }

    private static CommandResult UtilFlagsFromBlocks(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var blockCount = ParseUIntOption(options.GetRequired("block-count"), "block-count");
        var blockSize = ParseUIntOption(options.GetRequired("block-size"), "block-size");
        var version = ParseUIntOption(options.Get("version") ?? "0", "version");
        var flags = RpfResourceFileEntry.GetFlagsFromBlocks(blockCount, blockSize, version);
        return CommandResult.Ok("Flags computed from blocks.", new { blockCount, blockSize, version, flags, flagsHex = $"0x{flags:X8}" });
    }

    private static CommandResult UtilSizeFromFlags(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var flags = ParseUInt(options.GetRequired("flags"));
        var size = RpfResourceFileEntry.GetSizeFromFlags(flags);
        return CommandResult.Ok("Size computed from flags.", new { flags, flagsHex = $"0x{flags:X8}", size });
    }

    private static CommandResult UtilVersionFromFlags(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var sysFlags = ParseUInt(options.GetRequired("sys-flags"));
        var gfxFlags = ParseUInt(options.GetRequired("gfx-flags"));
        var version = RpfResourceFileEntry.GetVersionFromFlags(sysFlags, gfxFlags);
        return CommandResult.Ok("Version computed from flags.", new { sysFlags, gfxFlags, version });
    }

    private static CommandResult UtilPageFlags(IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var raw = ParseUInt(options.GetRequired("flags"));
        var flags = new RpfResourcePageFlags(raw);
        var pages = flags.Pages?.Select(p => new { p.Offset, p.Size }).ToArray() ?? Array.Empty<object>();
        return CommandResult.Ok("Page flags decoded.", new { flags = raw, flagsHex = $"0x{raw:X8}", flags.BaseShift, flags.BaseSize, flags.Count, flags.Size, pageCounts = flags.PageCounts, pageSizes = flags.PageSizes, pages });
    }

    private static CommandResult Create(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing create subcommand.", new { usage = "CodeWalker.CLI --gtafolder <path> rpf create <root-archive|archive|directory|file> ... --force true" });
        }

        return args[0].ToLowerInvariant() switch
        {
            "root-archive" => CreateRootArchive(context, args.Skip(1).ToArray()),
            "archive" => CreateArchive(context, args.Skip(1).ToArray()),
            "directory" => CreateDirectory(context, args.Skip(1).ToArray()),
            "file" => CreateFile(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown create subcommand: {args[0]}")
        };
    }

    private static CommandResult CreateRootArchive(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "create root archive");
        var gtaFolder = context.RequireGtaFolder();
        var relpath = options.GetRequired("relpath");
        var encryption = ParseEncryption(options.Get("encryption"));
        var file = RpfFile.CreateNew(gtaFolder, relpath, encryption);

        return CommandResult.Ok("Root archive created.", new
        {
            file.Name,
            file.Path,
            file.FilePath,
            file.FileSize,
            file.Encryption
        });
    }

    private static CommandResult CreateArchive(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "create archive");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var dir = FindDirectoryEntry(archive, options.Get("dir"));
        var name = options.GetRequired("name");
        var encryption = ParseEncryption(options.Get("encryption"));
        var created = RpfFile.CreateNew(dir, name, encryption);

        return CommandResult.Ok("Child archive created.", new
        {
            parentArchive = archive.Path,
            directory = dir.Path,
            created.Name,
            created.Path,
            created.FileSize,
            created.Encryption
        });
    }

    private static CommandResult CreateDirectory(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "create directory");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var dir = FindDirectoryEntry(archive, options.Get("dir"));
        var name = options.GetRequired("name");
        var created = RpfFile.CreateDirectory(dir, name);

        return CommandResult.Ok("Directory created.", new
        {
            archive = archive.Path,
            parentDirectory = dir.Path,
            created.Name,
            created.Path
        });
    }

    private static CommandResult CreateFile(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "create file");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var dir = FindDirectoryEntry(archive, options.Get("dir"));
        var name = options.GetRequired("name");
        var input = options.GetRequired("input");
        var overwrite = options.GetBool("overwrite", true);

        if (!File.Exists(input))
        {
            return CommandResult.Error($"Input file not found: {input}");
        }

        var bytes = File.ReadAllBytes(input);
        var created = RpfFile.CreateFile(dir, name, bytes, overwrite);
        return CommandResult.Ok("File created.", new
        {
            archive = archive.Path,
            directory = dir.Path,
            created.Name,
            created.Path,
            size = created.GetFileSize()
        });
    }

    private static CommandResult Edit(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing edit subcommand.", new { usage = "CodeWalker.CLI --gtafolder <path> rpf edit <rename-entry|delete-entry> ... --force true" });
        }

        return args[0].ToLowerInvariant() switch
        {
            "rename-entry" => RenameEntry(context, args.Skip(1).ToArray()),
            "delete-entry" => DeleteEntry(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown edit subcommand: {args[0]}")
        };
    }

    private static CommandResult RenameEntry(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "rename entry");
        var manager = context.CreateRpfManager();
        var path = options.GetRequired("path");
        var newName = options.GetRequired("newname");
        var entry = manager.GetEntry(path);
        if (entry == null)
        {
            return CommandResult.Error($"Entry not found: {path}");
        }

        var oldPath = entry.Path;
        RpfFile.RenameEntry(entry, newName);

        return CommandResult.Ok("Entry renamed.", new
        {
            previousPath = oldPath,
            entry.Name,
            entry.Path
        });
    }

    private static CommandResult DeleteEntry(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "delete entry");
        var manager = context.CreateRpfManager();
        var path = options.GetRequired("path");
        var entry = manager.GetEntry(path);
        if (entry == null)
        {
            return CommandResult.Error($"Entry not found: {path}");
        }

        var deletedPath = entry.Path;
        RpfFile.DeleteEntry(entry);

        return CommandResult.Ok("Entry deleted.", new { path = deletedPath });
    }

    private static CommandResult Crypto(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing crypto subcommand.", new { usage = "CodeWalker.CLI --gtafolder <path> rpf crypto <is-valid|ensure-valid|set> ..." });
        }

        return args[0].ToLowerInvariant() switch
        {
            "is-valid" => CryptoIsValid(context, args.Skip(1).ToArray()),
            "ensure-valid" => CryptoEnsureValid(context, args.Skip(1).ToArray()),
            "set" => CryptoSet(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown crypto subcommand: {args[0]}")
        };
    }

    private static CommandResult CryptoIsValid(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var recursive = options.GetBool("recursive", false);
        var valid = RpfFile.IsValidEncryption(archive, recursive);

        return CommandResult.Ok("Encryption validation complete.", new
        {
            archive = archive.Path,
            recursive,
            valid,
            encryption = archive.Encryption.ToString()
        });
    }

    private static CommandResult CryptoEnsureValid(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "ensure valid encryption");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var recursive = options.GetBool("recursive", false);
        var changed = new List<string>();
        var ok = RpfFile.EnsureValidEncryption(archive, file =>
        {
            changed.Add(file.Path);
            return true;
        }, recursive);

        return CommandResult.Ok("Ensure valid encryption complete.", new
        {
            archive = archive.Path,
            recursive,
            ok,
            changed
        });
    }

    private static CommandResult CryptoSet(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "set encryption");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var encryption = ParseEncryption(options.GetRequired("encryption"));
        RpfFile.SetEncryptionType(archive, encryption);

        return CommandResult.Ok("Encryption updated.", new { archive = archive.Path, encryption = encryption.ToString() });
    }

    private static CommandResult Maintenance(CommandContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Error("Missing maintenance subcommand.", new { usage = "CodeWalker.CLI --gtafolder <path> rpf maintenance <defrag-size|defragment-size (alias)|defragment> ..." });
        }

        return args[0].ToLowerInvariant() switch
        {
            "defrag-size" or "defragment-size" => MaintenanceDefragSize(context, args.Skip(1).ToArray()),
            "defragment" => MaintenanceDefragment(context, args.Skip(1).ToArray()),
            _ => CommandResult.Error($"Unknown maintenance subcommand: {args[0]}")
        };
    }

    private static CommandResult MaintenanceDefragSize(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        return InspectDefragSize(archive, options);
    }

    private static CommandResult MaintenanceDefragment(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        EnsureForce(options, "defragment archive");
        var manager = context.CreateRpfManager();
        var archive = FindArchive(manager, options.GetRequired("archive"));
        var recursive = options.GetBool("recursive", true);
        var log = new List<string>();
        RpfFile.Defragment(archive, (status, progress) => log.Add($"{status} {progress:P1}"), recursive);

        return CommandResult.Ok("Archive defragmentation complete.", new
        {
            archive = archive.Path,
            recursive,
            archive.FileSize,
            log = log.TakeLast(MaxLogEntries).ToArray()
        });
    }

    private static uint ParseUInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static RpfEncryption ParseEncryption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RpfEncryption.OPEN;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "OPEN" => RpfEncryption.OPEN,
            "NONE" => RpfEncryption.NONE,
            "AES" => RpfEncryption.AES,
            "NG" => RpfEncryption.NG,
            _ => throw new InvalidOperationException($"Unsupported --encryption value: {value}. Use OPEN|NONE|AES|NG.")
        };
    }

    private static void EnsureForce(OptionParser options, string action)
    {
        if (!options.GetBool("force", false))
        {
            throw new InvalidOperationException($"Refusing to {action} without --force true.");
        }
    }

    private static int ParseIntOption(string value, string optionName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"Invalid --{optionName} value: must be an integer.");
        }

        return result;
    }

    private static uint ParseUIntOption(string value, string optionName)
    {
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"Invalid --{optionName} value: must be a non-negative integer.");
        }

        return result;
    }

    private static RpfFile FindArchive(RpfManager manager, string archivePath)
    {
        var archive = manager.FindRpfFile(archivePath);
        if (archive == null)
        {
            throw new InvalidOperationException($"Archive not found: {archivePath}");
        }
        return archive;
    }

    private static RpfFileEntry? FindFileEntry(RpfFile archive, string entryPath)
    {
        return archive.AllEntries?.OfType<RpfFileEntry>()
            .FirstOrDefault(e => string.Equals(NormalizePath(e.Path), NormalizePath(entryPath), StringComparison.OrdinalIgnoreCase));
    }

    private static RpfDirectoryEntry FindDirectoryEntry(RpfFile archive, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return archive.Root;
        }

        var dir = archive.AllEntries?.OfType<RpfDirectoryEntry>()
            .FirstOrDefault(d => string.Equals(NormalizePath(d.Path), NormalizePath(directoryPath), StringComparison.OrdinalIgnoreCase));

        if (dir == null)
        {
            throw new InvalidOperationException($"Directory not found in archive: {directoryPath}");
        }

        return dir;
    }

    private static string NormalizePath(string path) =>
        path.Replace('/', '\\').Trim().ToLowerInvariant();
}

internal sealed class FileCommand : ICliCommand
{
    public string Name => "file";
    public string Description => "Game file commands.";
    public string Usage => "CodeWalker.CLI file <info|export> ...";

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

        var exportedFormat = "raw";
        var exportedXml = MetaXml.GetXml(entry, data, out _, Path.GetDirectoryName(outputPath) ?? string.Empty);
        if (!string.IsNullOrEmpty(exportedXml))
        {
            var target = OutputPathResolver.ResolveAndCreate(outputPath, entry.Name + ".xml");
            File.WriteAllText(target, exportedXml);
            outputPath = target;
            exportedFormat = "xml";
        }
        else
        {
            var target = OutputPathResolver.ResolveAndCreate(outputPath, entry.Name);
            File.WriteAllBytes(target, data);
            outputPath = target;
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
                    var ymapFile = RpfFile.GetFile<YmapFile>(entry, data);
                    details["entities"] = ymapFile?.AllEntities?.Length ?? 0;
                    details["carGenerators"] = ymapFile?.CarGenerators?.Length ?? 0;
                    break;
                case ".ytyp":
                    var ytypFile = RpfFile.GetFile<YtypFile>(entry, data);
                    details["archetypes"] = ytypFile?.AllArchetypes?.Length ?? 0;
                    break;
                case ".ybn":
                    var ybnFile = RpfFile.GetFile<YbnFile>(entry, data);
                    details["hasBounds"] = ybnFile?.Bounds != null;
                    break;
                case ".ydr":
                    var ydrFile = RpfFile.GetFile<YdrFile>(entry, data);
                    details["hasDrawable"] = ydrFile?.Drawable != null;
                    break;
                case ".ydd":
                    var yddFile = RpfFile.GetFile<YddFile>(entry, data);
                    details["drawables"] = yddFile?.DrawableDict?.Drawables?.data_items?.Length ?? 0;
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
    private const int DefaultSearchLimit = 100;

    public string Name => "search";
    public string Description => "Search for files in game archives.";
    public string Usage => "CodeWalker.CLI --gtafolder <path> search --pattern <text> [--limit <n>]";

    public CommandResult Execute(CommandContext context, IReadOnlyList<string> args)
    {
        var options = OptionParser.Parse(args);
        var pattern = options.GetRequired("pattern");
        var limitText = options.Get("limit");
        var limit = ParseLimit(limitText);
        if (limit <= 0)
        {
            return CommandResult.Error("Invalid --limit value. It must be a positive integer.");
        }

        var manager = context.CreateRpfManager();
        var matches = manager.EntryDict
            .Where(kv => kv.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
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

    private static int ParseLimit(string? limitText)
    {
        if (string.IsNullOrWhiteSpace(limitText))
        {
            return DefaultSearchLimit;
        }

        return int.TryParse(limitText, out var limit) ? limit : -1;
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
                throw new InvalidOperationException($"Missing value for option --{key}. Expected: --{key} <value>.");
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

    public bool GetBool(string key, bool defaultValue)
    {
        var value = Get(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => throw new InvalidOperationException($"Invalid boolean option --{key} value: {value}. Use true|false.")
        };
    }
}

internal sealed class CliOptions
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Json;
    public bool Compact { get; init; }
    public string? GtaFolder { get; init; }
    public string[] CommandArgs { get; init; } = Array.Empty<string>();

    public static CliOptions Parse(string[] args)
    {
        var remaining = new List<string>();
        string? gtaFolder = null;
        string? formatToken = null;
        var xmlAlias = false;
        var compact = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.Equals("--xml", StringComparison.OrdinalIgnoreCase))
            {
                xmlAlias = true;
                continue;
            }

            if (token.Equals("--compact", StringComparison.OrdinalIgnoreCase))
            {
                compact = true;
                continue;
            }

            if (token.Equals("--format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return new CliOptions { ErrorMessage = "Missing value for --format." };
                }

                formatToken = args[++i];
                continue;
            }

            if (token.Equals("--gtafolder", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return new CliOptions { ErrorMessage = "Missing value for --gtafolder." };
                }

                gtaFolder = args[++i];
                continue;
            }

            remaining.Add(token);
        }

        var format = ParseFormat(formatToken, xmlAlias, out var formatError);
        if (!string.IsNullOrEmpty(formatError))
        {
            return new CliOptions { ErrorMessage = formatError };
        }

        return new CliOptions
        {
            Success = true,
            OutputFormat = format,
            Compact = compact,
            GtaFolder = gtaFolder,
            CommandArgs = remaining.ToArray()
        };
    }

    private static OutputFormat ParseFormat(string? explicitFormat, bool xmlAlias, out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(explicitFormat))
        {
            switch (explicitFormat.Trim().ToLowerInvariant())
            {
                case "json": return OutputFormat.Json;
                case "xml": return OutputFormat.Xml;
                case "text": return OutputFormat.Text;
                default:
                    error = $"Unsupported --format value: {explicitFormat}. Use json|xml|text.";
                    return OutputFormat.Json;
            }
        }

        return xmlAlias ? OutputFormat.Xml : OutputFormat.Json;
    }
}

internal static class OutputPathResolver
{
    public static string ResolveAndCreate(string outputPath, string fileName)
    {
        var targetPath = outputPath;
        if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            targetPath = Path.Combine(outputPath, fileName);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        return targetPath;
    }
}
