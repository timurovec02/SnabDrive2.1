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
    public partial class AdminPage : Page
    {
        public AdminPage()
        {
            InitializeComponent();
        }

        private void UsersPage_Click(object sender, RoutedEventArgs e)
        {
            AdminFrame.Navigate(new UsersPage());
        }
        private void RegeditSettingsPage_Click(object sender, RoutedEventArgs e)
        {

        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new RegeditPage());
        }
    }
}

