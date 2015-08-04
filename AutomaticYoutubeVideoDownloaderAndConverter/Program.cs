using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Xml;
using System.IO;
using System.ServiceProcess;

namespace AutomaticYoutubeVideoDownloaderAndConverter
{
    static class Program
    {
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.SetResult(null);
            if (cancellationToken != default(CancellationToken))
                cancellationToken.Register(tcs.SetCanceled);

            return tcs.Task;
        }
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
#if DEBUG // If in debug mode, we want to start the service as if it were a console app.
            Mpgrabber service = new Mpgrabber();
            if (Environment.UserInteractive)
            {
                service.Start(null);
                service.Disposed += delegate
                {

                };
            }
            else
            {
                ServiceBase.Run(service);
                service.Disposed += delegate
                {

                };
            }
#else    // If not in debug mode, attempt to start service as usual
            ///////////////////////////////////
            ///////////////////////////////////
            StartReleaseMode();
            ///////////////////////////////////
            ///////////////////////////////////
#endif

        }

        private static void StartReleaseMode()
        {
            try
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[] 
                { 
                     new Mpgrabber()
                };
                Mpgrabber.LogDebugII("\nAbout to run service....");
                ServiceBase.Run(ServicesToRun);
            }
            catch (Exception err)
            {
                Mpgrabber.LogErrorII("\n" + err.Message + "\n\n" + err.StackTrace);
            }
        }
    }
}