using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using VRC.Core;
using VRCModLoader;
using VRCModNetwork;
using VRCTools;

namespace AvatarFav
{
    class VRCheatAvatarfileImporter
    {
        private static Dictionary<string, string> savedAvatars = new Dictionary<string, string>();

        private static int importedAvatars = 0;

        public static bool Importing { get; private set; }

        public static IEnumerator ImportAvatarfile()
        {
            VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Reading avatar list file...");
            FileInfo[] files = new DirectoryInfo(Environment.CurrentDirectory).GetFiles("Avatars.txt", SearchOption.AllDirectories);
            VRCModLogger.Log("[VRCheatAvatarfileImporter] Found " + files.Length + " Avatars.txt");
            if (files.Length > 0)
            {
                // Load all avatarIds
                foreach(FileInfo fi in files)
                {
                    string[] array = File.ReadAllLines(fi.FullName);
                    for (int i = 0; i < array.Length; i++)
                    {
                        string[] array2 = array[i].Split(new char[] { '|' });
                        if (array2.Length >= 3)
                        {
                            if (!savedAvatars.ContainsKey(array2[1]))
                                savedAvatars.Add(array2[1], array2[0]);
                        }
                    }
                }

                // Add all avatars to the favorite list
                importedAvatars = 0;
                int avatarsDone = 0;
                Importing = true;
                foreach (KeyValuePair<string, string> avatarIdAndName in savedAvatars)
                {
                    VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Adding avatar " + avatarsDone + "/" + savedAvatars.Count);
                    yield return AddAvatarToList(avatarIdAndName.Key, avatarIdAndName.Value);
                    avatarsDone++;
                }
                Importing = false;
                VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Imported " + importedAvatars + " new public avatars from the list (" + importedAvatars + "/" + savedAvatars.Count + " avatars)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
            }
            else VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Error: Unable to find any Avatars.txt file in your game directory", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
        }

        private static IEnumerator AddAvatarToList(string avatarId, string avatarName)
        {
            bool found = false;
            foreach (string avatarfavId in AvatarFavMod.favoriteAvatarList)
            {
                if (avatarfavId == avatarId)
                {
                    found = true;
                    VRCModLogger.LogError("[VRCheatAvatarfileImporter] Avatar " + avatarName + " already exist in list");
                    break;
                }
            }
            if (!found)
            {
                using (WWW avtrRequest = new WWW(API.GetApiUrl() + "avatars/" + avatarId + "?apiKey=" + AvatarFavMod.GetApiKey()))
                {
                    yield return avtrRequest;
                    int rc = WebRequestsUtils.GetResponseCode(avtrRequest);
                    if (rc == 200)
                    {
                        string uuid = APIUser.CurrentUser?.id ?? "";
                        SerializableApiAvatar aa = null;
                        try
                        {
                            aa = JsonConvert.DeserializeObject<SerializableApiAvatar>(avtrRequest.text);
                        }
                        catch (Exception e)
                        {
                            VRCModLogger.LogError("[VRCheatAvatarfileImporter] Unable to add the avatar " + avatarName + ": Unable to parse the API response. " + e);
                        }

                        if (aa != null)
                        {
                            if (aa.authorId != uuid)
                            {
                                if (aa.releaseStatus == "public")
                                {
                                    VRCModLogger.Log("[VRCheatAvatarfileImporter] Adding avatar " + avatarName + " to the database");
                                    yield return AddAvatar(avatarId, avatarName);
                                }
                                else VRCModLogger.Log("[VRCheatAvatarfileImporter] Unable to add the avatar " + avatarName + ": This avatar is not public anymore (private)");
                            }
                            else VRCModLogger.Log("[VRCheatAvatarfileImporter] Unable to add the avatar " + avatarName + ": This avatar is own avatar");
                        }
                    }
                    else VRCModLogger.Log("[VRCheatAvatarfileImporter] Unable to add the avatar " + avatarName + ": This avatar is not public anymore (deleted)");
                }
            }
        }


        private static IEnumerator AddAvatar(string id, string name)
        {
            bool done = false;
            VRCModNetworkManager.SendRPC("slaynash.avatarfav.addavatar", id, () =>
            {
                VRCModLogger.Log("[VRCheatAvatarfileImporter] Avatar " + name + " successfully added");
                importedAvatars++;
                done = true;
            }, (error) =>
            {
                VRCModLogger.Log("[VRCheatAvatarfileImporter] Unable to add the avatar " + name + ": VRCMNW Server returned error: " + error);
                done = true;
            });
            while (!done) yield return null;
        }
    }
}
