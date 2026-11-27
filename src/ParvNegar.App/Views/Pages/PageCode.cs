using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ParvNegar.App.Views.Pages
{
    public partial class MessageBanner : UserControl { public MessageBanner() { InitializeComponent(); } }

    /// <summary>Helpers shared by page code-behind: focus the first input and clear password boxes.</summary>
    internal static class PageHelpers
    {
        public static void FocusFirst(UserControl page)
        {
            page.Loaded += (s, e) =>
            {
                var first = Descendants<Control>(page).FirstOrDefault(c => (c is TextBox tb && !tb.IsReadOnly) || c is PasswordBox);
                if (first != null) Keyboard.Focus(first);
            };
        }

        public static void Clear(params PasswordBox[] boxes)
        {
            foreach (var b in boxes) b.Clear();
        }

        public static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var x in Descendants<T>(c)) yield return x;
            }
        }
    }
}
