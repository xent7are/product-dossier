namespace ProductDossier.Data.Entities
{
    // Модель для таблицы users
    public class User
    {
        public long IdUser { get; set; }
        public string Login { get; set; }
        public string Surname { get; set; }
        public string Name { get; set; }
        public string? Patronymic { get; set; }
    }
}