//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using System.Text.Json;

namespace ArcAnalyzer.Infrastructure.Vulnerabilities;

/// <summary>
/// Builds a mapping from assembly simple name -> (packageId, packageVersion)
/// by reading project.assets.json files in project obj folders.
/// </summary>
public static class NuGetAssemblyMapper
{
    /// <summary>
    /// Builds a mapping for all projects in the solution.
    /// Key: assembly simple name without .dll extension (e.g. "Newtonsoft.Json")
    /// Value: (packageId, packageVersion)
    /// </summary>
    public static Dictionary<string, (string PackageId, string PackageVersion)> BuildMappingForSolution(Microsoft.CodeAnalysis.Solution solution)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            try
            {
                if (string.IsNullOrEmpty(project.FilePath))
                    continue;

                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (string.IsNullOrEmpty(projectDir))
                    continue;

                var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
                if (!File.Exists(assetsPath))
                    continue;

                using var fs = File.OpenRead(assetsPath);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;

                if (root.TryGetProperty("targets", out var targetsElem) && targetsElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var frameworkProp in targetsElem.EnumerateObject())
                    {
                        var frameworkObj = frameworkProp.Value;
                        if (frameworkObj.ValueKind != JsonValueKind.Object) continue;

                        foreach (var packageProp in frameworkObj.EnumerateObject())
                        {
                            var packageKey = packageProp.Name;
                            var slashIdx = packageKey.IndexOf('/');
                            if (slashIdx <= 0) continue;

                            var pkgId = packageKey.Substring(0, slashIdx);
                            var pkgVer = packageKey.Substring(slashIdx + 1);

                            var packageObj = packageProp.Value;
                            if (packageObj.ValueKind != JsonValueKind.Object) continue;

                            foreach (var sectionName in new[] { "compile", "runtime", "native", "contentFiles" })
                            {
                                if (!packageObj.TryGetProperty(sectionName, out var sectionElem)) continue;
                                if (sectionElem.ValueKind != JsonValueKind.Object) continue;

                                foreach (var fileProp in sectionElem.EnumerateObject())
                                {
                                    var relativePath = fileProp.Name;
                                    var asmFile = Path.GetFileName(relativePath);
                                    if (string.IsNullOrEmpty(asmFile)) continue;
                                    if (!asmFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                                    var asmName = Path.GetFileNameWithoutExtension(asmFile);

                                    if (!map.ContainsKey(asmName))
                                    {
                                        map[asmName] = (pkgId, pkgVer);
                                    }
                                }
                            }
                        }
                    }
                }

                if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var libProp in libs.EnumerateObject())
                    {
                        var libKey = libProp.Name;
                        var slashIdx = libKey.IndexOf('/');
                        if (slashIdx <= 0) continue;

                        var pkgId = libKey.Substring(0, slashIdx);
                        var pkgVer = libKey.Substring(slashIdx + 1);

                        var libObj = libProp.Value;
                        if (libObj.ValueKind != JsonValueKind.Object) continue;

                        if (libObj.TryGetProperty("path", out var pathElem) && pathElem.ValueKind == JsonValueKind.String){}

                        if (libObj.TryGetProperty("files", out var filesElem) && filesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var fileElem in filesElem.EnumerateArray())
                            {
                                if (fileElem.ValueKind != JsonValueKind.String) continue;
                                var fileName = fileElem.GetString();
                                if (string.IsNullOrEmpty(fileName)) continue;
                                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                                var asmName = Path.GetFileNameWithoutExtension(fileName);
                                if (!map.ContainsKey(asmName))
                                {
                                    map[asmName] = (pkgId, pkgVer);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return map;
    }
}