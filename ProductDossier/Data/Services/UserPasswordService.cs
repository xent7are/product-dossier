using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductDossier.Data.Entities;

namespace ProductDossier.Data.Services
{
    // Сервис смены пароля пользователя с проверкой прав и правил безопасности
    public static class UserPasswordService
    {
        // Получение информации о пользователе для окна смены пароля
        public static async Task<(bool Exists, string RoleLabel, bool CanChange, bool IsSelf)> GetPasswordChangeTargetInfoAsync(string currentLogin, string targetLogin)
        {
            currentLogin = (currentLogin ?? string.Empty).Trim();
            targetLogin = (targetLogin ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(targetLogin))
            {
                return (false, string.Empty, false, false);
            }

            await using AppDbContext db = new AppDbContext();
            User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Login == targetLogin);
            if (user == null)
            {
                return (false, string.Empty, false, false);
            }

            string roleLabel = await UserRoleService.GetRoleLabelForLoginAsync(targetLogin);
            bool isSelf = string.Equals(currentLogin, targetLogin, StringComparison.OrdinalIgnoreCase);
            bool isTargetSuperAdmin = await LoginIsSuperAdminAsync(targetLogin);
            bool canChange = isSelf || !isTargetSuperAdmin;

            return (true, roleLabel, canChange, isSelf);
        }

        // Смена пароля выбранного пользователя
        public static async Task<bool> ChangeUserPasswordAsync(string currentLogin, string targetLogin, string newPassword)
        {
            currentLogin = (currentLogin ?? string.Empty).Trim();
            targetLogin = (targetLogin ?? string.Empty).Trim();
            newPassword = newPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentLogin))
                throw new ArgumentException("Не определён текущий пользователь.");

            if (string.IsNullOrWhiteSpace(targetLogin))
                throw new ArgumentException("Введите логин пользователя.");

            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("Введите новый пароль.");

            if (!await CurrentUserIsSuperAdminAsync())
                throw new UnauthorizedAccessException("Недостаточно прав: требуется роль Супер-администратор.");

            await using AppDbContext db = new AppDbContext();

            User? targetUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Login == targetLogin);
            if (targetUser == null)
                throw new InvalidOperationException("Пользователь с таким логином не найден.");

            bool isSelf = string.Equals(currentLogin, targetLogin, StringComparison.OrdinalIgnoreCase);
            bool targetIsSuperAdmin = await LoginIsSuperAdminAsync(targetLogin);
            if (targetIsSuperAdmin && !isSelf)
                throw new InvalidOperationException("Нельзя изменить пароль другого пользователя с ролью Супер-администратор.");

            if (!PasswordValidationService.IsPasswordValid(
                    newPassword,
                    targetUser.Login,
                    targetUser.Surname,
                    targetUser.Name,
                    targetUser.Patronymic,
                    out string passwordError))
            {
                throw new ArgumentException(passwordError);
            }

            await using (var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString))
            {
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CALL product_dossier.change_user_password(@target_login, @new_password);";
                cmd.Parameters.AddWithValue("target_login", targetLogin);
                cmd.Parameters.AddWithValue("new_password", newPassword);

                await cmd.ExecuteNonQueryAsync();
            }

            if (isSelf)
            {
                DbConnectionManager.ConnectionString = DbConnectionManager.BuildUserConnectionString(currentLogin, newPassword);
                AppDbContext.ClearDataSourceCache();
                NpgsqlConnection.ClearAllPools();
            }

            return isSelf;
        }

        // Проверка, что текущий пользователь подключён как супер-администратор
        private static async Task<bool> CurrentUserIsSuperAdminAsync()
        {
            await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_has_role(current_user, 'pd_superadmin', 'member');";
            object? result = await cmd.ExecuteScalarAsync();
            return result is bool b && b;
        }

        // Проверка, что указанный логин имеет роль супер-администратора
        private static async Task<bool> LoginIsSuperAdminAsync(string login)
        {
            await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_has_role(@login::text, 'pd_superadmin', 'member');";
            cmd.Parameters.AddWithValue("login", login);

            object? result = await cmd.ExecuteScalarAsync();
            return result is bool b && b;
        }
    }
}