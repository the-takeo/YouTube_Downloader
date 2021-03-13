using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoLibrary;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
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

            var req = WebRequest.Create(@"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId=" + IdOfUploadedPlayList + "&maxResults=50&key=" + ApiKey.apiKey);
            var res = req.GetResponse();
            PlayListJson info;
            using (res)
            {
                using (var resStream = res.GetResponseStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(PlayListJson));
                    info = (PlayListJson)serializer.ReadObject(resStream);

                    var result = new Dictionary<string, string>();

                    foreach (var item in info.items)
                    {
                        result.Add(@"https://www.youtube.com/watch?v=" + item.snippet.resourceId.videoId, item.snippet.title);
                    }

                    return result;
                }
            }
        }

        private static string getIdOfUploadedPlayList(string channelId)
        {
            var req = WebRequest.Create(@"https://www.googleapis.com/youtube/v3/channels?part=contentDetails&id=" + channelId + "&key=" + ApiKey.apiKey);
            var res = req.GetResponse();
            ChannelJson info;
            using (res)
            {
                using (var resStream = res.GetResponseStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(ChannelJson));
                    info = (ChannelJson)serializer.ReadObject(resStream);
                }
            }

            return info.items[0].contentDetails.relatedPlaylists.uploads;
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
