using System;
using System.Windows;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;

namespace ParvNegar.App
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var smokeDir = SmokeRunner.ParseSmokeArgument(e.Args);

            MainWindow main = null;
            var vm = new MainViewModel(XmlSchoolProfileSource.Default(),
                showAbout: () => new AboutWindow((MainViewModel)main.DataContext) { Owner = main }.ShowDialog(),
                exit: () => main.Close());
            main = new MainWindow(vm);
            MainWindow = main;

            if (smokeDir != null) SmokeRunner.Attach(this, main, vm, smokeDir);
            main.Show();
        }
    }
}
