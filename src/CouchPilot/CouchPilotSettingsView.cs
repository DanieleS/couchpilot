using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using CouchPilot.Server;
using static CouchPilot.Lang;

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

            Heading(Pc("Phone", "Telefono"));
            Field(Pc("Server port", "Porta del server"), nameof(CouchPilotSettings.Port), 120);
            Hint(settings.Addresses.Count > 0
                ? Pc("Open one of these addresses on the phone (same network):", "Apri uno di questi indirizzi dal telefono (stessa rete):")
                : Pc("The server isn't running: Playnite's notifications say why.", "Il server non è attivo: il motivo è nelle notifiche di Playnite."));
            foreach (var url in settings.Addresses)
            {
                panel.Children.Add(new TextBox { Text = url, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null });
            }

            panel.Children.Add(new TextBlock { Text = Pc("HTTPS certificate (.pfx)", "Certificato HTTPS (.pfx)"), Margin = new Thickness(0, 14, 0, 4) });
            var certificate = new DockPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
            var browse = new Button { Content = Pc("Browse…", "Sfoglia…"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += (s, e) => settings.PickCertificate();
            DockPanel.SetDock(browse, Dock.Right);
            certificate.Children.Add(browse);
            var certificatePath = new TextBox();
            certificatePath.SetBinding(TextBox.TextProperty, Bind(nameof(CouchPilotSettings.CertificatePath)));
            certificate.Children.Add(certificatePath);
            panel.Children.Add(certificate);

            panel.Children.Add(new TextBlock { Text = Pc("Certificate password", "Password del certificato"), Margin = new Thickness(0, 8, 0, 4) });
            var certificatePassword = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Password = settings.CertificatePassword ?? "" };
            certificatePassword.PasswordChanged += (s, e) => settings.CertificatePassword = certificatePassword.Password;
            panel.Children.Add(certificatePassword);
            panel.Children.Add(new TextBlock
            {
                Text = Pc("Empty: the server speaks HTTP. With a certificate it speaks HTTPS only, on the same port, and the phone can install the app. The README explains how to make one with a CA of your own.", "Vuoto: il server parla HTTP. Con un certificato parla solo HTTPS, sulla stessa porta, e il telefono può installare l'app. Come crearne uno con una tua CA è spiegato nel README."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading(Pc("Phones", "Telefoni"));
            var pairing = new CheckBox { Content = Pc("Ask new phones for a PIN", "Chiedi un PIN ai telefoni nuovi") };
            pairing.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.RequirePairing)));
            panel.Children.Add(pairing);
            panel.Children.Add(new TextBlock
            {
                Text = Pc("A phone opening CouchPilot for the first time has to type a six-digit PIN that shows up here on the PC's screen: only who can read it gets in. "
                    + "Off, anyone on the network gets in. Below you choose what each phone may do; changes apply at once.", "Un telefono che apre CouchPilot per la prima volta deve scrivere un PIN di sei cifre che compare qui sullo schermo del PC: entra solo chi può leggerlo. "
                    + "Spento, entra chiunque sia sulla rete, come prima. Qui sotto decidi cosa può fare ciascun telefono; le modifiche valgono subito."),
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

            Heading(Pc("Touchpad and keyboard", "Touchpad e tastiera"));
            var input = new CheckBox { Content = Pc("Let the phone use the PC's mouse and keyboard", "Permetti al telefono di usare mouse e tastiera del PC") };
            input.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.RemoteInput)));
            panel.Children.Add(input);
            panel.Children.Add(new TextBlock
            {
                Text = Pc("Anyone who reaches the server's port can move the mouse and type on the PC: turn it on only on a network you trust, or ask phones for the PIN and choose above which ones may.", "Chiunque raggiunga la porta del server può muovere il mouse e scrivere sul PC: attivalo solo su una rete di cui ti fidi, o chiedi il PIN ai telefoni e scegli sopra quali possono farlo."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading(Pc("Screenshots and clips", "Screenshot e clip"));
            var captures = new CheckBox { Content = Pc("Show screenshots and clips on the phone, and let it take new ones", "Mostra screenshot e clip sul telefono, e permetti di farne di nuovi"), Margin = new Thickness(0, 0, 0, 10) };
            captures.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.Captures)));
            panel.Children.Add(captures);

            var capture = new StackPanel();
            capture.SetBinding(IsEnabledProperty, Bind(nameof(CouchPilotSettings.Captures)));
            panel.Children.Add(capture);
            capture.Children.Add(new TextBlock { Text = Pc("Capture with", "Cattura con"), Margin = new Thickness(0, 0, 0, 4) });
            foreach (var (tool, label) in new[]
            {
                (Captures.CaptureTool.GameBar, Pc("Windows Game Bar (Win+Alt+PrtScn, Win+Alt+R, Win+Alt+G)", "Game Bar di Windows (Win+Alt+Stamp, Win+Alt+R, Win+Alt+G)")),
                (Captures.CaptureTool.Nvidia, Pc("NVIDIA: GeForce Experience or the NVIDIA app (Alt+F1, Alt+F9, Alt+F10)", "NVIDIA: GeForce Experience o app NVIDIA (Alt+F1, Alt+F9, Alt+F10)")),
            })
            {
                var option = new RadioButton { Content = label, GroupName = "CaptureTool", IsChecked = settings.CaptureTool == tool, Margin = new Thickness(0, 2, 0, 2) };
                option.Checked += (s, e) => settings.CaptureTool = tool;
                capture.Children.Add(option);
            }

            capture.Children.Add(new TextBlock { Text = Pc("Another folder to show (optional)", "Un'altra cartella da mostrare (facoltativa)"), Margin = new Thickness(0, 12, 0, 4) });
            var folder = new DockPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
            var pickFolder = new Button { Content = Pc("Browse…", "Sfoglia…"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            pickFolder.Click += (s, e) => settings.PickCaptureFolder();
            DockPanel.SetDock(pickFolder, Dock.Right);
            folder.Children.Add(pickFolder);
            var folderPath = new TextBox();
            folderPath.SetBinding(TextBox.TextProperty, Bind(nameof(CouchPilotSettings.CaptureFolder)));
            folder.Children.Add(folderPath);
            capture.Children.Add(folder);
            capture.Children.Add(new TextBlock
            {
                Text = Pc("The gallery already shows Game Bar's Captures folder and NVIDIA's. CouchPilot presses the chosen program's default shortcuts: the game must be in the foreground. "
                    + "To save the last seconds, Game Bar must record in the background (Windows Settings → Gaming → Captures) and NVIDIA must have Instant Replay on. "
                    + "Anyone who reaches the server's port can see the captures and bin them, unless you ask phones for the PIN and choose above which ones may.", "La galleria mostra già la cartella Acquisizioni di Game Bar e quella di NVIDIA. CouchPilot preme le scorciatoie predefinite del programma scelto: il gioco deve essere in primo piano. "
                    + "Per salvare gli ultimi secondi, Game Bar deve registrare in sottofondo (Impostazioni di Windows → Giochi → Acquisizioni) e NVIDIA deve avere il Replay istantaneo acceso. "
                    + "Chiunque raggiunga la porta del server può vedere le catture e cestinarle, a meno di chiedere il PIN ai telefoni e scegliere sopra quali possono."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading(Pc("Compatible extensions", "Estensioni compatibili"));
            foreach (var extension in settings.Compatible) Extension(extension);
            panel.Children.Add(new TextBlock
            {
                Text = Pc("CouchPilot reads what these Playnite extensions keep, when they're installed. Turn one off and CouchPilot acts as if it weren't there.",
                    "CouchPilot legge quello che tengono queste estensioni di Playnite, se sono installate. Se ne disattivi una, CouchPilot fa come se non ci fosse."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });

            Heading("Home Assistant (MQTT)");
            var enabled = new CheckBox { Content = Pc("Publish the PC's state to Home Assistant", "Pubblica lo stato del PC su Home Assistant"), Margin = new Thickness(0, 0, 0, 10) };
            enabled.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.MqttEnabled)));
            panel.Children.Add(enabled);

            var mqtt = new StackPanel();
            mqtt.SetBinding(IsEnabledProperty, Bind(nameof(CouchPilotSettings.MqttEnabled)));
            panel.Children.Add(mqtt);
            Field(Pc("Broker (address)", "Broker (indirizzo)"), nameof(CouchPilotSettings.MqttHost), 260, mqtt);
            Field(Pc("Port", "Porta"), nameof(CouchPilotSettings.MqttPort), 120, mqtt);
            Field(Pc("User", "Utente"), nameof(CouchPilotSettings.MqttUsername), 260, mqtt);

            mqtt.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 8, 0, 4) });
            var password = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Password = settings.MqttPassword ?? "" };
            password.PasswordChanged += (s, e) => settings.MqttPassword = password.Password;
            mqtt.Children.Add(password);

            var commands = new CheckBox
            {
                Content = Pc("Let Home Assistant control the PC (display presets, closing and starting games)", "Permetti a Home Assistant di comandare il PC (preset schermo, chiudere e avviare giochi)"),
                Margin = new Thickness(0, 14, 0, 0),
            };
            commands.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(CouchPilotSettings.MqttCommands)));
            mqtt.Children.Add(commands);

            Field(Pc("Base topic", "Topic di base"), nameof(CouchPilotSettings.MqttBaseTopic), 260, mqtt);
            Field(Pc("Discovery prefix", "Prefisso discovery"), nameof(CouchPilotSettings.MqttDiscoveryPrefix), 260, mqtt);
            mqtt.Children.Add(new TextBlock
            {
                Text = Pc("The PC shows up in Home Assistant as a device with the running game, the display preset and the Vibepollo stream.", "Il PC compare in Home Assistant come dispositivo con gioco in esecuzione, preset schermo e stream Vibepollo."),
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
                devices.Children.Add(new TextBlock { Text = Pc("No phone paired yet.", "Nessun telefono abbinato finora."), Opacity = 0.7 });
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
                    Text = seen.Date == DateTime.Today ? Pc($"Seen today at {seen:HH:mm}", $"Visto oggi alle {seen:HH:mm}") : Pc($"Seen on {seen:d}", $"Visto il {seen:d}"),
                    FontSize = 11,
                    Opacity = 0.6,
                });
                row.Children.Add(who);

                var input = new CheckBox { Content = Pc("Mouse and keyboard", "Mouse e tastiera"), IsChecked = device.AllowInput, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                input.Click += (s, e) => pairing.SetPermissions(id, input.IsChecked == true, null);
                Grid.SetColumn(input, 1);
                row.Children.Add(input);

                var captures = new CheckBox { Content = Pc("Captures", "Catture"), IsChecked = device.AllowCaptures, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                captures.Click += (s, e) => pairing.SetPermissions(id, null, captures.IsChecked == true);
                Grid.SetColumn(captures, 2);
                row.Children.Add(captures);

                var remove = new Button { Content = Pc("Remove", "Rimuovi"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                remove.Click += (s, e) => pairing.Forget(id);
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);

                devices.Children.Add(row);
            }
        }

        // A checkbox to use it, whether it's on this PC, and where to get it.
        private void Extension(CompatibleExtension extension)
        {
            var use = new CheckBox { Content = $"{extension.Name} ({extension.Purpose})" };
            use.SetBinding(ToggleButton.IsCheckedProperty, Bind(extension.Setting));
            panel.Children.Add(use);

            var state = !extension.Installed && !extension.HasData
                ? Pc("Not found on this PC", "Non trovata su questo PC")
                : !extension.HasData
                    ? Pc("Installed, no data yet", "Installata, ancora senza dati")
                    : Pc("Found on this PC", "Trovata su questo PC");
            var line = new TextBlock { Margin = new Thickness(20, 2, 0, 10), Opacity = 0.8 };
            line.Inlines.Add(new Run(state + " · "));
            var link = new Hyperlink(new Run("GitHub")) { NavigateUri = new Uri(extension.Url) };
            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    // No browser to hand it to: nothing else to do.
                }
                e.Handled = true;
            };
            line.Inlines.Add(link);
            panel.Children.Add(line);
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
