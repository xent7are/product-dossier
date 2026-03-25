using System.Windows;

namespace ProductDossier
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Начальная навигация на страницу авторизации
            MainFrame.Navigate(new AuthorizationPage());
        }
    }
}