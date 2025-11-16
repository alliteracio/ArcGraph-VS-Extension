using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArcGraph.Analyzer
{
    // Minimal dependency graph model for MVP
    public class DependencyGraph
    {
        public HashSet<string> Nodes { get; } = new();
        public List<(string From, string To, int Weight)> Edges { get; } = new();

        public void AddNode(string id) => Nodes.Add(id);
        public void AddEdge(string from, string to, int weight = 1) => Edges.Add((from, to, weight));
    }

    public class GraphBuilder
    {
        public GraphBuilder()
        {
        }

        // MVP: collect project-level dependencies (ProjectReference edges).
        // Later: add namespace/class-level edges and call-count weights by analyzing invocation sites.
        public async Task<DependencyGraph> BuildFromSolutionAsync(string solutionPath, CancellationToken ct = default)
        {
            var graph = new DependencyGraph();

            using var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (s, e) =>
            {
                // For now, just log to console; in future expose diagnostics to caller.
                Console.Error.WriteLine($"Workspace: {e.Diagnostic?.Message}");
            };

            var solution = await workspace.OpenSolutionAsync(solutionPath, null, ct).ConfigureAwait(false);

            // Map project id -> display name (use AssemblyName if available)
            var projectNames = solution.Projects.ToDictionary(
                p => p.Id,
                p => string.IsNullOrEmpty(p.AssemblyName) ? p.Name : p.AssemblyName);

            // Add nodes
            foreach (var kv in projectNames)
            {
                graph.AddNode(kv.Value);
            }

            // Add edges for project references
            foreach (var project in solution.Projects)
            {
                var fromName = projectNames[project.Id];
                foreach (var pref in project.ProjectReferences)
                {
                    if (projectNames.TryGetValue(pref.ProjectId, out var toName))
                    {
                        graph.AddEdge(fromName, toName, 1);
                    }
                }
            }

            // Simple heuristic: if a project references a package/assembly that corresponds to another project name,
            // or further analysis needed - that will be next step.

            return graph;
        }
    }
}