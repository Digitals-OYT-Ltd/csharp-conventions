using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// The analyzers a consumer gets from this package: the vendor DLLs from the packages this test
/// project restores at the versions in Directory.Packages.props, plus the DT rules built here.
/// Loaded by reflection and instantiated, because a descriptor ID only exists if some analyzer's
/// SupportedDiagnostics says so; the bytes of a DLL still mention IDs that were retired.
/// </summary>
internal static partial class ShippedAnalyzers
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<DiagnosticAnalyzer>>> LoadedVendors = new(LoadVendors);

    [GeneratedRegex(@"^roslyn(\d+)\.(\d+)$")]
    private static partial Regex RoslynFolder();

    /// <summary>Vendor analyzers keyed by package folder, as the test csproj hands them over.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DiagnosticAnalyzer>> Vendors() => LoadedVendors.Value;

    public static IReadOnlyList<DiagnosticAnalyzer> Custom() => Instantiate(typeof(TemporalTypeAnalyzer).Assembly).ToList();

    private static Dictionary<string, IReadOnlyList<DiagnosticAnalyzer>> LoadVendors()
    {
        var roots = typeof(ShippedAnalyzers).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "VendorAnalyzerPackages")
            .Value!
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        return roots.ToDictionary(
            root => Path.GetFileName(Path.GetDirectoryName(root)!),
            root => (IReadOnlyList<DiagnosticAnalyzer>)[.. AnalyzerAssemblies(root).SelectMany(dll => Instantiate(Load(dll)))]);
    }

    // Mirrors the compiler's folder choice. A package with analyzers/dotnet/roslynX.Y/ folders
    // contributes the newest X.Y not above the Roslyn in this process, which is the consumer floor
    // the tests pin; a package without them contributes every DLL under analyzers/. VB is skipped.
    private static IEnumerable<string> AnalyzerAssemblies(string packageRoot)
    {
        var analyzersDir = Path.Combine(packageRoot, "analyzers");
        var running = typeof(Compilation).Assembly.GetName().Version!;
        var byFolder = Directory.EnumerateFiles(analyzersDir, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !Path.GetRelativePath(analyzersDir, dll).Split(Path.DirectorySeparatorChar).Contains("vb"))
            .GroupBy(dll => Path.GetDirectoryName(dll)!)
            .ToList();

        var roslynFolders = byFolder
            .Select(folder => (Files: folder, Version: RoslynVersion(folder.Key)))
            .Where(folder => folder.Version is not null)
            .ToList();
        if (roslynFolders.Count == 0)
        {
            return byFolder.SelectMany(folder => folder);
        }

        return roslynFolders
            .Where(folder => folder.Version <= running)
            .OrderByDescending(folder => folder.Version)
            .First()
            .Files;
    }

    private static Version? RoslynVersion(string directory)
    {
        foreach (var segment in directory.Split(Path.DirectorySeparatorChar))
        {
            var match = RoslynFolder().Match(segment);
            if (match.Success)
            {
                return new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    // One load context per DLL. A vendor's own dependencies resolve from the same folder, and
    // Microsoft.CodeAnalysis resolves to the copy this process already runs on.
    private static Assembly Load(string dll)
    {
        var directory = Path.GetDirectoryName(dll)!;
        var context = new AssemblyLoadContext(dll, isCollectible: false);
        context.Resolving += (loader, name) =>
        {
            var sibling = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(sibling) ? loader.LoadFromAssemblyPath(sibling) : null;
        };

        return context.LoadFromAssemblyPath(dll);
    }

    private static IEnumerable<DiagnosticAnalyzer> Instantiate(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            // A type whose base lives in an assembly this process does not carry (a code fix on
            // Workspaces, say) is null here; it cannot be an analyzer we could instantiate anyway.
            types = partial.Types;
        }

        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }

            // Roslynator's spelling analyzer is built by a service and has no parameterless constructor.
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            yield return (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
        }
    }
}
