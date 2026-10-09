using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core;
using Typedown.Core.Utilities;
using Typedown.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Typedown
{
    public partial class App : Application
    {
        // Named after the instance (Config.InstanceName) - the application's own name, or the test host's, which is tied
        // to its data root.
        private static readonly Mutex mutex = new(true, Config.InstanceName + ".Mutex");

        private App()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                // Before the XAML exception hook exists: written down here, or a start that fails leaves no trace.
                Log.WriteLocal("AppConstruct", ex.ToString());
                throw;
            }
        }

        public static void Launch()
        {
            try
            {
                if (mutex.WaitOne(TimeSpan.Zero, true))
                {
                    LaunchNewApplication();
                }
                else
                {
                    OpenNewWindow();
                }
            }
            catch (AbandonedMutexException)
            {
                mutex.ReleaseMutex();
                Launch();
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", ExactSpelling = true)]
        private static extern bool IsUserAnAdmin();

        private static bool IsElevated()
        {
            try { return IsUserAnAdmin(); } catch { return false; }
        }

        public static void LaunchNewApplication()
        {
            Start(callback =>
            {
                // Awaits come back to the window thread, as they did on XAML Islands' CoreDispatcher.
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            });
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs launchArgs)
        {
            Log.Debug($"startup: version={Core.Controls.AboutApp.GetAppVersion()} windowsBuild={Config.WindowsBuild} osVersion={Environment.OSVersion.VersionString} mica={Config.IsMicaSupported} packaged={Config.IsPackaged} elevated={IsElevated()} exe={AppDomain.CurrentDomain.BaseDirectory}");
            try
            {
                // Logged and handled: an exception out of a click handler or an async void method ended the process,
                // with whatever was not saved yet. Out of memory is left to end it.
                Current.UnhandledException += (_, e) =>
                {
                    Log.WriteLocal("XamlUnhandledException", $"{e.Message}\n{e.Exception}");
                    if (e.Exception is not OutOfMemoryException) e.Handled = true;
                };
            }
            catch (Exception ex)
            {
                Log.WriteLocal("XamlUnhandledExceptionHook", ex.ToString());
            }
            if (!await EnvCheck.EnsureWebView2Installed())
            {
                Exit();
                return;
            }
            var window = new MainWindow();
            window.Start();
            ListenPipe(window.DispatcherQueue);
            // Local automation: nothing listens until the setting is turned on (docs/automation-api-spec.md, 5.1).
            Services.Automation.AutomationService.Initialize();
        }

        private static async void ListenPipe(DispatcherQueue dispatcher)
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Config.InstanceName + ".PiPe", PipeDirection.InOut);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    using var writer = new StreamWriter(server);
                    var args = (await reader.ReadLineAsync()).Split("\0");
                    Log.Debug($"another launch handed its arguments to this process: {string.Join(" ", args.Skip(1))}");
                    IntPtr handle;
                    try
                    {
                        handle = await dispatcher.RunIdleAsync(() => Utilities.Common.OpenNewWindow(args));
                    }
                    catch (Exception ex)
                    {
                        // A process that cannot open a window any more (its files replaced under it by an
                        // installer, say) must not keep the mutex: the launch that asked falls back to starting
                        // afresh, and this one goes.
                        Log.Debug($"could not open a window for another launch, exiting: {ex}");
                        try { await writer.WriteLineAsync("0"); await writer.FlushAsync(); } catch { }
                        Environment.Exit(1);
                        return;
                    }
                    await writer.WriteLineAsync(handle.ToString());
                    await writer.FlushAsync();
                }
                catch (Exception)
                {
                    await Task.Delay(1000);
                }
            }
        }

        internal static void OpenNewWindow()
        {
            // Another process holds the mutex: ask it for a window. Bounded waits throughout — a process that
            // holds the mutex but answers nothing (headless, hung, its files replaced) used to leave this
            // launch waiting invisibly, which reads as "clicked the icon and no window came".
            try
            {
                using var client = new NamedPipeClientStream(".", Config.InstanceName + ".PiPe", PipeDirection.InOut);
                client.Connect(3000);
                using var reader = new StreamReader(client);
                using var writer = new StreamWriter(client);
                writer.WriteLine(string.Join("\0", Config.StripHostArguments(Environment.GetCommandLineArgs())));
                writer.Flush();
                var reply = reader.ReadLineAsync();
                if (reply.Wait(TimeSpan.FromSeconds(10)) && long.TryParse(reply.Result, out var handle) && handle != 0)
                {
                    PInvoke.SetForegroundWindow((nint)handle);
                    return;
                }
                Log.Debug("the running instance did not produce a window in time; starting afresh");
            }
            catch (Exception ex)
            {
                Log.Debug($"could not hand over to the running instance ({ex.GetType().Name}); starting afresh");
            }
            LaunchNewApplication();
        }
    }
}
