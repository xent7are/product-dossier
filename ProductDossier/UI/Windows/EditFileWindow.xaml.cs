using Microsoft.Win32;
using ProductDossier.Data.Enums;
using ProductDossier.Data.Services;
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;

namespace ProductDossier
{
    public partial class EditFileWindow : Window
    {
        private readonly bool _canReplaceFile;

        public string DocumentNumber => tbDocNumber.Text.Trim();
        public string DocumentName => tbDocName.Text.Trim();
        public DocumentStatusEnum SelectedStatus => (DocumentStatusEnum)cbStatus.SelectedItem;

        public string? NewSourceFilePath { get; private set; }

        public EditFileWindow(DossierService.FileDetailsDto details, bool canReplaceFile)
        {
            InitializeComponent();

            _canReplaceFile = canReplaceFile;

            tbDocNumber.Text = details.Document.DocumentNumber;
            tbDocName.Text = details.Document.NameDocument;

            cbStatus.Items.Clear();
            foreach (DocumentStatusEnum v in Enum.GetValues(typeof(DocumentStatusEnum)))
            {
                if (v == DocumentStatusEnum.В_корзине)
                {
                    continue;
                }

                cbStatus.Items.Add(v);
            }
            cbStatus.SelectedItem = details.Document.Status == DocumentStatusEnum.В_корзине
                ? DocumentStatusEnum.В_работе
                : details.Document.Status;

            tbSelectedFile.Text = details.File.FileName;

            NewSourceFilePath = null;
        }

        // Метод для выбора нового файла (при наличии прав на замену)
        private void btnPick_Click(object sender, RoutedEventArgs e)
        {
            if (!_canReplaceFile)
            {
                MessageBox.Show(
                    "Заменять файл может только Администратор или Супер-администратор.",
                    "Недостаточно прав",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Выбор файла",
                CheckFileExists = true
            };

            if (ofd.ShowDialog() == true)
            {
                NewSourceFilePath = ofd.FileName;
                tbSelectedFile.Text = Path.GetFileName(ofd.FileName);
            }
        }

        // Метод для отмены редактирования документа и закрытия окна
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Метод для проверки введённых данных и подтверждения сохранения изменений
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DocumentNumber))
            {
                MessageBox.Show("Номер документа должен быть заполнен.", "Редактирование",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(DocumentName))
            {
                MessageBox.Show("Название документа должно быть заполнено.", "Редактирование",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (cbStatus.SelectedItem == null)
            {
                MessageBox.Show("Выберите статус документа.", "Редактирование",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}

