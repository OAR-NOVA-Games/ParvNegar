using System;
using System.Windows;

namespace ParvNegar.App.Views
{
    /// <summary>Startup splash with the OAR NOVA Games Studio mark (owner request, 2026-11-27: 2 seconds).</summary>
    public partial class SplashWindow : Window
    {
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(2);

        public SplashWindow()
        {
            InitializeComponent();
            WindowFit.ClampToWorkArea(this);
        }
    }
}
