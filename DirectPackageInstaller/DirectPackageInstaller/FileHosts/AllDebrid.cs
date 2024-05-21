﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace DirectPackageInstaller.FileHosts
{
    class AllDebrid : FileHostBase
    {

        static AllDebridApi? Info = null;
        static Dictionary<string, string> GenCache = new Dictionary<string, string>();

        public override string HostName => "AllDebrid";
        public override bool Limited => false;

        public override DownloadInfo GetDownloadInfo(string URL)
        {
            if (GenCache.ContainsKey(URL))
                return new DownloadInfo() { Url = GenCache[URL] };

            const string URLMask = "https://api.alldebrid.com/v4/link/unlock?agent=DirectPackageInstaller&apikey={0}&link={1}";

            var Response = DownloadString(string.Format(URLMask, App.Config.AllDebridApiKey, HttpUtility.UrlEncode(URL)));
            var Data = JsonSerializer.Deserialize<AllDebridApi>(Response);

            if (Info?.status != "success")
                throw new Exception();

            GenCache[URL] = Data.data.link;

            return new DownloadInfo()
            {
                Url = GenCache[URL] = Data.data.link
            };
        }

        public override bool IsValidUrl(string URL)
        {
            if (!App.Config.UseAllDebrid || App.Config.AllDebridApiKey.ToLowerInvariant() == "null" || string.IsNullOrEmpty(App.Config.AllDebridApiKey))
                return false;

            if (Info == null)
            {
                var Status = DownloadString("https://api.alldebrid.com/v4/user/hosts?agent=DirectPackageInstaller&apikey=" + App.Config.AllDebridApiKey);
                Info = JsonSerializer.Deserialize<AllDebridApi>(Status);
            }

            if (Info?.status != "success")
                return false;

            foreach (var Host in Info?.data.hosts) {
                if (new Regex(Host.Value.regexp).IsMatch(URL))
                    return true;
            }

            return false;
        }

        struct AllDebridApi
        {
            public string status { get; set; }
            public AllDebridApiData data { get; set; }
        }

        struct AllDebridApiData
        {
            public Dictionary<string, AllDebridHostsEntry> hosts { get; set; }

            public string link { get; set; }
            public string host { get; set; }
            public string hostDomain { get; set; }
            public string filename { get; set; }
            public bool paws { get; set; }
            public long filesize { get; set; }
            public string id { get; set; }
        }

        struct AllDebridHostsEntry
        {
            public string name { get; set; }
            public string type { get; set; }
            public string[] domains { get; set; }
            public string[] regexps { get; set; }
            public string regexp { get; set; }
            public bool status { get; set; }
        }
    }


}
