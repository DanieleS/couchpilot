using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace CouchPilot.Library
{
    public class LibraryViewDto
    {
        // desktop or fullscreen: each mode keeps its own presets, filters and sort.
        public string Mode { get; set; }
        // Playnite's filter presets, in the order the user gave them.
        public List<FilterPresetDto> Presets { get; set; }
        // The active preset, or null when the filters were changed by hand since.
        public string ActivePreset { get; set; }
        // A SortOrder in camelCase: name, lastActivity, playtime…
        public string Sort { get; set; }
        // How Playnite calls it, in Playnite's language: for sorts the phone has no name for.
        public string SortLabel { get; set; }
        public bool Descending { get; set; }
        // How Playnite groups the games, in Playnite's language, or null when it doesn't.
        public string Grouping { get; set; }
        // The games Playnite shows, in its order. Without grouping, one group with no name.
        public List<GameGroupDto> Groups { get; set; }
    }

    public class FilterPresetDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class GameGroupDto
    {
        // The group's value (a series, a genre, “Today”…), or null for the games without one.
        public string Name { get; set; }
        public List<string> Ids { get; set; }
    }

    // Playnite's library view as the remote sees it: the active filter preset, the sort, the
    // grouping, and the games it shows in that order. The phone may pick a preset or a sort, which
    // changes the view on the PC too, as if clicked there; grouping it only shows.
    //
    // Playnite has no event for the view changing, so the view is compared every second on the UI
    // thread and Changed fires when it differs. Everything here runs on the UI thread, where
    // Playnite keeps its view.
    public class LibraryView
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

        private readonly IPlayniteAPI api;
        private DispatcherTimer timer;
        private string lastShape;
        private bool groupsWarned;

        // The preset, sort, grouping or filters changed on the PC or from a phone.
        public event Action Changed;

        public LibraryView(IPlayniteAPI api)
        {
            this.api = api;
        }

        // The first comparison finds the view new, which only makes phones fetch it once more.
        public void Start()
        {
            OnUi(() =>
            {
                timer = new DispatcherTimer { Interval = Every };
                timer.Tick += (_, __) => Check();
                timer.Start();
            });
        }

        public void Stop() => timer?.Stop();

        public LibraryViewDto Snapshot() => OnUi(Build);

        // Like clicking the preset in Playnite's filter bar.
        public bool ApplyPreset(Guid id)
        {
            var found = OnUi(() =>
            {
                if (!Presets().Any(p => p.Id == id)) return false;
                api.MainView.ApplyFilterPreset(id);
                return true;
            });
            if (found) Check();
            return found;
        }

        // Like picking the sort in Playnite's view menu: a preset that carries its own sort is no
        // longer the active one, as in Playnite.
        public void Sort(SortOrder order, SortOrderDirection direction)
        {
            OnUi(() =>
            {
                var view = api.MainView;
                // The SDK's interface only reads the sort, but Playnite's implementation also
                // writes it, with the same effect as the menu.
                var setter = view.GetType().GetProperty(nameof(IMainViewAPI.SortOrder))?.GetSetMethod();
                if (setter != null)
                {
                    if (view.SortOrder != order) setter.Invoke(view, new object[] { order });
                    if (view.SortOrderDirection != direction) view.SortOrderDirection = direction;
                }
                else
                {
                    // An unnamed preset with the current filters and the new sort: the only way
                    // the SDK itself offers to set it.
                    view.ApplyFilterPreset(new FilterPreset
                    {
                        Name = "CouchPilot",
                        Settings = view.GetCurrentFilterSettings(),
                        SortingOrder = order,
                        SortingOrderDirection = direction,
                    });
                }
                return true;
            });
            Check();
        }

        // On the UI thread, so the timer and a phone's request never compare at the same time.
        private void Check()
        {
            bool changed;
            try
            {
                changed = OnUi(() =>
                {
                    var shape = Shape();
                    if (shape == lastShape) return false;
                    lastShape = shape;
                    return true;
                });
            }
            catch (Exception e)
            {
                logger.Debug(e, "Couldn't read Playnite's library view");
                return;
            }
            if (changed) Changed?.Invoke();
        }

        // What decides whether the view changed. The games it shows change with the library,
        // which phones already hear about.
        private string Shape()
        {
            var view = api.MainView;
            if (view == null) return null;
            return string.Join("|",
                api.ApplicationInfo.Mode,
                view.GetActiveFilterPreset(),
                view.SortOrder,
                view.SortOrderDirection,
                view.Grouping,
                string.Join(",", Presets().Select(p => p.Id + "=" + p.Name)),
                Serialization.ToJson(view.GetCurrentFilterSettings()));
        }

        private LibraryViewDto Build()
        {
            var view = api.MainView;
            var presets = Presets();
            var active = view.GetActiveFilterPreset();
            var grouping = view.Grouping;
            return new LibraryViewDto
            {
                Mode = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen ? "fullscreen" : "desktop",
                Presets = presets.Select(p => new FilterPresetDto { Id = p.Id.ToString(), Name = p.Name }).ToList(),
                ActivePreset = presets.Any(p => p.Id == active) ? active.ToString() : null,
                Sort = CamelCase(view.SortOrder.ToString()),
                SortLabel = Label(view.SortOrder),
                Descending = view.SortOrderDirection == SortOrderDirection.Descending,
                Grouping = grouping == GroupableField.None ? null : Label(grouping),
                Groups = (grouping == GroupableField.None ? null : Grouped()) ?? new List<GameGroupDto>
                {
                    new GameGroupDto { Name = null, Ids = view.FilteredGames.Select(g => g.Id.ToString()).ToList() },
                },
            };
        }

        private List<FilterPreset> Presets() =>
            (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                ? api.MainView.GetSortedFilterFullscreenPresets()
                : api.MainView.GetSortedFilterPresets()) ?? new List<FilterPreset>();

        // Playnite's groups exactly as it shows them. FilteredGames won't do: it lists a game once,
        // while a game with two genres sits in both genres' groups, and it doesn't say where a
        // group ends. The SDK doesn't expose the groups, so this reads the collection view behind
        // the main view; if a Playnite update moves it, the phone gets the games ungrouped.
        private List<GameGroupDto> Grouped()
        {
            try
            {
                var model = Member(api.MainView, "mainModel");
                var games = Member(model, "GamesView");
                if (!(Member(games, "CollectionView") is ICollectionView view) || view.Groups == null) throw new MissingMemberException("CollectionView.Groups");
                return view.Groups.OfType<CollectionViewGroup>().Select(group => new GameGroupDto
                {
                    Name = GroupName(group.Name),
                    Ids = group.Items.Select(entry => (Member(entry, "Game") as Game)?.Id.ToString()).Where(id => id != null).ToList(),
                }).ToList();
            }
            catch (Exception e)
            {
                if (!groupsWarned) logger.Warn(e, "Can't read Playnite's groups: the phone shows the games ungrouped");
                groupsWarned = true;
                return null;
            }
        }

        private string GroupName(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case DatabaseObject item:
                    return item.Id == Guid.Empty || string.IsNullOrEmpty(item.Name) ? null : item.Name;
                case Enum e:
                    return Label(e);
                default:
                    var text = value.ToString();
                    return string.IsNullOrEmpty(text) ? null : text;
            }
        }

        // An enum value's name as Playnite shows it: its Description is a string resource, or the
        // text itself.
        private string Label(Enum value)
        {
            var key = value.GetType().GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (string.IsNullOrEmpty(key)) return value.ToString();
            if (!key.StartsWith("LOC", StringComparison.Ordinal)) return key;
            var text = api.Resources.GetString(key);
            return string.IsNullOrEmpty(text) || text.Contains(key) ? value.ToString() : text;
        }

        // A property or field by name, public or not, from the most derived type that declares it.
        private static object Member(object target, string name)
        {
            if (target == null) throw new MissingMemberException(name);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(target);
                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            throw new MissingMemberException(target.GetType().Name, name);
        }

        private T OnUi<T>(Func<T> read)
        {
            var dispatcher = api.MainView.UIDispatcher;
            return dispatcher.CheckAccess() ? read() : dispatcher.Invoke(read);
        }

        private void OnUi(Action act) => OnUi(() => { act(); return true; });

        private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
