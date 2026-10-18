using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CoreBanking.ArchitectureTests;

/// <summary>
/// Enforces the structural decisions recorded in ADR-0001, ADR-0002 and ADR-0003.
/// The checks read project, build and solution files rather than loaded assemblies, so a
/// forbidden reference fails even when the referenced code is never used.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class RepositoryLayoutTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string[] ForbiddenPackagePrefixes =
    {
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore",
        "Confluent.Kafka",
        "StackExchange.Redis",
    };

    private static readonly string[] ApplicationLayer = { "Application" };

    private static readonly string[] ContractsLayer = { "Contracts" };

    private static readonly string[] TransportAndWorkerLayers = { "Infrastructure", "Api", "Workers" };

    private static readonly string[] ImplementationLayers = { "Application", "Infrastructure", "Api", "Workers" };

    private static readonly Regex ProjectNamePattern = new(
        "^CoreBanking\\.([A-Za-z]+)\\.(Domain|Application|Infrastructure|Contracts|Api|Workers|Observability|Testing|UnitTests|IntegrationTests|ContractTests|ArchitectureTests|EndToEndTests|PerformanceTests|SecurityTests)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RootTestNamePattern = new(
        "^CoreBanking\\.(ArchitectureTests|EndToEndTests|ContractTests|PerformanceTests|SecurityTests)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SolutionEntryPattern = new(
        "^Project\\(\"\\{[^}]+\\}\"\\) = \"[^\"]+\", \"([^\"]+\\.csproj)\",",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Global_json_pins_the_approved_sdk_line()
    {
        string path = Path.Combine(RepositoryRoot, "global.json");
        Assert.True(File.Exists(path), "global.json must exist at the repository root (ADR-0001).");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement sdk = document.RootElement.GetProperty("sdk");
        string version = sdk.GetProperty("version").GetString() ?? string.Empty;

        Assert.StartsWith("10.", version, StringComparison.Ordinal);
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    [Fact]
    public void Directory_build_props_applies_the_approved_policy()
    {
        string path = Path.Combine(RepositoryRoot, "Directory.Build.props");
        Assert.True(File.Exists(path), "Directory.Build.props must exist at the repository root (ADR-0003).");

        XDocument document = XDocument.Load(path);

        Assert.Equal("net10.0", PropertyValue(document, "TargetFramework"));
        Assert.Equal("enable", PropertyValue(document, "Nullable"));
        Assert.Equal("disable", PropertyValue(document, "ImplicitUsings"));
        Assert.Equal("true", PropertyValue(document, "TreatWarningsAsErrors"));
        Assert.Equal("Recommended", PropertyValue(document, "AnalysisMode"));
        Assert.Equal("true", PropertyValue(document, "EnforceCodeStyleInBuild"));
        Assert.Equal("true", PropertyValue(document, "Deterministic"));
    }

    [Fact]
    public void Central_build_files_declare_no_global_warning_suppression()
    {
        foreach (string file in new[] { "Directory.Build.props", "Directory.Packages.props" })
        {
            string path = Path.Combine(RepositoryRoot, file);
            if (!File.Exists(path))
            {
                continue;
            }

            XDocument document = XDocument.Load(path);
            Assert.Empty(document.Descendants("NoWarn"));
            Assert.Empty(document.Descendants("WarningsNotAsErrors"));
        }
    }

    [Fact]
    public void Domain_projects_reference_only_building_block_domain()
    {
        List<string> violations = new();

        foreach (string project in ProjectFiles().Where(project => string.Equals(LayerOf(ProjectName(project)), "Domain", StringComparison.Ordinal)))
        {
            foreach (string reference in ReferencedProjects(project))
            {
                string target = ProjectName(reference);
                bool allowed = target.StartsWith("CoreBanking.BuildingBlocks.", StringComparison.Ordinal)
                    && string.Equals(LayerOf(target), "Domain", StringComparison.Ordinal);
                if (!allowed)
                {
                    violations.Add(ProjectName(project) + " references " + target);
                }
            }
        }

        Assert.True(violations.Count == 0, "Domain projects must not reference other projects (ADR-0002): " + string.Join("; ", violations));
    }

    [Fact]
    public void Application_projects_do_not_reference_infrastructure_transport_or_workers()
    {
        AssertNoReferenceToLayers(ApplicationLayer, TransportAndWorkerLayers);
    }

    [Fact]
    public void Contracts_projects_do_not_reference_implementation_projects()
    {
        AssertNoReferenceToLayers(ContractsLayer, ImplementationLayers);
    }

    [Fact]
    public void No_project_references_another_service()
    {
        List<string> violations = new();

        foreach (string project in ProjectFiles())
        {
            string owner = OwnerOf(ProjectName(project));
            bool ownerIsBuildingBlock = owner.StartsWith("BuildingBlocks", StringComparison.Ordinal);

            foreach (string reference in ReferencedProjects(project))
            {
                string referenceOwner = OwnerOf(ProjectName(reference));
                bool referenceIsBuildingBlock = referenceOwner.StartsWith("BuildingBlocks", StringComparison.Ordinal);
                bool sameOwner = string.Equals(owner, referenceOwner, StringComparison.Ordinal);

                if (!sameOwner && !ownerIsBuildingBlock && !referenceIsBuildingBlock)
                {
                    violations.Add(ProjectName(project) + " references " + ProjectName(reference));
                }
            }
        }

        Assert.True(violations.Count == 0, "Cross-service project references are prohibited (ADR-0002): " + string.Join("; ", violations));
    }

    [Fact]
    public void Domain_application_and_contracts_projects_do_not_reference_forbidden_packages()
    {
        string[] guarded = { "Domain", "Application", "Contracts" };
        List<string> violations = new();

        foreach (string project in ProjectFiles().Where(project => guarded.Contains(LayerOf(ProjectName(project)), StringComparer.Ordinal)))
        {
            foreach (string package in PackageReferences(project))
            {
                if (ForbiddenPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add(ProjectName(project) + " references " + package);
                }
            }
        }

        Assert.True(violations.Count == 0, "Transport, persistence and provider packages are not permitted in Domain, Application or Contracts (ADR-0002): " + string.Join("; ", violations));
    }

    [Fact]
    public void Project_files_do_not_restate_central_build_policy()
    {
        string[] governed = { "TargetFramework", "Nullable", "ImplicitUsings", "TreatWarningsAsErrors", "AnalysisMode", "EnforceCodeStyleInBuild" };
        List<string> violations = new();

        foreach (string project in ProjectFiles())
        {
            XDocument document = XDocument.Load(project);
            foreach (string property in governed)
            {
                if (document.Descendants(property).Any())
                {
                    violations.Add(ProjectName(project) + " restates " + property);
                }
            }
        }

        Assert.True(violations.Count == 0, "Central build policy belongs in Directory.Build.props (ADR-0003): " + string.Join("; ", violations));
    }

    [Fact]
    public void Project_files_do_not_declare_package_versions()
    {
        List<string> violations = new();

        foreach (string project in ProjectFiles())
        {
            XDocument document = XDocument.Load(project);
            foreach (XElement reference in document.Descendants("PackageReference"))
            {
                string package = reference.Attribute("Include")?.Value ?? "(unnamed)";
                if (reference.Attribute("Version") is not null || reference.Elements("Version").Any())
                {
                    violations.Add(ProjectName(project) + " pins " + package);
                }
            }
        }

        Assert.True(violations.Count == 0, "Package versions belong in Directory.Packages.props (ADR-0003): " + string.Join("; ", violations));
    }

    [Fact]
    public void Projects_follow_the_documented_naming_and_layout()
    {
        List<string> violations = new();

        foreach (string project in ProjectFiles())
        {
            string name = ProjectName(project);
            string directory = Path.GetFileName(Path.GetDirectoryName(project) ?? string.Empty);
            string relative = Path.GetRelativePath(RepositoryRoot, project).Replace('\\', '/');

            if (!string.Equals(name, directory, StringComparison.Ordinal))
            {
                violations.Add(relative + " is not in a directory named after the project");
            }

            Match match = ProjectNamePattern.Match(name);
            if (RootTestNamePattern.IsMatch(name))
            {
                if (!relative.StartsWith("tests/", StringComparison.Ordinal))
                {
                    violations.Add(relative + " must live under tests/");
                }

                continue;
            }

            if (!match.Success)
            {
                violations.Add(name + " does not follow CoreBanking.<Owner>.<Layer>");
                continue;
            }

            string owner = match.Groups[1].Value;

            if (string.Equals(owner, "BuildingBlocks", StringComparison.Ordinal))
            {
                if (!relative.StartsWith("src/BuildingBlocks/", StringComparison.Ordinal))
                {
                    violations.Add(relative + " must live under src/BuildingBlocks/");
                }

                continue;
            }

            string expectedSource = "src/Services/" + owner + "/src/" + name + "/" + name + ".csproj";
            string expectedTests = "src/Services/" + owner + "/tests/" + name + "/" + name + ".csproj";
            if (!string.Equals(relative, expectedSource, StringComparison.Ordinal) && !string.Equals(relative, expectedTests, StringComparison.Ordinal))
            {
                violations.Add(relative + " must live under src/Services/" + owner + "/src/ or /tests/");
            }
        }

        Assert.True(violations.Count == 0, "Project naming or layout breaks the architecture document pattern (ADR-0003): " + string.Join("; ", violations));
    }

    [Fact]
    public void Solution_contains_every_project()
    {
        string solutionPath = Path.Combine(RepositoryRoot, "CoreBanking.sln");
        Assert.True(File.Exists(solutionPath), "CoreBanking.sln must exist at the repository root (ADR-0003).");

        HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(solutionPath))
        {
            Match match = SolutionEntryPattern.Match(line);
            if (match.Success)
            {
                declared.Add(match.Groups[1].Value.Replace('\\', '/'));
            }
        }

        List<string> missing = ProjectFiles()
            .Select(project => Path.GetRelativePath(RepositoryRoot, project).Replace('\\', '/'))
            .Where(relative => !declared.Contains(relative))
            .ToList();

        Assert.True(missing.Count == 0, "Every project belongs in CoreBanking.sln (ADR-0003): " + string.Join("; ", missing));
    }

    [Fact]
    public void Every_service_api_registers_observability_liveness_and_readiness()
    {
        List<string> violations = new();
        string servicesRoot = Path.Combine(RepositoryRoot, "src", "Services");

        foreach (string project in ProjectFiles().Where(project =>
                     string.Equals(LayerOf(ProjectName(project)), "Api", StringComparison.Ordinal)
                     && project.StartsWith(servicesRoot, StringComparison.OrdinalIgnoreCase)))
        {
            string? directory = Path.GetDirectoryName(project);
            string program = Path.Combine(directory ?? string.Empty, "Program.cs");
            if (!File.Exists(program))
            {
                violations.Add(ProjectName(project) + " has no Program.cs");
                continue;
            }

            string content = File.ReadAllText(program);
            if (!content.Contains("AddCoreBankingObservability", StringComparison.Ordinal))
            {
                violations.Add(ProjectName(project) + " does not register shared observability");
            }

            if (!content.Contains("/health/live", StringComparison.Ordinal))
            {
                violations.Add(ProjectName(project) + " has no liveness route");
            }

            if (!content.Contains("/health/ready", StringComparison.Ordinal))
            {
                violations.Add(ProjectName(project) + " has no readiness route");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every service API must expose the Phase 0 observability and health surface (ADR-0010): "
            + string.Join("; ", violations));
    }

    private static void AssertNoReferenceToLayers(string[] sourceLayers, string[] forbiddenLayers)
    {
        List<string> violations = new();

        foreach (string project in ProjectFiles().Where(project => sourceLayers.Contains(LayerOf(ProjectName(project)), StringComparer.Ordinal)))
        {
            foreach (string reference in ReferencedProjects(project))
            {
                if (forbiddenLayers.Contains(LayerOf(ProjectName(reference)), StringComparer.Ordinal))
                {
                    violations.Add(ProjectName(project) + " references " + ProjectName(reference));
                }
            }
        }

        Assert.True(violations.Count == 0, "Project references break the approved dependency direction (ADR-0002): " + string.Join("; ", violations));
    }

    private static string? PropertyValue(XDocument document, string name) =>
        document.Descendants(name).Select(element => element.Value).FirstOrDefault();

    private static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);

    private static string LayerOf(string projectName)
    {
        int index = projectName.LastIndexOf('.');
        return index < 0 ? string.Empty : projectName.Substring(index + 1);
    }

    private static string OwnerOf(string projectName)
    {
        const string prefix = "CoreBanking.";
        string withoutPrefix = projectName.StartsWith(prefix, StringComparison.Ordinal)
            ? projectName.Substring(prefix.Length)
            : projectName;
        int index = withoutPrefix.LastIndexOf('.');

        // Root-level names such as ArchitectureTests carry no owner segment.
        return index < 0 ? withoutPrefix : withoutPrefix.Substring(0, index);
    }

    private static List<string> ReferencedProjects(string projectFile)
    {
        string directory = Path.GetDirectoryName(projectFile) ?? RepositoryRoot;
        List<string> references = new();

        foreach (XElement element in XDocument.Load(projectFile).Descendants("ProjectReference"))
        {
            string include = element.Attribute("Include")?.Value ?? string.Empty;
            if (include.Length > 0)
            {
                references.Add(Path.GetFullPath(Path.Combine(directory, include)));
            }
        }

        return references;
    }

    private static List<string> PackageReferences(string projectFile)
    {
        List<string> packages = new();

        foreach (XElement element in XDocument.Load(projectFile).Descendants("PackageReference"))
        {
            string include = element.Attribute("Include")?.Value ?? string.Empty;
            if (include.Length > 0)
            {
                packages.Add(include);
            }
        }

        return packages;
    }

    private static List<string> ProjectFiles()
    {
        List<string> files = new();

        foreach (string area in new[] { "src", "tests" })
        {
            string root = Path.Combine(RepositoryRoot, area);
            if (!Directory.Exists(root))
            {
                continue;
            }

            files.AddRange(Directory
                .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Where(IsSourceFile)
                .OrderBy(path => path, StringComparer.Ordinal));
        }

        return files;
    }

    private static bool IsSourceFile(string path) =>
        !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>
    /// ADR-0007 allows publication only through the outbox relay, so nothing in a request path can
    /// publish an event that was not committed first, and only the adapter knows the broker.
    /// </summary>
    [Fact]
    public void Only_the_outbox_relay_publishes_events_and_only_the_adapter_knows_the_broker()
    {
        string messaging = "CoreBanking.BuildingBlocks.Infrastructure" + Path.DirectorySeparatorChar + "Messaging";

        string[] publisherAllowed =
        [
            Path.Combine(messaging, "IIntegrationStore.cs"),
            Path.Combine(messaging, "OutboxRelay.cs"),
            Path.Combine(messaging, "Kafka", "KafkaEventPublisher.cs"),
        ];

        List<string> publishViolations = new();
        List<string> brokerViolations = new();

        foreach (string file in SourceFiles(Path.Combine(RepositoryRoot, "src")))
        {
            string relative = Path.GetRelativePath(RepositoryRoot, file);

            // Test doubles legitimately implement the port; the rule is about production code.
            if (relative.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string content = File.ReadAllText(file);

            if (content.Contains("IEventPublisher", StringComparison.Ordinal)
                && !publisherAllowed.Any(allowed => relative.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                publishViolations.Add(relative);
            }

            if (content.Contains("using Confluent.Kafka", StringComparison.Ordinal)
                && !relative.Contains(
                    Path.Combine("Messaging", "Kafka") + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                brokerViolations.Add(relative);
            }
        }

        Assert.True(
            publishViolations.Count == 0,
            "Only the outbox relay may publish events (ADR-0007): " + string.Join("; ", publishViolations));

        Assert.True(
            brokerViolations.Count == 0,
            "Only the Kafka adapter may reference the broker client (ADR-0007): " + string.Join("; ", brokerViolations));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(IsSourceFile).OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CoreBanking.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("CoreBanking.sln was not found above the test output directory.");
    }
}
