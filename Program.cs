using System.Collections.Generic;
using System.IO;
using VideoLibrary;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace YouTube_Downloader
{
    class Program
    {
        static void Main(string[] args)
        {
            string channelId = args[0];
            string downloadPath = args[1];

            foreach (var url in YouTubeProcess.GetMovieUrlsOfChannel(channelId))
            {
                YouTubeProcess.Download(url.Key, downloadPath);
            }
        }
    }

    public static class YouTubeProcess
    {
        public static void Download(string url, string path)
        {
            var youTube = YouTube.Default;
            var video = youTube.GetVideo(url);
            File.WriteAllBytes(path + @"\" + video.FullName, video.GetBytes());
        }

        public static Dictionary<string, string> GetMovieUrlsOfChannel(string channelId)
        {
            string IdOfUploadedPlayList = getIdOfUploadedPlayList(channelId);

            string request = @"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId=" + IdOfUploadedPlayList + "&maxResults=50&key=" + ApiKey.apiKey;
            var jsonResult = getJsonRequest<PlayListJson>(request);

            var result = new Dictionary<string, string>();

            foreach (var item in jsonResult.items)
            {
                result.Add(@"https://www.youtube.com/watch?v=" + item.snippet.resourceId.videoId, item.snippet.title);
            }

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
