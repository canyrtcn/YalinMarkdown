using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace YalinMarkdown;

internal enum FindingSeverity
{
    Info,
    Warning,
    Critical
}

internal sealed record AgentFinding(
    FindingSeverity Severity,
    string Category,
    string Message,
    string? FilePath = null,
    int? Line = null)
{
    public override string ToString()
    {
        var location = FilePath is null ? string.Empty : $"{Path.GetFileName(FilePath)}{(Line is null ? string.Empty : $":{Line}")} · ";
        return $"[{Severity}] {location}{Category}: {Message}";
    }
}

internal sealed record SectionTokenCost(string Heading, int Tokens, int Line)
{
    public override string ToString() => $"~{Tokens:N0} tokens · L{Line} · {Heading}";
}

internal sealed class AgentAnalysisResult
{
    public int Tokens { get; init; }
    public int Lines { get; init; }
    public List<AgentFinding> Findings { get; } = [];
    public List<SectionTokenCost> Sections { get; } = [];
}

internal sealed class WorkspaceAnalysisResult
{
    public string RootPath { get; init; } = string.Empty;
    public List<string> AgentFiles { get; } = [];
    public List<AgentFinding> Findings { get; } = [];
    public int TotalTokens { get; set; }
    public string MergedInstructions { get; set; } = string.Empty;
}

internal sealed record SkillMatch(string FilePath, string Name, int Score, string Description)
{
    public override string ToString() => $"{Score,3}% · {Name} · {Description}";
}

internal static class AgentAnalyzer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly string[] AgentFileNames =
    [
        "AGENTS.md", "AGENTS.override.md", "SOUL.md", "SKILL.md", "CLAUDE.md",
        "USER.md", "MEMORY.md", "TOOLS.md", "HEARTBEAT.md"
    ];

    private static readonly Regex SecretPattern = new(
        @"(?ix)(api[_-]?key|access[_-]?token|secret|password|private[_-]?key)\s*[:=]\s*['""]?[A-Za-z0-9_\-\/+=]{12,}",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex DangerousCommandPattern = new(
        @"(?ix)(curl\s+.+\|\s*(bash|sh)|wget\s+.+\|\s*(bash|sh)|rm\s+-rf|Remove-Item\s+.+-Recurse.+-Force|Invoke-Expression|\biex\b|chmod\s+777|allowed-tools\s*:\s*\*)",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex VagueInstructionPattern = new(
        @"(?ix)\b(as needed|when appropriate|do it properly|best practices|gerekirse|uygun olduğunda|düzgün yap|en iyi uygulamalar)\b",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex PathPattern = new(
        @"`(?<path>(?:\.{0,2}[\\/])?[\w.\-\\/]+\.[A-Za-z0-9]{1,8})`",
        RegexOptions.Compiled, RegexTimeout);

    internal static AgentAnalysisResult AnalyzeDocument(string fileName, string content, string? fullPath = null, string? workspaceRoot = null)
    {
        var result = new AgentAnalysisResult
        {
            Tokens = EstimateTokens(content),
            Lines = content.Replace("\r\n", "\n").Split('\n').Length
        };

        AnalyzeSections(content, result);
        AnalyzeSecurity(content, fullPath, result);
        AnalyzeDuplicates(content, fullPath, result);
        AnalyzeContradictions(content, fullPath, result);
        AnalyzeVagueInstructions(content, fullPath, result);
        AnalyzePaths(content, fullPath, workspaceRoot, result);
        AnalyzeFileType(fileName, content, fullPath, result);
        return result;
    }

    internal static WorkspaceAnalysisResult AnalyzeWorkspace(string rootPath, string? activeFilePath = null)
    {
        var result = new WorkspaceAnalysisResult { RootPath = rootPath };
        // A workspace with very many Markdown files must not consume unbounded resources.
        foreach (var file in EnumerateMarkdownFiles(rootPath).Take(2_000))
        {
            if (IsAgentFile(file))
                result.AgentFiles.Add(file);

            try
            {
                var content = File.ReadAllText(file);
                var analysis = AnalyzeDocument(Path.GetFileName(file), content, file, rootPath);
                result.TotalTokens += analysis.Tokens;
                result.Findings.AddRange(analysis.Findings);
            }
            catch (Exception ex)
            {
                result.Findings.Add(new AgentFinding(
                    FindingSeverity.Warning, "Read", $"File could not be analyzed: {ex.Message}", file));
            }
        }

        result.AgentFiles.Sort(StringComparer.OrdinalIgnoreCase);
        result.MergedInstructions = BuildMergedInstructions(rootPath, activeFilePath, result.AgentFiles);
        return result;
    }

    internal static bool IsAgentFile(string filePath) =>
        AgentFileNames.Contains(Path.GetFileName(filePath), StringComparer.OrdinalIgnoreCase);

    internal static int EstimateTokens(string text) =>
        text.Length == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(Encoding.UTF8.GetByteCount(text) / 4.0));

    internal static List<SkillMatch> MatchSkills(string rootPath, string task)
    {
        var taskWords = GetMeaningfulWords(task);
        var matches = new List<SkillMatch>();
        if (taskWords.Count == 0)
            return matches;

        foreach (var file in EnumerateMarkdownFiles(rootPath)
                     .Where(file => Path.GetFileName(file).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var content = File.ReadAllText(file);
                var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
                var frontmatter = content.StartsWith("---", StringComparison.Ordinal) && end > 0 ? content[3..end] : string.Empty;
                var name = GetFrontmatterValue(frontmatter, "name") ?? Path.GetFileName(Path.GetDirectoryName(file));
                var description = GetFrontmatterValue(frontmatter, "description") ?? string.Empty;
                var skillWords = GetMeaningfulWords($"{name} {description}");
                var overlap = taskWords.Intersect(skillWords, StringComparer.OrdinalIgnoreCase).Count();
                var score = Math.Min(100, overlap * 25 + (description.Contains(task, StringComparison.OrdinalIgnoreCase) ? 40 : 0));
                if (score > 0)
                    matches.Add(new SkillMatch(file, name ?? "unnamed-skill", score, description));
            }
            catch
            {
                // Unreadable skills are reported by workspace analysis.
            }
        }
        return matches.OrderByDescending(match => match.Score).ThenBy(match => match.Name).ToList();
    }

    private static IEnumerable<string> EnumerateMarkdownFiles(string rootPath)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string resolvedDirectory;
            try
            {
                resolvedDirectory = Path.GetFullPath(directory);
                if (!visited.Add(resolvedDirectory))
                    continue;
            }
            catch
            {
                continue;
            }
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                // Materialize within the try block: lazy enumeration can otherwise throw
                // outside the exception handler when an inaccessible folder is encountered.
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory, "*.md");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var canAnalyze = false;
                try
                {
                    canAnalyze = new FileInfo(file).Length <= 2 * 1024 * 1024;
                }
                catch
                {
                    // Ignore files that cannot be inspected safely.
                }
                if (canAnalyze)
                    yield return file;
            }

            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }
                if (name.StartsWith('.') ||
                    name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("dist", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(child);
            }
        }
    }

    private static void AnalyzeSections(string content, AgentAnalysisResult result)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var heading = "(Introduction)";
        var headingLine = 1;
        var section = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].TrimStart().StartsWith('#'))
            {
                AddSection(result, heading, headingLine, section);
                heading = lines[index].Trim().TrimStart('#').Trim();
                headingLine = index + 1;
            }
            section.AppendLine(lines[index]);
        }
        AddSection(result, heading, headingLine, section);
    }

    private static void AddSection(AgentAnalysisResult result, string heading, int line, StringBuilder section)
    {
        if (section.Length == 0)
            return;
        result.Sections.Add(new SectionTokenCost(heading, EstimateTokens(section.ToString()), line));
        section.Clear();
    }

    private static void AnalyzeSecurity(string content, string? filePath, AgentAnalysisResult result)
    {
        AddRegexFindings(content, SecretPattern, FindingSeverity.Critical, "Secret",
            "Possible secret or credential. Keep credentials outside agent instruction files.", filePath, result);
        AddRegexFindings(content, DangerousCommandPattern, FindingSeverity.Critical, "Command",
            "Potentially destructive or overly broad command/permission.", filePath, result);

        if (content.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("ignore all previous instructions", StringComparison.OrdinalIgnoreCase))
        {
            result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Prompt injection",
                "Instruction asks the agent to ignore previous instructions.", filePath));
        }
    }

    private static void AnalyzeDuplicates(string content, string? filePath, AgentAnalysisResult result)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var normalized = NormalizeInstruction(lines[index]);
            if (normalized.Length < 24)
                continue;
            if (seen.TryGetValue(normalized, out var firstLine))
            {
                result.Findings.Add(new AgentFinding(FindingSeverity.Info, "Duplicate",
                    $"Instruction repeats line {firstLine}.", filePath, index + 1));
            }
            else
            {
                seen[normalized] = index + 1;
            }
        }
    }

    private static void AnalyzeContradictions(string content, string? filePath, AgentAnalysisResult result)
    {
        var positive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var negative = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var normalized = NormalizeInstruction(lines[index]);
            if (normalized.Length < 12)
                continue;

            var isNegative = normalized.Contains("do not ") || normalized.Contains("never ") ||
                             normalized.Contains("don't ") || normalized.Contains("yapma") ||
                             normalized.Contains("asla ");
            var core = normalized
                .Replace("do not ", string.Empty)
                .Replace("never ", string.Empty)
                .Replace("don't ", string.Empty)
                .Replace("always ", string.Empty)
                .Replace("asla ", string.Empty)
                .Replace("her zaman ", string.Empty)
                .Replace("yapma", "yap");
            if (core.Length < 12)
                continue;

            var opposite = isNegative ? positive : negative;
            if (opposite.TryGetValue(core, out var oppositeLine))
            {
                result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Conflict",
                    $"This instruction may conflict with line {oppositeLine}.", filePath, index + 1));
            }
            (isNegative ? negative : positive)[core] = index + 1;
        }
    }

    private static void AnalyzeVagueInstructions(string content, string? filePath, AgentAnalysisResult result) =>
        AddRegexFindings(content, VagueInstructionPattern, FindingSeverity.Info, "Clarity",
            "Vague instruction; define an observable condition or expected action.", filePath, result);

    private static void AnalyzePaths(string content, string? filePath, string? workspaceRoot, AgentAnalysisResult result)
    {
        if (workspaceRoot is null)
            return;
        foreach (Match match in PathPattern.Matches(content))
        {
            var path = match.Groups["path"].Value;
            if (path.Contains('*') || Uri.TryCreate(path, UriKind.Absolute, out _))
                continue;
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    result.Findings.Add(new AgentFinding(FindingSeverity.Info, "Path",
                        $"Referenced path does not exist: {path}", filePath, GetLine(content, match.Index)));
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Path",
                    $"Referenced path is invalid: {path}", filePath, GetLine(content, match.Index)));
            }
        }
    }

    private static void AnalyzeFileType(string fileName, string content, string? filePath, AgentAnalysisResult result)
    {
        if (fileName.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("AGENTS.override.md", StringComparison.OrdinalIgnoreCase))
        {
            RequireConcept(content, ["test", "pytest", "npm test", "dotnet test", "cargo test"], "Tests",
                "No concrete test command or test guidance found.", filePath, result);
            RequireConcept(content, ["install", "setup", "build", "kurulum", "derle"], "Setup",
                "No setup/build guidance found.", filePath, result);
            RequireConcept(content, ["security", "secret", "credential", "güvenlik", "şifre"], "Security",
                "No explicit security or secret-handling guidance found.", filePath, result, FindingSeverity.Info);
        }
        else if (fileName.Equals("SOUL.md", StringComparison.OrdinalIgnoreCase))
        {
            RequireConcept(content, ["identity", "personality", "tone", "kimlik", "kişilik", "üslup"], "Soul",
                "No clear identity/personality/tone section found.", filePath, result);
            RequireConcept(content, ["boundary", "limits", "must not", "sınır", "yapmamalı"], "Soul",
                "No behavioral boundaries found.", filePath, result);
        }
        else if (fileName.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            AnalyzeSkill(content, filePath, result);
        }
    }

    private static void AnalyzeSkill(string content, string? filePath, AgentAnalysisResult result)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            result.Findings.Add(new AgentFinding(FindingSeverity.Critical, "Skill",
                "SKILL.md must begin with YAML frontmatter.", filePath, 1));
            return;
        }
        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            result.Findings.Add(new AgentFinding(FindingSeverity.Critical, "Skill",
                "YAML frontmatter is not closed.", filePath, 1));
            return;
        }
        var frontmatter = content[3..end];
        var name = GetFrontmatterValue(frontmatter, "name");
        var description = GetFrontmatterValue(frontmatter, "description");
        if (string.IsNullOrWhiteSpace(name))
            result.Findings.Add(new AgentFinding(FindingSeverity.Critical, "Skill", "Missing required 'name'.", filePath, 1));
        else if (!Regex.IsMatch(name, @"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.None, RegexTimeout))
            result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Skill", "Skill name should use lowercase kebab-case.", filePath, 1));
        if (string.IsNullOrWhiteSpace(description))
            result.Findings.Add(new AgentFinding(FindingSeverity.Critical, "Skill", "Missing required 'description'.", filePath, 1));
        else if (description.Length < 30)
            result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Skill", "Description may be too vague to trigger reliably.", filePath, 1));
        else if (description.Length > 500)
            result.Findings.Add(new AgentFinding(FindingSeverity.Info, "Skill", "Description is long and increases startup prompt cost.", filePath, 1));
        var directoryName = filePath is null ? null : Path.GetFileName(Path.GetDirectoryName(filePath));
        if (directoryName is not null && name is not null &&
            !directoryName.Equals(name, StringComparison.OrdinalIgnoreCase))
            result.Findings.Add(new AgentFinding(FindingSeverity.Warning, "Skill", "Skill name does not match its directory name.", filePath, 1));
    }

    private static string? GetFrontmatterValue(string frontmatter, string key)
    {
        foreach (var line in frontmatter.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                return line[(line.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
        }
        return null;
    }

    private static HashSet<string> GetMeaningfulWords(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}_-]{2,}", RegexOptions.None, RegexTimeout)
            .Select(match => match.Value)
            .Where(word => word is not ("the" or "and" or "for" or "with" or "this" or "that" or "use" or "when" or "bir" or "için" or "ile"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return words;
    }

    private static void RequireConcept(
        string content, string[] concepts, string category, string message, string? filePath,
        AgentAnalysisResult result, FindingSeverity severity = FindingSeverity.Warning)
    {
        if (!concepts.Any(concept => content.Contains(concept, StringComparison.OrdinalIgnoreCase)))
            result.Findings.Add(new AgentFinding(severity, category, message, filePath));
    }

    private static void AddRegexFindings(
        string content, Regex pattern, FindingSeverity severity, string category, string message,
        string? filePath, AgentAnalysisResult result)
    {
        foreach (Match match in pattern.Matches(content))
            result.Findings.Add(new AgentFinding(severity, category, message, filePath, GetLine(content, match.Index)));
    }

    private static string NormalizeInstruction(string line)
    {
        var value = line.Trim().TrimStart('-', '*', '>', '#').Trim().ToLowerInvariant();
        return Regex.Replace(value, @"\s+", " ", RegexOptions.None, RegexTimeout);
    }

    private static int GetLine(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n')
                line++;
        return line;
    }

    private static string BuildMergedInstructions(string rootPath, string? activeFilePath, IReadOnlyCollection<string> agentFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Approximate Agent Instruction Context");
        builder.AppendLine();
        builder.AppendLine("> This view combines common workspace instruction files. Actual loading rules vary by agent.");
        builder.AppendLine();

        AddMergedFile(builder, Path.Combine(rootPath, "SOUL.md"), rootPath);
        AddMergedFile(builder, Path.Combine(rootPath, "USER.md"), rootPath);

        var activeDirectory = activeFilePath is not null && IsWithinRoot(rootPath, activeFilePath)
            ? Path.GetDirectoryName(activeFilePath) ?? rootPath
            : rootPath;
        var directories = new Stack<string>();
        for (var current = activeDirectory;
             current is not null && current.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
             current = Path.GetDirectoryName(current))
        {
            directories.Push(current);
            if (string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
                break;
        }

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            var overridePath = Path.Combine(directory, "AGENTS.override.md");
            var agentsPath = Path.Combine(directory, "AGENTS.md");
            AddMergedFile(builder, File.Exists(overridePath) ? overridePath : agentsPath, rootPath);
        }

        var skills = agentFiles.Where(file => Path.GetFileName(file).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)).ToList();
        if (skills.Count > 0)
        {
            builder.AppendLine("## Available Skill Metadata");
            foreach (var skill in skills)
            {
                try
                {
                    var content = File.ReadAllText(skill);
                    var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
                    var frontmatter = end > 0 ? content[3..end] : string.Empty;
                    builder.AppendLine($"- `{Path.GetRelativePath(rootPath, skill)}` — {GetFrontmatterValue(frontmatter, "description") ?? "No description"}");
                }
                catch
                {
                    builder.AppendLine($"- `{Path.GetRelativePath(rootPath, skill)}` — unreadable");
                }
            }
        }
        return builder.ToString();
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
            return relative != ".." &&
                   !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relative);
        }
        catch
        {
            return false;
        }
    }

    private static void AddMergedFile(StringBuilder builder, string filePath, string rootPath)
    {
        if (!File.Exists(filePath))
            return;
        try
        {
            builder.AppendLine($"## {Path.GetRelativePath(rootPath, filePath)}");
            builder.AppendLine(File.ReadAllText(filePath));
            builder.AppendLine();
        }
        catch
        {
            builder.AppendLine($"## {Path.GetRelativePath(rootPath, filePath)}");
            builder.AppendLine("> File could not be read.");
            builder.AppendLine();
        }
    }
}
