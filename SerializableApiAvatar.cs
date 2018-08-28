using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AvatarFav
{
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
    }
}
