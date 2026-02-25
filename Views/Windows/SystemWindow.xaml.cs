using SnabDrive2._0.Views.Pages;
using System.Windows;

namespace SnabDrive2._0.Views.Windows
{
    public partial class SystemWindow : Window
    {
       

        public SystemWindow()
        {
            InitializeComponent();
            MyFrame.Navigate(new RegeditPage());
        }

       

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
        }

        
    }
}