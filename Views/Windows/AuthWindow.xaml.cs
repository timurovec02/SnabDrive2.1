using SnabDrive2._0.Models;
using System.Windows;
using System.Windows.Threading;

namespace SnabDrive2._0.Views.Windows
{
    /// <summary>
    /// Логика взаимодействия для AuthWindow.xaml
    /// </summary>
    public partial class AuthWindow : Window
    {
        private int attemp = 0;
        private DispatcherTimer timer;
        private bool isBlocked = false;

        public AuthWindow()
        {
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMinutes(1); // Таймер на 1 минуту
            timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Разблокируем кнопку и сбрасываем счетчик попыток
            isBlocked = false;
            attemp = 0;
            BtnEnter.IsEnabled = true;
            timer.Stop();
            MessageBox.Show("Вход разблокирован. Вы можете повторить попытку.");
        }

        private void BtnEnter_Click(object sender, RoutedEventArgs e)
        {
            if (isBlocked)
            {
                MessageBox.Show($"Слишком много неудачных попыток. Подождите {Math.Ceiling((timer.Interval - TimeSpan.FromMilliseconds(Environment.TickCount)).TotalSeconds)} секунд.");
                return;
            }

            using (var context = new SnabDriveDBContext())
            {
                var User = context.User.FirstOrDefault(u => u.Login == TxbLogin.Text && u.Password == PsbPassword1.Password);
                if (User != null)
                {
                    CurrentUser.Id = User.Id;
                    CurrentUser.Login = User.Login;
                    CurrentUser.Password = User.Password;
                    CurrentUser.Email = User.Email;
                    CurrentUser.IsAdmin = User.IsAdmin;
                    CurrentUser.IsAuthorized = true;

                    SystemWindow systemWindow = new SystemWindow();
                    Close();
                    systemWindow.Show();
                }
                else
                {
                    attemp++;
                    MessageBox.Show($"Неверное имя пользователя или пароль! Попытка: {attemp}");

                    if (attemp >= 3)
                    {
                        // Блокируем кнопку и запускаем таймер
                        isBlocked = true;
                        BtnEnter.IsEnabled = false;
                        timer.Start();
                        MessageBox.Show("Превышено количество попыток входа. Доступ заблокирован на 1 минуту.");
                    }
                }
            }
        }
    }
}