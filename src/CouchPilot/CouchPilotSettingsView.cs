using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        private readonly StackPanel unlockPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

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

            Heading(Pc("Unlock the PC", "Sblocca il PC"));
            panel.Children.Add(new TextBlock
            {
                Text = Pc("Let a paired phone get past the lock screen — the couch case: the PC woke from sleep, or you pressed Win+L, and you'd rather not reach for a keyboard. "
                    + "It uses your Windows account password (not a Hello PIN), kept sealed on this PC and never sent to the phone. Grant it per phone with the Unlock box above. "
                    + "It can't sign in after a reboot, only unlock a PC you'd already signed into. “Only allow Windows Hello sign-in” must be off in Windows' sign-in options. Read docs/unlock.md before turning it on.", "Permetti a un telefono abbinato di superare la schermata di blocco — il caso divano: il PC si è svegliato dalla sospensione, o hai premuto Win+L, e preferiresti non prendere la tastiera. "
                    + "Usa la password dell'account Windows (non un PIN di Hello), tenuta cifrata su questo PC e mai inviata al telefono. Concedilo per telefono con la casella Sblocco qui sopra. "
                    + "Non può accedere dopo un riavvio, solo sbloccare un PC a cui avevi già fatto l'accesso. «Consenti solo l'accesso con Windows Hello» deve essere spento nelle opzioni di accesso di Windows. Leggi docs/unlock.md prima di attivarlo."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0.7,
            });
            panel.Children.Add(unlockPanel);
            RebuildUnlock();
            if (settings.Unlock != null)
            {
                Action onUnlockChanged = () => Dispatcher.BeginInvoke(new Action(RebuildUnlock));
                settings.Unlock.Changed += onUnlockChanged;
                Unloaded += (s, e) => settings.Unlock.Changed -= onUnlockChanged;
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

                var unlock = new CheckBox { Content = Pc("Unlock", "Sblocco"), IsChecked = device.AllowUnlock, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                unlock.Click += (s, e) => pairing.SetPermissions(id, null, null, unlock.IsChecked == true);
                Grid.SetColumn(unlock, 3);
                row.Children.Add(unlock);

                var remove = new Button { Content = Pc("Remove", "Rimuovi"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                remove.Click += (s, e) => pairing.Forget(id);
                Grid.SetColumn(remove, 4);
                row.Children.Add(remove);

                devices.Children.Add(row);
            }
        }

        // The unlock section's body: a note when the native component is missing, an on/off state
        // once it's there. Rebuilt whenever the feature is turned on or off.
        private void RebuildUnlock()
        {
            unlockPanel.Children.Clear();
            var service = settings.Unlock;
            if (service == null) return;

            if (!service.Available)
            {
                unlockPanel.Children.Add(new TextBlock
                {
                    Text = Pc("The unlock component isn't in this build of CouchPilot.", "Il componente di sblocco non è presente in questa versione di CouchPilot."),
                    Opacity = 0.7,
                });
                return;
            }

            // The helper runs elevated and blocks on the UAC prompt, so Enable/Disable go on a
            // background thread: the settings stay responsive and "Setting up…" actually paints.
            if (service.Enabled)
            {
                var on = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                on.Children.Add(new TextBlock
                {
                    Text = Pc($"On, for {service.Account}.", $"Attivo, per {service.Account}."),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var off = new Button { Content = Pc("Turn off", "Disattiva"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(16, 0, 0, 0) };
                var offStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.8 };
                off.Click += async (s, e) =>
                {
                    off.IsEnabled = false;
                    offStatus.Text = "";
                    string error = null;
                    var ok = await System.Threading.Tasks.Task.Run(() => service.Disable(out error));
                    if (!ok) { offStatus.Text = error; off.IsEnabled = true; }
                    // On success the service raises Changed and this section is rebuilt.
                };
                on.Children.Add(off);
                unlockPanel.Children.Add(on);
                unlockPanel.Children.Add(offStatus);
                return;
            }

            unlockPanel.Children.Add(new TextBlock { Text = Pc("Windows account", "Account Windows"), Margin = new Thickness(0, 8, 0, 4) });
            var account = new TextBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            try { account.Text = Environment.UserDomainName + "\\" + Environment.UserName; } catch { }
            unlockPanel.Children.Add(account);

            unlockPanel.Children.Add(new TextBlock { Text = Pc("Account password", "Password dell'account"), Margin = new Thickness(0, 8, 0, 4) });
            var password = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            unlockPanel.Children.Add(password);

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.8 };
            var enable = new Button { Content = Pc("Enable (asks for admin)", "Attiva (chiede i permessi di amministratore)"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            enable.Click += async (s, e) =>
            {
                enable.IsEnabled = false;
                status.Text = Pc("Setting up…", "Configurazione…");
                var acct = account.Text;
                var pwd = password.Password;
                string error = null;
                var ok = await System.Threading.Tasks.Task.Run(() => service.Enable(acct, pwd, out error));
                if (!ok)
                {
                    status.Text = error;
                    enable.IsEnabled = true;
                }
                // On success the service raises Changed and this whole section is rebuilt.
            };
            unlockPanel.Children.Add(enable);
            unlockPanel.Children.Add(status);
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
