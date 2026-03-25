namespace ProductDossier.Data.Services
{
    internal sealed class ExecutedFileMove
    {
        public required string SourceAbsolutePath { get; init; }
        public required string TargetAbsolutePath { get; init; }
    }
}