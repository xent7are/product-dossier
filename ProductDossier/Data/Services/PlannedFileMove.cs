using ProductFile = ProductDossier.Data.Entities.FileItem;

namespace ProductDossier.Data.Services
{
    internal sealed class PlannedFileMove
    {
        public required ProductFile File { get; init; }
        public string? SourceAbsolutePath { get; init; }
        public string? TargetAbsolutePath { get; init; }
        public string? TargetPathForDb { get; init; }
    }
}