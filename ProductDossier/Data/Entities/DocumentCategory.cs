namespace ProductDossier.Data.Entities
{
    // Модель для таблицы document_categories
    public class DocumentCategory
    {
        public long IdDocumentCategory { get; set; }
        public string NameDocumentCategory { get; set; } = string.Empty;
        public string? DescriptionDocumentCategory { get; set; }
        public int SortOrder { get; set; }

        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}