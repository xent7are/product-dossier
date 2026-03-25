namespace ProductDossier.Data.Entities
{
    // Модель для таблицы product_documents (связь многие-ко-многим)
    public class ProductDocument
    {
        public long IdProduct { get; set; }
        public Product? Product { get; set; }

        public long IdDocument { get; set; }
        public Document? Document { get; set; }
    }
}