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

        // only set from API requests, not VRCModNW requests
        public string authorId;
        public string releaseStatus; 

        public static SerializableApiAvatar[] ParseJson(string json)
        {
            return JsonConvert.DeserializeObject<SerializableApiAvatarList>(json)?.list ?? new SerializableApiAvatar[0];
        }
    }

    internal class SerializableApiAvatarList
    {
        public SerializableApiAvatar[] list;
    }
}
