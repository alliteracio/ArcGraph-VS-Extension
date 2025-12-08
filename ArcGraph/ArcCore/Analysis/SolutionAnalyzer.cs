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
         
            if (typeDecl.BaseList != null)
            {
                foreach (var baseTypeSyntax in typeDecl.BaseList.Types)
                {
                    var baseTypeInfo = semanticModel.GetTypeInfo(baseTypeSyntax.Type);
                    var baseType = baseTypeInfo.Type as INamedTypeSymbol;
                    if (baseType != null)
                    {
                        var baseId = baseType.ToDisplayString();
                        if (baseId != nodeId)
                            AddEdge(graph, nodeId, baseId, DependencyKind.Inheritance);
                    }
                }
            }

            var fieldDecls = typeDecl.DescendantNodes()
                .OfType<FieldDeclarationSyntax>();

            foreach (var fieldDecl in fieldDecls)
            {
                var fieldTypeSyntax = fieldDecl.Declaration.Type;
                var fieldType = semanticModel.GetTypeInfo(fieldTypeSyntax).Type as INamedTypeSymbol;
                if (fieldType != null)
                {
                    var targetId = fieldType.ToDisplayString();
                    if (targetId != nodeId)
                        AddEdge(graph, nodeId, targetId, DependencyKind.Field);
                }
            }

            var propDecls = typeDecl.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            foreach (var propDecl in propDecls)
            {
                var propTypeSyntax = propDecl.Type;
                var propType = semanticModel.GetTypeInfo(propTypeSyntax).Type as INamedTypeSymbol;
                if (propType != null)
                {
                    var targetId = propType.ToDisplayString();
                    if (targetId != nodeId)
                        AddEdge(graph, nodeId, targetId, DependencyKind.Property);
                }
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
                        var targetId = returnType.ToDisplayString();
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ReturnType);
                    }
                }

                foreach (var param in methodDecl.ParameterList.Parameters)
                {
                    if (param.Type != null)
                    {
                        var paramType = semanticModel.GetTypeInfo(param.Type).Type as INamedTypeSymbol;
                        if (paramType != null)
                        {
                            var targetId = paramType.ToDisplayString();
                            if (targetId != nodeId)
                                AddEdge(graph, nodeId, targetId, DependencyKind.ParameterType);
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

                    var targetId = targetType.ToDisplayString();
                    if (targetId == nodeId)
                        continue;

                    AddEdge(graph, nodeId, targetId, DependencyKind.MethodCall);
                }

                var creations = methodDecl.DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>();

                foreach (var creation in creations)
                {
                    var typeInfo = semanticModel.GetTypeInfo(creation);
                    var createdType = typeInfo.Type as INamedTypeSymbol;
                    if (createdType != null)
                    {
                        var targetId = createdType.ToDisplayString();
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ObjectCreation);
                    }
                }
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
                        var targetId = targetType.ToDisplayString();
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.MethodCall);
                    }
                }

                var creations = ctor.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
                foreach (var creation in creations)
                {
                    var typeInfo = semanticModel.GetTypeInfo(creation);
                    var createdType = typeInfo.Type as INamedTypeSymbol;
                    if (createdType != null)
                    {
                        var targetId = createdType.ToDisplayString();
                        if (targetId != nodeId)
                            AddEdge(graph, nodeId, targetId, DependencyKind.ObjectCreation);
                    }
                }
            }
        }
    }

    private static void AddEdge(DependencyGraph graph, string fromId, string toId, DependencyKind kind)
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