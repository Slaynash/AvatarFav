using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AvatarFav
{
    [Serializable]
    public class SerializableApiAvatar
    {
        public string id;
        public string name;
		public string imageUrl;
		public string authorName;
		public string authorId;
        public string assetUrl;
		public string description;
		public string[] tags;
		public double version;
        public string unityPackageUrl;
        public string thumbnailImageUrl;

        public SerializableApiAvatar(string id, string name, string imageUrl, string authorName, string authorId, string assetUrl, string description, List<string> tags, int version, string unityPackageUrl, string thumbnailImageUrl)
        {
            this.id = id;
            this.name = name;
            this.imageUrl = imageUrl;
            this.authorName = authorName;
            this.authorId = authorId;
            this.assetUrl = assetUrl;
            this.description = description;
            this.tags = tags.ToArray();
            this.version = version;
            this.unityPackageUrl = unityPackageUrl;
            this.thumbnailImageUrl = thumbnailImageUrl;
        }

        public Dictionary<string, object> getDictionary()
        {
            Dictionary<string, object> dic = new Dictionary<string, object>();

            dic.Add("id", id);
            dic.Add("name", name);
            dic.Add("imageUrl", imageUrl);
            dic.Add("authorName", authorName);
            dic.Add("authorId", authorId);
            dic.Add("assetUrl", assetUrl);
            dic.Add("description", description);
            List<object> tagList = new List<object>();
            tagList.AddRange(tags);
            dic.Add("tags", tagList);
            dic.Add("version", version);
            dic.Add("unityPackageUrl", unityPackageUrl);
            dic.Add("thumbnailImageUrl", thumbnailImageUrl);
            dic.Add("releaseStatus", "public");

            return dic;
        }

        public static List<SerializableApiAvatar> ParseJson(String json)
        {

            List<SerializableApiAvatar> saa = new List<SerializableApiAvatar>();

            string parts = json.Substring(9);
            parts = parts.Substring(0, parts.Length-2);
            MatchCollection matches = Regex.Matches(parts, @"({.+?})");
            
            foreach(Match m in matches)
            {
                saa.Add(JsonUtility.FromJson<SerializableApiAvatar>(m.Value));
            }
            return saa;
        }

        public string AsJson()
        {

            string tagsS = "[";
            bool first = true;
            foreach(string tag in tags)
            {
                if (!first) tagsS += ",";
                tagsS += "\"" + tag.Replace("\"", "\\\"") + "\"";
                first = false;
            }
            tagsS += "]";

            string jsonString =
                "{" +
                    "\"id\":\"" + id + "\"," +
                    "\"name\":\"" + name.Replace("\"", "\\\"") + "\"," +
                    "\"imageUrl\":\"" + imageUrl + "\"," +
                    "\"authorName\":\"" + authorName.Replace("\"", "\\\"") + "\"," +
                    "\"authorId\":\"" + authorId + "\"," +
                    "\"assetUrl\":\"" + assetUrl + "\"," +
                    "\"description\":\"" + description.Replace("\"", "\\\"") + "\"," +
                    "\"tags\":" + tagsS + "," +
                    "\"version\":\"" + version + "\"," +
                    "\"unityPackageUrl\":\"" + unityPackageUrl + "\"," +
                    "\"thumbnailImageUrl\":\"" + thumbnailImageUrl + "\"" +
                "}";

            return jsonString;
        }
    }
}
