using Microsoft.EntityFrameworkCore;
using SnabDrive2._0.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SnabDrive2._0.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для UsersPage.xaml
    /// </summary>
    public partial class UsersPage : Page
    {
        private readonly SnabDriveDBContext dbContext = new SnabDriveDBContext();
        private ObservableCollection<User> users;
        public UsersPage()
        {
            InitializeComponent();
            InitializeCollections();
            LoadDataAsync();
            UsersDGrid.ItemsSource = users;
        }

        private void InitializeCollections()
        {
            users = new ObservableCollection<User>();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var allUser = await dbContext.User.ToListAsync();

                users.Clear();
                foreach (var item in allUser)
                    users.Add(item);

            }

            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteUserAsync()
        {
            var selectedItem = UsersDGrid.SelectedItem as User;
            if (selectedItem == null)
            {
                MessageBox.Show("Выберите пользователя!", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Вы уверены, что хотите удалить пользователя \"{selectedItem.Login}\"?",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Удаляем из всех коллекций
                    users.Remove(selectedItem);


                    // Удаляем из базы данных
                    dbContext.User.Remove(selectedItem);
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    UsersDGrid.Items.Refresh();
                    MessageBox.Show("Запись успешно удалена.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            UsersAdd.Visibility = Visibility.Visible;
            SaveBtn.Visibility = Visibility.Visible;
            UpdateBtn.Visibility = Visibility.Collapsed;
            LoginTxb.Clear();
            PasswordTxb.Clear();
            EmailTxb.Clear();
            IsAdminChB.IsChecked = false;
        }

        private async void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await DeleteUserAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private async void UpdateUser_Click(object sender, RoutedEventArgs e)
        {
            UsersAdd.Visibility = Visibility.Visible;
            await SelectedUser();
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, заполнены ли обязательные поля
                if (string.IsNullOrWhiteSpace(LoginTxb.Text) ||
                    string.IsNullOrWhiteSpace(PasswordTxb.Text))
                {
                    MessageBox.Show("Заполните логин и пароль!", "Предупреждение",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var existingUser = await dbContext.User
                    .FirstOrDefaultAsync(u => u.Login == LoginTxb.Text);

                if (existingUser != null)
                {
                    var result = MessageBox.Show(
                        $"Пользователь с логином {existingUser.Login} уже существует! \nВы хотите изменить данные этого пользователя?",
                        "Подтверждение",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Заполняем поля данными существующего пользователя
                        LoginTxb.Text = existingUser.Login;
                        PasswordTxb.Text = existingUser.Password;
                        EmailTxb.Text = existingUser.Email;
                        IsAdminChB.IsChecked = existingUser.IsAdmin;

                        SaveBtn.Visibility = Visibility.Collapsed;
                        UpdateBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        LoginTxb.Clear();
                        PasswordTxb.Clear();
                        EmailTxb.Clear();
                        IsAdminChB.IsChecked = false;
                    }
                }
                else
                {
                    var newUser = new User
                    {
                        Login = LoginTxb.Text,
                        Password = PasswordTxb.Text,
                        Email = EmailTxb.Text,
                        IsAdmin = IsAdminChB.IsChecked ?? false
                    };

                    dbContext.User.Add(newUser);
                    await dbContext.SaveChangesAsync();

                    users.Add(newUser); // Добавляем в ObservableCollection
                    UsersDGrid.Items.Refresh();

                    // Очищаем поля и скрываем панель добавления
                    LoginTxb.Clear();
                    PasswordTxb.Clear();
                    EmailTxb.Clear();
                    IsAdminChB.IsChecked = false;

                    UsersAdd.Visibility = Visibility.Collapsed;
                    MessageBox.Show("Пользователь успешно добавлен!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SelectedUser()
        {
            try
            {
                var selectedUser = UsersDGrid.SelectedItem as User;
                if (selectedUser != null)
                {
                    LoginTxb.Text = selectedUser.Login;
                    PasswordTxb.Text = selectedUser.Password;
                    EmailTxb.Text = selectedUser.Email;
                    IsAdminChB.IsChecked = selectedUser.IsAdmin;

                    SaveBtn.Visibility = Visibility.Collapsed;
                    UpdateBtn.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке данных пользователя: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedUser = UsersDGrid.SelectedItem as User;
                if (selectedUser == null)
                {
                    MessageBox.Show("Выберите пользователя для изменения!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Получаем пользователя из базы данных по ID (или другому уникальному идентификатору)
                var userToUpdate = await dbContext.User.FindAsync(selectedUser.Id);

                if (userToUpdate != null)
                {
                    // Обновляем свойства существующего пользователя
                    userToUpdate.Login = LoginTxb.Text;
                    userToUpdate.Password = PasswordTxb.Text;
                    userToUpdate.Email = EmailTxb.Text;
                    userToUpdate.IsAdmin = IsAdminChB.IsChecked ?? false;

                    // Сохраняем изменения
                    await dbContext.SaveChangesAsync();

                    // Обновляем отображение
                    UsersDGrid.Items.Refresh();

                    // Очищаем поля и скрываем панель редактирования
                    LoginTxb.Clear();
                    PasswordTxb.Clear();
                    EmailTxb.Clear();
                    IsAdminChB.IsChecked = false;

                    UsersAdd.Visibility = Visibility.Collapsed;
                    SaveBtn.Visibility = Visibility.Visible;
                    UpdateBtn.Visibility = Visibility.Collapsed;

                    MessageBox.Show("Данные пользователя успешно обновлены!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
