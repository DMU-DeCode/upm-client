using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UPM.Core;

namespace UPM.Desktop
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    /// <summary>
    /// 앱 전역 테마(Dark/Light) 전환 및 저장.
    /// App.xaml의 MergedDictionaries에서 Themes/*.xaml 사전을 교체하고, 모든 창의 제목 표시줄 색도 맞춥니다.
    /// 설정 경로: %LocalAppData%/UPM/settings.json
    /// </summary>
    public static class ThemeManager
    {
        private static readonly string _settingsPath = UpmAppSettings.SettingsFilePath;

        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static event EventHandler? ThemeChanged;

        /// <summary>앱 시작 시 1회: 저장된 테마를 적용하고, 이후 열리는 창의 제목 표시줄도 자동으로 맞춥니다.</summary>
        public static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => { if (s is Window w) ApplyTitleBar(w); }));

            Apply(LoadSavedTheme(), save: false);
        }

        public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

        public static void Apply(AppTheme theme, bool save = true)
        {
            var app = Application.Current;
            if (app == null) return;

            var dicts = app.Resources.MergedDictionaries;
            var newDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute)
            };

            var old = dicts.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase));
            if (old != null)
                dicts[dicts.IndexOf(old)] = newDict;
            else
                dicts.Add(newDict);

            Current = theme;

            foreach (Window w in app.Windows)
                ApplyTitleBar(w);

            if (save)
                SaveTheme(theme);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>현재 테마 사전에서 Color 리소스를 꺼냅니다 (LiveCharts 등 코드 측 색상용).</summary>
        public static Color GetColor(string key, Color fallback)
        {
            return Application.Current?.TryFindResource(key) is Color c ? c : fallback;
        }

        #region 제목 표시줄 (Win10 1809+/Win11 immersive dark mode)

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int dark = Current == AppTheme.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPM] 제목 표시줄 테마 적용 실패: {ex.Message}");
            }
        }

        #endregion

        #region 저장/로드

        private sealed class Settings
        {
            public string? Theme { get; set; }
        }

        private static AppTheme LoadSavedTheme()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
                    if (Enum.TryParse<AppTheme>(s?.Theme, ignoreCase: true, out var t))
                        return t;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPM] 테마 설정 로드 실패: {ex.Message}");
            }
            return AppTheme.Dark;
        }

        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings { Theme = theme.ToString() }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPM] 테마 설정 저장 실패: {ex.Message}");
            }
        }

        #endregion
    }
}
