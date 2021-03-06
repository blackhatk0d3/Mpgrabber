﻿using System;
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
    class Mpgrabber : ServiceBase
    {
        MpgrabberSettings setting = new MpgrabberSettings();
        String proxyjs = File.ReadAllText("C:\\mpgrabber\\proxyjs.js");

        public struct PlaylistData
        {
            public String link;
            public String name;
            public String length;
            public Boolean protectedVid;
        }

        private String createId()
        {
            return Guid.NewGuid().ToString().Replace("-", "");
        }

        private async Task<bool> waitForDataToBecomeAvailable(int numofmillisecondstowait, Socket youtubeSock)
        {
            return await Task<bool>.Run(() =>
            {
                var startTime = DateTime.Now;
                var i = 0;
                TimeSpan totTime;

                while (youtubeSock.Available == 0 && i++ < 150000)
                {
                    //System.Threading.Thread.Sleep(1);

                    if ((totTime = DateTime.Now.Subtract(startTime)).TotalMilliseconds > numofmillisecondstowait)
                    {
                        if (youtubeSock.Connected)
                        {
                            youtubeSock.Shutdown(SocketShutdown.Both);
                        }

                        youtubeSock.Close();
                        youtubeSock.Dispose();
                        LogError("\nwaitForDataToBecomeAvailable() - Dropping http request. Request data took more than 1 sec to send data.\n");

                        return false;
                    }
                };

                return true;
            });
        }

        private async Task<String> httpRequest(Byte method, String headerAndBodyRaw, String link)
        {
            Socket youtubeSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Byte[] requbytes = new Byte[8192];
            Int32 recvd = -1;
            String request = "";
            String headerAndBody = ((method == 1) ? "GET /" : "POST /") + link + headerAndBodyRaw.Substring(headerAndBodyRaw.IndexOf("HTTP/1.1") - 1);

            try
            {
                await Task.Factory.FromAsync(youtubeSock.BeginConnect, youtubeSock.EndConnect, "www.youtube.com", 80, null);

                if (youtubeSock.Connected)
                {
                    var buffer = Encoding.ASCII.GetBytes(headerAndBodyRaw);
                    int bytessent = await Task.Factory.FromAsync<int>(youtubeSock.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, null, youtubeSock), youtubeSock.EndSend);

                    if (await waitForDataToBecomeAvailable(2000, youtubeSock))
                    {
                        recvd = youtubeSock.Receive(requbytes);
                        request = Encoding.ASCII.GetString(requbytes).Substring(0, recvd);
                    }

                    youtubeSock.Dispose();
                }

                return request;
            }
            catch (Exception err)
            {
                LogError("\nhttpRequest() - Error: " + err.Message + "\n\n" + err.StackTrace + "\n" + headerAndBodyRaw);
            }

            return "";
        }

        //private async Task convrt

        private async Task processCmd(Socket accSock)
        {
            try
            {
                WebClient client = new WebClient();
                String request = "";
                String youtubeLink = "";
                StringBuilder responseHeaders = new StringBuilder("HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\nContent-type: text/html\r\n");
                Byte[] requbytes = new Byte[8192];
                List<PlaylistData> youtubeLinks = new List<PlaylistData>();
                Int32 recvd = -1;
                String id = null;
                String respStr = "";
                String remoteip = ((accSock.RemoteEndPoint is IPEndPoint) ? ((IPEndPoint)accSock.RemoteEndPoint).Address.ToString() : null);
                bool error = false;
                int start = 0;
                int len = 0;

                if (!String.IsNullOrEmpty(remoteip))
                    LogHttpReq("\nIpaddress: " + remoteip + "\n");

                if (!await waitForDataToBecomeAvailable(2000, accSock))
                    return;

                recvd = accSock.Receive(requbytes);
                request = Encoding.ASCII.GetString(requbytes).Substring(0, recvd);

                LogHttpReq("\n" + request + "\n");

                if (request.IndexOf("/utube*") > -1)
                {
                    id = createId();
                    start = request.IndexOf("*") + 1;

                    if (request.IndexOf('|', start) > -1)
                        len = request.IndexOf('|', start) - start;
                    else
                        len = request.IndexOf("%7C", start) - start;

                    youtubeLink = request.Substring(start, len);

                    if (youtubeLink.IndexOf("&") > -1 || youtubeLink.IndexOf("playlist?list=") > -1)
                    {
                        if (youtubeLink.IndexOf("list=") > -1)
                        {
                            if (youtubeLink.IndexOf("&list=") > 1)
                                youtubeLinks = await CollectLinkFromPlaylist(youtubeLink, true);
                            else
                                youtubeLinks = await CollectLinkFromPlaylist(youtubeLink);

                            if (!youtubeLinks.Any())
                            {
                                youtubeLink = youtubeLink.Substring(0, youtubeLink.IndexOf("&"));
                            }
                        }
                        else
                        {
                            youtubeLink = youtubeLink.Substring(0, youtubeLink.IndexOf("&"));
                        }
                    }
                    else if (youtubeLink.IndexOf("youtu.be/") > -1)
                    {
                        youtubeLink = "http://www.youtube.com/watch?v=" + youtubeLink.Substring(youtubeLink.IndexOf("e/") + 2);
                    }
                    else if (youtubeLink.IndexOf("/watch?v=") == -1)
                    {
                        error = true;
                    }

                    if (youtubeLinks.Any())
                    {
                        StringBuilder execcode = new StringBuilder("links = [");
                        PlaylistData data;

                        youtubeLinks.Reverse();

                        for (var i = 0; i < youtubeLinks.Count; i++)
                        {
                            data = youtubeLinks[i];

                            execcode.Append("{ link: '");
                            execcode.Append(data.link);
                            execcode.Append("', name: '");
                            execcode.Append(data.name.Replace("\n", "").Replace("\t", ""));
                            execcode.Append("' },");
                        }

                        execcode.Remove((execcode.Length - 1), 1);
                        execcode.Append("]; startProc(links.pop(), true);");

                        responseHeaders.Append("Content-Length: ");
                        responseHeaders.Append(execcode.Length);
                        responseHeaders.Append("\r\n\r\n");

                        accSock.Send(Encoding.ASCII.GetBytes(responseHeaders.ToString() + execcode));
                        youtubeLinks.Clear();
                    }
                    else if (!error)
                    {
                        String filetomove = "";
                        String arguments_str = " -o %(title)s" + id + ".%(ext)s";

                        if (request.IndexOf("mp3") > -1)
                            arguments_str += " -x -f 18 --prefer-ffmpeg --audio-format \"mp3\"";
                        else
                            arguments_str += " -f mp4 --recode-video mp4 ";

                        LogDebug("\nyoutube-dl " + arguments_str + "\n");

                        Process youtubedl = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = "youtube-dl.exe",
                            WorkingDirectory = "C:\\temp\\",
                            Arguments = " " + youtubeLink + arguments_str,
                            CreateNoWindow = false
                        });

                        await Program.WaitForExitAsync(youtubedl);
                        youtubedl.Dispose();

                        filetomove = System.IO.Directory.GetFiles("C:\\temp\\", "*" + id + "*").FirstOrDefault();

                        if (System.IO.File.Exists(filetomove))
                        {
                            //WRITE CODE TO CHECK IF ITS BOTH MP3 AND MP4. IF SO ALTER JSCRIPT TO DOWNLOAD BOTJ FILES. ALSO, ADD
                            //FFMPEG CODE TO MAKE MP4 AN MP3. EX: ffmpeg.exe -i song.mp4 -b:a 192K -vn song2.mp3
                            System.IO.File.Move(filetomove, "C:\\temp\\music\\" + filetomove.Replace("C:\\temp\\", ""));
                            respStr = "if(!window.open(\"http://\" + ip + \":8080/*" + id + "|\")) { document.getElementById('error').innerHTML = ('<font color=\"red\"><strong>Please enable your popup blocker for mpgrabber.com</strong></font>'); window.location.href = \"http://\" + ip + \":8080/*" + id + "|\"; converting = false; } setTimeout(continueProcessing, 500);";
                            responseHeaders.Append("Content-Length: ");
                            responseHeaders.Append(respStr.Length);
                            responseHeaders.Append("\r\n\r\n");
                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders + respStr));
                        }
                        else
                        {
                            respStr = "document.getElementById('error').innerHTML = ('Error...no file to download'); setTimeout(continueProcessing, 500);";
                            responseHeaders.Append("Content-Length: ");
                            responseHeaders.Append(respStr.Length);
                            responseHeaders.Append("\r\n\r\n");
                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders + respStr));
                        }
                    }
                    else
                    {
                        respStr = "document.getElementById('error').innerHTML = ('Error...There's an issue with the link you are trying to convert. Check the link and resubmit please'); setTimeout(continueProcessing, 500);";
                        responseHeaders.Append("Content-Length: ");
                        responseHeaders.Append(respStr.Length);
                        responseHeaders.Append("\r\n\r\n");
                        accSock.Send(Encoding.ASCII.GetBytes(responseHeaders + respStr));
                    }

                    accSock.Shutdown(SocketShutdown.Both);
                    accSock.Close();
                }
                else
                {
                    if (request.IndexOf("identify*") > -1)
                    {
                        PlaylistData data;

                        start = request.IndexOf("*") + 1;

                        if (request.IndexOf('|', start) > -1)
                            len = request.IndexOf('|', start) - start;
                        else
                            len = request.IndexOf("%7C", start) - start;

                        youtubeLink = request.Substring(start, len);

                        if (youtubeLink.IndexOf("youtu.be/") > -1)
                        {
                            youtubeLink = "http://www.youtube.com/watch?v=" + youtubeLink.Substring(youtubeLink.IndexOf("e/") + 2);
                        }

                        data = await identifyDataFromLink(youtubeLink);

                        if (!String.IsNullOrEmpty(data.name))
                        {
                            respStr = "songtitle = '" + data.name + "'; video_length = " + data.length;
                            responseHeaders.Append("Content-Length: ");
                            responseHeaders.Append(respStr.Length);
                            responseHeaders.Append("\r\n\r\n");
                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders + respStr));
                            accSock.Shutdown(SocketShutdown.Both);
                            accSock.Close();
                        }
                        else
                        {
                            responseHeaders = responseHeaders.Replace("200", "504");
                            responseHeaders.Append("Content-Length: 0\r\n\r\n");
                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders + respStr));
                            accSock.Shutdown(SocketShutdown.Both);
                            accSock.Close();
                        }
                    }
                    else
                    {
                        List<Byte> resp = new List<Byte>();
                        String file = null;
                        String realFile = null;
                        FileInfo fi = null;

                        start = request.IndexOf("*") + 1;
                        len = request.IndexOf("%7C", start) - start;
                        id = request.Substring(start, len);
                        file = System.IO.Directory.GetFiles("C:\\temp\\music\\", "*" + id + "*").FirstOrDefault();
                        realFile = file.Replace("C:\\temp\\music\\", "");
                        fi = new DirectoryInfo("C:\\temp\\music\\").GetFiles().FirstOrDefault(f => f.Name.Equals(realFile));

                        responseHeaders = new StringBuilder("HTTP/1.1 200 OK\r\n");
                        responseHeaders.Append("Access-Control-Allow-Origin: *\r\n");
                        responseHeaders.Append("Content-Type: application/octet-stream\r\n");
                        responseHeaders.Append("Content-Length: " + fi.Length + "\r\n");
                        responseHeaders.Append("Content-Disposition: attachment; filename=" + file.Replace("C:\\temp\\music\\", "").Replace(id, "") + "\r\n\r\n");

                        resp.AddRange(Encoding.ASCII.GetBytes(responseHeaders.ToString()));
                        resp.AddRange(System.IO.File.ReadAllBytes(file));

                        accSock.Send(resp.ToArray());
                        accSock.Shutdown(SocketShutdown.Both);
                        accSock.Close();
                        accSock.Dispose();
                    }
                }
            }
            catch (Exception err)
            {
                LogError("\nCall Error: " + err.Message + "\n\n" + err.StackTrace + "\n");
            }
        }

        static public void LogErrorII(String logmsg)
        {
            try
            {
                System.IO.File.AppendAllText("C:\\mpgrabber\\errorlog.log", DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
            }
            catch { }
        }

        private void LogError(String logmsg)
        {
            try
            {
                System.IO.File.AppendAllText(setting.ErrorFileName, DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
            }
            catch
            {
                try
                {
                    System.IO.File.AppendAllText("C:\\mpgrabber\\errorlog.log", DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
                }
                catch { }
            }
        }

        private void LogHttpReq(String logmsg)
        {
            try
            {
                System.IO.File.AppendAllText(setting.HttpRequestFileName, DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
            }
            catch
            {  }
        }

        private void LogDebug(String logmsg)
        {
            if (setting.Debug)
            {
                try
                {
                    System.IO.File.AppendAllText(setting.DebugFileName, DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
                }
                catch
                {
                    //System.IO.File.AppendAllText("C:\\mpgrabber\\lawg.log", logmsg);
                }
            }
        }

        public static void LogDebugII(String logmsg)
        {
            try
            {
                System.IO.File.AppendAllText("C:\\mpgrabber\\debuglog.log", DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "  " + logmsg);
            }
            catch
            {
                //System.IO.File.AppendAllText("C:\\mpgrabber\\lawg.log", logmsg);
            }
        }

        private async Task<PlaylistData> identifyDataFromLink(String link)
        {
            PlaylistData plist = new PlaylistData();

            try
            {
                WebClient client = new WebClient();
                String html = "";
                var xhtml = new HtmlAgilityPack.HtmlDocument();

                html = await client.DownloadStringTaskAsync(link);
                xhtml.LoadHtml(html);

                if (link.IndexOf("&list") > -1 || link.IndexOf("playlist") > -1)
                {
                    var div = xhtml.DocumentNode.DescendantNodes().Where(h => h.Name == "div" && h.Attributes.Any(attr => attr.Value == "playlist-header-content")).FirstOrDefault();

                    if(div != null)
                    {
                        plist.name = div.Attributes.FirstOrDefault(a => a.Name == "data-list-title").Value;
                    }
                }
                else
                {
                    var span = xhtml.DocumentNode.DescendantNodes().Where(h => h.Name == "span" && h.Attributes.Any(attr => attr.Value.Equals("eow-title"))).FirstOrDefault();

                    if (span != null)
                        plist.name = span.InnerHtml.Replace("\n", "").TrimStart().TrimEnd();
                }

                plist.length = "0";
                client.Dispose();

                return plist;
            }
            catch (Exception err)
            {
                LogError("Error: identifySongFromLink() - " + err.StackTrace + "\n\n" + err.Message + "\nlink: " + link);
                return plist;
            }
        }

        public async Task<List<PlaylistData>> CollectLinkFromPlaylist(String link, bool convertLink = false)
        {
            List<PlaylistData> links = new List<PlaylistData>(200);

            try
            {
                WebClient client = new WebClient();
                String html = "";
                var xhtml = new HtmlAgilityPack.HtmlDocument();

                if (convertLink && link.IndexOf("list=") > -1)
                {
                    int index = 0;

                    index = link.IndexOf("list=");
                    link = link.Substring(index);

                    if (link.IndexOf("&") > -1)
                    {
                        index = link.IndexOf("&");
                        link = link.Substring(0, index);
                    }

                    link = "https://www.youtube.com/playlist?" + link;
                }

                html = await client.DownloadStringTaskAsync(link);
                xhtml.LoadHtml(html);

                var a = xhtml.DocumentNode.DescendantNodes().Where(h => h.Name == "a").ToList();

                foreach (HtmlAgilityPack.HtmlNode nodes in a)
                {
                    List<HtmlAgilityPack.HtmlAttribute> attrs = nodes.Attributes.ToList();
                    HtmlAgilityPack.HtmlAttribute attr = attrs.Find(n => n.Name.Equals("class"));

                    if (attr != null && (attr.Value.Equals("pl-video-title-link yt-uix-tile-link yt-uix-sessionlink  spf-link ")))
                    {
                        links.Add(new PlaylistData()
                        {
                            link = "http://www.youtube.com" + attrs.Find(n => n.Name.Equals("href")).Value,
                            name = nodes.InnerHtml
                        });
                    }
                }
            }
            catch (Exception err)
            {
                LogError("\nError in CollectLinkFromPlaylist method: " + err.Message + "\n\n" + err.StackTrace);
            }

            return links;
        }

        private async Task proxy(Socket accSock)
        {
            WebClient client = new WebClient();
            HtmlAgilityPack.HtmlDocument xhtml = new HtmlAgilityPack.HtmlDocument();
            Byte[] requbytes = new Byte[8192];
            Byte[] image = null;
            List<Byte> respbs = new List<Byte>();
            List<Byte> requbs = new List<Byte>();
            List<PlaylistData> plistdata = new List<PlaylistData>(); 
            Int32 recvd = -1;
            Int32 start = -1;
            Int32 len = -1;
            String remoteip = ((accSock.RemoteEndPoint is IPEndPoint) ? ((IPEndPoint)accSock.RemoteEndPoint).Address.ToString() : null);
            //StringBuilder responseHeaders = new StringBuilder("HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\nContent-type: text/html\r\n");
            StringBuilder responseHeaders = new StringBuilder("HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\n");
            String link = "";
            String request = "";
            String html = "";
            Boolean overrideResp = false;

            try
            {
                if (!String.IsNullOrEmpty(remoteip))
                    LogHttpReq("\nIpaddress: " + remoteip + "\n");

                if (await waitForDataToBecomeAvailable(2000, accSock))
                {
                    do
                    {
                        recvd = accSock.Receive(requbytes); //Allow for biiger msgs than 8k to be returned
                        request += Encoding.ASCII.GetString(requbytes).Substring(0, recvd);
                        requbs.AddRange(requbytes);
                    }
                    while (accSock.Available > 0);

                    if ((start = request.IndexOf("/proxySrch*")) > -1)
                    {
                        if ((start = request.IndexOf('*')) > -1)
                        {
                            if ((len = request.IndexOf("%7C")) > -1)
                            {
                                link = request.Substring(start+1, (len - start -1));
                                html = await client.DownloadStringTaskAsync(string.Format("https://www.youtube.com/results?search_query={0}", link).Replace(" ", "+"));
                                xhtml.LoadHtml(html);

                                if (xhtml.DocumentNode != null)
                                {
                                    if (xhtml.DocumentNode.OuterHtml != null)
                                    {
                                        foreach (var node in xhtml.DocumentNode.DescendantNodes().Where(h => h.Attributes != null && h.Attributes.Any(attr => attr.Value.Equals("yt-lockup-dismissable"))))
                                        {
                                            foreach (var subnode in node.DescendantNodes())
                                            {
                                                PlaylistData tmp = new PlaylistData();
                                                Boolean abandonsearch = false;
                                                //FIX ALL ALGORITHMS SO IT LOOPS 1NCE THRU EVERYTHING
                                                if(subnode.Attributes.Any(attr => attr.Value == "yt-uix-sessionlink yt-uix-tile-link yt-ui-ellipsis yt-ui-ellipsis-2       spf-link "))
                                                {
                                                    if (node.DescendantNodes().FirstOrDefault(span => span.Attributes.Any(attr => attr.Value == "yt-uix-tooltip yt-channel-title-icon-verified yt-sprite")) != null)
                                                    {
                                                        continue;
                                                    }

                                                    tmp.name = subnode.InnerHtml;   
                                                    tmp.link = subnode.Attributes["href"].Value;

                                                    foreach (var subnodeII in node.ChildNodes[0].ChildNodes[0].ChildNodes)
                                                    {
                                                        if(subnodeII.Name == "span")
                                                        {
                                                            if (subnodeII.InnerHtml.IndexOf(":") > -1)
                                                            {
                                                                tmp.length = subnodeII.InnerHtml;
                                                                break;
                                                            }     
                                                            else
                                                            {
                                                                abandonsearch = true;
                                                                break;
                                                            }
                                                        }

                                                        if (subnodeII.Name == "div" && subnodeII.FirstChild != null && subnodeII.FirstChild.FirstChild != null && subnodeII.FirstChild.FirstChild.Attributes.Any(attr => attr.Name == "src"))
                                                        {
                                                            tmp.protectedVid = (subnodeII.FirstChild.FirstChild.Attributes["src"].Value.IndexOf(".jpg") > -1);
                                                        }
                                                    }

                                                    if(abandonsearch)
                                                    {
                                                        abandonsearch = false;
                                                        continue;
                                                    }

                                                    if(!String.IsNullOrEmpty(tmp.length))
                                                    {
                                                        plistdata.Add(tmp);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if(plistdata.Any())
                        {
                            StringBuilder execcode = new StringBuilder("plistdata = [");
                            PlaylistData data;

                            for (var i = 0; i < plistdata.Count; i++)
                            {
                                data = plistdata[i];

                                execcode.Append("{ link: '");
                                execcode.Append(data.link);
                                execcode.Append("', name: '");
                                execcode.Append(data.name.Replace("\n", "").Replace("\t", ""));                             
                                execcode.Append("', length: '");
                                execcode.Append(data.length.Replace("\n", "").Replace("\t", ""));
                                execcode.Append("', protectedVid: ");
                                execcode.Append(data.protectedVid.ToString().ToLower());
                                execcode.Append(" },");
                            }

                            execcode.Remove((execcode.Length - 1), 1);
                            execcode.Append("]; showData();");

                            responseHeaders.Append("Content-Length: ");
                            responseHeaders.Append(execcode.Length);
                            responseHeaders.Append("\r\n\r\n");

                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders.ToString() + execcode));
                            return;
                        
                        }
                    }
                    else if ((start = request.IndexOf("/proxy*")) > -1)
                    {
                        if (request.IndexOf("referer") < start)
                        {
                            String referer = "";

                            start += 7;

                            if (request.IndexOf("proxy**") > -1)
                            {
                                start++;
                                len = (request.IndexOf("HTTP/1.1") - 13);
                            }
                            else if (request.IndexOf('|', start) > -1)
                                len = request.IndexOf('|', start) - start;
                            else if (request.IndexOf("%7C", start) > -1)
                                len = request.IndexOf("%7C", start) - start;

                            link = request.Substring(start, len);

                           var begin = request.IndexOf("Referer: ") + "Referer: ".Length;
                            var end = request.IndexOf('\n', begin);

                            referer = request.Substring(begin, (end - begin - 1));
                           request = request.Replace(referer, "http://www.youtube.com");
                        }
                        else
                        {
                            start = request.IndexOf(":8083/") + 6;
                            len = request.IndexOf(" HTTP/1.1") - start;
                            link = request.Substring(start, len);
                        }
                    }
                    else
                    {
                        start = 4;
                        len = request.IndexOf(" HTTP/1.1") - start;
                        link = request.Substring(start, len);
                    }

                    if (link.IndexOf("://") < 0)
                    {
                        link = "http://www.youtube.com" + link;
                        link = link.Replace(" ", "");
                    }

                    LogHttpReq(request);

                    if (request.IndexOf("text") > -1 && request.IndexOf("Accept: image") == -1 || ((request.IndexOf("Accept") == -1 || request.IndexOf("Accept: */*") > -1) && request.IndexOf("Content-type") == -1))
                    {
                        ServicePointManager.ServerCertificateValidationCallback = delegate
                        {
                            return true;
                        };

                        if (request.IndexOf("GET ") == 0)
                        {
                            //if (request.IndexOf("video/webm") > -1)
                                //request = request.Replace("video/webm", "audio/mp4");

                            //client.UseDefaultCredentials = true;                            
                            {
                                var headers = request.Split(new string[] 
                                {
                                    "\r\n"
                                }, 100, StringSplitOptions.None);

                                //for(var i = 1; i < headers.) parse headers and to client.headers

                                html = await client.DownloadStringTaskAsync(link);
                                //html = await httpRequest(accSock, 1, request, link);

                                if (html.IndexOf("//s.ytimg.com/yts/jsbin/html5player-new-en_US-vflSnomqH/html5player-new.js") > -1)
                                {
                                    overrideResp = true;
                                    html = html.Replace("//s.ytimg.com/yts/jsbin/html5player-new-en_US-vflSnomqH/html5player-new.js", "https://" + setting.IpAddress + ":8083/proxy**http://s.ytimg.com/yts/jsbin/html5player-new-en_US-vflSnomqH/html5player-new.js");
                                }

                                if (html.IndexOf("//s.ytimg.com/yts/jsbin/spf-vflVcVpX_/spf.js") > -1)
                                {
                                    overrideResp = true;
                                    html = html.Replace("//s.ytimg.com/yts/jsbin/spf-vflVcVpX_/spf.js", "https://" + setting.IpAddress + ":8083/proxy**http://s.ytimg.com/yts/jsbin/spf-vflVcVpX_/spf.js?");
                                }

                                if (html.IndexOf("//s.ytimg.com/yts/jsbin/www-en_US-vflENJpqL/base.js") > -1)
                                {
                                    overrideResp = true;
                                    html = html.Replace("//s.ytimg.com/yts/jsbin/www-en_US-vflENJpqL/base.js", "https://" + setting.IpAddress + ":8083/proxy**http://s.ytimg.com/yts/jsbin/www-en_US-vflENJpqL/base.js");
                                }

                                if (link.IndexOf("html5player-new.js") > -1 || link.IndexOf("base.js") > -1 || link.IndexOf("spf.js") > -1)
                                {
                                    overrideResp = true;
                                    html = html.Replace("l.send(d)", "l.send('http://" + setting.IpAddress + ":8083/proxy*' + d + '|')");
                                    html = html.Replace("this.j.open(\"GET\",a);", "this.j.open(\"GET\", 'http://" + setting.IpAddress + ":8083/proxy*' + a + '|');");
                                }

                                if (request.IndexOf("GET /proxy*http://www.youtube.com") > -1 || request.IndexOf("GET /results?search_query=") > -1)
                                {
                                    html = html.Replace("</body></html>", "");
                                    html += proxyjs;
                                }

                                xhtml.LoadHtml(html);
                            }
                        }
                        else
                        {
                            String postData = request.Substring(request.IndexOf("\r\n\r\n")+4);

                            LogDebug("link for POST call..." + link + " and POST data " + postData);

                            html = client.UploadString(link, postData);
                            xhtml.LoadHtml(html);
                        }
                    }

                    if (xhtml.DocumentNode != null)
                    {
                        if (xhtml.DocumentNode.OuterHtml != null)
                        {
                            foreach (var node in xhtml.DocumentNode.DescendantNodes().Where(h => (h.Name == "a") || h.Name == "link"))
                            {
                                foreach (var attr in node.Attributes.Where(h => h.Name == "href"))
                                {
                                    if (attr.Value.IndexOf("http://") == -1 && attr.Value.IndexOf("https://") == -1)
                                        if (attr.Value.IndexOf(".com") == -1)
                                            attr.Value = "http://www.youtube.com" + attr.Value;
                                        else
                                            attr.Value = "http:" + attr.Value;

                                    attr.Value = "http://" + setting.IpAddress + ":8083/proxy*" + attr.Value + "|";
                                }
                            }

                            foreach (var node in xhtml.DocumentNode.DescendantNodes().Where(h => (h.Name == "img")))
                            {
                                foreach (var attr in node.Attributes.Where(h => h.Name == "src"))
                                {
                                    if (attr.Value.IndexOf("http://") == -1 && attr.Value.IndexOf("https://") == -1)
                                        if (attr.Value.IndexOf(".com") == -1)
                                            attr.Value = "http://www.youtube.com" + attr.Value;
                                        else
                                            attr.Value = "http:" + attr.Value;

                                    attr.Value = "http://" + setting.IpAddress + ":8083/proxy*" + attr.Value + "|";
                                }
                            }

                            //if (!client.ResponseHeaders.AllKeys.Contains("Transfer-Encoding"))
                            //{
                                responseHeaders.Append("Content-Length: ");
                                responseHeaders.Append(xhtml.DocumentNode.OuterHtml.Length);
                                responseHeaders.Append("\r\n");
                            //}

                            foreach(var header in client.ResponseHeaders.AllKeys)
                            {
                                if (responseHeaders.ToString().IndexOf(header + ":") == -1 && ((header != "Transfer-Encoding") && (header != "X-XSS-Protection") && (header != "P3P")))
                                {
                                    responseHeaders.Append(header + ": ");
                                    responseHeaders.Append(client.ResponseHeaders[header]);
                                    responseHeaders.Append("\r\n");
                                }
                            }
                            
                            responseHeaders.Append("\r\n");

                            if (!overrideResp)
                            {
                                accSock.Send(Encoding.ASCII.GetBytes(responseHeaders.ToString() + xhtml.DocumentNode.OuterHtml));
                            }
                            else
                            {
                                respbs.InsertRange(0, Encoding.ASCII.GetBytes(responseHeaders.ToString()));
                                //respbs.AddRange(await client.DownloadDataTaskAsync(link));
                                var bs = await httpRequest(1, request, link);
                                accSock.Send(respbs.ToArray());
                            }

                            System.Threading.Thread.Sleep(100);
                            accSock.Close();
                           // accSock.Shutdown(SocketShutdown.Both);
                        }
                        else
                        {
                            responseHeaders.Append("Content-Length: ");
                            responseHeaders.Append(html.Length);
                            responseHeaders.Append("\r\n");

                            foreach (var header in client.ResponseHeaders.AllKeys)
                            {
                                if (responseHeaders.ToString().IndexOf(header + ":") == -1)
                                {
                                    responseHeaders.Append(header + ": ");
                                    responseHeaders.Append(client.ResponseHeaders[header]);
                                    responseHeaders.Append("\r\n");
                                }
                            }
                            
                            responseHeaders.Append("\r\n");

                            accSock.Send(Encoding.ASCII.GetBytes(responseHeaders.ToString() + html));
                            System.Threading.Thread.Sleep(100);
                            accSock.Close();
                            //accSock.Shutdown(SocketShutdown.Both);
                        }
                    }
                    else
                    {
                        image = await client.DownloadDataTaskAsync(link);
                        responseHeaders.AppendFormat("{0}{1}{2}", "Content-Length: ", image.Length, "\r\n\r\n");
                        respbs.AddRange(Encoding.ASCII.GetBytes(responseHeaders.ToString()));
                        respbs.AddRange(image);
                        accSock.Send(respbs.ToArray());
                        System.Threading.Thread.Sleep(100);
                        //accSock.Shutdown(SocketShutdown.Both);
                        accSock.Close();
                    }

                    client.Dispose();
                    accSock.Dispose();
                }
            }
            catch (Exception err)
            {
                if (accSock.Connected)
                {
                    accSock.Shutdown(SocketShutdown.Both);
                    accSock.Close();
                    accSock.Dispose();
                }

                LogError("\nProxy error: " + err.Message + "\n\n" + err.StackTrace + "\n\n" + request);
            }
        }

        protected override void OnStart(string[] args)
        {
            LogDebug("\nMpgrabber OnStart()\n");

            try
            {
                //ThreadPool.QueueUserWorkItem(async (Object o) =>
                var mainthread = new Thread(new ThreadStart(() =>
                {
                    while (true)
                    {
                    restart:

                        try
                        {
                            LogDebug("\nMpgrabber onStart()\n");

                            IPAddress ipAddr;
                            /*   ServicePointManager.ServerCertificateValidationCallback = delegate
                               {
                                  return true;
                               };*/

                            Task.WaitAll(new Task[]
                            {
                                Task.Run(async() => 
                                {
                                    Socket listeningSocket1 = null;

                                    LogDebug("\nStarting thread\n");

                                    while (true)
                                    {
                                        try
                                        {
                                            ipAddr = Dns.GetHostAddresses(Dns.GetHostName()).ToList().Find(ip => ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString().IndexOf(setting.IpAddress) > -1);
                                            LogDebug("\nLoop start for port 8081\n");

                                            if (listeningSocket1 == null) 
                                                listeningSocket1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);

                                            if (listeningSocket1.LocalEndPoint == null)
                                            {
                                                listeningSocket1.Bind(new IPEndPoint(ipAddr, 8081));
                                                listeningSocket1.Listen(int.MaxValue);
                                            }

                                            processCmd(await Task.Factory.FromAsync<Socket>(listeningSocket1.BeginAccept, listeningSocket1.EndAccept, true));
                                        }
                                        catch(Exception err)
                                        {
                                            LogError("\nError in download thread: " + err.Message + "\n\n" + err.StackTrace);
                                        }
                                    }
                                }),
                                Task.Run(async() => 
                                {                                
                                    Socket listeningSocket2 = null;

                                    while (true)
                                    {
                                        try
                                        {
                                            ipAddr = Dns.GetHostAddresses(Dns.GetHostName()).ToList().Find(ip => ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString().IndexOf(setting.IpAddress) > -1);
                                            LogDebug("\nLoop start for port 8080\n");

                                            if (listeningSocket2 == null) 
                                                listeningSocket2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);

                                            if (listeningSocket2.LocalEndPoint == null)
                                            {
                                                listeningSocket2.Bind(new IPEndPoint(ipAddr, 8080));
                                                listeningSocket2.Listen(int.MaxValue);
                                            }

                                            processCmd(await Task.Factory.FromAsync<Socket>(listeningSocket2.BeginAccept, listeningSocket2.EndAccept, true));
                                            GC.Collect(2, GCCollectionMode.Optimized, false);
                                        }
                                        catch(Exception err)
                                        {
                                            LogError("\nError in main thread: " + err.Message + "\n\n" + err.StackTrace);
                                        }
                                    }
                                }),
                                Task.Run(async() => 
                                {                                
                                    Socket listeningSocket3 = null;

                                    while (true)
                                    {
                                        try
                                        {
                                            ipAddr = Dns.GetHostAddresses(Dns.GetHostName()).ToList().Find(ip => ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString().IndexOf(setting.IpAddress) > -1);
                                            LogDebug("\nLoop start for port 8082\n");

                                            if (listeningSocket3 == null) 
                                                listeningSocket3 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);

                                            if (listeningSocket3.LocalEndPoint == null)
                                            {
                                                listeningSocket3.Bind(new IPEndPoint(ipAddr, 8082));
                                                listeningSocket3.Listen(int.MaxValue);
                                            }

                                            processCmd(await Task.Factory.FromAsync<Socket>(listeningSocket3.BeginAccept, listeningSocket3.EndAccept, true));
                                        }
                                        catch(Exception err)
                                        {
                                            LogError("\nError in identify thread: " + err.Message + "\n\n" + err.StackTrace);
                                        }
                                    }
                                }),
                                Task.Run(async() => 
                                {                                
                                    Socket listeningSocket4 = null;

                                    while (true)
                                    {
                                        try
                                        {
                                            ipAddr = Dns.GetHostAddresses(Dns.GetHostName()).ToList().Find(ip => ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString().IndexOf(setting.IpAddress) > -1);
                                            LogDebug("\nLoop start for port 8083\n");

                                            if (listeningSocket4 == null) 
                                                listeningSocket4 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);

                                            if (listeningSocket4.LocalEndPoint == null)
                                            {
                                                listeningSocket4.Bind(new IPEndPoint(ipAddr, 8083));
                                                listeningSocket4.Listen(int.MaxValue);
                                            }

                                           proxy(await Task.Factory.FromAsync<Socket>(listeningSocket4.BeginAccept, listeningSocket4.EndAccept, true));
                                        }
                                        catch(Exception err)
                                        {
                                            LogError("\nError in proxy thread: " + err.Message + "\n\n" + err.StackTrace);
                                        }
                                    }
                                })
                            });
                        }
                        catch (Exception err)
                        {

                        }

                        LogDebug("\nRestarting\n");
                        goto restart;
                    }
                }));
                mainthread.Start();
            }
            catch (Exception err)
            {
                LogError("\nError: " + err.Message + "\n\n" + err.StackTrace);
            }
        }

        protected override void OnStop()
        {
            LogError("\nMpgrabber OnStop()\n");
        }

        protected override void OnContinue()
        {

        }

        public void Start(String[] args)
        {
            //if (System.IO.File.Exists(setting.DebugFileName))
            //    System.IO.File.Delete(setting.DebugFileName);

            LogDebug("\nStarting...\n");
            OnStart(args);
        }

        public Mpgrabber()
        {
            LogDebug("\ndbgfilename: " + setting.DebugFileName + "\nerrfilename: " + setting.ErrorFileName + "\nDebug: " + setting.Debug + "\nIpaddress: " + setting.IpAddress + "\n");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.ServiceName = "Mpgrabber";
        }
    }
}