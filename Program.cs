// All application code in one file. .NET 8 + Avalonia 11.2.3.
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Platform.Storage;

class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<ManagerApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

class ManagerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ManagerWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

record Asset(string Name, string Url, long Size, string Digest = "");
record Release(string Tag, List<Asset> Assets, string Notes);
record Installed(string Repo, string Version, string Hash);
class Settings
{
    public string Plugins { get; set; } = "";
    public bool Animations { get; set; } = true;
    public bool Prereleases { get; set; } = false;
    public List<string> Repos { get; set; } = new()
    {
        "https://gitlab.com/gretergttr/mikumenu",
        "https://github.com/MrDiamond64/Hydra",
        "https://github.com/omqitzrlyjames/Kewl-Menu",
        "https://github.com/fugodev/TenkaiMenu",
        "https://github.com/f9p1h7-cell/Dr-hacker-3-menu",
        "https://github.com/NewTabGames/Hydralum",
        "https://github.com/scp222thj/MalumMenu",
        "https://gitlab.com/Wextikit/ElysiumModMenu",
        "https://github.com/The-HyperMenu-Team/HyperMenu",
        "https://github.com/bilalmuslu321-stack/another-among-us-mod-menus/releases/tag/A"
    };
    public Dictionary<string, Installed> Installed { get; set; } = new();
}

partial class ManagerWindow : Window
{
    static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrewmateManagerAvalonia");
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };
    const string Disclaimer = "Independent third-party manager. Not affiliated with the listed mods, their authors, or Innersloth. Private lobbies only, with everyone's informed consent. Do not gain unfair advantages in public games. Plugins execute code; hosting does not prove safety. No process injection or anti-cheat bypass.";
    Settings cfg;
    StackPanel content = new()
    {
        Spacing = 16
    };
    StackPanel nav = new()
    {
        Spacing = 8
    };
    TextBlock status = new()
    {
        Text = "Ready",
        Foreground = Brushes.LightGray,
        TextWrapping = TextWrapping.Wrap
    };
    TextBox pathBox = new();
    ProgressBar progress = new()
    {
        Minimum = 0,
        Maximum = 100,
        Height = 5,
        IsVisible = false
    };
    Button? cancelTransfer;
    CancellationTokenSource? cts;
    bool busy;
    int page;
    string filter = "";
    ComboBox? assets;
    Release? activeRelease;
    string activeRepo = "";
    ListBox? locals;
    readonly List<string> localPaths = new();
    readonly List<string> history = new();
    public ManagerWindow()
    {
        Title = "Crewmate • Mod Library";
        Width = 1240;
        Height = 860;
        MinWidth = 1020;
        MinHeight = 640;
        Background = Brush("#0B0E19");
        Directory.CreateDirectory(Data);
        try
        {
            cfg = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Data, "settings.json"))) ?? new();
        }
        catch
        {
            cfg = new();
        }

        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CrewmateManager/0.4");
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("230,*"),
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        var side = new StackPanel
        {
            Spacing = 24,
            Margin = new Thickness(22, 30)
        };
        side.Children.Add(Text("◈  crewmate", 24, true));
        side.Children.Add(Text("YOUR MOD WORKSPACE", 11, false, "#8E94AD"));
        string[] names =
        {
            "◉  Current Mod Menus",
            "↓  Downloads",
            "⚙  Settings"
        };
        for (int i = 0; i < names.Length; i++)
        {
            int n = i;
            nav.Children.Add(Button(names[i], () =>
            {
                if (!busy)
                    Show(n);
            }, true));
        }

        side.Children.Add(nav);
        ConfigureStudioStyles();
        side.Children.Add(Card(Stack(Text("MADE FOR YOUR LOBBY", 10, true, "#A99CFF"), Text("Pick a mod.\nMake it your own.", 17, true), Text("Private lobbies, shared fun.", 11, false, "#929BB8"))));
        var sidebar = new Border
        {
            Background = Brush("#111626"),
            Child = side,
            BorderBrush = Brush("#242B42"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        root.Children.Add(sidebar);
        var scroll = new ScrollViewer
        {
            Content = content,
            Margin = new Thickness(30),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        cancelTransfer = Button("Cancel download", () => cts?.Cancel());
        cancelTransfer.IsVisible = false;
        var statusBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        statusBar.Children.Add(status);
        Grid.SetColumn(cancelTransfer, 1);
        statusBar.Children.Add(cancelTransfer);
        var footer = Stack(progress, statusBar, Text("Independent manager · Private lobbies with everyone’s consent · Plugins are not security-reviewed", 10, false, "#6E7895"));
        footer.Margin = new Thickness(24, 12);
        var bottom = new Border
        {
            Background = Brush("#101523"),
            Child = footer
        };
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        root.Children.Add(bottom);
        Content = root;
        Show(1);
        Closing += (_, e) =>
        {
            if (busy)
            {
                e.Cancel = true;
                status.Text = "Cancel the current operation before closing.";
            }
        };
    }

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
    static TextBlock Text(string s, double size = 14, bool bold = false, string color = "#E8ECFA") => new()
    {
        Text = s,
        FontSize = size,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap
    };
    static StackPanel Stack(params Control[] controls)
    {
        var s = new StackPanel
        {
            Spacing = 10
        };
        foreach (var c in controls)
            s.Children.Add(c);
        return s;
    }

    static StackPanel Row(params Control[] controls)
    {
        var s = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        foreach (var c in controls)
            s.Children.Add(c);
        return s;
    }

    static Border Card(Control child) => new()
    {
        Background = Brush("#161D30"),
        BorderBrush = Brush("#29314B"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(18),
        Child = child
    };
    Button Button(string label, Action action, bool wide = false)
    {
        var b = new Button
        {
            Content = label,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left
        };
        b.Click += (_, _) =>
        {
            if (busy && label != "Cancel download")
                return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log("Error: " + e.Message);
            }
        };
        return b;
    }

    Button AsyncButton(string label, Func<Task> action) => Button(label, async () =>
    {
        if (busy)
            return;
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Log("Error: " + e.Message);
        }
    });
    void Log(string message)
    {
        journal.Add("INFO", message);
        status.Text = message;
        history.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (history.Count > 100)
            history.RemoveAt(100);
    }

    void Save()
    {
        var p = Path.Combine(Data, "settings.json");
        File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(cfg, JsonOptions));
        File.Move(p + ".tmp", p, true);
    }

    void Show(int n)
    {
        page = n;
        localGallery = null;
        content.Children.Clear();
        for (int i = 0; i < nav.Children.Count; i++)
        {
            if (nav.Children[i] is Button item)
            {
                item.Background = Brush(i == n ? "#312754" : "#111626");
                item.Foreground = Brush(i == n ? "#D5C6FF" : "#8F99B5");
                item.BorderBrush = Brush(i == n ? "#53417E" : "#111626");
            }
        }
        activeRelease = null;
        content.Children.Add(Text(n == 0 ? "Your collection" : n == 1 ? "Find your next favorite." : "Make yourself at home.", 32, true));
        content.Children.Add(Text(n == 0 ? "All your installed menus, neatly in one place." : n == 1 ? "A little less setup. A lot more time with your crew." : "A few preferences. Everything else stays out of your way.", 14, false, "#949EBB"));
        if (n == 0)
            LocalPage();
        else if (n == 1)
            DownloadPage();
        else
            SettingsPage();
        if (cfg.Animations)
        {
            content.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = new CubicEaseOut()
                }
            };
            content.Opacity = 0;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => content.Opacity = 1, Avalonia.Threading.DispatcherPriority.Background);
        }
        else
        {
            content.Transitions = null;
            content.Opacity = 1;
        }
    }

    string Plugins()
    {
        if (string.IsNullOrWhiteSpace(cfg.Plugins)) throw new Exception("Choose your BepInEx/plugins folder in Settings to get started.");
        var p = Path.GetFullPath(cfg.Plugins);
        var dir = new DirectoryInfo(p);
        if (!dir.Exists || !dir.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase) || dir.Parent == null || !dir.Parent.Name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Select the existing BepInEx/plugins folder in Settings.");
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new Exception("Symbolic-link plugin folders are not supported.");
        return p;
    }

    void SettingsPage()
    {
        pathBox = new TextBox
        {
            Text = cfg.Plugins,
            Watermark = "C:\\Games\\Among Us\\BepInEx\\plugins"
        };
        content.Children.Add(Card(Stack(Text("PLUGIN DESTINATION", 12, true, "#A99CFF"), pathBox, Row(AsyncButton("Browse folder", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select BepInEx/plugins", AllowMultiple = false });
            if (folders.Count > 0)
                pathBox.Text = folders[0].TryGetLocalPath() ?? "";
        }), Button("Save & validate", () =>
        {
            var old = cfg.Plugins;
            cfg.Plugins = pathBox.Text?.Trim() ?? "";
            try
            {
                Plugins();
                Save();
                Log("Plugin destination saved.");
            }
            catch
            {
                cfg.Plugins = old;
                throw;
            }
        })), Text("BepInEx must already be installed. On Linux, select the plugins folder in your existing Wine/Proton game installation.", 12, false, "#929BB8"))));
        var anim = new CheckBox
        {
            Content = "Animated page transitions",
            IsChecked = cfg.Animations
        };
        anim.IsCheckedChanged += (_, _) =>
        {
            cfg.Animations = anim.IsChecked == true;
            Save();
        };
        var pre = new CheckBox
        {
            Content = "Include pre-releases when finding the latest release",
            IsChecked = cfg.Prereleases
        };
        pre.IsCheckedChanged += (_, _) =>
        {
            cfg.Prereleases = pre.IsChecked == true;
            Save();
        };
        content.Children.Add(Card(Stack(Text("PREFERENCES", 12, true, "#A99CFF"), anim, pre, Text("Explicit release URLs stay pinned to their tag. Downloads never install automatically.", 12, false, "#929BB8"))));
        content.Children.Add(Card(Stack(Text("TRUST & SAFETY", 12, true, "#F6C878"), Text(Disclaimer), Text("No malware scanner or mod compatibility checker is included. Close the game before modifying any plugins. A PE header and SHA-256 check do not establish safety.", 12, false, "#929BB8"))));
        content.Children.Add(Text("Settings location: " + Data, 12, false, "#929BB8"));
        content.Children.Add(new Expander { Header = "Advanced · Diagnostics & source collections", Content = CollectControls(AddDiagnosticsCard), HorizontalAlignment = HorizontalAlignment.Stretch });
    }

    void LocalPage()
    {
        locals = new ListBox();
        localGallery = new WrapPanel { Orientation = Orientation.Horizontal };
        var search = new TextBox { Watermark = "Search your collection…", Text = filter };
        search.TextChanged += (_, _) => { filter = search.Text ?? ""; RefreshLocal(); };
        content.Children.Add(search);
        content.Children.Add(localGallery);
        content.Children.Add(new Expander
        {
            Header = "More tools · Profiles, backups & integrity",
            Content = CollectControls(() =>
            {
                AddStudioTools();
                content.Children.Add(Row(Button("Refresh collection", RefreshLocal), AsyncButton("Restore backup", Restore)));
            }),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        RefreshLocal();
    }

    void RefreshLocal()
    {
        if (locals == null)
            return;
        localPaths.Clear();
        var rows = new List<string>();
        try
        {
            foreach (var p in Directory.GetFiles(Plugins()).OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileName(p);
                if (!(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)) || !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                localPaths.Add(p);
                cfg.Installed.TryGetValue(p, out var info);
                rows.Add($"{(name.EndsWith(".disabled") ? "○ DISABLED" : "● ENABLED")}    {name}    •    {info?.Version ?? "Untracked"}    •    {new FileInfo(p).Length / 1024.0:N0} KB");
            }

            Log($"{rows.Count} plugin file(s) found.");
        }
        catch (Exception e)
        {
            status.Text = e.Message;
        }

        locals.ItemsSource = rows;
        RenderLocalCards();
    }

    string SelectedFile()
    {
        if (locals == null || locals.SelectedIndex < 0)
            throw new Exception("Select a plugin file first.");
        var p = localPaths[locals.SelectedIndex];
        CheckFile(p);
        return p;
    }

    static void CheckFile(string p)
    {
        if (File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
            throw new Exception("Symbolic links are not supported.");
    }

    static string Backup(string p)
    {
        var dir = Path.Combine(Path.GetDirectoryName(p)!, ".crewmate-backups");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, DateTime.UtcNow.Ticks + "__" + Path.GetFileName(p));
        File.Copy(p, target);
        return target;
    }

    async Task Toggle()
    {
        var p = SelectedFile();
        var target = p.EndsWith(".disabled") ? p[..^9] : p + ".disabled";
        if (File.Exists(target))
            throw new Exception("Destination file already exists.");
        if (!await Confirm("Change plugin state?", "Close Among Us first.\n\n" + Path.GetFileName(p)))
            return;
        File.Move(p, target);
        if (cfg.Installed.Remove(p, out var record))
            cfg.Installed[target] = record;
        Save();
        RefreshLocal();
        Log("Plugin state changed.");
    }

    async Task Uninstall()
    {
        var p = SelectedFile();
        if (!await Confirm("Uninstall plugin?", "Close Among Us first. A backup will be created.\n\n" + Path.GetFileName(p)))
            return;
        Backup(p);
        File.Delete(p);
        cfg.Installed.Remove(p);
        Save();
        RefreshLocal();
        Log("Removed plugin; backup retained.");
    }

    async Task Restore()
    {
        var folder = Plugins();
        var dir = Path.Combine(folder, ".crewmate-backups");
        if (!Directory.Exists(dir))
            throw new Exception("No backups available.");
        var choices = Directory.GetFiles(dir).Where(p => Path.GetFileName(p).Contains("__")).OrderByDescending(p => p).ToArray();
        if (choices.Length == 0)
            throw new Exception("No backups available.");
        var pick = new ComboBox
        {
            ItemsSource = choices.Select(Path.GetFileName).ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!await Confirm("Restore a backup", "Choose a backup. Close the game first. Existing files will be backed up.", pick))
            return;
        var src = choices[pick.SelectedIndex];
        CheckFile(src);
        var name = Path.GetFileName(src).Split("__", 2)[1];
        var target = Path.Combine(folder, name);
        CheckFile(target);
        if (File.Exists(target))
            Backup(target);
        File.Copy(src, target, true);
        cfg.Installed.Remove(target);
        Save();
        RefreshLocal();
        Log("Backup restored; version metadata cleared.");
    }

    void DownloadPage()
    {
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var words = Stack(
            Text("THE CREW’S COLLECTION", 10, true, "#BEA7FF"),
            Text("Good company.\nA different kind of game.", 27, true),
            Text("Your favorite menus, without the folder juggling.", 12, false, "#B3A9D0"));
        hero.Children.Add(words);
        var illustration = Text("◈", 90, true, "#AC8EF0");
        illustration.Margin = new Thickness(36, 0, 30, 0);
        Grid.SetColumn(illustration, 1);
        hero.Children.Add(illustration);
        content.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new GradientStop(Color.Parse("#2F2250"), 0), new GradientStop(Color.Parse("#1B223B"), 1) }
            },
            BorderBrush = Brush("#4B3A70"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(26), Child = hero
        });
        var search = new TextBox { Watermark = "Search menus…", CornerRadius = new CornerRadius(10) };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbar.Children.Add(search);
        var add = AsyncButton("+ Add source", AddSourceDialog);
        add.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(add, 1); toolbar.Children.Add(add);
        content.Children.Add(toolbar);
        var label = Text("ALL MENUS  /  " + cfg.Repos.Count, 11, true, "#8D98B5");
        content.Children.Add(label);
        var gallery = new WrapPanel { Orientation = Orientation.Horizontal };
        void Render()
        {
            gallery.Children.Clear();
            var query = search.Text ?? "";
            foreach (var source in cfg.Repos)
            {
                var display = ModIdentity(source);
                if (!display.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                gallery.Children.Add(MenuCard(source, display));
            }
            if (gallery.Children.Count == 0)
                gallery.Children.Add(Card(Stack(Text("No menus found", 19, true), Text("Try another name or add a source.", 13, false, "#939DB6"))));
        }
        search.TextChanged += (_, _) => Render();
        content.Children.Add(gallery);
        content.Children.Add(Text("Sources are user-added, not endorsed. Compatibility and safety are not guaranteed.", 11, false, "#6F7B98"));
        Render();
    }

    static string Normalize(string input)
    {
        var(host, repo, tag) = Parse(input);
        return "https://" + host + "/" + repo + (tag == null ? "" : host == "github.com" ? "/releases/tag/" + Uri.EscapeDataString(tag) : "/-/releases/" + Uri.EscapeDataString(tag));
    }

    static (string Host, string Repo, string? Tag) Parse(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var u) || u.Scheme != "https" || (u.Host != "github.com" && u.Host != "gitlab.com") || !string.IsNullOrEmpty(u.UserInfo))
            throw new Exception("Use an HTTPS github.com or gitlab.com repository URL.");
        var parts = u.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            throw new Exception("Repository path is missing.");
        string? tag = null;
        string repo;
        if (u.Host == "github.com")
        {
            repo = string.Join('/', parts.Take(2));
            if (parts.Length >= 5 && parts[2] == "releases" && parts[3] == "tag")
                tag = Uri.UnescapeDataString(string.Join('/', parts.Skip(4)));
        }
        else
        {
            var i = Array.IndexOf(parts, "-");
            repo = string.Join('/', i < 0 ? parts : parts.Take(i));
            if (i >= 0 && parts.Length > i + 2 && parts[i + 1] == "releases")
                tag = Uri.UnescapeDataString(string.Join('/', parts.Skip(i + 2)));
        }

        repo = repo.EndsWith(".git") ? repo[..^4] : repo;
        if (repo.Split('/').Any(p => !System.Text.RegularExpressions.Regex.IsMatch(p, @"^[\w.\-]+$") || p == "." || p == ".."))
            throw new Exception("Invalid repository path.");
        return (u.Host, repo, tag);
    }

    async Task<JsonDocument> GetJson(string url, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"API HTTP {(int)response.StatusCode}. Repository/release may be unavailable, private, or rate-limited.");
        await using var s = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var b = new byte[32768];
        int n;
        while ((n = await s.ReadAsync(b, token)) > 0)
        {
            if (memory.Length + n > 8 * 1024 * 1024)
                throw new Exception("API response too large.");
            memory.Write(b, 0, n);
        }

        memory.Position = 0;
        return await JsonDocument.ParseAsync(memory, cancellationToken: token);
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    async Task<Release> FetchRelease(string source, CancellationToken token)
    {
        var(host, repo, tag) = Parse(source);
        bool github = host == "github.com";
        var endpoint = github ? "https://api.github.com/repos/" + repo + "/releases" + (tag != null ? "/tags/" + Uri.EscapeDataString(tag) : cfg.Prereleases ? "?per_page=30" : "/latest") : "https://gitlab.com/api/v4/projects/" + Uri.EscapeDataString(repo) + "/releases" + (tag != null ? "/" + Uri.EscapeDataString(tag) : "?per_page=1&order_by=released_at&sort=desc");
        using var doc = await GetJson(endpoint, token);
        var r = doc.RootElement;
        if (r.ValueKind == JsonValueKind.Array)
        {
            if (r.GetArrayLength() == 0)
                throw new Exception("No published releases found.");
            r = r.EnumerateArray().FirstOrDefault(e => !github || !e.GetProperty("draft").GetBoolean());
            if (r.ValueKind == JsonValueKind.Undefined)
                throw new Exception("No published release found.");
        }

        var files = new List<Asset>();
        if (github)
        {
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                var name = Str(a, "name");
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    files.Add(new(name, Str(a, "browser_download_url"), a.GetProperty("size").GetInt64(), Str(a, "digest")));
            }
        }
        else if (r.TryGetProperty("assets", out var aa) && aa.TryGetProperty("links", out var links))
            foreach (var a in links.EnumerateArray())
            {
                var url = Str(a, "direct_asset_url");
                if (url == "")
                    url = Str(a, "url");
                var name = Str(a, "name");
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    files.Add(new(name, url, 0));
            }

        return new(Str(r, "tag_name"), files, Str(r, github ? "body" : "description"));
    }

    async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy)
            return;
        busy = true;
        if (cancelTransfer != null) cancelTransfer.IsVisible = true;
        cts = new();
        progress.IsVisible = true;
        progress.IsIndeterminate = true;
        // Freeze navigation and all content except cancellation while a transfer is in progress.
        nav.IsEnabled = false;
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Operation cancelled.");
        }
        catch (Exception e)
        {
            Log("Error: " + e.Message);
        }
        finally
        {
            busy = false;
            if (cancelTransfer != null) cancelTransfer.IsVisible = false;
            cts.Dispose();
            cts = null;
            progress.IsVisible = false;
            progress.IsIndeterminate = false;
            nav.IsEnabled = true;
        }
    }

    async Task Install()
    {
        if (activeRelease == null || assets == null || assets.SelectedIndex < 0)
            throw new Exception("Check a release and select its DLL first.");
        var release = activeRelease;
        var repo = activeRepo;
        var asset = release.Assets[assets.SelectedIndex];
        var folder = Plugins();
        if (!System.Text.RegularExpressions.Regex.IsMatch(asset.Name, @"^[A-Za-z0-9_][A-Za-z0-9_. -]*\.dll$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new Exception("Unsafe asset filename.");
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new Exception("HTTPS asset URL required.");
        var dst = Path.Combine(folder, asset.Name);
        CheckFile(dst);
        if (!await Confirm("Install this DLL?", $"Source: {repo}\nRelease: {release.Tag}\nAsset: {asset.Name}\nURL: {asset.Url}\nDestination: {dst}\n\nClose Among Us first. This plugin executes code and has not been reviewed for safety. Existing file will be backed up.\n\nUse only in private lobbies with everyone's consent."))
            return;
        await Run(async token =>
        {
            var temp = Path.Combine(folder, ".crewmate-" + Guid.NewGuid() + ".part");
            try
            {
                Log("Downloading " + asset.Name + "…");
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https")
                    throw new Exception("Insecure redirect rejected.");
                long? length = response.Content.Headers.ContentLength;
                const long max = 100 * 1024 * 1024;
                if (length > max)
                    throw new Exception("DLL exceeds 100 MB limit.");
                progress.IsIndeterminate = length is null;
                progress.Value = 0;
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = 0;
                await using (var stream = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536];
                    int n;
                    while ((n = await stream.ReadAsync(buffer, token)) > 0)
                    {
                        total += n;
                        if (total > max)
                            throw new Exception("DLL exceeds 100 MB limit.");
                        await output.WriteAsync(buffer.AsMemory(0, n), token);
                        sha.AppendData(buffer, 0, n);
                        if (length > 0)
                            progress.Value = total * 100.0 / length.Value;
                    }
                }

                if (length.HasValue && total != length.Value)
                    throw new Exception("Incomplete download.");
                using (var check = File.OpenRead(temp))
                {
                    if (check.ReadByte() != 0x4D || check.ReadByte() != 0x5A)
                        throw new Exception("Not a PE DLL (MZ header missing).");
                }

                var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (asset.Digest.StartsWith("sha256:") && !hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Publisher API digest mismatch. Installation blocked.");
                token.ThrowIfCancellationRequested();
                CheckFile(dst);
                if (File.Exists(dst))
                    Backup(dst);
                File.Move(temp, dst, true);
                cfg.Installed[dst] = new(repo, release.Tag, hash);
                Save();
                Log($"Installed {asset.Name} • {release.Tag} • SHA-256 {hash}. Hash is integrity tracking, not proof of safety.");
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        });
    }

    async Task<bool> Confirm(string title, string body, Control? extra = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 620,
            Height = 520,
            MinWidth = 480,
            MinHeight = 360,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#111626")
        };
        var stack = Stack(Text(title, 23, true), Text(body, 14));
        if (extra != null)
            stack.Children.Add(extra);
        var ok = new Button
        {
            Content = "Confirm",
            Padding = new Thickness(20, 10)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 10)
        };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        stack.Children.Add(Row(cancel, ok));
        dialog.Content = new ScrollViewer
        {
            Content = stack,
            Margin = new Thickness(26)
        };
        return await dialog.ShowDialog<bool>(this);
    }
}

// Studio features share the same window and the same on-disk configuration.
partial class ManagerWindow
{
    readonly StudioJournal journal = new StudioJournal(Data);
    readonly ProfileStore profiles = new ProfileStore(Data);
    readonly LibraryInspector inspector = new LibraryInspector();
    bool toolOpen;
    void ConfigureStudioStyles()
    {
        var pointer = new Style(x => x.OfType<Button>().Class(":pointerover"));
        pointer.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, Brush("#37305E")));
        pointer.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Brush("#9B87F5")));
        Styles.Add(pointer);
        var button = new Style(x => x.OfType<Button>());
        button.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, Brush("#242C45")));
        button.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Brush("#414A69")));
        button.Setters.Add(new Setter(Avalonia.Controls.Button.ForegroundProperty, Brush("#EEEAFE")));
        Styles.Add(button);
        var input = new Style(x => x.OfType<TextBox>());
        input.Setters.Add(new Setter(TextBox.CornerRadiusProperty, new CornerRadius(8)));
        Styles.Add(input);
        var selection = new Style(x => x.OfType<ListBox>());
        selection.Setters.Add(new Setter(ListBox.CornerRadiusProperty, new CornerRadius(8)));
        selection.Setters.Add(new Setter(ListBox.BackgroundProperty, Brush("#0F1526")));
        Styles.Add(selection);
    }

    void AddStudioTools()
    {
        var tabs = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        var actions = new (string Label, Func<Task> Action)[]
        {
            ("Inspect selected DLL", InspectSelected),
            ("Verify library", VerifyLibrary),
            ("Profiles", OpenProfiles),
            ("Activity log", OpenJournal),
            ("Export inventory", ExportInventory),
            ("Backup catalog", OpenBackupCatalog),
            ("Compare release", CompareRecordedVersion)
        };
        foreach (var item in actions)
        {
            var button = AsyncButton(item.Label, async () =>
            {
                if (toolOpen)
                {
                    return;
                }

                toolOpen = true;
                try
                {
                    await item.Action();
                }
                finally
                {
                    toolOpen = false;
                }
            });
            button.Margin = new Thickness(0, 0, 8, 8);
            tabs.Children.Add(button);
        }

        content.Children.Add(Card(Stack(Text("LIBRARY WORKSPACE", 11, true, "#B8A6FF"), tabs, Text("Profiles capture enable/disable states, not DLL contents. Integrity checks compare files with your own recorded hashes, not a malware database.", 12, false, "#9BA7C1"))));
    }

    void AddDiagnosticsCard()
    {
        content.Children.Add(Card(Stack(Text("WORKSPACE HEALTH", 11, true, "#B8A6FF"), Text("Check your folder layout, runtime and local storage before changing plugins.", 13, false, "#A0ABC2"), Row(AsyncButton("Run diagnostics", OpenDiagnostics), AsyncButton("Export activity", ExportJournal)), Row(AsyncButton("Import sources", ImportSources), AsyncButton("Export sources", ExportSources)))));
    }

    async Task InspectSelected()
    {
        var path = SelectedFile();
        InspectionResult? result = null;
        await Run(async token =>
        {
            Log("Inspecting file without loading or executing it…");
            result = await inspector.Inspect(path, cfg.Installed.GetValueOrDefault(path), token);
        });
        if (result == null)
        {
            return;
        }

        var report = result.Report();
        await ReportDialog("Plugin inspector", report, async () =>
        {
            await SaveText("plugin-inspection.txt", report);
        });
    }

    async Task VerifyLibrary()
    {
        var folder = Plugins();
        var results = new List<InspectionResult>();
        await Run(async token =>
        {
            var paths = PluginPaths.List(folder);
            for (var i = 0; i < paths.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var path = paths[i];
                Log($"Verifying {i + 1}/{paths.Count}: {Path.GetFileName(path)}");
                results.Add(await inspector.Inspect(path, cfg.Installed.GetValueOrDefault(path), token));
            }
        });
        if (results.Count == 0)
        {
            Log("No plugin files were verified.");
            return;
        }

        var changed = results.Count(x => x.Integrity == "CHANGED");
        var matched = results.Count(x => x.Integrity == "MATCH");
        var report = $"LIBRARY INTEGRITY REPORT\nGenerated: {DateTimeOffset.Now:O}\nFolder: {folder}\n\n";
        report += $"Checked: {results.Count}\nMatched: {matched}\nChanged: {changed}\nUntracked: {results.Count - changed - matched}\n\n";
        report += string.Join("\n\n--------------------\n\n", results.Select(x => x.Report()));
        report += "\n\nA matching hash only means the bytes match the recorded local install. It does not establish safety or compatibility.";
        Log($"Integrity check finished: {matched} match, {changed} changed.");
        await ReportDialog("Library verification", report, async () =>
        {
            await SaveText("library-verification.txt", report);
        });
    }

    async Task ExportInventory()
    {
        var folder = Plugins();
        var inventory = new InventoryDocument
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Folder = folder,
            SourceCount = cfg.Repos.Count
        };
        foreach (var path in PluginPaths.List(folder))
        {
            var file = new FileInfo(path);
            cfg.Installed.TryGetValue(path, out var metadata);
            inventory.Files.Add(new InventoryFile { Name = file.Name, Bytes = file.Length, Enabled = !PluginPaths.Disabled(path), ModifiedUtc = file.LastWriteTimeUtc, Repository = metadata?.Repo ?? "", Version = metadata?.Version ?? "", RecordedSha256 = metadata?.Hash ?? "" });
        }

        await SaveText("crewmate-inventory.json", JsonSerializer.Serialize(inventory, JsonOptions));
    }

    async Task OpenDiagnostics()
    {
        var report = new List<string>();
        report.Add("CREWMATE / WORKSPACE DIAGNOSTICS");
        report.Add("Generated: " + DateTimeOffset.Now.ToString("O"));
        report.Add("");
        report.Add("OS: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        report.Add("Architecture: " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        report.Add("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        report.Add("Application: 0.3.0");
        report.Add("Settings: " + Data);
        report.Add("Sources: " + cfg.Repos.Count);
        report.Add("Animations: " + cfg.Animations);
        report.Add("Pre-releases: " + cfg.Prereleases);
        report.Add("");
        try
        {
            var folder = Plugins();
            PluginPaths.ValidateFolder(folder);
            report.Add("[PASS] Existing BepInEx/plugins layout");
            report.Add("Destination: " + folder);
            var files = PluginPaths.List(folder);
            report.Add("Plugin count: " + files.Count);
            var duplicates = files.GroupBy(PluginPaths.CanonicalName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToList();
            if (duplicates.Count == 0)
            {
                report.Add("[PASS] No enabled/disabled name pairs detected");
            }
            else
            {
                foreach (var pair in duplicates)
                {
                    report.Add("[WARN] Multiple states for " + pair.Key);
                }
            }

            var backups = Path.Combine(folder, ".crewmate-backups");
            var count = Directory.Exists(backups) ? Directory.GetFiles(backups).Length : 0;
            report.Add("Backup count: " + count);
            var marker = Path.Combine(folder, ".crewmate-write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                await File.WriteAllTextAsync(marker, "write-test");
                report.Add("[PASS] Plugin folder is writable");
            }
            finally
            {
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                }
            }
        }
        catch (Exception e)
        {
            report.Add("[FAIL] " + e.Message);
        }

        report.Add("");
        report.Add("This diagnostic does not check game compatibility, BepInEx binaries, running processes, mod trust, or network availability.");
        var text = string.Join(Environment.NewLine, report);
        await ReportDialog("Workspace diagnostics", text, async () =>
        {
            await SaveText("crewmate-diagnostics.txt", text);
        });
    }

    async Task OpenJournal()
    {
        var events = journal.Read();
        var text = string.Join(Environment.NewLine, events.Select(x => $"{x.Time:yyyy-MM-dd HH:mm:ss zzz} [{x.Level}] {x.Message}"));
        if (text.Length == 0)
        {
            text = "No recorded events.";
        }

        await ReportDialog("Activity journal", text, ExportJournal);
    }

    async Task ExportJournal()
    {
        var text = JsonSerializer.Serialize(journal.Read(), JsonOptions);
        await SaveText("crewmate-activity.json", text);
    }

    async Task SaveText(string suggestedName, string text)
    {
        var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export report", SuggestedFileName = suggestedName, ShowOverwritePrompt = true });
        if (destination == null)
        {
            return;
        }

        await using var stream = await destination.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
        Log("Exported " + suggestedName + ". Reports may contain local paths; review before sharing.");
    }

    async Task ReportDialog(string title, string report, Func<Task> export)
    {
        var window = ToolWindow(title, 800, 640);
        var viewer = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24)
        };
        grid.Children.Add(Text(title, 24, true));
        Grid.SetRow(viewer, 1);
        grid.Children.Add(viewer);
        var buttons = Row(AsyncButton("Export report", export), Button("Close", () => window.Close()));
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        window.Content = grid;
        await window.ShowDialog(this);
    }

    Window ToolWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 520,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#101626")
        };
    }

    async Task OpenProfiles()
    {
        var folder = Plugins();
        var window = ToolWindow("Plugin profiles", 760, 640);
        var list = new ListBox
        {
            Height = 200
        };
        var name = new TextBox
        {
            Watermark = "Profile name, e.g. Private lobby / minimal"
        };
        var details = Text("Capture the enable/disable states of your current DLLs. No plugin binaries are included in a profile.", 13, false, "#A4AFC6");
        var feedback = Text("Ready", 12, false, "#9DD8C7");
        var items = new List<PluginProfile>();
        void Reload()
        {
            items = profiles.List();
            list.ItemsSource = items.Select(x => $"{x.Name}  •  {x.Entries.Count} files  •  {x.CreatedUtc.ToLocalTime():g}").ToArray();
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0)
            {
                return;
            }

            var selected = items[list.SelectedIndex];
            details.Text = $"{selected.Name}\nSource folder: {selected.Folder}\n" + string.Join("\n", selected.Entries.Take(5).Select(x => $"{(x.Enabled ? "ON" : "OFF")}  {x.FileName}"));
        };
        PluginProfile Selected()
        {
            if (list.SelectedIndex < 0)
            {
                throw new Exception("Select a profile first.");
            }

            return items[list.SelectedIndex];
        }

        async Task Execute(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                feedback.Text = e.Message;
                journal.Add("ERROR", "Profile operation: " + e.Message);
            }
        }

        var capture = AsyncButton("Capture current", () => Execute(async () =>
        {
            var profile = await profiles.Capture(folder, name.Text ?? "", CancellationToken.None);
            profiles.Save(profile);
            name.Text = "";
            Reload();
            feedback.Text = "Profile saved with file states and hashes.";
            Log("Captured profile: " + profile.Name);
        }));
        var apply = AsyncButton("Preview & apply", () => Execute(async () =>
        {
            var profile = Selected();
            var plan = profiles.Plan(profile, folder);
            var report = plan.Describe();
            if (plan.Blockers.Count > 0)
            {
                await ReportDialog("Profile cannot be applied", report, () => SaveText("profile-plan.txt", report));
                return;
            }

            if (!await Confirm("Apply profile?", report + "\n\nClose the game first. Unlisted files are left unchanged."))
            {
                return;
            }

            var changed = profiles.Apply(plan);
            foreach (var move in plan.Moves)
            {
                if (cfg.Installed.Remove(move.From, out var metadata))
                {
                    cfg.Installed[move.To] = metadata;
                }
            }

            Save();
            RefreshLocal();
            feedback.Text = $"Applied {changed} state changes.";
            Log("Applied profile " + profile.Name + ": " + changed + " changes.");
        }));
        var remove = AsyncButton("Delete profile", () => Execute(async () =>
        {
            var profile = Selected();
            if (await Confirm("Delete saved profile?", "Delete '" + profile.Name + "'? Plugin files are not affected."))
            {
                profiles.Delete(profile.Id);
                Reload();
                feedback.Text = "Profile deleted. Plugin files were not changed.";
            }
        }));
        var export = AsyncButton("Export JSON", () => Execute(async () =>
        {
            var profile = Selected();
            await SaveText("plugin-profile.json", JsonSerializer.Serialize(profile, JsonOptions));
        }));
        var import = AsyncButton("Import JSON", () => Execute(async () =>
        {
            var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import plugin state profile", AllowMultiple = false });
            if (picked.Count == 0)
            {
                return;
            }

            await using var source = await picked[0].OpenReadAsync();
            using var reader = new StreamReader(source);
            var buffer = new char[1024 * 1024 + 1];
            var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (count > 1024 * 1024)
            {
                throw new Exception("Profile exceeds the 1 MB limit.");
            }

            var profile = profiles.Import(new string (buffer, 0, count));
            Reload();
            feedback.Text = "Imported " + profile.Name + ". Review before applying.";
        }));
        var panel = Stack(Text("PLUGIN PROFILES", 25, true), Text("State presets, with an explicit preview before anything changes.", 13, false, "#9BA8C2"), list, name, Row(capture, apply, remove), Row(import, export), details, feedback, Button("Close", () => window.Close()));
        panel.Margin = new Thickness(24);
        window.Content = new ScrollViewer
        {
            Content = panel
        };
        Reload();
        await window.ShowDialog(this);
    }
}

class InventoryDocument
{
    public int Schema { get; set; } = 1;
    public DateTimeOffset GeneratedUtc { get; set; }
    public string Folder { get; set; } = "";
    public int SourceCount { get; set; }
    public List<InventoryFile> Files { get; set; } = new();
}

class InventoryFile
{
    public string Name { get; set; } = "";
    public long Bytes { get; set; }
    public bool Enabled { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string Repository { get; set; } = "";
    public string Version { get; set; } = "";
    public string RecordedSha256 { get; set; } = "";
}

class InspectionResult
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string Sha256 { get; set; } = "";
    public string Integrity { get; set; } = "UNTRACKED";
    public string Machine { get; set; } = "Unknown";
    public bool PeHeader { get; set; }
    public bool DllFlag { get; set; }
    public string Error { get; set; } = "";
    public Installed? Metadata { get; set; }

    public string Report()
    {
        var lines = new List<string>
        {
            "File: " + System.IO.Path.GetFileName(Path),
            "Path: " + Path,
            "Bytes: " + Bytes,
            "Modified UTC: " + ModifiedUtc.ToString("O"),
            "State: " + (PluginPaths.Disabled(Path) ? "Disabled" : "Enabled"),
            "PE header: " + PeHeader,
            "PE DLL flag: " + DllFlag,
            "Machine: " + Machine,
            "SHA-256: " + Sha256,
            "Local recorded hash: " + Integrity,
            "Recorded release: " + (Metadata?.Version ?? "None"),
            "Recorded source: " + (Metadata?.Repo ?? "None")
        };
        if (Error.Length > 0)
        {
            lines.Add("Error: " + Error);
        }

        lines.Add("");
        lines.Add("This is a static file inspection. The DLL is never loaded by this inspector. Header flags and hashes are not a trust verdict.");
        return string.Join(Environment.NewLine, lines);
    }
}

class LibraryInspector
{
    public async Task<InspectionResult> Inspect(string path, Installed? metadata, CancellationToken token)
    {
        var result = new InspectionResult
        {
            Path = path,
            Metadata = metadata
        };
        try
        {
            PluginPaths.ValidateFile(path);
            var before = new FileInfo(path);
            result.Bytes = before.Length;
            result.ModifiedUtc = before.LastWriteTimeUtc;
            if (result.Bytes > 512L * 1024 * 1024)
            {
                throw new IOException("File exceeds the 512 MB inspection limit.");
            }

            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var digest = await SHA256.HashDataAsync(input, token);
            result.Sha256 = Convert.ToHexString(digest).ToLowerInvariant();
            if (metadata != null)
            {
                result.Integrity = result.Sha256.Equals(metadata.Hash, StringComparison.OrdinalIgnoreCase) ? "MATCH" : "CHANGED";
            }

            input.Position = 0;
            ReadHeaders(input, result);
            var after = new FileInfo(path);
            if (after.Length != result.Bytes || after.LastWriteTimeUtc != result.ModifiedUtc)
            {
                result.Error = "File changed during inspection; results should not be relied on.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            result.Error = e.Message;
        }

        return result;
    }

    static void ReadHeaders(Stream input, InspectionResult result)
    {
        if (input.Length < 64)
        {
            result.Error = "Too small for a DOS/PE header.";
            return;
        }

        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true);
        if (reader.ReadUInt16() != 0x5A4D)
        {
            result.Error = "MZ signature missing.";
            return;
        }

        input.Position = 0x3C;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > input.Length - 24)
        {
            result.Error = "PE offset is outside the file.";
            return;
        }

        input.Position = offset;
        if (reader.ReadUInt32() != 0x00004550)
        {
            result.Error = "PE signature missing.";
            return;
        }

        result.PeHeader = true;
        var machine = reader.ReadUInt16();
        result.Machine = machine switch
        {
            0x014C => "x86 / managed AnyCPU possible",
            0x8664 => "x64",
            0xAA64 => "ARM64",
            0x01C4 => "ARM Thumb-2",
            _ => "0x" + machine.ToString("X4")};
        input.Position = offset + 22;
        var characteristics = reader.ReadUInt16();
        result.DllFlag = (characteristics & 0x2000) != 0;
    }
}

static class PluginPaths
{
    public static bool Disabled(string path)
    {
        return path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
    }

    public static string CanonicalName(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return Disabled(name) ? name[..^9] : name;
    }

    public static bool ValidName(string name)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_][A-Za-z0-9_. -]*\.dll$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static void ValidateFolder(string folder)
    {
        var directory = new DirectoryInfo(System.IO.Path.GetFullPath(folder));
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException("Plugin folder does not exist.");
        }

        if (!directory.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase) || directory.Parent == null || !directory.Parent.Name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Expected an existing BepInEx/plugins folder.");
        }

        for (DirectoryInfo? current = directory; current != null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Linked folder paths are not supported for profile operations.");
            }
        }
    }

    public static void ValidateFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Plugin file does not exist.", path);
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Symbolic-link plugin files are not supported.");
        }
    }

    public static List<string> List(string folder)
    {
        ValidateFolder(folder);
        return Directory.GetFiles(folder).Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Disabled(x)).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

class JournalEvent
{
    public DateTimeOffset Time { get; set; }
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = "";
}

class StudioJournal
{
    readonly string file;
    readonly object gate = new();
    readonly List<JournalEvent> entries = new();
    const int MaxEntries = 1000;
    public StudioJournal(string directory)
    {
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "activity.json");
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length <= 4 * 1024 * 1024)
            {
                entries = JsonSerializer.Deserialize<List<JournalEvent>>(File.ReadAllText(file)) ?? new();
            }
        }
        catch
        {
        // A malformed optional log must not prevent startup.
        }
    }

    public void Add(string level, string message)
    {
        lock (gate)
        {
            entries.Insert(0, new JournalEvent { Time = DateTimeOffset.Now, Level = level, Message = message.Length > 4096 ? message[..4096] : message });
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            try
            {
                var temporary = file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
                File.Move(temporary, file, true);
            }
            catch
            {
            // Keep the in-memory log usable even if the disk becomes read-only.
            }
        }
    }

    public List<JournalEvent> Read()
    {
        lock (gate)
        {
            return entries.Select(x => new JournalEvent { Time = x.Time, Level = x.Level, Message = x.Message }).ToList();
        }
    }
}

class PluginProfile
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ProfileEntry> Entries { get; set; } = new();
}

class ProfileEntry
{
    public string FileName { get; set; } = "";
    public bool Enabled { get; set; }
    public string Sha256 { get; set; } = "";
}

record ProfileMove(string From, string To);
class ProfilePlan
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<ProfileMove> Moves { get; } = new();
    public List<string> Blockers { get; } = new();
    public List<string> Warnings { get; } = new();
    public Dictionary<string, string> ExpectedHashes { get; } = new();
    public int Unchanged { get; set; }

    public string Describe()
    {
        var lines = new List<string>
        {
            "Profile: " + Name,
            "Destination: " + Folder,
            "State changes: " + Moves.Count,
            "Already correct: " + Unchanged,
            "Blocking issues: " + Blockers.Count,
            ""
        };
        foreach (var move in Moves)
        {
            lines.Add(Path.GetFileName(move.From) + " → " + Path.GetFileName(move.To));
        }

        foreach (var blocker in Blockers)
        {
            lines.Add("BLOCKED: " + blocker);
        }

        foreach (var warning in Warnings)
        {
            lines.Add("NOTE: " + warning);
        }

        if (Moves.Count == 0 && Blockers.Count == 0)
        {
            lines.Add("No files need to be renamed.");
        }

        lines.Add("");
        lines.Add("Profiles do not download, delete, or execute plugins. Extra files are left untouched. The profile's captured hashes are checked before renaming.");
        return string.Join(Environment.NewLine, lines);
    }
}

class ProfileStore
{
    readonly string directory;
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };
    public ProfileStore(string data)
    {
        directory = Path.Combine(data, "profiles");
        Directory.CreateDirectory(directory);
    }

    public List<PluginProfile> List()
    {
        var result = new List<PluginProfile>();
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 1024 * 1024)
                {
                    continue;
                }

                var profile = JsonSerializer.Deserialize<PluginProfile>(File.ReadAllText(file));
                if (profile != null)
                {
                    Validate(profile);
                    result.Add(profile);
                }
            }
            catch
            {
            // Invalid profile files are not eligible for applying.
            }
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<PluginProfile> Capture(string folder, string name, CancellationToken token)
    {
        name = name.Trim();
        if (name.Length < 1 || name.Length > 80)
        {
            throw new ArgumentException("Enter a profile name between 1 and 80 characters.");
        }

        var profile = new PluginProfile
        {
            Name = name,
            Folder = Path.GetFullPath(folder)
        };
        var paths = PluginPaths.List(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            PluginPaths.ValidateFile(path);
            var canonical = PluginPaths.CanonicalName(path);
            if (!seen.Add(canonical))
            {
                throw new IOException("Both enabled and disabled copies exist for " + canonical + ". Resolve the duplicate before capturing.");
            }

            if (!PluginPaths.ValidName(canonical))
            {
                throw new IOException("Unsupported plugin filename: " + canonical);
            }

            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (input.Length > 512L * 1024 * 1024)
            {
                throw new IOException("Plugin exceeds the profile hash limit: " + canonical);
            }

            var hash = await SHA256.HashDataAsync(input, token);
            profile.Entries.Add(new ProfileEntry { FileName = canonical, Enabled = !PluginPaths.Disabled(path), Sha256 = Convert.ToHexString(hash).ToLowerInvariant() });
        }

        Validate(profile);
        return profile;
    }

    public void Save(PluginProfile profile)
    {
        Validate(profile);
        var path = Path.Combine(directory, profile.Id + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
        File.Move(temp, path, true);
    }

    public PluginProfile Import(string json)
    {
        if (json.Length > 1024 * 1024)
        {
            throw new IOException("Profile exceeds 1 MB.");
        }

        var profile = JsonSerializer.Deserialize<PluginProfile>(json) ?? throw new IOException("Profile JSON was empty.");
        Validate(profile);
        profile.Id = Guid.NewGuid().ToString("N");
        Save(profile);
        return profile;
    }

    public void Delete(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("Invalid profile ID.");
        }

        File.Delete(Path.Combine(directory, id + ".json"));
    }

    static void Validate(PluginProfile profile)
    {
        if (profile.Schema != 1)
        {
            throw new IOException("Unsupported profile schema.");
        }

        if (!Guid.TryParseExact(profile.Id, "N", out _))
        {
            throw new IOException("Profile ID must be a valid UUID.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80)
        {
            throw new IOException("Invalid profile name.");
        }

        if (profile.Entries == null || profile.Entries.Count > 500)
        {
            throw new IOException("Profile must contain at most 500 entries.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Entries)
        {
            if (entry == null || !PluginPaths.ValidName(entry.FileName))
            {
                throw new IOException("Invalid or unsafe plugin filename in profile.");
            }

            if (!seen.Add(entry.FileName))
            {
                throw new IOException("Duplicate plugin entry in profile.");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(entry.Sha256 ?? "", "^[a-fA-F0-9]{64}$"))
            {
                throw new IOException("A profile hash is missing or invalid.");
            }
        }
    }

    public ProfilePlan Plan(PluginProfile profile, string folder)
    {
        Validate(profile);
        PluginPaths.ValidateFolder(folder);
        var plan = new ProfilePlan
        {
            Name = profile.Name,
            Folder = Path.GetFullPath(folder)
        };
        var files = PluginPaths.List(folder);
        var indexed = files.GroupBy(PluginPaths.CanonicalName, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Entries)
        {
            if (!indexed.TryGetValue(entry.FileName, out var matches))
            {
                plan.Blockers.Add("Missing plugin: " + entry.FileName);
                continue;
            }

            if (matches.Length != 1)
            {
                plan.Blockers.Add("Ambiguous enabled/disabled files: " + entry.FileName);
                continue;
            }

            var source = matches[0];
            try
            {
                PluginPaths.ValidateFile(source);
                using var input = File.OpenRead(source);
                if (input.Length > 512L * 1024 * 1024)
                {
                    throw new IOException("File too large for profile validation.");
                }

                var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Blockers.Add("File bytes differ from captured profile: " + entry.FileName);
                    continue;
                }

                plan.ExpectedHashes[source] = hash;
            }
            catch (Exception e)
            {
                plan.Blockers.Add(entry.FileName + ": " + e.Message);
                continue;
            }

            var disabled = PluginPaths.Disabled(source);
            if (entry.Enabled == !disabled)
            {
                plan.Unchanged++;
                continue;
            }

            var target = disabled ? source[..^9] : source + ".disabled";
            if (File.Exists(target))
            {
                plan.Blockers.Add("Target already exists: " + Path.GetFileName(target));
                continue;
            }

            plan.Moves.Add(new ProfileMove(source, target));
        }

        var extras = indexed.Keys.Except(profile.Entries.Select(x => x.FileName), StringComparer.OrdinalIgnoreCase).Count();
        if (extras > 0)
        {
            plan.Warnings.Add($"{extras} unlisted plugin(s) will be left unchanged.");
        }

        if (!string.Equals(profile.Folder, plan.Folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            plan.Warnings.Add("This profile was captured in a different folder. Hashes must still match.");
        }

        return plan;
    }

    public int Apply(ProfilePlan plan)
    {
        if (plan.Blockers.Count > 0)
        {
            throw new IOException("A blocked profile cannot be applied.");
        }

        PluginPaths.ValidateFolder(plan.Folder);
        // Revalidate after the confirmation dialog to catch intervening changes.
        foreach (var expected in plan.ExpectedHashes)
        {
            PluginPaths.ValidateFile(expected.Key);
            using var stream = File.OpenRead(expected.Key);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!hash.Equals(expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("File changed after preview: " + Path.GetFileName(expected.Key));
            }
        }

        foreach (var move in plan.Moves)
        {
            if (File.Exists(move.To))
            {
                throw new IOException("A destination appeared after preview: " + Path.GetFileName(move.To));
            }
        }

        var completed = new List<ProfileMove>();
        try
        {
            foreach (var move in plan.Moves)
            {
                File.Move(move.From, move.To);
                completed.Add(move);
            }
        }
        catch (Exception original)
        {
            var failures = new List<string>();
            for (var i = completed.Count - 1; i >= 0; i--)
            {
                var move = completed[i];
                try
                {
                    File.Move(move.To, move.From);
                }
                catch (Exception rollback)
                {
                    failures.Add(Path.GetFileName(move.From) + ": " + rollback.Message);
                }
            }

            if (failures.Count > 0)
            {
                throw new IOException("Profile failed and rollback was incomplete. Inspect files manually. " + string.Join("; ", failures), original);
            }

            throw new IOException("Profile failed; completed renames were rolled back. " + original.Message, original);
        }

        return completed.Count;
    }
}

partial class ManagerWindow
{
    async Task OpenBackupCatalog()
    {
        var folder = Plugins();
        var backupFolder = Path.Combine(folder, ".crewmate-backups");
        var catalog = new List<BackupCatalogEntry>();
        if (Directory.Exists(backupFolder))
        {
            foreach (var path in Directory.GetFiles(backupFolder).OrderByDescending(x => x))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var file = new FileInfo(path);
                var name = file.Name;
                var separator = name.IndexOf("__", StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                catalog.Add(new BackupCatalogEntry { StoredName = name, PluginName = name[(separator + 2)..], Bytes = file.Length, ModifiedUtc = file.LastWriteTimeUtc });
            }
        }

        var report = new List<string>
        {
            "BACKUP CATALOG",
            "Folder: " + backupFolder,
            "Count: " + catalog.Count,
            "Total: " + HumanSize(catalog.Sum(x => x.Bytes)),
            "",
            "Backups are local copies, not trusted versions. This view never deletes files.",
            "Use Restore backup in the library to select one for restoration.",
            ""
        };
        foreach (var item in catalog)
        {
            report.Add(item.PluginName);
            report.Add("  Size: " + HumanSize(item.Bytes));
            report.Add("  Modified: " + item.ModifiedUtc.ToString("O"));
            report.Add("  Stored as: " + item.StoredName);
            report.Add("");
        }

        await ReportDialog("Backup catalog", string.Join(Environment.NewLine, report), async () =>
        {
            await SaveText("backup-catalog.json", JsonSerializer.Serialize(catalog, JsonOptions));
        });
    }

    static string HumanSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        if (bytes < 1024L * 1024)
        {
            return (bytes / 1024.0).ToString("N1") + " KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("N1") + " MB";
        }

        return (bytes / (1024.0 * 1024 * 1024)).ToString("N1") + " GB";
    }

    async Task ExportSources()
    {
        var document = new SourceCollection
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Sources = cfg.Repos.ToList()
        };
        await SaveText("crewmate-sources.json", JsonSerializer.Serialize(document, JsonOptions));
    }

    async Task ImportSources()
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import source collection JSON", AllowMultiple = false });
        if (selected.Count == 0)
        {
            return;
        }

        await using var stream = await selected[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var buffer = new char[1024 * 1024 + 1];
        var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (count > 1024 * 1024)
        {
            throw new IOException("Source collection exceeds the 1 MB limit.");
        }

        var document = JsonSerializer.Deserialize<SourceCollection>(new string (buffer, 0, count));
        if (document == null || document.Schema != 1 || document.Sources == null)
        {
            throw new IOException("Unsupported source collection.");
        }

        if (document.Sources.Count > 200)
        {
            throw new IOException("A collection can contain at most 200 sources.");
        }

        var valid = new List<string>();
        var errors = new List<string>();
        foreach (var source in document.Sources)
        {
            try
            {
                valid.Add(Normalize(source));
            }
            catch (Exception e)
            {
                errors.Add((source ?? "<null>") + ": " + e.Message);
            }
        }

        if (errors.Count > 0)
        {
            await ReportDialog("Source import rejected", "Nothing was imported. Fix these invalid URLs first:\n\n" + string.Join("\n", errors), () => SaveText("source-import-errors.txt", string.Join("\n", errors)));
            return;
        }

        var additions = valid.Distinct(StringComparer.OrdinalIgnoreCase).Except(cfg.Repos, StringComparer.OrdinalIgnoreCase).ToList();
        if (additions.Count == 0)
        {
            Log("Collection contains no new sources.");
            return;
        }

        var summary = "Add " + additions.Count + " source(s)?\n\n" + string.Join("\n", additions);
        summary += "\n\nNo releases will be downloaded automatically.";
        if (!await Confirm("Import source collection", summary))
        {
            return;
        }

        cfg.Repos.AddRange(additions);
        Save();
        Show(1);
        Log("Imported " + additions.Count + " sources.");
    }

    async Task CompareRecordedVersion()
    {
        var path = SelectedFile();
        if (!cfg.Installed.TryGetValue(path, out var record))
        {
            throw new Exception("This file has no tracked repository. Install through Downloads to record its source first.");
        }

        Release? release = null;
        await Run(async token =>
        {
            release = await FetchRelease(record.Repo, token);
        });
        if (release == null)
        {
            return;
        }

        var same = string.Equals(record.Version, release.Tag, StringComparison.Ordinal);
        var lines = new List<string>
        {
            "RECORDED RELEASE COMPARISON",
            "File: " + Path.GetFileName(path),
            "Repository: " + record.Repo,
            "Installed tag: " + record.Version,
            "Source currently resolves to: " + release.Tag,
            "Result: " + (same ? "Same tag" : "Different tag available"),
            "",
            "Tag strings are compared exactly. This is not semantic version ordering.",
            "A pinned source checks its pinned tag, not the repository's latest release.",
            "A release may replace bytes without changing its tag; verify the local hash separately.",
            "No files were downloaded or modified.",
            "",
            "DLL ASSETS"
        };
        lines.AddRange(release.Assets.Select(x => x.Name));
        lines.Add("");
        lines.Add("RELEASE NOTES");
        lines.Add(release.Notes);
        var report = string.Join(Environment.NewLine, lines);
        await ReportDialog("Release comparison", report, () => SaveText("release-comparison.txt", report));
    }
}

class BackupCatalogEntry
{
    public string StoredName { get; set; } = "";
    public string PluginName { get; set; } = "";
    public long Bytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

class SourceCollection
{
    public int Schema { get; set; } = 1;
    public DateTimeOffset GeneratedUtc { get; set; }
    public List<string> Sources { get; set; } = new();
}

record ModDisplay(string Name, string Subtitle, string Accent, string Monogram);

partial class ManagerWindow
{
    WrapPanel? localGallery;

    StackPanel CollectControls(Action build)
    {
        var previous = content;
        var collected = new StackPanel { Spacing = 14 };
        content = collected;
        try { build(); }
        finally { content = previous; }
        return collected;
    }

    static ModDisplay ModIdentity(string source)
    {
        var lower = source.ToLowerInvariant();
        if (lower.Contains("/mikumenu")) return new("Miku Menu", "A fresh look for your next lobby.", "#69D5CF", "MI");
        if (lower.Contains("/hydralum")) return new("Hydralum", "Hydra & Malum, brought together.", "#A9ABF6", "HL");
        if (lower.Contains("/hydra")) return new("Hydra", "Utilities for your Among Us setup.", "#70CBA5", "HY");
        if (lower.Contains("/kewl-menu")) return new("Kewl Menu", "A menu with a personality of its own.", "#E5B879", "KW");
        if (lower.Contains("/tenkaimenu")) return new("Tenkai Menu", "Another way to customize your lobby.", "#CE99E4", "TK");
        if (lower.Contains("/dr-hacker")) return new("Dr. Hacker 3", "Keep your menu in easy reach.", "#E69A9D", "DR");
        if (lower.Contains("/malummenu")) return new("Malum Menu", "Your familiar menu, neatly managed.", "#AC9BED", "MA");
        if (lower.Contains("/elysiummodmenu")) return new("Elysium", "A place in your personal collection.", "#8CB9E9", "EL");
        if (lower.Contains("/hypermenu")) return new("Hyper Menu", "Ready when your crew is.", "#D9A2CD", "HP");
        if (lower.Contains("/another-among-us-mod-menus")) return new("Community Collection", "Viper & Vortex · choose a DLL in details.", "#A8CC83", "CC");
        try
        {
            var parsed = Parse(source);
            var name = parsed.Repo.Split('/').Last();
            return new(name, "A source from your own collection.", "#A99CFF", name[..Math.Min(2, name.Length)].ToUpperInvariant());
        }
        catch { return new("Custom source", "Review this source before installing.", "#A99CFF", "+"); }
    }

    static Border Pill(string label, string accent)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = Brush("#222A3E"),
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(label, 10, false, accent)
        };
    }

    Button Primary(string label, Func<Task> action)
    {
        var button = AsyncButton(label, action);
        button.Background = Brush("#7960D8");
        button.BorderBrush = Brush("#9E83F0");
        button.Foreground = Brushes.White;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    Border MenuCard(string source, ModDisplay display)
    {
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var mark = new Border
        {
            Width = 52, Height = 52,
            CornerRadius = new CornerRadius(14),
            Background = Brush("#25263D"),
            BorderBrush = Brush(display.Accent),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = display.Monogram,
                FontSize = 18, FontWeight = FontWeight.Bold,
                Foreground = Brush(display.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        top.Children.Add(mark);
        var host = Pill(source.Contains("gitlab.com") ? "GITLAB" : "GITHUB", "#8F99B6");
        host.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(host, 2); top.Children.Add(host);
        var installed = cfg.Installed.Values.Any(x => x.Repo == source);
        var body = Stack(
            top,
            Text(display.Name, 20, true),
            new TextBlock { Text = display.Subtitle, FontSize = 12, Foreground = Brush("#8F9AB5"), TextWrapping = TextWrapping.Wrap, MinHeight = 36 },
            Pill(installed ? "● In your collection" : "○ Ready to explore", installed ? "#77D4B3" : "#B8A3ED"),
            Primary(installed ? "View release  →" : "View & install  →", () => OpenModDetails(source))
        );
        body.Spacing = 16;
        var card = new Border
        {
            Width = 276, Padding = new Thickness(20), Margin = new Thickness(0, 0, 14, 14),
            Background = Brush("#161B2D"), BorderBrush = Brush("#2C324B"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = body
        };
        if (cfg.Animations)
        {
            card.Transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(150) },
                new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(150) }
            };
        }
        card.PointerEntered += (_, _) => { card.Background = Brush("#1D2339"); card.BorderBrush = Brush(display.Accent); };
        card.PointerExited += (_, _) => { card.Background = Brush("#161B2D"); card.BorderBrush = Brush("#2C324B"); };
        return card;
    }

    async Task AddSourceDialog()
    {
        var input = new TextBox { Watermark = "https://github.com/owner/repository" };
        if (!await Confirm("Add to your collection", "Paste a GitHub or GitLab repository link. A release link stays pinned to that version.", input)) return;
        var source = Normalize(input.Text ?? "");
        if (!cfg.Repos.Contains(source))
        {
            cfg.Repos.Add(source); Save(); Show(1); Log("Added " + ModIdentity(source).Name + ".");
        }
    }

    async Task OpenModDetails(string source)
    {
        var display = ModIdentity(source);
        Release? release = null;
        await Run(async token =>
        {
            Log("Finding release for " + display.Name + "…");
            release = await FetchRelease(source, token);
        });
        if (release == null)
        {
            await Confirm("Could not load this release", status.Text ?? "Try again later.");
            return;
        }
        activeRepo = source;
        activeRelease = release;
        assets = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = release.Assets.Select(x => x.Name + (x.Size > 0 ? "  ·  " + HumanSize(x.Size) : "")).ToArray(),
            PlaceholderText = "No direct DLL assets in this release"
        };
        if (release.Assets.Count > 0) assets.SelectedIndex = 0;
        var window = ToolWindow(display.Name, 650, 650);
        var sourceDetails = Stack(
            Text(source, 12, false, "#A0AAC1"),
            Text(string.IsNullOrWhiteSpace(release.Notes) ? "No release notes supplied." : release.Notes, 12, false, "#A0AAC1")
        );
        var install = Primary("Install selected DLL", async () =>
        {
            window.Close();
            await Install();
        });
        install.IsEnabled = release.Assets.Count > 0;
        var remove = AsyncButton("Remove source", async () =>
        {
            window.Close();
            if (await Confirm("Remove source?", "Remove " + display.Name + " from Downloads? Installed files will not be changed."))
            {
                cfg.Repos.Remove(source); Save(); Show(1);
            }
        });
        var panel = Stack(
            Text(display.Name, 30, true),
            Text(display.Subtitle, 14, false, "#A0AAC1"),
            Row(Pill("RELEASE " + release.Tag, display.Accent), Pill(release.Assets.Count + " DLL ASSETS", "#A0AAC1")),
            Text("Choose your file", 16, true),
            assets,
            Text("Installed to", 11, true, "#818EAD"),
            Text(string.IsNullOrEmpty(cfg.Plugins) ? "Choose BepInEx/plugins in Settings first." : cfg.Plugins, 12, false, "#A0AAC1"),
            Card(Text("Close the game before installing. Only use plugins from sources you trust, in private lobbies with everyone’s consent.", 12, false, "#C5B88F")),
            install,
            new Expander { Header = "Source & release notes", Content = sourceDetails, HorizontalAlignment = HorizontalAlignment.Stretch },
            Row(Button("Back to collection", () => window.Close()), remove)
        );
        panel.Spacing = 18;
        panel.Margin = new Thickness(28);
        window.Content = new ScrollViewer { Content = panel };
        await window.ShowDialog(this);
    }

    void RenderLocalCards()
    {
        if (localGallery == null) return;
        localGallery.Children.Clear();
        if (localPaths.Count == 0)
        {
            localGallery.Children.Add(new Border
            {
                Width = 580, Padding = new Thickness(34), CornerRadius = new CornerRadius(18),
                Background = Brush("#171C30"), BorderBrush = Brush("#303650"), BorderThickness = new Thickness(1),
                Child = Stack(
                    Text("Your collection starts here.", 26, true),
                    Text(string.IsNullOrEmpty(cfg.Plugins) ? "First, choose your BepInEx/plugins folder. Then pick a menu from Downloads." : "No matching menus in this folder. Explore Downloads or try another search.", 14, false, "#99A4C0"),
                    Row(Button("Explore menus  →", () => Show(1)), Button("Choose folder", () => Show(2)))
                )
            });
            return;
        }
        foreach (var path in localPaths)
        {
            var file = path;
            cfg.Installed.TryGetValue(path, out var record);
            var disabled = PluginPaths.Disabled(path);
            var name = PluginPaths.CanonicalName(path);
            name = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            var display = record != null ? ModIdentity(record.Repo) : new ModDisplay(name, "A locally installed plugin.", "#A99CFF", "◇");
            var toggle = Primary(disabled ? "Enable menu" : "Disable menu", async () =>
            {
                SelectLocal(file);
                await Toggle();
            });
            var details = AsyncButton("Manage  ···", () => OpenLocalDetails(file));
            details.HorizontalAlignment = HorizontalAlignment.Stretch;
            details.HorizontalContentAlignment = HorizontalAlignment.Center;
            localGallery.Children.Add(new Border
            {
                Width = 276, Padding = new Thickness(20), Margin = new Thickness(0, 0, 14, 14),
                Background = Brush("#161B2D"), BorderBrush = Brush("#2C324B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Child = Stack(
                    Text(display.Monogram, 28, true, display.Accent),
                    Text(display.Name, 20, true),
                    Text(record?.Version ?? "Local plugin", 12, false, "#98A3BC"),
                    Pill(disabled ? "○ Disabled" : "● Enabled", disabled ? "#A29BBE" : "#7AD4B5"),
                    toggle,
                    details
                )
            });
        }
    }

    void SelectLocal(string path)
    {
        var index = localPaths.IndexOf(path);
        if (index < 0 || locals == null) throw new Exception("File list changed. Refresh your collection.");
        locals.SelectedIndex = index;
    }

    async Task OpenLocalDetails(string path)
    {
        SelectLocal(path);
        var window = ToolWindow("Manage menu", 620, 540);
        var file = new FileInfo(path);
        cfg.Installed.TryGetValue(path, out var record);
        var panel = Stack(
            Text(record != null ? ModIdentity(record.Repo).Name : file.Name, 27, true),
            Text("Everything about this installed file.", 13, false, "#97A2BD"),
            Card(Stack(Text("FILE", 10, true, "#9E8CCC"), Text(file.Name), Text(HumanSize(file.Length) + "  ·  " + (record?.Version ?? "Untracked"), 12, false, "#97A2BD"))),
            new Expander { Header = "File location", Content = Text(path, 12, false, "#97A2BD"), HorizontalAlignment = HorizontalAlignment.Stretch },
            Row(
                AsyncButton("Inspect file", async () => { window.Close(); SelectLocal(path); await InspectSelected(); }),
                AsyncButton("Compare release", async () => { window.Close(); SelectLocal(path); await CompareRecordedVersion(); })
            ),
            AsyncButton("Uninstall · keeps a backup", async () => { window.Close(); SelectLocal(path); await Uninstall(); }),
            Button("Done", () => window.Close())
        );
        panel.Spacing = 18; panel.Margin = new Thickness(28);
        window.Content = new ScrollViewer { Content = panel };
        await window.ShowDialog(this);
    }
}
