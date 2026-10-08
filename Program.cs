using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace YouTube_Downloader
{
    class Program
    {
        static void Main(string[] args)
        {
            // CLI モード: チャンネルIDとダウンロードパスの2引数
            if (args.Length == 2
                && !args[0].StartsWith("chrome-extension://")
                && args[0] != "--native-messaging")
            {
                string channelId = args[0];
                string downloadPath = args[1];
                if (YouTubeProcess.FindYtDlp() == null)
                {
                    Console.Error.WriteLine(YouTubeProcess.YtDlpNotFoundMessage);
                    Environment.Exit(1);
                }
                foreach (var url in YouTubeProcess.GetMovieUrlsOfChannel(channelId))
                {
                    YouTubeProcess.Download(url.Key, downloadPath);
                }
                return;
            }

            // それ以外はネイティブメッセージングモード
            // （引数なし、--native-messaging、またはChromeが渡すchrome-extension://...）
            NativeMessagingHost.Run();
        }
    }

    public static class NativeMessagingHost
    {
        public static void Run()
        {
            var stdin = Console.OpenStandardInput();
            var stdout = Console.OpenStandardOutput();
            var cancelSource = new System.Threading.CancellationTokenSource();

            while (true)
            {
                var lenBuf = new byte[4];
                if (stdin.Read(lenBuf, 0, 4) < 4) break;

                int len = BitConverter.ToInt32(lenBuf, 0);
                var msgBuf = new byte[len];
                int totalRead = 0;
                while (totalRead < len)
                    totalRead += stdin.Read(msgBuf, totalRead, len - totalRead);

                var json = Encoding.UTF8.GetString(msgBuf);
                string type;
                string path = null;
                List<string> urls = null;

                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    type = root.TryGetProperty("type", out var t) ? t.GetString() : "download";

                    if (type == "download")
                    {
                        path = root.GetProperty("path").GetString();
                        urls = root.GetProperty("urls")
                                   .EnumerateArray()
                                   .Select(u => u.GetString())
                                   .ToList();
                    }
                }

                if (type == "cancel")
                {
                    cancelSource.Cancel();
                    continue;
                }

                // 新しいダウンロード要求。実行中のダウンロードは背景スレッドで動かし、
                // メインループはstdinの読み取り（＝キャンセル要求の受信）を継続できるようにする。
                cancelSource = new System.Threading.CancellationTokenSource();
                var token = cancelSource.Token;
                System.Threading.Tasks.Task.Run(() => ProcessDownload(urls, path, stdout, token));
            }
        }

        static void Send(Stream stdout, object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            var lenBytes = BitConverter.GetBytes(bytes.Length);
            lock (stdout)
            {
                stdout.Write(lenBytes, 0, 4);
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
            }
        }

        static void ProcessDownload(List<string> urls, string path, Stream stdout, System.Threading.CancellationToken token)
        {
            try
            {
                int total = urls.Count;
                int downloaded = 0;
                int failed = 0;

                if (YouTubeProcess.FindYtDlp() == null)
                {
                    Send(stdout, new { type = "error", message = YouTubeProcess.YtDlpNotFoundMessage });
                    return;
                }

                foreach (var url in urls)
                {
                    if (token.IsCancellationRequested)
                    {
                        Send(stdout, new { type = "cancelled", downloaded, failed, total });
                        return;
                    }

                    string title = url;
                    try
                    {
                        title = YouTubeProcess.Download(url, path);
                        downloaded++;
                    }
                    catch
                    {
                        failed++;
                    }

                    Send(stdout, new { type = "progress", current = downloaded + failed, total, title });
                }

                Send(stdout, new { type = "complete", downloaded, failed, total });
            }
            catch (Exception ex)
            {
                Send(stdout, new { type = "error", message = ex.Message });
            }
        }
    }

    public static class YouTubeProcess
    {
        public const string YtDlpNotFoundMessage =
            "yt-dlp.exe が見つかりません。YouTube_Downloader.exe と同じフォルダに置くか、PATH に追加してください。";

        // yt-dlp で最高画質の映像と最高音質の音声を取得し、ffmpeg で MP4 に結合する。
        // 保存したファイル名を返す。
        public static string Download(string url, string path)
        {
            string ytDlp = FindYtDlp() ?? throw new FileNotFoundException(YtDlpNotFoundMessage);

            var args = new List<string>
            {
                "--no-playlist", "--no-progress", "--encoding", "utf-8",
                "-f", "bv*+ba/b", "--merge-output-format", "mp4",
                "-o", Path.Combine(path, "%(title)s.%(ext)s"),
                "--print", "after_move:filepath", "--no-simulate",
            };
            string ffmpeg = FindTool("ffmpeg.exe");
            if (ffmpeg != null)
                args.AddRange(new[] { "--ffmpeg-location", ffmpeg });
            args.Add(url);

            var psi = new ProcessStartInfo(ytDlp)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // ネイティブメッセージングでは標準入力が Chrome とのパイプなので、子プロセスに継承させない
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using (var proc = Process.Start(psi))
            {
                proc.StandardInput.Close();
                // 両方を並行して読まないとバッファが詰まってハングする
                var errTask = proc.StandardError.ReadToEndAsync();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                string err = errTask.Result;

                if (proc.ExitCode != 0)
                    throw new Exception("yt-dlp でのダウンロードに失敗しました: " + err.Trim());

                string filePath = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l != "");
                return filePath != null ? Path.GetFileName(filePath) : url;
            }
        }

        public static string FindYtDlp() => FindTool("yt-dlp.exe");

        // EXE と同じフォルダ、または PATH 上から探す
        private static string FindTool(string fileName)
        {
            var dirs = new List<string> { AppContext.BaseDirectory };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

            foreach (var dir in dirs)
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim('"'), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        public static Dictionary<string, string> GetMovieUrlsOfChannel(string channelId)
        {
            string IdOfUploadedPlayList = getIdOfUploadedPlayList(channelId);

            var result = new Dictionary<string, string>();
            string nextPageToken = null;

            do
            {
                string request = @"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId=" + IdOfUploadedPlayList + "&maxResults=50&key=" + ApiKey.apiKey;
                if (nextPageToken != null)
                    request += "&pageToken=" + nextPageToken;

                var jsonResult = getJsonRequest<PlayListJson>(request);

                foreach (var item in jsonResult.items)
                {
                    result.Add(@"https://www.youtube.com/watch?v=" + item.snippet.resourceId.videoId, item.snippet.title);
                }

                nextPageToken = jsonResult.nextPageToken;
            } while (nextPageToken != null);

            return result;
        }

        private static string getIdOfUploadedPlayList(string channelId)
        {
            string request = @"https://www.googleapis.com/youtube/v3/channels?part=contentDetails&id=" + channelId + "&key=" + ApiKey.apiKey;
            var jsonResult = getJsonRequest<ChannelJson>(request);

            return jsonResult.items[0].contentDetails.relatedPlaylists.uploads;
        }

        private static T getJsonRequest<T>(string request)
        {
            var req = WebRequest.Create(request);
            var res = req.GetResponse();
            T result;
            using (res)
            {
                using (var resStream = res.GetResponseStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    result = (T)serializer.ReadObject(resStream);
                }
            }

            return result;
        }
    }

    [DataContract]
    public class ChannelJson
    {
        [DataMember]
        public List<item> items { get; set; }

        [DataContract]
        public class item
        {
            [DataMember]
            public contentDetail contentDetails { get; set; }
        }

        [DataContract]
        public class contentDetail
        {
            [DataMember]
            public relatedPlaylist relatedPlaylists { get; set; }
        }

        [DataContract]
        public class relatedPlaylist
        {
            [DataMember]
            public string uploads { get; set; }
        }
    }

    [DataContract]
    public class PlayListJson
    {
        [DataMember]
        public List<item> items { get; set; }

        [DataMember]
        public string nextPageToken { get; set; }

        [DataContract]
        public class item
        {
            [DataMember]
            public snippet snippet { get; set; }
        }

        [DataContract]
        public class snippet
        {
            [DataMember]
            public resourceId resourceId { get; set; }

            [DataMember]
            public string title { get; set; }
        }

        [DataContract]
        public class resourceId
        {
            [DataMember]
            public string videoId { get; set; }
        }
    }
}
