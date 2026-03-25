using ProductDossier.Data.Enums;

namespace ProductDossier.Data.Entities
{
    // Модель для таблицы products
    public class Product
    {
        public long IdProduct { get; set; }
        public string ProductNumber { get; set; } = string.Empty;
        public string NameProduct { get; set; } = string.Empty;
        public string? DescriptionProduct { get; set; }
        public ProductStatusEnum Status { get; set; }
        public ProductStatusEnum? StatusBeforeDelete { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public long? DeletedBy { get; set; }

        public ICollection<ProductDocument> ProductDocuments { get; set; } = new List<ProductDocument>();
    }
}
