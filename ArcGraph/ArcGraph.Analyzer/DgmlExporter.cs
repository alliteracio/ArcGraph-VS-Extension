using System.Text;

namespace ArcGraph.Analyzer
{
    public class DgmlExporter
    {
        public string ExportToDgml(DependencyGraph graph)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
            sb.AppendLine(@"<DirectedGraph Title=""ArcGraph"" xmlns=""http://schemas.microsoft.com/vs/2009/dgml"">");
            sb.AppendLine("  <Nodes>");
            foreach (var n in graph.Nodes)
            {
                sb.AppendLine($"    <Node Id=\"{System.Security.SecurityElement.Escape(n)}\" Label=\"{System.Security.SecurityElement.Escape(n)}\" />");
            }
            sb.AppendLine("  </Nodes>");
            sb.AppendLine("  <Links>");
            foreach (var e in graph.Edges)
            {
                sb.AppendLine($"    <Link Source=\"{System.Security.SecurityElement.Escape(e.From)}\" Target=\"{System.Security.SecurityElement.Escape(e.To)}\" Weight=\"{e.Weight}\" />");
            }
            sb.AppendLine("  </Links>");
            sb.AppendLine("</DirectedGraph>");
            return sb.ToString();
        }
    }
}