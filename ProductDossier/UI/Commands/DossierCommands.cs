using System.Windows.Input;

namespace ProductDossier.UI.Commands
{
    // Команды для контекстного меню дерева досье
    public static class DossierCommands
    {
        public static RoutedUICommand AddFileToCategory { get; }
            = new RoutedUICommand("Добавить файл в эту категорию", "AddFileToCategory", typeof(DossierCommands));

        public static RoutedUICommand AddChildFile { get; }
            = new RoutedUICommand("Добавить дочерний файл", "AddChildFile", typeof(DossierCommands));

        public static RoutedUICommand OpenFile { get; }
            = new RoutedUICommand("Открыть", "OpenFile", typeof(DossierCommands));

        public static RoutedUICommand ChangeDocumentCategory { get; }
            = new RoutedUICommand("Изменить категорию", "ChangeDocumentCategory", typeof(DossierCommands));

        public static RoutedUICommand EditFile { get; }
            = new RoutedUICommand("Редактировать", "EditFile", typeof(DossierCommands));

        public static RoutedUICommand FileProperties { get; }
            = new RoutedUICommand("Свойства", "FileProperties", typeof(DossierCommands));

        public static RoutedUICommand DeleteFile { get; }
            = new RoutedUICommand("Удалить файл", "DeleteFile", typeof(DossierCommands));

        public static RoutedUICommand DeleteDocument { get; }
            = new RoutedUICommand("Удалить документ", "DeleteDocument", typeof(DossierCommands));

        public static RoutedUICommand DeleteProduct { get; }
            = new RoutedUICommand("Удалить изделие", "DeleteProduct", typeof(DossierCommands));
    }
}