//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcAnalyzer.Domain.Results;

public class VulnerableNugetResult
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public double? CvssScore { get; set; }
    public string? VersionRanges { get; set; }
    public string? Reference { get; set; }
}

