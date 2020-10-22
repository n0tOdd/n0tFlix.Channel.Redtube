using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using n0tFlix.Helpers.YoutubeDL.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static n0tFlix.Channel.Redtube.Models.Info;

namespace n0tFlix.Channel.Redtube
{
    public class Channel : IChannel, IRequiresMediaInfoCallback
    {
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        public ChannelParentalRating ParentalRating => ChannelParentalRating.Adult;

        public Channel(IHttpClient httpClient, IJsonSerializer jsonSerializer, ILogger<Channel> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
        }

        public string Name { get { return Plugin.Instance.Name; } }
        public string HomePageUrl { get { return "https://www.redtube.com/"; } }

        public string DataVersion
        {
            get
            {
                // Increment as needed to invalidate all caches
                return "1.0.0.0";
            }
        }

        public string Description
        {
            get { return Plugin.Instance.Description; }
        }

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Clip
                },

                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },
                MaxPageSize = 20
            };
        }

        public bool IsEnabledFor(string userId)
        {
            return true;
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            _logger.LogDebug("cat ID : " + query.FolderId);
            if (query.FolderId == null)
            {
                return await GetCategories(cancellationToken).ConfigureAwait(false);
            }
            var catSplit = query.FolderId.Split('_');
            query.FolderId = catSplit[1];
            if (catSplit[0] == "videos")
            {
                return await GetVideos(query, cancellationToken).ConfigureAwait(false);
            }
            return null;
        }

        private async Task<ChannelItemResult> GetCategories(CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();

            using (var site = await _httpClient.Get(new HttpRequestOptions() { Url = "http://api.redtube.com/?data=redtube.Categories.getCategoriesList&output=json" }))
            {
                var categories = _jsonSerializer.DeserializeFromStream<RootObject>(site);

                foreach (var c in categories.categories)
                {
                    if (c.category != "japanesecensored")
                    {
                        items.Add(new ChannelItemInfo
                        {
                            Name = c.category.Substring(0, 1).ToUpper() + c.category.Substring(1),
                            Id = "videos_" + c.category,
                            Type = ChannelItemType.Folder,
                            ImageUrl =
                                "http://img.l3.cdn.redtubefiles.com/_thumbs/categories/categories-180x135/" + c.category.ToLower() +
                                "_001.jpg"
                        });
                    }
                }
            }

            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }

        private async Task<ChannelItemResult> GetVideos(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();
            int total = 0;

            int? page = null;

            if (query.StartIndex.HasValue && query.Limit.HasValue)
            {
                page = 1 + (query.StartIndex.Value / query.Limit.Value) % query.Limit.Value;
            }
            for (int i = 0; 5 >= i; i++)
            {
                using (var site = await _httpClient.Get(new HttpRequestOptions() { Url = String.Format("http://api.redtube.com/?data=redtube.Videos.searchVideos&output=json&category={0}&ordering=newest&thumbsize=large&page={1}", query.FolderId, i) }))
                {
                    var videos = _jsonSerializer.DeserializeFromStream<RootObject>(site);

                    total = total + videos.count;

                    foreach (var v in videos.videos)
                    {
                        var durationNode = v.video.duration.Split(':');
                        _logger.LogDebug(durationNode[0] + "." + durationNode[1]);
                        var time = Convert.ToDouble(durationNode[0] + "." + durationNode[1]);

                        items.Add(new ChannelItemInfo
                        {
                            Type = ChannelItemType.Media,
                            ContentType = ChannelMediaContentType.Clip,
                            MediaType = ChannelMediaType.Video,
                            ImageUrl = v.video.default_thumb.Replace("m.jpg", "b.jpg"),
                            Name = v.video.title,
                            Id = v.video.url,
                            RunTimeTicks = TimeSpan.FromMinutes(time).Ticks,
                            //Tags = v.video.tags == null ? new List<string>() : v.video.tags.Select(t => t.title).ToList(),
                            DateCreated = DateTime.Parse(v.video.publish_date),
                            CommunityRating = float.Parse(v.video.rating)
                        });
                    }
                }
            }
            return new ChannelItemResult
            {
                Items = items.ToList(),
                TotalRecordCount = total
            };
        }

        private async Task<ChannelItemResult> GetTags(CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();
            var page = new HtmlDocument();

            using (var site = await _httpClient.Get(new HttpRequestOptions() { Url = "http://www.beeg.com/" }))
            {
                page.Load(site, Encoding.UTF8);
                if (page.DocumentNode != null)
                {
                    foreach (var node in page.DocumentNode.SelectNodes("//a[contains(@href, \"tag\")]"))
                    {
                        var title = node.InnerText;
                        var url = node.Attributes["href"].Value;

                        items.Add(new ChannelItemInfo
                        {
                            Name = title,
                            Id = "video_" + "http://www.beeg.com" + url,
                            Type = ChannelItemType.Folder,
                            OfficialRating = "GB-18"
                        });
                    }
                }
            }

            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }

        public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            string path = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!Directory.Exists(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl")))
                    Directory.CreateDirectory(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl"));
                if (!File.Exists(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl.exe")))
                    new WebClient()
                        .DownloadFile("https://yt-dl.org/downloads/latest/youtube-dl.exe", Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl.exe"));
                path = Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl.exe");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (!Directory.Exists(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl")))
                    Directory.CreateDirectory(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl"));
                if (!File.Exists(Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl")))
                    new WebClient()
                        .DownloadFile("https://yt-dl.org/downloads/latest/youtube-dl", Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl"));
                path = Path.Combine(Plugin.Instance.DataFolderPath, "Youtube-dl", "youtube-dl");
            }

            n0tFlix.Helpers.YoutubeDL.YoutubeDL youtubeDL = new Helpers.YoutubeDL.YoutubeDL(path);
            youtubeDL.Options.VerbositySimulationOptions.GetUrl = true;
            DownloadInfo downloadInfo = await youtubeDL.GetDownloadInfoAsync(id);
            VideoDownloadInfo video = downloadInfo as VideoDownloadInfo;
            _logger.LogInformation(video.Url);
            return new List<MediaSourceInfo>
                    {
                        new MediaSourceInfo
                        {
                            Id = video.Id,
                            Path = video.Url,
                            IsRemote = true,
                            Protocol = MediaProtocol.File,
                            EncoderProtocol = MediaProtocol.File
                        }
                    };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            switch (type)
            {
                case ImageType.Thumb:
                    {
                        var path = GetType().Namespace + ".Images.logo.png";

                        return Task.FromResult(new DynamicImageResponse
                        {
                            Format = ImageFormat.Png,
                            HasImage = true,

                            Stream = GetType().Assembly.GetManifestResourceStream(path)
                        });
                    }
                default:
                    throw new ArgumentException("Unsupported image type: " + type);
            }
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>
            {
                ImageType.Thumb
            };
        }
    }
}