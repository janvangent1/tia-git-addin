using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class HmiChecklistItem : INotifyPropertyChanged
    {
        private string state = "pending";
        private int count;
        private string detail = string.Empty;

        public HmiChecklistItem(string key, string device, string category)
        {
            Key = key ?? string.Empty;
            Device = device ?? string.Empty;
            Category = category ?? string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }

        public string Device { get; }

        public string Category { get; }

        public string State => state;

        public string Mark
        {
            get
            {
                switch (state)
                {
                    case "done":
                        return "\u2713";
                    case "running":
                        return "\u2026";
                    case "skipped":
                        return "\u2013";
                    case "failed":
                        return "!";
                    default:
                        return "\u25cb";
                }
            }
        }

        public string Line
        {
            get
            {
                if (state == "pending")
                {
                    return Category;
                }

                if (state == "skipped" || state == "failed")
                {
                    return string.IsNullOrEmpty(detail) ? Category : Category + " \u2014 " + detail;
                }

                string line = Category + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";
                if (!string.IsNullOrEmpty(detail) && state == "running")
                {
                    line += " \u2014 " + detail;
                }

                return line;
            }
        }

        public void Apply(HmiExportUpdate update)
        {
            state = update.State ?? "pending";
            count = update.Count;
            detail = update.Detail ?? string.Empty;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Mark));
            OnPropertyChanged(nameof(Line));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class HmiChecklistGroup : INotifyPropertyChanged
    {
        private bool isExpanded;

        public HmiChecklistGroup(string device)
        {
            Device = device ?? string.Empty;
            Items = new ObservableCollection<HmiChecklistItem>();
            Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Header));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Device { get; }

        public ObservableCollection<HmiChecklistItem> Items { get; }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded == value)
                {
                    return;
                }

                isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(Header));
            }
        }

        public string Header
        {
            get
            {
                int finished = 0;
                bool running = false;
                bool failed = false;
                HmiChecklistItem? active = null;
                foreach (HmiChecklistItem item in Items)
                {
                    if (item.State == "done" || item.State == "skipped")
                    {
                        finished++;
                    }
                    else if (item.State == "failed")
                    {
                        failed = true;
                    }
                    else if (item.State == "running")
                    {
                        running = true;
                        active = item;
                    }
                }

                string mark = failed ? "!" : running ? "\u2026" : finished == Items.Count && Items.Count > 0 ? "\u2713" : "\u25cb";
                string name = string.IsNullOrEmpty(Device) ? "Project" : Device;
                string header = mark + "  " + name + "    " + finished.ToString(CultureInfo.InvariantCulture) + " / " + Items.Count.ToString(CultureInfo.InvariantCulture);
                if (!isExpanded && active != null)
                {
                    header += "    " + active.Line;
                }

                return header;
            }
        }

        public void Add(HmiChecklistItem item)
        {
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Header));
            Items.Add(item);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
