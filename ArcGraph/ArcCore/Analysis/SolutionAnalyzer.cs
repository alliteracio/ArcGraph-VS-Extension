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

    public async Task<DependencyGraph> AnalyzeAsync(IProgress<AnalysisProgress>? progress = null, IDictionary<string, (string PackageId, string PackageVersion)>? assemblyPackageMap = null, CancellationToken cancellationToken = default)
    {
        var graph = new DependencyGraph();

        var projects = _solution.Projects.ToList();
        var totalProjects = projects.Count;
        var processed = 0;

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                processed++;
                progress?.Report(new AnalysisProgress
                {
                    ProjectsProcessed = processed,
                    TotalProjects = totalProjects,
                    CurrentProject = project.Name,
                    NodesFound = graph.Nodes.Count,
                    EdgesFound = graph.Edges.Count
                });
                continue;
            }

            AnalyzeProject(compilation, graph, cancellationToken, assemblyPackageMap);

            processed++;
            progress?.Report(new AnalysisProgress
            {
                ProjectsProcessed = processed,
                TotalProjects = totalProjects,
                CurrentProject = project.Name,
                NodesFound = graph.Nodes.Count,
                EdgesFound = graph.Edges.Count
            });
        }

        return graph;
    }

    private static void AnalyzeProject(Compilation compilation, DependencyGraph graph, CancellationToken cancellationToken, IDictionary<string, (string PackageId, string PackageVersion)>? assemblyPackageMap)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(tree);
            AnalyzeSyntaxTree(tree, semanticModel, graph, assemblyPackageMap);
        }
    }

    private static string GetSymbolId(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static void AnalyzeSyntaxTree(SyntaxTree tree, SemanticModel semanticModel, DependencyGraph graph, IDictionary<string, (string PackageId, string PackageVersion)>? assemblyPackageMap)
    {
        var root = tree.GetRoot();

        var typeDeclarations = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            if (typeSymbol is null)
                continue;

            var nodeId = GetSymbolId(typeSymbol);

            if (!graph.Nodes.TryGetValue(nodeId, out var node))
            {
                node = new GraphNode
                {
                    Id = nodeId,
                    Name = typeSymbol.Name,
                    Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty
                };

                node.AssemblyName = typeSymbol.ContainingAssembly?.Name ?? string.Empty;
                node.IsExternal = !SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, semanticModel.Compilation.Assembly);
                node.TypeKind = typeSymbol.TypeKind.ToString();
                node.Accessibility = typeSymbol.DeclaredAccessibility.ToString();
                node.GenericArity = typeSymbol.Arity;

                try
                {
                    if (!string.IsNullOrEmpty(node.AssemblyName) && assemblyPackageMap != null && assemblyPackageMap.TryGetValue(node.AssemblyName, out var pkg))
                    {
                        node.PackageId = pkg.PackageId;
                        node.PackageVersion = pkg.PackageVersion;
                    }
                }
                catch
                {
                }

                try
                {
                    node.ImplementedInterfaces = typeSymbol.Interfaces
                        .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToList();
                }
                catch
                {
                }

                try
                {
                    var bases = new List<string>();
                    var bt = typeSymbol.BaseType;
                    while (bt != null)
                    {
                        bases.Add(bt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        bt = bt.BaseType;
                    }
                    node.BaseTypes = bases;
                }
                catch
                {
                }

                try
                {
                    node.Attributes = typeSymbol.GetAttributes()
                        .Select(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                catch
                {
                }

                try
                {
                    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(tree.FilePath))
                        files.Add(tree.FilePath);

                    foreach (var loc in typeSymbol.Locations)
                    {
                        if (loc.IsInSource && !string.IsNullOrEmpty(loc.SourceTree?.FilePath))
                            files.Add(loc.SourceTree!.FilePath);
                    }

                    node.SourceFilePaths = files.ToList();
                }
                catch
                {
                }

                graph.Nodes[nodeId] = node;
            }

            var fieldDecls = typeDecl.DescendantNodes()
                .OfType<FieldDeclarationSyntax>();

            foreach (var fieldDecl in fieldDecls)
            {
                var fieldTypeSyntax = fieldDecl.Declaration.Type;
                var fieldType = semanticModel.GetTypeInfo(fieldTypeSyntax).Type as INamedTypeSymbol;
                if (fieldType != null)
                {
                    var targetId = fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (targetId != nodeId)
                        AddEdge(graph, nodeId, targetId, DependencyKind.Field, semanticModel.Compilation, assemblyPackageMap);
                }
                node.FieldCount++;
            }

            var propDecls = typeDecl.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            foreach (var propDecl in propDecls)
            {
                var propTypeSyntax = propDecl.Type;
                var propType = semanticModel.GetTypeInfo(propTypeSyntax).Type as INamedTypeSymbol;
                if (propType != null)
                {
                    var targetId = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (targetId != nodeId)
                        AddEdge(graph, nodeId, targetId, DependencyKind.Property, semanticModel.Compilation, assemblyPackageMap);
                }
                node.PropertyCount++;
            }

            var methodDecls = typeDecl.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDecls)
            {
                if (methodDecl.ReturnType != null)
                {
                    var returnType = semanticModel.GetTypeInfo(methodDecl.ReturnType).Type as INamedTypeSymbol;
                    if (returnType != null)
                    {
                        var targetId = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ReturnType, semanticModel.Compilation, assemblyPackageMap);
                    }
                }

                foreach (var param in methodDecl.ParameterList.Parameters)
                {
                    if (param.Type != null)
                    {
                        var paramType = semanticModel.GetTypeInfo(param.Type).Type as INamedTypeSymbol;
                        if (paramType != null)
                        {
                            var targetId = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (targetId != nodeId)
                                AddEdge(graph, nodeId, targetId, DependencyKind.ParameterType, semanticModel.Compilation, assemblyPackageMap);
                        }
                    }
                }

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

                    var targetId = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (targetId == nodeId)
                        continue;

                    AddEdge(graph, nodeId, targetId, DependencyKind.MethodCall, semanticModel.Compilation, assemblyPackageMap);
                }

                var creations = methodDecl.DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>();

                foreach (var creation in creations)
                {
                    var typeInfo = semanticModel.GetTypeInfo(creation);
                    var createdType = typeInfo.Type as INamedTypeSymbol;
                    if (createdType != null)
                    {
                        var targetId = createdType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ObjectCreation, semanticModel.Compilation, assemblyPackageMap);
                    }
                }

                node.MethodCount++;
            }

            var ctorDecls = typeDecl.DescendantNodes()
                .OfType<ConstructorDeclarationSyntax>();

            foreach (var ctor in ctorDecls)
            {
                if (ctor.Initializer != null)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(ctor.Initializer);
                    var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
                    var targetType = methodSymbol?.ContainingType;
                    if (targetType != null)
                    {
                        var targetId = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.MethodCall, semanticModel.Compilation, assemblyPackageMap);
                    }
                }

                var creations = ctor.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
                foreach (var creation in creations)
                {
                    var typeInfo = semanticModel.GetTypeInfo(creation);
                    var createdType = typeInfo.Type as INamedTypeSymbol;
                    if (createdType != null)
                    {
                        var targetId = createdType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ObjectCreation, semanticModel.Compilation, assemblyPackageMap);
                    }
                }
            }
        }
    }

    private static void AddEdge(DependencyGraph graph, string fromId, string toId, DependencyKind kind, Compilation? compilation = null, IDictionary<string, (string PackageId, string PackageVersion)>? assemblyPackageMap = null)
    {
        if (!graph.Nodes.ContainsKey(toId))
        {
            var newNode = new GraphNode
            {
                Id = toId,
                Name = toId.Split('.').Last(),
                Namespace = string.Join('.', toId.Split('.').Reverse().Skip(1).Reverse())
            };

            if (compilation != null)
            {
                try
                {
                    var symbol = compilation.GetTypeByMetadataName(toId.Replace("global::", string.Empty));
                    if (symbol != null)
                    {
                        newNode.AssemblyName = symbol.ContainingAssembly?.Name ?? string.Empty;
                        newNode.IsExternal = !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);
                        newNode.TypeKind = symbol.TypeKind.ToString();
                        newNode.Accessibility = symbol.DeclaredAccessibility.ToString();
                        newNode.GenericArity = symbol.Arity;

                        if (!string.IsNullOrEmpty(newNode.AssemblyName) && assemblyPackageMap != null && assemblyPackageMap.TryGetValue(newNode.AssemblyName, out var pkg))
                        {
                            newNode.PackageId = pkg.PackageId;
                            newNode.PackageVersion = pkg.PackageVersion;
                        }
                    }
                }
                catch
                {
                }
            }

            graph.Nodes[toId] = newNode;
        }

        var edge = graph.Edges.FirstOrDefault(e => e.SourceId == fromId && e.TargetId == toId && e.Kind == kind);
        if (edge is null)
        {
            edge = new GraphEdge
            {
                SourceId = fromId,
                TargetId = toId,
                Weight = 0,
                Kind = kind
            };
            graph.Edges.Add(edge);
        }

        edge.Weight++;
    }
}