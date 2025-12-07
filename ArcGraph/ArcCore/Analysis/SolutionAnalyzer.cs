//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.GraphModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArcCore.Analysis;

public class SolutionAnalyzer
{
    private readonly Solution _solution;

    public SolutionAnalyzer(Solution solution) => _solution = solution ?? throw new ArgumentNullException(nameof(solution));

    public async Task<DependencyGraph> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var graph = new DependencyGraph();

        foreach (var project in _solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            AnalyzeProject(compilation, graph, cancellationToken);
        }

        return graph;
    }

    private static void AnalyzeProject(Compilation compilation, DependencyGraph graph, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(tree);
            AnalyzeSyntaxTree(tree, semanticModel, graph);
        }
    }

    private static void AnalyzeSyntaxTree(SyntaxTree tree, SemanticModel semanticModel, DependencyGraph graph)
    {
        var root = tree.GetRoot();

        var typeDeclarations = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            if (typeSymbol is null)
                continue;

            var nodeId = typeSymbol.ToDisplayString();

            if (!graph.Nodes.TryGetValue(nodeId, out var node))
            {
                node = new GraphNode
                {
                    Id = nodeId,
                    Name = typeSymbol.Name,
                    Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty
                };

                graph.Nodes[nodeId] = node;
            }

            var methodDecls = typeDecl.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDecls)
            {
                var invocations = methodDecl.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    var targetMethod = symbolInfo.Symbol as IMethodSymbol;
                    if (targetMethod is null)
                        continue;

                    var targetType = targetMethod.ContainingType;
                    if (targetType is null)
                        continue;

                    var targetId = targetType.ToDisplayString();
                    if (targetId == nodeId)
                        continue;

                    AddEdge(graph, nodeId, targetId);
                }
            }
        }
    }

    private static void AddEdge(DependencyGraph graph, string fromId, string toId)
    {
        if (!graph.Nodes.ContainsKey(toId))
        {
            graph.Nodes[toId] = new GraphNode
            {
                Id = toId,
                Name = toId.Split('.').Last(),
                Namespace = string.Join('.', toId.Split('.').Reverse().Skip(1).Reverse())
            };
        }

        var edge = graph.Edges.FirstOrDefault(e => e.FromNodeId == fromId && e.ToNodeId == toId);
        if (edge is null)
        {
            edge = new GraphEdge
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                Weight = 0
            };
            graph.Edges.Add(edge);
        }

        edge.Weight++;
    }
}
