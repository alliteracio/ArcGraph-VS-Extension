//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.Visualisation
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

        public static void ComputeLayout(IList<Node> nodes, IList<Edge> edges, int width = 800, int height = 600, int iterations = 400)
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

            double minX = nodes.Min(nd => nd.X);
            double maxX = nodes.Max(nd => nd.X);
            double minY = nodes.Min(nd => nd.Y);
            double maxY = nodes.Max(nd => nd.Y);

            double spanX = Math.Max(1e-6, maxX - minX);
            double spanY = Math.Max(1e-6, maxY - minY);

            double margin = 40;
            double targetW = Math.Max(100, width - 2 * margin);
            double targetH = Math.Max(100, height - 2 * margin);

            for (int i = 0; i < n; i++)
            {
                nodes[i].X = margin + ((nodes[i].X - minX) / spanX) * targetW;
                nodes[i].Y = margin + ((nodes[i].Y - minY) / spanY) * targetH;
            }
        }
    }
}
