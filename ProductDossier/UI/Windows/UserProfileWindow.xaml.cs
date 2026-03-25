using System.Windows;
using ProductDossier.Data.Entities;

namespace ProductDossier
{
    public partial class UserProfileWindow : Window
    {
        private readonly User _user;
        private readonly string _roleText;

        public bool IsLogoutRequested { get; private set; }

        public UserProfileWindow(User user, string currentUserRole)
        {
            InitializeComponent();

            _user = user ?? throw new ArgumentNullException(nameof(user));
            _roleText = currentUserRole ?? string.Empty;

            DataContext = this;
        }

        public string LoginLine => _user.Login;

        public string SurnameLine => _user.Surname;

        public string NameLine => _user.Name;

        public string PatronymicLine =>
            string.IsNullOrWhiteSpace(_user.Patronymic) ? "—" : _user.Patronymic;

        public string RoleLine => string.IsNullOrWhiteSpace(_roleText) ? "—" : _roleText;

        // Метод для запроса выхода из аккаунта и закрытия окна
        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            IsLogoutRequested = true;
            DialogResult = true;
            Close();
        }

        // Метод для закрытия окна профиля без выхода из аккаунта
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}