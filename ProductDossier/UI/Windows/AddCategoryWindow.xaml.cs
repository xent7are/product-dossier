using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProductDossier
{
    /// <summary>
    /// Логика взаимодействия для AddCategoryWindow.xaml
    /// </summary>
    public partial class AddCategoryWindow : Window
    {
        public string CategoryName { get; private set; } = string.Empty;
        public string? CategoryDescription { get; private set; }
        public int SortOrder { get; private set; }

        // Метод для инициализации окна добавления категории
        public AddCategoryWindow()
        {
            InitializeComponent();

            tbSortOrder.Text = "0";
            tbCategoryName.Focus();
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
            string name = tbCategoryName.Text?.Trim() ?? string.Empty;
            string desc = tbCategoryDescription.Text?.Trim() ?? string.Empty;

            // Проверка названия категории
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Название категории должно быть заполнено.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbCategoryName.Focus();
                return;
            }

            // Проверка сортировки
            if (!int.TryParse(tbSortOrder.Text?.Trim(), out int sortOrder))
            {
                MessageBox.Show("Порядок сортировки должен быть числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbSortOrder.Focus();
                return;
            }

            if (sortOrder < 0)
            {
                MessageBox.Show("Порядок сортировки не может быть отрицательным.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                tbSortOrder.Focus();
                return;
            }

            CategoryName = name;
            CategoryDescription = string.IsNullOrWhiteSpace(desc) ? null : desc;
            SortOrder = sortOrder;

            DialogResult = true;
            Close();
        }

        // Метод для ограничения ввода в поле сортировки только цифрами
        private void tbSortOrder_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        // Метод для восстановления значения сортировки при очистке поля
        private void tbSortOrder_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (tbSortOrder == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(tbSortOrder.Text))
            {
                tbSortOrder.Text = "0";
                tbSortOrder.CaretIndex = tbSortOrder.Text.Length;
            }
        }
    }
}