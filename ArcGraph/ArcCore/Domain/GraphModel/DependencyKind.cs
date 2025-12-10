//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.Domain.GraphModel
{
    public enum DependencyKind
    {
        Unknown = 0,
        MethodCall,
        Inheritance,
        Field,
        Property,
        ObjectCreation,
        ParameterType,
        ReturnType,
        Reference
    }
}
