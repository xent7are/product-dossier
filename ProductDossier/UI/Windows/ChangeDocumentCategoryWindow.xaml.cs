using ProductDossier.Data.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ProductDossier
{
    public partial class ChangeDocumentCategoryWindow : Window
    {
        private readonly long _documentId;
        public long? SelectedCategoryId { get; private set; }

        public ChangeDocumentCategoryWindow(long documentId, long currentCategoryId, List<DocumentCategory> categories)
        {
            InitializeComponent();

            _documentId = documentId;

            cbCategory.ItemsSource = categories;
            cbCategory.DisplayMemberPath = nameof(DocumentCategory.NameDocumentCategory);
            cbCategory.SelectedValuePath = nameof(DocumentCategory.IdDocumentCategory);

            cbCategory.SelectedValue = currentCategoryId;

            if (cbCategory.SelectedItem == null && categories.Any())
            {
                cbCategory.SelectedIndex = 0;
            }
        }

        // Метод для отмены изменения категории и закрытия окна
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Метод для подтверждения выбора новой категории документа
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (cbCategory.SelectedValue is long id)
            {
                SelectedCategoryId = id;
                DialogResult = true;
                Close();
                return;
            }

            MessageBox.Show("Выберите категорию.", "Изменение категории",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}