using Newtonsoft.Json;
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
        public string description;
        public string authorId;
        public string authorName;
        public string[] tags;
        public string assetUrl;
        public string imageUrl;
        public string thumbnailImageUrl;
        public string releaseStatus; // only set from API requests, not VRCModNW requests
        public int version;
        // ...
        public string unityPackageUrl;

        public static SerializableApiAvatar[] ParseJson(String json)
        {
            return JsonConvert.DeserializeObject<SerializableApiAvatarList>(json)?.list ?? new SerializableApiAvatar[0];
        }
    }

    internal class SerializableApiAvatarList
    {
        public SerializableApiAvatar[] list;
    }
}
