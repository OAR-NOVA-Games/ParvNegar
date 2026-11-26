using System.Windows;
using ParvNegar.App.ViewModels;

namespace ParvNegar.App.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += (s, e) => CloseButton.Focus();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
