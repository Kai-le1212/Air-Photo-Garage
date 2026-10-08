using System;
using System.IO;
using System.Threading.Tasks;
using AirPhotoGarage.Services;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;

namespace AirPhotoGarage;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    internal static readonly string DefaultLibraryRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPhotoGarage");

    /// <summary>
    /// 照片库根目录。可通过设置页更改（更改后需重启应用生效）。
    /// 默认：%LocalAppData%\AirPhotoGarage
    /// </summary>
    public static string LibraryRoot { get; private set; } = DefaultLibraryRoot;

    /// <summary>数据库文件路径 = 库根目录下的 library.db。</summary>
    public static string DatabasePath => Path.Combine(LibraryRoot, "library.db");

    /// <summary>库根目录配置文件的路径（固定放在默认目录下，保证总能找到）。</summary>
    private static string LibraryRootConfigPath =>
        Path.Combine(DefaultLibraryRoot, "library-root.txt");

    /// <summary>
    /// 从配置文件载入照片库根目录；不存在或无效时回退到默认目录。
    /// 在 <see cref="OnLaunched"/> 创建服务之前调用。
    /// </summary>
    public static void LoadLibraryRoot()
    {
        try
        {
            if (File.Exists(LibraryRootConfigPath))
            {
                var p = File.ReadAllText(LibraryRootConfigPath).Trim();
                if (!string.IsNullOrWhiteSpace(p))
                {
                    Directory.CreateDirectory(p);
                    LibraryRoot = p;
                    return;
                }
            }
        }
        catch { /* 回退默认 */ }
        LibraryRoot = DefaultLibraryRoot;
        try { Directory.CreateDirectory(LibraryRoot); } catch { }
    }

    /// <summary>
    /// 保存新的照片库根目录到配置（不立即切换运行时实例；调用方应提示重启）。
    /// </summary>
    public static void SaveLibraryRoot(string path)
    {
        var dir = Path.GetDirectoryName(LibraryRootConfigPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(LibraryRootConfigPath, path.Trim());
    }

    public static IDatabaseService Database { get; private set; } = null!;
    public static IExifService Exif { get; private set; } = null!;
    public static IPhotoImportService Importer { get; private set; } = null!;
    public static IAircraftRecognizer Recognizer { get; private set; } = null!;
    public static IUiDispatcher UiDispatcher { get; private set; } = null!;
    public static IAirportCatalogService AirportCatalog { get; private set; } = null!;
    public static IAircraftCatalogService AircraftCatalog { get; private set; } = null!;

    public static GarageViewModel GarageViewModel { get; private set; } = null!;
    public static ImportWizardViewModel ImportWizardViewModel { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPhotoGarage", "app-startup.log");

    static App()
    {
        // 类型构造器：App 类被加载时立即触发。写在 instance 构造器之前会被 JIT 自动调用。
        Log("App.cctor");
    }

    public App()
    {
        Log("App..ctor enter");
        InitializeComponent();
        Log("App..ctor after InitializeComponent");

        UnhandledException += (s, e) =>
        {
            Log("UnhandledException: " + e.Exception);
            try { e.Handled = true; } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log("AppDomain.UnhandledException: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, e) =>
            Log("TaskScheduler.UnobservedTaskException: " + e.Exception);
    }

    public static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Log("OnLaunched start");
        try
        {
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            UiDispatcher = new DispatcherQueueAdapter(DispatcherQueue);
            Log("DispatcherQueue ready");

            // 载入用户配置的照片库根目录（必须在创建 Database / Importer 之前）
            LoadLibraryRoot();
            Log($"LibraryRoot = {LibraryRoot}");

            Database = new DatabaseService(DatabasePath);
            Database.InitializeAsync().GetAwaiter().GetResult();
            Log("Database ready");

            Exif = new ExifService();
            Recognizer = new NullAircraftRecognizer();
            Importer = new PhotoImportService(LibraryRoot, Exif, Recognizer);
            AirportCatalog = new AirportCatalogService();
            AircraftCatalog = new AircraftCatalogService();
            Log("Services ready");

            GarageViewModel = new GarageViewModel(Database, Importer, UiDispatcher);
            ImportWizardViewModel = new ImportWizardViewModel(Importer, UiDispatcher, AirportCatalog, AircraftCatalog);
            SettingsViewModel = new SettingsViewModel();
            Log("ViewModels ready");

            Window = new MainWindow();
            Log("MainWindow created");
            Window.Activate();
            Log("Window.Activate() called");
        }
        catch (Exception ex)
        {
            Log("OnLaunched EXCEPTION: " + ex);
            throw;
        }
    }
}
