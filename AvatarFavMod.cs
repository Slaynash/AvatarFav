using AvatarFav.IL;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.UI;
using VRCModLoader;
using VRCModNetwork;
using VRCSDK2;
using VRCTools;

namespace AvatarFav
{
    [VRCModInfo("AvatarFav", "1.2.2", "Slaynash")]
    public class AvatarFavMod : VRCMod
    {


        public static AvatarFavMod instance;


        private static MethodInfo updateAvatarListMethod;

        internal static List<ApiAvatar> favoriteAvatarList = new List<ApiAvatar>();
        private static bool avatarAvailables = false;
        private bool freshUpdate = false;
        private bool waitingForServer = false;

        private static UiAvatarList favList;
        private static Transform favButton;
        private static Text favButtonText;
        private static PageAvatar pageAvatar;
        private static Transform avatarModel;
        private static FieldInfo applyAvatarField;

        private static Vector3 baseAvatarModelPosition;
        private static string currentUiAvatarId = "";
        private static ApiAvatar oldWearedAvatar = null;

        private bool alreadyLoaded = false;
        private bool initialised = false;
        private string addError;

        private Button.ButtonClickedEvent baseChooseEvent;
        private int lastPickerCound = 0;

        private ApiWorld currentRoom;

        void OnLevelWasLoaded(int level)
        {
            VRCModLogger.Log("[AvatarFav] OnLevelWasLoaded (" + level + ")");
            if (level == 1 && !alreadyLoaded)
            {
                alreadyLoaded = true;

                if (instance != null)
                {
                    Debug.LogWarning("[AvatarFav] Trying to load the same plugin two time !");
                    return;
                }
                instance = this;
                VRCModLogger.Log("[AvatarFav] Getting game version");
                PropertyInfo vrcApplicationSetupInstanceProperty = typeof(VRCApplicationSetup).GetProperties(BindingFlags.Public | BindingFlags.Static).First((pi) => pi.PropertyType == typeof(VRCApplicationSetup));
                VRCModLogger.Log("[AvatarFav] Adding button to UI - Looking up for Change Button");
                // Add a "Favorite" / "Unfavorite" button over the "Choose" button of the AvatarPage
                int buildNumber = -1;
                if (vrcApplicationSetupInstanceProperty != null) buildNumber = ((VRCApplicationSetup)vrcApplicationSetupInstanceProperty.GetValue(null, null)).buildNumber;
                VRCModLogger.Log("[AvatarFav] Game build " + buildNumber);
                pageAvatar = Resources.FindObjectsOfTypeAll<PageAvatar>()[(vrcApplicationSetupInstanceProperty != null && buildNumber < 623 ) ? 1 : 0];
                Transform changeButton = pageAvatar.transform.Find("Change Button");

                //PrintHierarchy(pageAvatar.transform, 0);

                VRCModLogger.Log("[AvatarFav] Adding avatar check on Change button");

                baseChooseEvent = changeButton.GetComponent<Button>().onClick;

                changeButton.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                changeButton.GetComponent<Button>().onClick.AddListener(() =>
                {
                    VRCModLogger.Log("Fetching avatar releaseStatus for " + pageAvatar.avatar.apiAvatar.name + " (" + pageAvatar.avatar.apiAvatar.id + ")");
                    //API.SendGetRequest("avatars/" + pageAvatar.avatar.apiAvatar.id, responseContainer, null, true, 3600f);
                    ModManager.StartCoroutine(CheckAndWearAvatar());
                });



                VRCModLogger.Log("[AvatarFav] Adding favorite button to UI - Duplicating Button");
                favButton = UnityUiUtils.DuplicateButton(changeButton, "Favorite", new Vector2(0, 80));
                favButton.name = "ToggleFavorite";
                favButton.gameObject.SetActive(false);
                favButtonText = favButton.Find("Text").GetComponent<Text>();
                favButton.GetComponent<Button>().interactable = false;

                favButton.GetComponent<Button>().onClick.AddListener(ToggleAvatarFavorite);

                VRCModLogger.Log("[AvatarFav] Storing default AvatarModel position");
                avatarModel = pageAvatar.transform.Find("AvatarModel");
                baseAvatarModelPosition = avatarModel.localPosition;


                FileInfo[] files = new DirectoryInfo(Environment.CurrentDirectory).GetFiles("Avatars.txt", SearchOption.AllDirectories);
                VRCModLogger.Log("[AvatarFavMod] Found " + files.Length + " Avatars.txt");
                if (files.Length > 0)
                {

                    VRCModLogger.Log("[AvatarFav] Adding import button to UI - Duplicating Button");
                    Transform importButton = UnityUiUtils.DuplicateButton(changeButton, "Import Avatars", new Vector2(0, 0));
                    importButton.name = "ImportAvatars";

                    importButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(560, 371);

                    importButton.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Do you want to import the public avatars from your VRCheat avatar list ?",
                        "Yes", () =>
                        {
                            ModManager.StartCoroutine(VRCheatAvatarfileImporter.ImportAvatarfile());
                        },
                        "No", () =>
                        {
                            VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                        });
                        VRCheatAvatarfileImporter.ImportAvatarfile();
                    });

                }


                VRCModLogger.Log("[AvatarFav] Looking up for dev avatar list");
                UiAvatarList[] uiAvatarLists = Resources.FindObjectsOfTypeAll<UiAvatarList>();
                VRCModLogger.Log("[AvatarFav] Found " + uiAvatarLists.Length + " UiAvatarList");

                // Get "developper" list as favList
                FieldInfo categoryField = typeof(UiAvatarList).GetField("category", BindingFlags.Public | BindingFlags.Instance);
                favList = uiAvatarLists.First((list) => (int)categoryField.GetValue(list) == 0);

                VRCModLogger.Log("[AvatarFav] Updating list name and activating");
                // Enable list and change name
                favList.GetComponentInChildren<Button>(true).GetComponentInChildren<Text>().text = "Favorite (Unofficial)";
                favList.gameObject.SetActive(true);

                VRCModLogger.Log("[AvatarFav] Moving list to the first in siblings hierarchy");
                // Set siblings index to first
                favList.transform.SetAsFirstSibling();

                // Get "UpdateAvatarList" method
                VRCModLogger.Log("[AvatarFav] Looking up for UpdateAvatar methods");
                var tmp1 = typeof(UiAvatarList).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Where((m) =>
                {
                    ParameterInfo[] parameters = m.GetParameters();
                    return parameters.Length == 1 && parameters.First().ParameterType == typeof(List<ApiAvatar>);
                });
                VRCModLogger.Log("[AvatarFav] Looking up for the real UpdateAvatar method (Found " + tmp1.ToList().Count + " matching methods)");
                updateAvatarListMethod = tmp1.First((m) =>
                {
                    return m.Parse().Any((i) =>
                    {
                        return i.OpCode == OpCodes.Ldstr && i.GetArgument<string>() == "AvatarsAvailable, page: ";
                    });
                });

                VRCModLogger.Log("[AvatarFav] Disabling dev check of PageAvatar");
                // Disable "dev" check of PageAvatar (to remove auto-disable of the list)
                typeof(PageAvatar)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where((fi) => fi.FieldType == typeof(bool)).ToList()
                    [1]
                    .SetValue(pageAvatar, true);

                // Get Getter of VRCUiContentButton.PressAction
                applyAvatarField = typeof(VRCUiContentButton).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).First((field) => field.FieldType == typeof(Action));
                
                VRCModNetworkManager.OnAuthenticated += () =>
                {
                    RequestAvatars();
                };

                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.serverconnected", (senderId, data) =>
                {
                    if(waitingForServer) RequestAvatars();
                });

                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.error", (senderId, data) => 
                {
                    addError = data;
                });

                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.avatarlistupdated", (senderId, data) =>
                {
                    lock (favoriteAvatarList)
                    {
                        // Update Ui
                        favButton.GetComponent<Button>().interactable = true;
                        favoriteAvatarList.Clear();
                        
                        SerializableApiAvatar[] serializedAvatars = SerializableApiAvatar.ParseJson(data);
                        foreach (SerializableApiAvatar serializedAvatar in serializedAvatars)
                        {
                            ApiAvatar tmpAvatar = new ApiAvatar();
                            tmpAvatar.id = serializedAvatar.id;
                            tmpAvatar.name = serializedAvatar.name;
                            tmpAvatar.imageUrl = serializedAvatar.imageUrl;
                            tmpAvatar.authorName = serializedAvatar.authorName;
                            tmpAvatar.authorId = serializedAvatar.authorId;
                            tmpAvatar.assetUrl = serializedAvatar.assetUrl;
                            tmpAvatar.description = serializedAvatar.description;
                            tmpAvatar.tags = new List<String>(serializedAvatar.tags);
                            tmpAvatar.version = (int)serializedAvatar.version;
                            tmpAvatar.unityPackageUrl = serializedAvatar.unityPackageUrl;
                            tmpAvatar.thumbnailImageUrl = serializedAvatar.thumbnailImageUrl;
                            tmpAvatar.releaseStatus = "public";
                            favoriteAvatarList.Add(tmpAvatar);
                        }

                        avatarAvailables = true;
                    }
                });

                VRCModLogger.Log("[AvatarFav] AvatarFav Initialised !");
                initialised = true;
            }
        }

        public void OnUpdate()
        {
            if (!initialised) return;

            try
            {
                //Update list if element is active
                if (favList.gameObject.activeInHierarchy)
                {
                    lock (favoriteAvatarList)
                    {
                        if (avatarAvailables)
                        {
                            avatarAvailables = false;
                            UpdateFavList();
                            freshUpdate = true;
                        }
                        if(!VRCheatAvatarfileImporter.Importing && ((favList.pickers.Count == 0 && favoriteAvatarList.Count != 0) || (favList.pickers.Count == lastPickerCound && lastPickerCound != favoriteAvatarList.Count)))
                        {
                            VRCModLogger.Log("[AvatarFav] Picker count in favlist mismatch. Updating favlist (" + favList.pickers.Count + " vs " + favoriteAvatarList.Count + ")");
                            UpdateFavList();
                            freshUpdate = true;
                        }
                        lastPickerCound = favList.pickers.Count;
                    }
                }

                //Auto-refresh weared avatar to PageAvatar UI, so we can fav it (and observe it also) 
                if (pageAvatar.avatar != null && pageAvatar.avatar.apiAvatar != null && CurrentUserUtils.GetApiAvatar() != null && (oldWearedAvatar == null || oldWearedAvatar.id != CurrentUserUtils.GetApiAvatar().id))
                {
                    oldWearedAvatar = CurrentUserUtils.GetApiAvatar();
                    if (pageAvatar.avatar.apiAvatar.id != oldWearedAvatar.id)
                    {
                        pageAvatar.avatar.Refresh(oldWearedAvatar);
                    }
                }

                if (pageAvatar.avatar != null && pageAvatar.avatar.apiAvatar != null && CurrentUserUtils.GetGetCurrentUser().GetValue(null) != null && !currentUiAvatarId.Equals(pageAvatar.avatar.apiAvatar.id) || freshUpdate)
                {
                    currentUiAvatarId = pageAvatar.avatar.apiAvatar.id;

                    if (!pageAvatar.avatar.apiAvatar.releaseStatus.Equals("public") || pageAvatar.avatar.apiAvatar.authorId == APIUser.CurrentUser.id)
                    {
                        favButton.gameObject.SetActive(false);
                        avatarModel.localPosition = baseAvatarModelPosition;
                    }
                    else
                    {
                        favButton.gameObject.SetActive(true);
                        avatarModel.localPosition = baseAvatarModelPosition + new Vector3(0, 60, 0);

                        bool found = false;

                        foreach (ApiAvatar avatar in favoriteAvatarList)
                        {
                            if (avatar.id.Equals(currentUiAvatarId))
                            {
                                favButtonText.text = "Unfavorite";
                                found = true;
                                break;
                            }
                        }
                        if(!found) favButtonText.text = "Favorite";
                    }
                }

                //Show returned error if exists
                if(addError != null)
                {
                    VRCUiPopupManagerUtils.ShowPopup("Error", addError, "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    addError = null;
                }


                if (RoomManager.currentRoom != null && RoomManager.currentRoom.id != null && RoomManager.currentRoom.currentInstanceIdOnly != null)
                {
                    if(currentRoom == null)
                    {
                        currentRoom = RoomManager.currentRoom;

                        if (currentRoom.releaseStatus != "public")
                        {
                            VRCModLogger.Log("[AvatarFav] Current world release status isn't public. Pedestal scan disabled.");
                        }
                        else
                        {
                            VRC_AvatarPedestal[] pedestalsInWorld = GameObject.FindObjectsOfType<VRC_AvatarPedestal>();
                            VRCModLogger.Log("[AvatarFav] Found " + pedestalsInWorld.Length + " VRC_AvatarPedestal in current world");
                            string dataToSend = currentRoom.id;
                            foreach (VRC_AvatarPedestal p in pedestalsInWorld)
                            {
                                if (p.blueprintId == null || p.blueprintId == "")
                                    continue;

                                dataToSend += ";" + p.blueprintId;
                            }

                            VRCModNetworkManager.SendRPC("slaynash.avatarfav.avatarsinworld", dataToSend);
                        }
                    }
                }
                else
                {
                    currentRoom = null;
                }


            }
            catch (Exception e)
            {
                VRCModLogger.Log("[AvatarFav] [ERROR] " + e.ToString());
            }
            freshUpdate = false;
        }



        private IEnumerator CheckAndWearAvatar()
        {
            //DebugUtils.PrintHierarchy(pageAvatar.avatar.transform, 0);
            PipelineManager avatarPipelineManager = pageAvatar.avatar.GetComponentInChildren<PipelineManager>();
            if (avatarPipelineManager == null) // Avoid avatars locking for builds <625
            {
                if (pageAvatar.avatar.transform.GetChild(0).name == "avatar_loading2(Clone)")
                    VRCUiPopupManagerUtils.ShowPopup("Error", "Please wait for this avatar to finish loading before wearing it", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                else
                    baseChooseEvent.Invoke();
            }
            else
            {
                bool copied = false;

                string avatarBlueprintID = avatarPipelineManager?.blueprintId ?? "";
                if (!avatarBlueprintID.Equals("") && !avatarBlueprintID.Equals(pageAvatar.avatar.apiAvatar.id))
                    copied = true;

                using (WWW avtrRequest = new WWW(API.GetApiUrl() + "avatars/" + (copied ? avatarBlueprintID : pageAvatar.avatar.apiAvatar.id) + "?apiKey=" + API.ApiKey))
                {
                    yield return avtrRequest;
                    int rc = WebRequestsUtils.GetResponseCode(avtrRequest);
                    if (rc == 200)
                    {
                        try
                        {
                            string uuid = APIUser.CurrentUser?.id ?? "";
                            SerializableApiAvatar aa = JsonConvert.DeserializeObject<SerializableApiAvatar>(avtrRequest.text);
                            if (aa.releaseStatus.Equals("public") || aa.authorId.Equals(uuid))
                            {
                                baseChooseEvent.Invoke();
                            }
                            else
                            {
                                if(copied)
                                    VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not the original one, and the one is not public anymore (private)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                                else VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore (private)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                            }
                        }
                        catch (Exception e)
                        {
                            VRCModLogger.LogError(e.ToString());
                            VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: Unable to parse API response", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                        }
                    }
                    else
                    {
                        if (copied)
                            VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not the original one, and the one is not public anymore (deleted)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                        else VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore (deleted)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    }
                }
            }
        }

        private void UpdateFavList()
        {
            object[] parameters = new object[] { favoriteAvatarList };
            updateAvatarListMethod.Invoke(favList, parameters);
        }

        private void ToggleAvatarFavorite()
        {
            ApiAvatar currentApiAvatar = pageAvatar.avatar.apiAvatar;
            //Check if the current avatar is favorited, and ask to remove it from list if so
            foreach (ApiAvatar avatar in favoriteAvatarList)
            {
                if (avatar.id == currentApiAvatar.id)
                {
                    favButton.GetComponent<Button>().interactable = false;
                    ShowRemoveAvatarConfirmPopup(avatar.id);
                    return;
                }
            }
            //Else, add it to the favorite list
            if (currentApiAvatar.releaseStatus != "public")
            {
                VRCUiPopupManagerUtils.GetVRCUiPopupManager().ShowStandardPopup("Error", "Unable to favorite avatar :<br>This avatar is not public", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                return;
            }
            favButton.GetComponent<Button>().interactable = false;
            AddAvatar(currentApiAvatar.id);
        }

        private void ShowRemoveAvatarConfirmPopup(string avatarId)
        {
            VRCUiPopupManagerUtils.GetVRCUiPopupManager().ShowStandardPopup("AvatarFav", "Do you really want to unfavorite this avatar ?",
                "Yes", () => {
                    VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                    RemoveAvatar(avatarId);
                },
                "Cancel", () =>
                {
                    VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                    favButton.GetComponent<Button>().interactable = true;
                }
            );
        }





        private void RequestAvatars()
        {
            new Thread(() => VRCModNetworkManager.SendRPC("slaynash.avatarfav.getavatars", "", null, (error) =>
            {
                VRCModLogger.Log("[AvatarFav] Unable to fetch avatars: Server returned " + error);
                if (error.Equals("SERVER_DISCONNECTED"))
                {
                    waitingForServer = true;
                }
            })).Start();
        }
        private void AddAvatar(string id)
        {
            new Thread(() =>
            {
                VRCModNetworkManager.SendRPC("slaynash.avatarfav.addavatar", id, null, (error) =>
                {
                    addError = "Unable to favorite avatar: " + error;
                    favButton.GetComponent<Button>().interactable = true;
                });
            }).Start();
        }
        private void RemoveAvatar(string id)
        {
            new Thread(() =>
            {
                VRCModNetworkManager.SendRPC("slaynash.avatarfav.removeavatar", id, null, (error) =>
                {
                    addError = "Unable to unfavorite avatar: " + error;
                    favButton.GetComponent<Button>().interactable = true;
                });
            }).Start();
        }
    }
}
