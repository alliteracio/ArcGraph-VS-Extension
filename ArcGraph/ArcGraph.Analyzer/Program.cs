using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

namespace ArcGraph.Analyzer
{
    internal static class Program
    {
        // Usage: ArcGraph.Analyzer "C:\path\to\solution.sln" [out.dgml]
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                Console.WriteLine($"Current process: {proc.ProcessName} (PID {proc.Id})");

                // Warn if running inside Visual Studio process
                if (string.Equals(proc.ProcessName, "devenv", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Warning: running inside Visual Studio (devenv). Prefer running this analyzer out-of-process.");
                }

                // Register MSBuild (must be done before any Roslyn MSBuild usage)
                if (!MSBuildLocator.IsRegistered)
                {
                    var ok = TryRegisterMsBuild();
                    if (!ok)
                    {
                        Console.Error.WriteLine("WARNING: Could not register an MSBuild instance with a usable .NET SDK resolver.");
                        Console.Error.WriteLine("You may still see Workspace errors. Install/modify Visual Studio workloads or install .NET SDK and restart.");
                    }
                }
                else
                {
                    Console.WriteLine("MSBuildLocator already registered.");
                }

                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: ArcGraph.Analyzer <solution.sln> [output.dgml]");
                    return 0;
                }

                var solutionPath = args[0];
                var outPath = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "out.dgml");

                try
                {
                    var builder = new GraphBuilder();
                    var graph = await builder.BuildFromSolutionAsync(solutionPath).ConfigureAwait(false);

                    var exporter = new DgmlExporter();
                    var dgml = exporter.ExportToDgml(graph);
                    File.WriteAllText(outPath, dgml);
                    Console.WriteLine($"Wrote DGML to: {outPath}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Analysis error: " + ex);
                    return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal error during startup: " + ex);
                return 4;
            }
        }

        // Try register MSBuild via QueryVisualStudioInstances, vswhere fallback, and dotnet SDK directories.
        private static bool TryRegisterMsBuild()
        {
            try
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
                Console.WriteLine($"MSBuildLocator.QueryVisualStudioInstances() returned {instances.Length} instance(s).");
                if (instances.Length > 0)
                {
                    var best = instances.OrderByDescending(i => i.Version).First();
                    Console.WriteLine($"Registering MSBuild from instance: {best.Name} ({best.Version})");
                    MSBuildLocator.RegisterInstance(best);
                    if (HasSdkResolver(best.MSBuildPath))
                    {
                        Console.WriteLine("SDK resolver found under Visual Studio MSBuild path.");
                        return true;
                    }
                    Console.WriteLine("SDK resolver NOT found under Visual Studio MSBuild path.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("QueryVisualStudioInstances attempt threw: " + ex.Message);
            }

            // vswhere fallback
            var vswhere = FindVswhere();
            if (vswhere != null)
            {
                Console.WriteLine($"vswhere found at: {vswhere}");
                try
                {
                    var instPath = RunVswhereForInstallationPath(vswhere);
                    if (!string.IsNullOrEmpty(instPath))
                    {
                        var msbuildPath = Path.Combine(instPath, "MSBuild", "Current", "Bin");
                        Console.WriteLine($"Trying RegisterMSBuildPath with Visual Studio path: {msbuildPath}");
                        if (Directory.Exists(msbuildPath))
                        {
                            try
                            {
                                MSBuildLocator.RegisterMSBuildPath(msbuildPath);
                                Console.WriteLine("Registered MSBuildPath from Visual Studio installation.");
                                if (HasSdkResolver(msbuildPath))
                                {
                                    Console.WriteLine("Found SDK resolver under that MSBuild path.");
                                    return true;
                                }
                                Console.WriteLine("SDK resolver NOT found under that MSBuild path.");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine("RegisterMSBuildPath(VisualStudio) failed: " + ex.Message);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Computed msbuildPath does not exist: " + msbuildPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("vswhere fallback failed: " + ex.Message);
                }
            }
            else
            {
                Console.WriteLine("vswhere.exe not found on PATH or in common locations.");
            }

            // dotnet SDK fallback - try SDK directories under Program Files\dotnet\sdk
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ??
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            var sdkRoot = Path.Combine(dotnetRoot, "sdk");
            Console.WriteLine($"Looking for dotnet SDKs under: {sdkRoot}");
            if (Directory.Exists(sdkRoot))
            {
                var sdks = Directory.GetDirectories(sdkRoot)
                                    .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
                if (sdks.Length > 0)
                {
                    Console.WriteLine("Found dotnet SDK directories (top 5):");
                    foreach (var s in sdks.Take(5)) Console.WriteLine("  " + s);

                    foreach (var sdkDir in sdks)
                    {
                        try
                        {
                            Console.WriteLine("Trying RegisterMSBuildPath with dotnet SDK directory: " + sdkDir);
                            MSBuildLocator.RegisterMSBuildPath(sdkDir);
                            if (HasSdkResolver(sdkDir))
                            {
                                Console.WriteLine("SDK resolver found under the registered dotnet SDK path.");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine("SDK resolver NOT found under this dotnet SDK path.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("RegisterMSBuildPath(dotnet sdkDir) threw: " + ex.Message);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("dotnet sdk root does not exist: " + sdkRoot);
            }

            // final attempt - if MSBuildLocator is registered, try best-effort detection
            return MSBuildLocator.IsRegistered && HasSdkResolverFromRegistered();
        }

        private static bool HasSdkResolver(string root)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
                var found = Directory.EnumerateFiles(root, "Microsoft.DotNet.MSBuildSdkResolver.dll", SearchOption.AllDirectories)
                                     .FirstOrDefault();
                if (!string.IsNullOrEmpty(found))
                {
                    Console.WriteLine("Found SDK resolver at: " + found);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasSdkResolverFromRegistered()
        {
            try
            {
                var possible = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2022", "Community", "MSBuild", "Current", "Bin")
                };
                foreach (var p in possible)
                {
                    if (HasSdkResolver(p)) return true;
                }
            }
            catch { }
            return false;
        }

        private static string? FindVswhere()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in path.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(p.Trim(), "vswhere.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var common = Path.Combine(pf86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (File.Exists(common)) return common;
            return null;
        }

        private static string? RunVswhereForInstallationPath(string vswherePath)
        {
            var psi = new ProcessStartInfo(vswherePath, "-latest -products * -requires Microsoft.Component.MSBuild -format value -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var outp = proc!.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return string.IsNullOrEmpty(outp) ? null : outp;
        }
    }
}