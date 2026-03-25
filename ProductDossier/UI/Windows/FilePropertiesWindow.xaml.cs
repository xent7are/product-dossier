using ProductDossier.Data.Services;
using System.Globalization;
using System.Windows;

namespace ProductDossier
{
    public partial class FilePropertiesWindow : Window
    {
        public FilePropertiesWindow(DossierService.FileDetailsDto details)
        {
            InitializeComponent();

            tbProduct.Text = $"{details.Product.ProductNumber} — {details.Product.NameProduct}";
            tbCategory.Text = details.Category?.NameDocumentCategory ?? $"ID {details.Document.IdDocumentCategory}";
            tbDocument.Text = $"{details.Document.DocumentNumber} — {details.Document.NameDocument}";
            tbStatus.Text = details.Document.Status.ToString();

            tbParent.Text = details.ParentDocument == null
                ? "—"
                : $"{details.ParentDocument.DocumentNumber} — {details.ParentDocument.NameDocument}";

            tbFile.Text = details.File.FileName;

            double mb = details.File.FileSizeBytes / 1024d / 1024d;
            tbSize.Text = $"{mb:N2} МБ";

            tbPath.Text = details.ResolvedFilePath;

            string uploaded = details.File.UploadedAt
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

            string modified = details.File.LastModifiedAt
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

            tbDates.Text = $"Загружен: {uploaded}; Изменён: {modified}";
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}