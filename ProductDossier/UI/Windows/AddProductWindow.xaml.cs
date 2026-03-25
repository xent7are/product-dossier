using ProductDossier.Data.Enums;
using System;
using System.Net.NetworkInformation;
using System.Windows;

namespace ProductDossier
{
    /// <summary>
    /// Логика взаимодействия для AddProductWindow.xaml
    /// </summary>
    public partial class AddProductWindow : Window
    {
        public string ProductNumber { get; private set; } = string.Empty;
        public string ProductName { get; private set; } = string.Empty;
        public string? ProductDescription { get; private set; }
        public ProductStatusEnum ProductStatus { get; private set; } = ProductStatusEnum.В_работе;

        public AddProductWindow()
        {
            InitializeComponent();

            cbStatus.Items.Clear();
            foreach (ProductStatusEnum v in Enum.GetValues(typeof(ProductStatusEnum)))
            {
                if (v == ProductStatusEnum.В_корзине)
                {
                    continue;
                }

                cbStatus.Items.Add(v);
            }
            cbStatus.SelectedItem = ProductStatusEnum.В_работе;

            tbProductNumber.Focus();
        }

        // Метод для закрытия окна без сохранения
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Метод для сохранения введённых данных и закрытия окна
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string number = tbProductNumber.Text?.Trim() ?? string.Empty;
            string name = tbProductName.Text?.Trim() ?? string.Empty;
            string desc = tbProductDescription.Text?.Trim() ?? string.Empty;

            // Проверка номера изделия
            if (string.IsNullOrWhiteSpace(number))
            {
                MessageBox.Show("Номер изделия должен быть заполнен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbProductNumber.Focus();
                return;
            }

            // Проверка названия изделия
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Название изделия должно быть заполнено.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbProductName.Focus();
                return;
            }

            // Проверка статуса изделия
            if (cbStatus.SelectedItem == null)
            {
                MessageBox.Show("Статус изделия должен быть выбран.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                cbStatus.Focus();
                return;
            }

            ProductNumber = number;
            ProductName = name;
            ProductDescription = string.IsNullOrWhiteSpace(desc) ? null : desc;
            ProductStatus = (ProductStatusEnum)cbStatus.SelectedItem;

            DialogResult = true;
            Close();
        }
    }
}