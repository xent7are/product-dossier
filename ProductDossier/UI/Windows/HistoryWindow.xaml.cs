using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;
using ProductDossier.Data.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ProductDossier
{
    // Окно для просмотра истории изменений документов
    public partial class HistoryWindow : Window
    {
        public ObservableCollection<HistoryRowVm> Rows { get; } = new();

        private readonly DispatcherTimer _debounceTimer;
        private CancellationTokenSource? _cts;
        private int _requestId;
        private bool _isLoaded;

        // Модель для элемента фильтра по операции
        private sealed class OperationItem
        {
            public string Display { get; }
            public HistoryOperationEnum? Value { get; }

            // Метод для инициализации элемента фильтра по операции
            public OperationItem(string display, HistoryOperationEnum? value)
            {
                Display = display;
                Value = value;
            }

            // Метод для возврата отображаемого названия элемента фильтра
            public override string ToString() => Display;
        }

        // Метод для инициализации окна истории
        public HistoryWindow()
        {
            InitializeComponent();
            DataContext = this;

            cbOperation.ItemsSource = new[]
            {
                new OperationItem("Все", null),
                new OperationItem("Добавление", HistoryOperationEnum.Добавление),
                new OperationItem("Редактирование", HistoryOperationEnum.Редактирование),
                new OperationItem("Изменение категории документа", HistoryOperationEnum.Изменение_категории_документа),
                new OperationItem("Изменение данных документа", HistoryOperationEnum.Изменение_данных_документа),
                new OperationItem("Перемещение в Recycle bin", HistoryOperationEnum.Перемещение_в_корзину),
                new OperationItem("Восстановление", HistoryOperationEnum.Восстановление),
                new OperationItem("Окончательное удаление", HistoryOperationEnum.Окончательное_удаление),
            };
            cbOperation.SelectedIndex = 0;

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += HistoryWindow_Loaded;
        }

        // Метод для обработки события загрузки окна и первоначальной загрузки данных
        private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            await ReloadAsync();
        }

        // Метод для обработки изменения фильтров с запуском debounce-таймера
        private void Filters_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        // Метод, вызываемый по таймеру для перезагрузки данных после паузы
        private async void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            await ReloadAsync();
        }

        // Метод для асинхронной загрузки и применения фильтров к истории
        private async Task ReloadAsync()
        {
            if (tbFio == null || tbProduct == null || tbDocument == null || cbOperation == null)
            {
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            int myRequestId = ++_requestId;

            try
            {
                string fio = tbFio.Text?.Trim() ?? string.Empty;
                string product = tbProduct.Text?.Trim() ?? string.Empty;
                string doc = tbDocument.Text?.Trim() ?? string.Empty;
                HistoryOperationEnum? op = (cbOperation.SelectedItem as OperationItem)?.Value;

                var list = await HistoryService.SearchAsync(fio, op, product, doc, limit: 500);

                token.ThrowIfCancellationRequested();
                if (myRequestId != _requestId)
                {
                    return;
                }

                Rows.Clear();
                foreach (var h in list)
                {
                    Rows.Add(new HistoryRowVm(h));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Модель представления строки истории для отображения в DataGrid
        public sealed class HistoryRowVm
        {
            public string ChangedAtLocal { get; }
            public string UserFullName { get; }
            public string OperationDisplay { get; }
            public string ProductDisplay { get; }
            public string DocumentDisplay { get; }
            public string FileName { get; }
            public string FilePath { get; }

            // Метод для инициализации строки истории
            public HistoryRowVm(DocumentChangeHistory h)
            {
                ChangedAtLocal = h.ChangedAt
                    .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

                string p = string.IsNullOrWhiteSpace(h.UserPatronymic) ? "" : $" {h.UserPatronymic}";
                UserFullName = $"{h.UserSurname} {h.UserName}{p}".Trim();

                OperationDisplay = h.Operation.ToString().Replace('_', ' ');

                ProductDisplay = $"{h.ProductNumber} — {h.ProductName}";
                DocumentDisplay = $"{h.DocumentNumber} — {h.DocumentName}";
                FileName = h.FileName;
                FilePath = h.FilePath;
            }
        }
    }
}