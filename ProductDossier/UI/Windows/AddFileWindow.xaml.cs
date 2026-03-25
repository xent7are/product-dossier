using ProductDossier.Data.Enums;
using ProductDossier.UI.Services;
using System;
using System.Windows;
using System.Windows.Media;

namespace ProductDossier
{
    /// <summary>
    /// Логика взаимодействия для AddFileWindow.xaml
    /// </summary>
    public partial class AddFileWindow : Window
    {
        private static readonly Brush DefaultDropZoneBackground = (Brush)new BrushConverter().ConvertFromString("#EDF2FB")!;
        private static readonly Brush DefaultDropZoneBorderBrush = (Brush)new BrushConverter().ConvertFromString("#177EF3")!;
        private static readonly Brush ActiveDropZoneBackground = (Brush)new BrushConverter().ConvertFromString("#E9F3FF")!;
        private static readonly Brush ActiveDropZoneBorderBrush = (Brush)new BrushConverter().ConvertFromString("#5A9BF6")!;

        public string DocumentNumber { get; private set; } = string.Empty;
        public string DocumentName { get; private set; } = string.Empty;
        public DocumentStatusEnum DocumentStatus { get; private set; } = DocumentStatusEnum.В_работе;
        public string SourceFilePath { get; private set; } = string.Empty;

        public AddFileWindow()
        {
            InitializeComponent();

            cbStatus.Items.Clear();
            foreach (DocumentStatusEnum v in Enum.GetValues(typeof(DocumentStatusEnum)))
            {
                if (v == DocumentStatusEnum.В_корзине)
                {
                    continue;
                }

                cbStatus.Items.Add(v);
            }
            cbStatus.SelectedItem = DocumentStatusEnum.В_работе;

            tbSelectedFile.Text = "Нажмите «Выбрать файл» или перетащите файл сюда";
            btnAdd.IsEnabled = false;
            tbDocNumber.Focus();

            ResetDropZoneState();
        }

        // Метод для выбора файла через OpenFileDialog
        private void btnPick_Click(object sender, RoutedEventArgs e)
        {
            if (!DocumentFileSelectionService.TrySelectFromDialog(out SelectedDocumentFile? selectedFile, out string? errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    MessageBox.Show(errorMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                return;
            }

            ApplySelectedFile(selectedFile!);
        }

        // Метод для проверки введённых данных и подтверждения добавления документа
        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SourceFilePath))
            {
                MessageBox.Show("Сначала выберите файл!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string num = tbDocNumber.Text?.Trim() ?? string.Empty;
            string name = tbDocName.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(num))
            {
                MessageBox.Show("Введите номер документа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbDocNumber.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введите название документа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbDocName.Focus();
                return;
            }

            if (cbStatus.SelectedItem == null)
            {
                MessageBox.Show("Выберите статус документа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                cbStatus.Focus();
                return;
            }

            DocumentNumber = num;
            DocumentName = name;
            DocumentStatus = (DocumentStatusEnum)cbStatus.SelectedItem;

            DialogResult = true;
            Close();
        }

        // Метод для отмены добавления документа и закрытия окна
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Метод для обработки перемещения файла над окном
        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            bool canProcess = DocumentFileSelectionService.CanProcessDrop(e.Data);

            e.Effects = canProcess ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropZoneState(canProcess);
            e.Handled = true;
        }

        // Метод для обработки сброса файла в окно
        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            ResetDropZoneState();

            if (!DocumentFileSelectionService.TrySelectFromDrop(e.Data, out SelectedDocumentFile? selectedFile, out string? errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    MessageBox.Show(errorMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            ApplySelectedFile(selectedFile!);
        }

        // Метод для сброса подсветки зоны перетаскивания
        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            ResetDropZoneState();
        }

        // Метод для применения выбранного файла к форме
        private void ApplySelectedFile(SelectedDocumentFile selectedFile)
        {
            SourceFilePath = selectedFile.FullPath;
            tbSelectedFile.Text = selectedFile.FileName;

            if (string.IsNullOrWhiteSpace(tbDocName.Text))
            {
                tbDocName.Text = selectedFile.SuggestedDocumentName;
            }

            btnAdd.IsEnabled = true;
            ResetDropZoneState();
        }

        // Метод для установки активного состояния области загрузки
        private void SetDropZoneState(bool isActive)
        {
            bdDropZone.Background = isActive ? ActiveDropZoneBackground : DefaultDropZoneBackground;
            bdDropZone.BorderBrush = isActive ? ActiveDropZoneBorderBrush : DefaultDropZoneBorderBrush;
        }

        // Метод для возврата стандартного состояния области загрузки
        private void ResetDropZoneState()
        {
            SetDropZoneState(false);
        }
    }
}

