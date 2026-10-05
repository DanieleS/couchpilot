using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace CouchPilot
{
    // Built in code rather than XAML: a handful of fields don't earn a XAML build step.
    public class CouchPilotSettingsView : UserControl
    {
        private readonly CouchPilotSettings settings;
        private readonly StackPanel panel = new StackPanel { Margin = new Thickness(20) };

        public CouchPilotSettingsView(CouchPilotSettings settings)
        {
            this.settings = settings;

            Heading("Telefono");
            Field("Porta del server", nameof(CouchPilotSettings.Port), 120);
            Hint(settings.Addresses.Count > 0
                ? "Apri uno di questi indirizzi dal telefono (stessa rete):"
                : "Il server non è attivo: il motivo è nelle notifiche di Playnite.");
            foreach (var url in settings.Addresses)
            {
                panel.Children.Add(new TextBox { Text = url, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null });
            }

            panel.Children.Add(new TextBlock { Text = "Certificato HTTPS (.pfx)", Margin = new Thickness(0, 14, 0, 4) });
            var certificate = new DockPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
            var browse = new Button { Content = "Sfoglia…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += (s, e) => settings.PickCertificate();
            DockPanel.SetDock(browse, Dock.Right);
            certificate.Children.Add(browse);
            var certificatePath = new TextBox();
            certificatePath.SetBinding(TextBox.TextProperty, Bind(nameof(CouchPilotSettings.CertificatePath)));
            certificate.Children.Add(certificatePath);
            panel.Children.Add(certificate);

            panel.Children.Add(new TextBlock { Text = "Password del certificato", Margin = new Thickness(0, 8, 0, 4) });
            var certificatePassword = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Password = settings.CertificatePassword ?? "" };
            certificatePassword.PasswordChanged += (s, e) => settings.CertificatePassword = certificatePassword.Password;
            panel.Children.Add(certificatePassword);
            panel.Children.Add(new TextBlock
            {
                Text = "Vuoto: il server parla HTTP. Con un certificato parla solo HTTPS, sulla stessa porta, e il telefono può installare l'app. Come crearne uno con una tua CA è spiegato nel README.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading("Touchpad e tastiera");
            var input = new CheckBox { Content = "Permetti al telefono di usare mouse e tastiera del PC" };
            input.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.RemoteInput)));
            panel.Children.Add(input);
            panel.Children.Add(new TextBlock
            {
                Text = "Chiunque raggiunga la porta del server può muovere il mouse e scrivere sul PC: attivalo solo su una rete di cui ti fidi.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading("Home Assistant (MQTT)");
            var enabled = new CheckBox { Content = "Pubblica lo stato del PC su Home Assistant", Margin = new Thickness(0, 0, 0, 10) };
            enabled.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.MqttEnabled)));
            panel.Children.Add(enabled);

            var mqtt = new StackPanel();
            mqtt.SetBinding(IsEnabledProperty, Bind(nameof(CouchPilotSettings.MqttEnabled)));
            panel.Children.Add(mqtt);
            Field("Broker (indirizzo)", nameof(CouchPilotSettings.MqttHost), 260, mqtt);
            Field("Porta", nameof(CouchPilotSettings.MqttPort), 120, mqtt);
            Field("Utente", nameof(CouchPilotSettings.MqttUsername), 260, mqtt);

            mqtt.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 8, 0, 4) });
            var password = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Password = settings.MqttPassword ?? "" };
            password.PasswordChanged += (s, e) => settings.MqttPassword = password.Password;
            mqtt.Children.Add(password);

            var commands = new CheckBox
            {
                Content = "Permetti a Home Assistant di comandare il PC (preset schermo, chiudere e avviare giochi)",
                Margin = new Thickness(0, 14, 0, 0),
            };
            commands.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.MqttCommands)));
            mqtt.Children.Add(commands);

            Field("Topic di base", nameof(CouchPilotSettings.MqttBaseTopic), 260, mqtt);
            Field("Prefisso discovery", nameof(CouchPilotSettings.MqttDiscoveryPrefix), 260, mqtt);
            mqtt.Children.Add(new TextBlock
            {
                Text = "Il PC compare in Home Assistant come dispositivo con gioco in esecuzione, preset schermo e stream Vibepollo.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.7,
            });

            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private Binding Bind(string path) => new Binding(path) { Source = settings, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

        private void Heading(string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 24, 0, 8),
            });
        }

        private void Hint(string text)
        {
            panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 12, 0, 4) });
        }

        private void Field(string label, string path, double width, Panel into = null)
        {
            into = into ?? panel;
            into.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            box.SetBinding(TextBox.TextProperty, Bind(path));
            into.Children.Add(box);
        }
    }
}
