using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PhotoGeoExplorer;

internal static partial class Program
{
    private const uint WindowsAppSdkMajorMinor = 0x00010008;
    private const int AppModelErrorNoPackage = 15700;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Reset();
        AppLog.Info($"Program.Main starting. SessionId: {AppLog.CurrentSessionId}");

        var isPackaged = IsRunningAsPackagedApp();
        AppLog.Info($"Running as packaged app: {isPackaged}");

        var bootstrapInitialized = false;

        // MSIX パッケージ（Store 版）では Bootstrap は不要（呼ぶとエラーになる）
        // 非パッケージ（MSI 版）でのみ Bootstrap を実行
        if (!isPackaged)
        {
            try
            {
                Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.Initialize(WindowsAppSdkMajorMinor);
                bootstrapInitialized = true;
                AppLog.Info("Windows App Runtime bootstrap initialized.");
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or
                DllNotFoundException or
                EntryPointNotFoundException or
                FileNotFoundException or
                InvalidOperationException or
                System.Runtime.InteropServices.COMException or
                UnauthorizedAccessException)
            {
                AppLog.Error("Windows App SDK bootstrap failed. Application cannot continue.", ex);
                ShowFatalErrorDialog(
                    "起動エラー / Startup Error",
                    "Windows App SDK の初期化に失敗しました。\n" +
                    "アプリケーションを起動できません。\n\n" +
                    "Windows App SDK initialization failed.\n" +
                    "The application cannot start.\n\n" +
                    $"詳細 / Details: {ex.Message}");
                return;
            }
        }

        try
        {
            AppLog.Info("Calling Application.Start.");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(startArgs =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = startArgs;
                _ = new App();
            });
            AppLog.Info("Application.Start returned.");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.Runtime.InteropServices.COMException or
            UnauthorizedAccessException)
        {
            AppLog.Error("Application.Start failed.", ex);
            throw;
        }
        finally
        {
            AppLog.Info("Program.Main entering shutdown sequence.");
            // Bootstrap が成功した場合のみ Shutdown を呼ぶ
            if (bootstrapInitialized)
            {
                try
                {
                    Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.Shutdown();
                    AppLog.Info("Windows App Runtime bootstrap shutdown.");
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException or
                    DllNotFoundException or
                    EntryPointNotFoundException or
                    FileNotFoundException or
                    InvalidOperationException or
                    UnauthorizedAccessException)
                {
                    AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
                }
            }

            AppLog.Info("Program.Main finished.");
        }
    }

    /// <summary>
    /// MSIX パッケージとして実行されているかどうかを判定します。
    /// Win32 API を使用した堅牢な判定方法です。
    /// Package.Current による判定は特定の環境で不安定なため使用しません。
    /// </summary>
    private static bool IsRunningAsPackagedApp()
    {
        try
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);

            // APPMODEL_ERROR_NO_PACKAGE (15700) が返された場合は非パッケージ
            return result != AppModelErrorNoPackage;
        }
        catch (DllNotFoundException ex)
        {
            // P/Invoke 失敗時は安全側（非パッケージ扱い）に倒す
            AppLog.Error("Failed to determine package status via Win32 API.", ex);
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            AppLog.Error("Failed to determine package status via Win32 API.", ex);
            return false;
        }
    }

    /// <summary>
    /// 致命的なエラーダイアログを表示します。
    /// WinUI/XAML が初期化されていない状態でも表示できるよう Win32 MessageBox を使用します。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MessageBox failure should not propagate")]
    private static void ShowFatalErrorDialog(string title, string message)
    {
        try
        {
            _ = MessageBox(IntPtr.Zero, message, title, 0x10); // MB_ICONERROR
        }
        catch (Exception ex)
        {
            // MessageBox すら失敗した場合はログのみ
            AppLog.Error("Failed to show fatal error dialog.", ex);
        }
    }
}
