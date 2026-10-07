using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CouchPilot.Server;
using static CouchPilot.Lang;

namespace CouchPilot
{
    // The PIN a phone has to type, big, on top of whatever is on the PC's screen. Built in code
    // like the settings view. Closes by itself once the phone is in, the PIN expires, or you
    // refuse it from here.
    public class PinWindow : Window
    {
        private readonly PairingService pairing;
        private readonly PinRequest request;
        private readonly TextBlock countdown = new TextBlock { FontSize = 14, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public PinWindow(PairingService pairing, PinRequest request)
        {
            this.pairing = pairing;
            this.request = request;

            Title = "CouchPilot";
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16));
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xEF, 0xE6));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F));
            BorderThickness = new Thickness(1);

            var panel = new StackPanel { Margin = new Thickness(40, 32, 40, 28), MinWidth = 380 };
            panel.Children.Add(new TextBlock
            {
                Text = Pc("A phone wants to connect to CouchPilot", "Un telefono chiede di collegarsi a CouchPilot"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = request.DeviceName,
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                // «482 913»: in two groups, as the phone shows its boxes.
                Text = request.Pin.Substring(0, 3) + "  " + request.Pin.Substring(3),
                FontSize = 64,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI, Arial"),
                Margin = new Thickness(0, 18, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = Pc("Type this PIN on the phone.", "Scrivi questo PIN sul telefono."),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(countdown);
            var refuse = new Button
            {
                Content = Pc("Refuse", "Rifiuta"),
                Padding = new Thickness(18, 6, 18, 6),
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            refuse.Click += (s, e) => pairing.CancelPin();
            panel.Children.Add(refuse);
            Content = panel;

            timer.Tick += (s, e) => Tick();
            Tick();
            timer.Start();
            Closed += (s, e) => timer.Stop();
        }

        private void Tick()
        {
            var left = request.ExpiresAt - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                pairing.CancelPin();
                Close();
                return;
            }
            countdown.Text = Pc($"Valid for {left.Minutes}:{left.Seconds:00}", $"Vale ancora {left.Minutes}:{left.Seconds:00}");
            countdown.Margin = new Thickness(0, 4, 0, 0);
        }
    }
}
