using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Islet
{
    public enum ChatApp
    {
        Discord,
        WhatsApp,
        Messenger,
        Browser
    }

    public sealed record ChatNotification(
        ChatApp App,
        string AppName,
        string Sender,
        string Message,
        BitmapSource? Icon,
        bool FromBrowser);

    /// <summary>
    /// Reads Windows toast notifications (with the user's permission) and picks
    /// out Discord, WhatsApp and Messenger messages. Those apps are recognised
    /// by name, which includes web apps installed from the browser ("Install
    /// app"). A plain browser tab's notification is recognised by the site
    /// address the browser adds as its last line; other sites are optional and
    /// labelled with the browser's name.
    /// </summary>
    public sealed class NotificationWatcher
    {
        readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
        readonly HashSet<uint> seen = new();

        UserNotificationListener? listener;
        bool polling;

        public UserNotificationListenerAccessStatus Status { get; private set; } =
            UserNotificationListenerAccessStatus.Unspecified;

        public event Action<ChatNotification>? Received;

        static readonly string[] Browsers =
            { "chrome", "edge", "brave", "firefox", "opera", "vivaldi" };

        static readonly Regex ChatSite = new(
            @"^(https?://)?([\w-]+\.)*(?<site>discord|whatsapp|messenger)\.com(/\S*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public NotificationWatcher()
        {
            timer.Tick += async (s, e) => await PollAsync();
        }

        public async Task<bool> StartAsync()
        {
            if (timer.IsEnabled)
                return true;

            try
            {
                listener = UserNotificationListener.Current;
                Status = await listener.RequestAccessAsync();
                Log.Info("Notification access: " + Status);

                if (Status != UserNotificationListenerAccessStatus.Allowed)
                    return false;

                // Don't replay what's already in the notification center
                foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
                    seen.Add(n.Id);

                timer.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Notification listener unavailable", ex);
                Status = UserNotificationListenerAccessStatus.Denied;
                return false;
            }
        }

        public void Stop() => timer.Stop();

        async Task PollAsync()
        {
            if (polling || listener == null)
                return;

            polling = true;

            try
            {
                var current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                var ids = new HashSet<uint>();

                foreach (var n in current)
                {
                    ids.Add(n.Id);

                    if (seen.Contains(n.Id))
                        continue;

                    var parsed = await ParseAsync(n);

                    if (parsed != null)
                        Received?.Invoke(parsed);
                }

                // Forget dismissed notifications so the set doesn't grow forever
                seen.Clear();
                seen.UnionWith(ids);
            }
            catch (Exception ex)
            {
                Log.Error("Reading notifications failed", ex);
            }
            finally
            {
                polling = false;
            }
        }

        static async Task<ChatNotification?> ParseAsync(UserNotification n)
        {
            string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);

            if (binding == null)
                return null;

            var texts = binding.GetTextElements().Select(t => t.Text?.Trim() ?? "")
                .Where(t => t.Length > 0).ToList();

            var (app, fromBrowser) = Classify(appName, texts);

            if (app == null || !IsEnabled(app.Value) || texts.Count == 0)
                return null;

            BitmapSource? icon = null;

            try
            {
                var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                icon = await MediaController.LoadImageAsync(logo);
            }
            catch
            {
            }

            return new ChatNotification(
                app.Value,
                app.Value switch
                {
                    ChatApp.Discord => "Discord",
                    ChatApp.WhatsApp => "WhatsApp",
                    ChatApp.Messenger => "Messenger",
                    _ => appName
                },
                texts[0],
                string.Join(" ", texts.Skip(1)),
                icon,
                fromBrowser);
        }

        /// <summary>
        /// Which chat app a toast belongs to. A browser tab's site address is
        /// removed from <paramref name="texts"/>, as it isn't part of the message.
        /// </summary>
        internal static (ChatApp? App, bool FromBrowser) Classify(string appName, List<string> texts)
        {
            string name = appName.ToLowerInvariant();
            bool fromBrowser = Browsers.Any(name.Contains);

            if (name.Contains("discord")) return (ChatApp.Discord, fromBrowser);
            if (name.Contains("whatsapp")) return (ChatApp.WhatsApp, fromBrowser);
            if (name.Contains("messenger")) return (ChatApp.Messenger, fromBrowser);

            if (!fromBrowser)
                return (null, false);

            var site = texts.Count > 1 ? ChatSite.Match(texts[^1]) : Match.Empty;

            if (!site.Success)
                return (ChatApp.Browser, true);

            texts.RemoveAt(texts.Count - 1);

            return (site.Groups["site"].Value.ToLowerInvariant() switch
            {
                "discord" => ChatApp.Discord,
                "whatsapp" => ChatApp.WhatsApp,
                _ => ChatApp.Messenger
            }, true);
        }

        static bool IsEnabled(ChatApp app)
        {
            var settings = AppSettings.Current;

            return app switch
            {
                ChatApp.Discord => settings.NotifyDiscord,
                ChatApp.WhatsApp => settings.NotifyWhatsApp,
                ChatApp.Messenger => settings.NotifyMessenger,
                _ => settings.NotifyBrowsers
            };
        }
    }
}
