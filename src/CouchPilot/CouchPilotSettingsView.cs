using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CouchPilot.Server;

namespace CouchPilot
{
    // Built in code rather than XAML: a handful of fields don't earn a XAML build step.
    public class CouchPilotSettingsView : UserControl
    {
        private readonly CouchPilotSettings settings;
        private readonly StackPanel panel = new StackPanel { Margin = new Thickness(20) };
        private readonly StackPanel devices = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

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

            Heading("Telefoni");
            var pairing = new CheckBox { Content = "Chiedi un PIN ai telefoni nuovi" };
            pairing.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.RequirePairing)));
            panel.Children.Add(pairing);
            panel.Children.Add(new TextBlock
            {
                Text = "Un telefono che apre CouchPilot per la prima volta deve scrivere un PIN di sei cifre che compare qui sullo schermo del PC: entra solo chi può leggerlo. "
                    + "Spento, entra chiunque sia sulla rete, come prima. Qui sotto decidi cosa può fare ciascun telefono; le modifiche valgono subito.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });
            panel.Children.Add(devices);
            RebuildDevices();
            if (settings.Pairing != null)
            {
                Action onChanged = () => Dispatcher.BeginInvoke(new Action(RebuildDevices));
                settings.Pairing.Changed += onChanged;
                Unloaded += (s, e) => settings.Pairing.Changed -= onChanged;
            }

            Heading("Touchpad e tastiera");
            var input = new CheckBox { Content = "Permetti al telefono di usare mouse e tastiera del PC" };
            input.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.RemoteInput)));
            panel.Children.Add(input);
            panel.Children.Add(new TextBlock
            {
                Text = "Chiunque raggiunga la porta del server può muovere il mouse e scrivere sul PC: attivalo solo su una rete di cui ti fidi, o chiedi il PIN ai telefoni e scegli sopra quali possono farlo.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading("Screenshot e clip");
            var captures = new CheckBox { Content = "Mostra screenshot e clip sul telefono, e permetti di farne di nuovi", Margin = new Thickness(0, 0, 0, 10) };
            captures.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.Captures)));
            panel.Children.Add(captures);

            var capture = new StackPanel();
            capture.SetBinding(IsEnabledProperty, Bind(nameof(CouchPilotSettings.Captures)));
            panel.Children.Add(capture);
            capture.Children.Add(new TextBlock { Text = "Cattura con", Margin = new Thickness(0, 0, 0, 4) });
            foreach (var (tool, label) in new[]
            {
                (Captures.CaptureTool.GameBar, "Game Bar di Windows (Win+Alt+Stamp, Win+Alt+R, Win+Alt+G)"),
                (Captures.CaptureTool.Nvidia, "NVIDIA: GeForce Experience o app NVIDIA (Alt+F1, Alt+F9, Alt+F10)"),
            })
            {
                var option = new RadioButton { Content = label, GroupName = "CaptureTool", IsChecked = settings.CaptureTool == tool, Margin = new Thickness(0, 2, 0, 2) };
                option.Checked += (s, e) => settings.CaptureTool = tool;
                capture.Children.Add(option);
            }

            capture.Children.Add(new TextBlock { Text = "Un'altra cartella da mostrare (facoltativa)", Margin = new Thickness(0, 12, 0, 4) });
            var folder = new DockPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
            var pickFolder = new Button { Content = "Sfoglia…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            pickFolder.Click += (s, e) => settings.PickCaptureFolder();
            DockPanel.SetDock(pickFolder, Dock.Right);
            folder.Children.Add(pickFolder);
            var folderPath = new TextBox();
            folderPath.SetBinding(TextBox.TextProperty, Bind(nameof(CouchPilotSettings.CaptureFolder)));
            folder.Children.Add(folderPath);
            capture.Children.Add(folder);
            capture.Children.Add(new TextBlock
            {
                Text = "La galleria mostra già la cartella Acquisizioni di Game Bar e quella di NVIDIA. CouchPilot preme le scorciatoie predefinite del programma scelto: il gioco deve essere in primo piano. "
                    + "Per salvare gli ultimi secondi, Game Bar deve registrare in sottofondo (Impostazioni di Windows → Giochi → Acquisizioni) e NVIDIA deve avere il Replay istantaneo acceso. "
                    + "Chiunque raggiunga la porta del server può vedere le catture e cestinarle, a meno di chiedere il PIN ai telefoni e scegliere sopra quali possono.",
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

        // One row a paired phone: its name, when it was last here, what it may do, and a way out.
        private void RebuildDevices()
        {
            devices.Children.Clear();
            var pairing = settings.Pairing;
            if (pairing == null) return;
            var list = pairing.Devices;
            if (list.Count == 0)
            {
                devices.Children.Add(new TextBlock { Text = "Nessun telefono abbinato finora.", Opacity = 0.7 });
                return;
            }
            foreach (var device in list)
            {
                var id = device.Id;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 260 });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                who.Children.Add(new TextBlock { Text = device.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var seen = device.LastSeenAt.ToLocalTime();
                who.Children.Add(new TextBlock
                {
                    Text = seen.Date == DateTime.Today ? $"Visto oggi alle {seen:HH:mm}" : $"Visto il {seen:d}",
                    FontSize = 11,
                    Opacity = 0.6,
                });
                row.Children.Add(who);

                var input = new CheckBox { Content = "Mouse e tastiera", IsChecked = device.AllowInput, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                input.Click += (s, e) => pairing.SetPermissions(id, input.IsChecked == true, null);
                Grid.SetColumn(input, 1);
                row.Children.Add(input);

                var captures = new CheckBox { Content = "Catture", IsChecked = device.AllowCaptures, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                captures.Click += (s, e) => pairing.SetPermissions(id, null, captures.IsChecked == true);
                Grid.SetColumn(captures, 2);
                row.Children.Add(captures);

                var remove = new Button { Content = "Rimuovi", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                remove.Click += (s, e) => pairing.Forget(id);
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);

                devices.Children.Add(row);
            }
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
