using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CouchPilot
{
    // Built in code rather than XAML: one text box doesn't earn a XAML build step.
    public class CouchPilotSettingsView : UserControl
    {
        public CouchPilotSettingsView(CouchPilotSettings settings)
        {
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock { Text = "Porta del server", Margin = new Thickness(0, 0, 0, 6) });
            var port = new TextBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            port.SetBinding(TextBox.TextProperty, new Binding(nameof(CouchPilotSettings.Port))
            {
                Source = settings,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            panel.Children.Add(port);

            panel.Children.Add(new TextBlock
            {
                Text = "Apri uno di questi indirizzi dal telefono (stessa rete):",
                Margin = new Thickness(0, 20, 0, 6),
            });
            foreach (var url in settings.Addresses)
            {
                panel.Children.Add(new TextBox { Text = url, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null });
            }

            Content = panel;
        }
    }
}
