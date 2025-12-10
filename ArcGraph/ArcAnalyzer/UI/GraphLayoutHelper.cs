//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.Layered;

namespace ArcAnalyzer.UI
{
    public static class GraphLayoutHelper
    {
        public class Node
        {
            public string Id { get; set; } = default!;
            public double X { get; set; }
            public double Y { get; set; }
        }

        public class Edge
        {
            public string Source { get; set; } = default!;
            public string Target { get; set; } = default!;
        }

        public static void ComputeLayoutObsolate(IList<Node> nodes, IList<Edge> edges, int width = 800, int height = 600, int iterations = 400, double spacingFactor = 1.25)
        {
            if (nodes == null || nodes.Count == 0) return;

            var n = nodes.Count;
            var rand = new Random(0);

            double radius = Math.Min(width, height) * 0.35;
            for (int i = 0; i < n; i++)
            {
                double angle = 2.0 * Math.PI * i / n;
                nodes[i].X = radius * Math.Cos(angle) + (rand.NextDouble() - 0.5) * 10;
                nodes[i].Y = radius * Math.Sin(angle) + (rand.NextDouble() - 0.5) * 10;
            }

            var k = Math.Sqrt((width * height) / (double)n);
            double t = Math.Max(width, height) / 10.0;
            double cooling = t / (iterations + 1.0);

            var adjacency = new HashSet<(string, string)>();
            foreach (var e in edges)
            {
                adjacency.Add((e.Source, e.Target));
                adjacency.Add((e.Target, e.Source));
            }

            var nodeIndex = nodes.Select((nd, idx) => (nd.Id, idx)).ToDictionary(x => x.Id, x => x.idx);

            for (int iter = 0; iter < iterations; iter++)
            {
                var dispX = new double[n];
                var dispY = new double[n];

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        double dx = nodes[i].X - nodes[j].X;
                        double dy = nodes[i].Y - nodes[j].Y;
                        double dist2 = dx * dx + dy * dy;
                        double dist = Math.Sqrt(Math.Max(dist2, 1e-6));
                        double force = (k * k) / dist;
                        double ux = dx / dist;
                        double uy = dy / dist;
                        dispX[i] += ux * force;
                        dispY[i] += uy * force;
                        dispX[j] -= ux * force;
                        dispY[j] -= uy * force;
                    }
                }

                foreach (var e in edges)
                {
                    if (!nodeIndex.TryGetValue(e.Source, out var si)) continue;
                    if (!nodeIndex.TryGetValue(e.Target, out var ti)) continue;

                    double dx = nodes[si].X - nodes[ti].X;
                    double dy = nodes[si].Y - nodes[ti].Y;
                    double dist2 = dx * dx + dy * dy;
                    double dist = Math.Sqrt(Math.Max(dist2, 1e-6));
                    double force = (dist * dist) / k;
                    double ux = dx / dist;
                    double uy = dy / dist;
                    dispX[si] -= ux * force;
                    dispY[si] -= uy * force;
                    dispX[ti] += ux * force;
                    dispY[ti] += uy * force;
                }

                for (int i = 0; i < n; i++)
                {
                    double dx = dispX[i];
                    double dy = dispY[i];
                    double disp = Math.Sqrt(dx * dx + dy * dy);
                    if (disp > 1e-6)
                    {
                        double limited = Math.Min(disp, t);
                        nodes[i].X += (dx / disp) * limited;
                        nodes[i].Y += (dy / disp) * limited;
                    }

                    nodes[i].X += (rand.NextDouble() - 0.5) * 0.01;
                    nodes[i].Y += (rand.NextDouble() - 0.5) * 0.01;
                }

                t -= cooling;
                if (t <= 0) break;
            }

            
            NormalizePositions(nodes, width, height, spacingFactor);
        }

        public static void ComputeLayout(IList<Node> nodes, IList<Edge> edges, int width = 800, int height = 600, int iterations = 400, double spacingFactor = 1.25)
        {
            if (nodes == null || nodes.Count == 0) return;

            try
            {
                ComputeLayoutWithMsagl(nodes, edges, width, height, spacingFactor);
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[GraphLayoutHelper] MSAGL layout failed, falling back to FR. Error: " + ex);
            }

            // fallback
            ComputeLayoutObsolate(nodes, edges, width, height, iterations, spacingFactor);
        }

        private static void ComputeLayoutWithMsagl(IList<Node> nodes, IList<Edge> edges, int width, int height, double spacingFactor)
        {
            var g = new Microsoft.Msagl.Drawing.Graph();

            foreach (var n in nodes)
            {
                var dn = g.AddNode(n.Id);
                dn.Attr.Shape = Shape.Circle;
                dn.Attr.Id = n.Id;
                dn.Attr.Padding = 0;
                dn.Attr.XRadius = 10;
                dn.Attr.YRadius = 10;
            }

            foreach (var e in edges)
            {
                try
                {
                    g.AddEdge(e.Source, e.Target);
                }
                catch
                {
                }
            }

            var geometryGraph = g.GeometryGraph;
            if (geometryGraph == null)
                throw new InvalidOperationException("MSAGL geometry graph not available.");

            var settings = new SugiyamaLayoutSettings
            {
            };

            var sug = new LayeredLayout(geometryGraph, settings);
            sug.Run();

            foreach (var dn in g.Nodes)
            {
                var id = dn.Id;
                var geom = dn.GeometryNode;
                if (geom == null) continue;

                var center = geom.Center;
                var node = nodes.FirstOrDefault(x => x.Id == id);
                if (node != null)
                {
                    node.X = center.X;
                    node.Y = center.Y;
                }
            }

            NormalizePositions(nodes, width, height, spacingFactor);
        }

        private static void NormalizePositions(IList<Node> nodes, int width, int height, double spacingFactor)
        {
            if (nodes == null || nodes.Count == 0) return;

            double minX = nodes.Min(nd => nd.X);
            double maxX = nodes.Max(nd => nd.X);
            double minY = nodes.Min(nd => nd.Y);
            double maxY = nodes.Max(nd => nd.Y);

            double spanX = Math.Max(1e-6, maxX - minX);
            double spanY = Math.Max(1e-6, maxY - minY);

            double margin = 40;
            double targetW = Math.Max(100, width - 2 * margin);
            double targetH = Math.Max(100, height - 2 * margin);

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].X = margin + ((nodes[i].X - minX) / spanX) * targetW;
                nodes[i].Y = margin + ((nodes[i].Y - minY) / spanY) * targetH;
            }
           
            if (spacingFactor <= 1.0) return;

            double centerX = nodes.Average(n => n.X);
            double centerY = nodes.Average(n => n.Y);

            double maxAllowedX = width - margin;
            double minAllowedX = margin;
            double maxAllowedY = height - margin;
            double minAllowedY = margin;

            for (int i = 0; i < nodes.Count; i++)
            {
                var dx = nodes[i].X - centerX;
                var dy = nodes[i].Y - centerY;
                nodes[i].X = centerX + dx * spacingFactor;
                nodes[i].Y = centerY + dy * spacingFactor;

                nodes[i].X = Math.Max(minAllowedX, Math.Min(maxAllowedX, nodes[i].X));
                nodes[i].Y = Math.Max(minAllowedY, Math.Min(maxAllowedY, nodes[i].Y));
            }
        }
    }
}