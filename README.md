# VS Dependency Graph Analyzer (Visual Studi Extension)

Overview
--------
VS Dependency Graph Analyzer is a Visual Studio extension that provides an interactive Tool Window for building, analyzing and visualizing dependency graphs of .NET solutions. It uses Roslyn to parse opened .sln and .cs files, extracts compile-time project references and NuGet package usage, runs configurable clustering strategies (ML-based and rule-based), highlights vulnerabilities and architectural issues, and can export analysis reports.

Key features
------------
- Parse .sln and related .cs files using Roslyn Workspace and Compilation APIs
- Build a dependency graph where nodes represent projects/compilation units and edges represent compile-time references
- Extract NuGet package dependencies (package id + version) and include them in the graph metadata
- Interactive graph visualization in a Tool Window (zoom, pan, tooltips, node selection)
- Two clustering methods:
  - ML.NET-based clustering (e.g., KMeans) using numeric features (reference counts, package counts, etc.)
  - Roslyn + config.json rule-based clustering (configurable feature extraction and grouping rules)
- Color nodes by cluster and scale nodes by weight (reference count) so important nodes appear larger
- Integrate with external vulnerability APIs (OSS Index) to list vulnerable packages and mark affected nodes
- Detect layer violations (configurable layer rules) and circular/cyclic references and highlight them visually
- Export analysis
