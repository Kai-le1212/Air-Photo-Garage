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
    /// 照片库根目录。首次启动时由用户选择，之后可在设置页更改（更改后需重启应用生效）。
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

    /// <summary>记录异常（带上下文标签），用于排查「静默失败」类问题。</summary>
    public static void LogError(string context, Exception ex) =>
        Log($"ERROR [{context}] {ex.GetType().Name}: {ex.Message}\n{ex}");

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Log("OnLaunched start");
        try
        {
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            UiDispatcher = new DispatcherQueueAdapter(DispatcherQueue);
            Log("DispatcherQueue ready");

            // 先建窗口并激活：首次启动引导需要有效的 XamlRoot 与窗口句柄。
            Window = new MainWindow();
            Log("MainWindow created");
            Window.Activate();
            Log("Window.Activate() called");

            // 首次启动：在创建任何服务之前让用户决定照片存放位置。
            // 必须 await（不能同步阻塞），否则会在 UI 线程上死锁导致窗口全白。
            var firstRunRoot = await RunFirstRunSetupIfNeededAsync();
            if (!string.IsNullOrWhiteSpace(firstRunRoot))
            {
                LibraryRoot = firstRunRoot!;
                try { Directory.CreateDirectory(LibraryRoot); } catch { }
                Log($"FirstRunSetup: LibraryRoot = {LibraryRoot}");
            }
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

            // 服务就绪后再让主窗口进入首页（引导期间先不导航，避免页面拿到未初始化的 VM）
            if (Window is MainWindow mw)
            {
                mw.StartAfterSetup();
            }
            Log("OnLaunched completed");
        }
        catch (Exception ex)
        {
            Log("OnLaunched EXCEPTION: " + ex);
            throw;
        }
    }

    /// <summary>
    /// 首次启动引导：等窗口内容就绪后弹窗，让用户选择照片存放位置。
    /// 返回用户选择的目录；null 表示保持默认（含取消/关闭的情形）。
    /// </summary>
    private static async Task<string?> RunFirstRunSetupIfNeededAsync()
    {
        try
        {
            if (Views.FirstRunSetup.IsCompleted)
            {
                Log("FirstRunSetup: skipped (already completed)");
                return null;
            }

            // 等窗口内容挂载完成，XamlRoot 才可用。
            var xamlRoot = await WaitForXamlRootAsync();
            if (xamlRoot is null)
            {
                Log("FirstRunSetup: XamlRoot unavailable, skipped");
                return null;
            }

            Log("FirstRunSetup: prompting");
            var picked = await Views.FirstRunSetup.PromptAsync(xamlRoot, WindowHandle);
            if (!string.IsNullOrWhiteSpace(picked))
            {
                SaveLibraryRoot(picked!);
                Log($"FirstRunSetup: user chose {picked}");
            }
            else
            {
                Log("FirstRunSetup: user kept default");
            }
            return picked;
        }
        catch (Exception ex)
        {
            // 引导失败不应阻塞启动
            LogError("FirstRunSetup", ex);
            return null;
        }
    }

    /// <summary>
    /// 等待窗口内容就绪（XamlRoot 可用且窗口已完成首次布局），最多约 5 秒。
    /// <c>Window.Activate()</c> 返回时 XamlRoot 可能仍为 null，且此刻直接弹
    /// ContentDialog 会「已显示但不可见」，故需等一次布局完成。
    /// </summary>
    private static async Task<Microsoft.UI.Xaml.XamlRoot?> WaitForXamlRootAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            var root = Window.Content?.XamlRoot;
            if (root is not null && root.Content is not null)
            {
                // 再等一拍，确保首帧已提交，对话框才会正常渲染。
                await Task.Delay(250);
                return root;
            }
            await Task.Delay(100);
        }
        return null;
    }
}
