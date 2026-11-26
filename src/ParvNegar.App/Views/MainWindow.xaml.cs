using System.Windows;
using ParvNegar.App.ViewModels;

namespace ParvNegar.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsHome))
                    Dispatcher.BeginInvoke(new System.Action(() => { if (viewModel.IsHome) StartButton.Focus(); else BackButton.Focus(); }));
            };
            Loaded += (s, e) => StartButton.Focus();
        }
    }
}
