using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AutomaticYoutubeVideoDownloaderAndConverter
{
    class MpgrabberSettings
    {
        FileSystemWatcher watcher = new FileSystemWatcher();

        public MpgrabberSettings()
        {
            loadConfig();

            watcher.Changed += (Object obj, FileSystemEventArgs fsinfo) =>
            {
                loadConfig();
            };
        }

        private void loadConfig()
        {
            try
            {
                XElement mpgrabberConfig = XElement.Load("C:\\mpgrabber\\MpgrabberSettings.xml");
                var debugLog = mpgrabberConfig.Elements().Where(xml => xml.Name == "DebugLog");
                var errorLog = mpgrabberConfig.Elements().Where(xml => xml.Name == "ErrorLog");
                var debug = mpgrabberConfig.Elements().Where(xml => xml.Name == "Debug");
                var ipAddress = mpgrabberConfig.Elements().Where(xml => xml.Name == "IpAddress");

                if (debugLog.Any())
                {
                    var x = debugLog.First();

                    if (x.Name == ("DebugLog"))
                    {
                        var fname = x.Element("FileName").Value;
                        DebugFileName = fname.ToString();
                    }
                }
                if (errorLog.Any())
                {
                    var x = errorLog.First();

                    if (x.Name == "ErrorLog")
                    {
                        var fname = x.Element("FileName").Value;
                        ErrorFileName = fname.ToString();
                    }
                }
                if (debug.Any())
                {
                    var x = debug.First();

                    if (x.Name == "Debug")
                    {
                        var dbg = Boolean.Parse(x.Value);
                        Debug = dbg;
                    }
                }
                if (ipAddress.Any())
                {
                    var x = ipAddress.First();

                    if (x.Name == "IpAddress")
                    {
                        var ip = x.Value;
                        IpAddress = ip;
                    }
                }
            }
            catch(Exception err)
            {
                Mpgrabber.LogErrorII("\nError loading conf\n" + err.Message + "\n\n" + err.StackTrace);
            }
        }

        public String DebugFileName { get; set; }
        public String ErrorFileName { get; set; }
        public String IpAddress { get; set; }
        public Boolean Debug { get; set; }
    }
}