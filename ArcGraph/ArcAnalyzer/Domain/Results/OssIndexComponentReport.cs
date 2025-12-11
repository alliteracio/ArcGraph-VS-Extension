//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcAnalyzer.Domain.Results;

public class OssIndexComponentReport
{
    public string Coordinates { get; set; } = string.Empty;
    public VulnerableNugetResult[]? Vulnerabilities { get; set; }
}
