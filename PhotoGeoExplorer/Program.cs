using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PhotoGeoExplorer;

internal static class Program
{
    private const uint WindowsAppSdkMajorMinor = 0x00010008;

    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Info("Program.Main starting.");

        try
        {
            Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.Initialize(WindowsAppSdkMajorMinor);
            AppLog.Info("Windows App Runtime bootstrap initialized.");
        }
        catch (BadImageFormatException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (DllNotFoundException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (FileNotFoundException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Windows App SDK bootstrap failed.", ex);
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(args =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = args;
                _ = new App();
            });
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Application.Start failed.", ex);
            throw;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Application.Start failed.", ex);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Application.Start failed.", ex);
            throw;
        }
        finally
        {
            try
            {
                Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.Shutdown();
                AppLog.Info("Windows App Runtime bootstrap shutdown.");
            }
            catch (BadImageFormatException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
            catch (DllNotFoundException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
            catch (FileNotFoundException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
            catch (InvalidOperationException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Error("Windows App SDK bootstrap shutdown failed.", ex);
            }
        }
    }
}
