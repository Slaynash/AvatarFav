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
    [VRCModInfo("AvatarFav", "1.2.6", "Slaynash")]
    public class AvatarFavMod : VRCMod
    {


        public static AvatarFavMod instance;

        private static string _apiKey = null;
        private static int _buildNumber = -1;


        //private static MethodInfo updateAvatarListMethod;

        //internal static List<ApiAvatar> favoriteAvatarList = new List<ApiAvatar>();
        internal static List<string> favoriteAvatarList = new List<string>();
        private static bool avatarAvailables = false;
        private bool freshUpdate = false;
        private bool waitingForServer = false;
        private bool newAvatarsFirst = true;
        private static UiAvatarList favList;
        private static Transform favButton;
        private static Text favButtonText;
        private static PageAvatar pageAvatar;
        private static Transform avatarModel;
        private static FieldInfo applyAvatarField;

        private static Vector3 baseAvatarModelPosition;
        private static string currentUiAvatarId = "";

        private bool alreadyLoaded = false;
        private bool initialised = false;
        private string addError;

        private Button.ButtonClickedEvent baseChooseEvent;

        private ApiWorld currentRoom;
        private UiAvatarList avatarSearchList;
        private UiInputField searchbar;


        internal static FieldInfo categoryField;
        private List<Action> actions = new List<Action>();

        void OnApplicationStart()
        {
            VRCTools.ModPrefs.RegisterCategory("avatarfav", "AvatarFav");
            VRCTools.ModPrefs.RegisterPrefBool("avatarfav", "newavatarsfirst", newAvatarsFirst, "Show new avatars first");

            newAvatarsFirst = VRCTools.ModPrefs.GetBool("avatarfav", "newavatarsfirst");
            
            categoryField = typeof(UiAvatarList).GetField("category", BindingFlags.Public | BindingFlags.Instance);
        }

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
                VRCModLogger.Log("[AvatarFav] Adding button to UI - Looking up for Change Button");
                // Add a "Favorite" / "Unfavorite" button over the "Choose" button of the AvatarPage
                
                VRCUiPage[] pages = Resources.FindObjectsOfTypeAll<VRCUiPage>();
                for (int i = 0; i < pages.Length; i++)
                {
                    if (pages[i].displayName == "Avatars")
                    {
                        pageAvatar = (PageAvatar)pages[i];
                        break;
                    }
                }
                
                Transform changeButton = pageAvatar.transform.Find("Change Button");

                VRCModLogger.Log("[AvatarFav] Adding avatar check on Change button");

                baseChooseEvent = changeButton.GetComponent<Button>().onClick;

                changeButton.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                changeButton.GetComponent<Button>().onClick.AddListener(() =>
                {
                    VRCModLogger.Log("[AvatarFav] Fetching avatar releaseStatus for " + pageAvatar.avatar.apiAvatar.name + " (" + pageAvatar.avatar.apiAvatar.id + ")");
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

                
                favList = AvatarPageHelper.AddNewList("Favorite (Unofficial)", 1);



                // Get Getter of VRCUiContentButton.PressAction
                applyAvatarField = typeof(VRCUiContentButton).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).First((field) => field.FieldType == typeof(Action));

                VRCModLogger.Log("[AvatarFav] Registering VRCModNetwork events");
                VRCModNetworkManager.OnAuthenticated += () =>
                {
                    RequestAvatars();
                };

                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.serverconnected", (senderId, data) => { if (waitingForServer) RequestAvatars(); });
                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.error", (senderId, data) => addError = data);
                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.avatarlistupdated", (senderId, data) =>
                {
                    lock (favoriteAvatarList)
                    {
                        // Update Ui
                        favButton.GetComponent<Button>().interactable = true;
                        SerializableApiAvatar[] serializedAvatars = SerializableApiAvatar.ParseJson(data);
                        favoriteAvatarList.Clear();
                        foreach (SerializableApiAvatar serializedAvatar in serializedAvatars)
                            favoriteAvatarList.Add(serializedAvatar.id);

                        avatarAvailables = true;
                    }
                });




                VRCModLogger.Log("[AvatarFav] Adding avatar search list");

                if (pageAvatar != null)
                {
                    VRCUiPageHeader pageheader = VRCUiManagerUtils.GetVRCUiManager().GetComponentInChildren<VRCUiPageHeader>(true);
                    if (pageheader != null)
                    {
                        searchbar = pageheader.searchBar;
                        if (searchbar != null)
                        {
                            VRCModLogger.Log("[AvatarFav] creating avatar search list");
                            avatarSearchList = AvatarPageHelper.AddNewList("Search Results", 0);
                            avatarSearchList.ClearAll();
                            avatarSearchList.gameObject.SetActive(false);
                            avatarSearchList.collapsedCount = 50;
                            avatarSearchList.expandedCount = 50;
                            avatarSearchList.collapseRows = 5;
                            avatarSearchList.extendRows = 5;
                            avatarSearchList.contractedHeight = 850f;
                            avatarSearchList.expandedHeight = 850f;
                            avatarSearchList.GetComponent<LayoutElement>().minWidth = 1600f;
                            avatarSearchList.GetComponentInChildren<GridLayoutGroup>(true).constraintCount = 5;
                            avatarSearchList.expandButton.image.enabled = false;

                            VRCModLogger.Log("[AvatarFav] Overwriting search button");
                            VRCUiManagerUtils.OnPageShown += (page) =>
                            {
                                if (page.GetType() == typeof(PageAvatar))
                                {
                                    UiVRCList[] lists = page.GetComponentsInChildren<UiVRCList>(true);
                                    foreach(UiVRCList list in lists)
                                    {
                                        if (list != avatarSearchList && (list.GetType() != typeof(UiAvatarList) || ((int)categoryField.GetValue(list)) != 0))
                                            list.gameObject.SetActive(true);
                                        else
                                            list.gameObject.SetActive(false);
                                    }
                                    VRCModLogger.Log("[AvatarFav] PageAvatar shown. Enabling searchbar next frame");
                                    ModManager.StartCoroutine(EnableSearchbarNextFrame());
                                }
                            };

                            VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.searchresults", (senderid, data) =>
                            {
                                AddMainAction(() =>
                                {
                                    if (data.StartsWith("ERROR"))
                                    {
                                        VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Unable to fetch avatars: Server returned error: " + data.Substring("ERROR ".Length), "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                                    }
                                    else
                                    {
                                        avatarSearchList.ClearSpecificList();
                                        if (!avatarSearchList.gameObject.activeSelf)
                                        {
                                            UiVRCList[] lists = pageAvatar.GetComponentsInChildren<UiVRCList>(true);
                                            foreach (UiVRCList list in lists)
                                            {
                                                if (list != avatarSearchList)
                                                    list.gameObject.SetActive(false);
                                            }
                                        }

                                        SerializableApiAvatar[] serializedAvatars = SerializableApiAvatar.ParseJson(data);

                                        string[] avatarsIds = new string[serializedAvatars.Length];

                                        for (int i = 0; i < serializedAvatars.Length; i++) avatarsIds[i] = serializedAvatars[i].id;

                                        avatarSearchList.specificListIds = avatarsIds;
                                        if (avatarSearchList.gameObject.activeSelf)
                                            avatarSearchList.Refresh();
                                        else
                                            avatarSearchList.gameObject.SetActive(true);
                                    }
                                });
                            });
                        }
                        else
                            VRCModLogger.LogError("[AvatarFav] Unable to find search bar");
                    }
                    else
                        VRCModLogger.LogError("[AvatarFav] Unable to find page header");
                }
                else
                    VRCModLogger.LogError("[AvatarFav] Unable to find avatar page");
                




                VRCModLogger.Log("[AvatarFav] AvatarFav Initialised !");
                initialised = true;
            }
        }

        private void AddMainAction(Action a)
        {
            lock (actions)
            {
                actions.Add(a);
            }
        }

        private IEnumerator EnableSearchbarNextFrame()
        {
            yield return null;
            searchbar.editButton.interactable = true;
            searchbar.onDoneInputting = (text) =>
            {
                SearchForAvatars(text);
            };
        }

        private void SearchForAvatars(string text)
        {
            VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Looking for avatars");
            VRCModNetworkManager.SendRPC("slaynash.avatarfav.search", text.Trim(), () => { }, (error) => {
                VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Unable to fetch avatars:\nVRVCModNetwork returned error:\n" + error, "Close", () =>  VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
            });
        }

        public void OnUpdate()
        {
            if (!initialised) return;

            lock (actions)
            {
                foreach (Action a in actions)
                {
                    try
                    {
                        a();
                    }
                    catch (Exception e)
                    {
                        VRCModLogger.Log("[AvatarFav] Error while calling action from main thread: " + e);
                    }
                }
                actions.Clear();
            }

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
                            favList.ClearSpecificList();

                            if (newAvatarsFirst)
                            {
                                List<string> favReversed = favoriteAvatarList.ToList();
                                favReversed.Reverse();
                                favList.specificListIds = favReversed.ToArray();
                            }
                            else
                                favList.specificListIds = favoriteAvatarList.ToArray();
                            

                            favList.Refresh();

                            freshUpdate = true;
                        }
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
                        
                        foreach (string avatarId in favoriteAvatarList)
                        {
                            if (avatarId == currentUiAvatarId)
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
                VRCModLogger.Log("[AvatarFav] Current avatar prefab name: " + pageAvatar.avatar.transform.GetChild(0).name);
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

                using (WWW avtrRequest = new WWW(API.GetApiUrl() + "avatars/" + (copied ? avatarBlueprintID : pageAvatar.avatar.apiAvatar.id) + "?apiKey=" + GetApiKey()))
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

        internal static string GetApiKey()
        {
            if(_apiKey == null)
            {
                VRCModLogger.Log("[AvatarFav] Trying to get ApiKey from VRC.Core.API");
                foreach (FieldInfo fi_ in typeof(API).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (fi_ != null && fi_.Name == "ApiKey")
                    {
                        _apiKey = fi_.GetValue(null) as string;
                        break;
                    }
                }
                if(_apiKey == null)
                {
                    VRCModLogger.Log("[AvatarFav] Unable to find field ApiKey in VRC.Core.API. Using default key.");
                    _apiKey = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
                }
                VRCModLogger.Log("[AvatarFav] Current ApiKey: " + _apiKey);
            }
            return _apiKey;
        }

        internal static int GetBuildNumber()
        {
            if (_buildNumber == -1)
            {
                VRCModLogger.Log("[AvatarFav] Fetching build number");
                PropertyInfo vrcApplicationSetupInstanceProperty = typeof(VRCApplicationSetup).GetProperties(BindingFlags.Public | BindingFlags.Static).First((pi) => pi.PropertyType == typeof(VRCApplicationSetup));
                _buildNumber = ((VRCApplicationSetup)vrcApplicationSetupInstanceProperty.GetValue(null, null)).buildNumber;
                VRCModLogger.Log("[AvatarFav] Game build " + _buildNumber);
            }
            return _buildNumber;
        }
        /*
        private void UpdateFavList()
        {
            object[] parameters = new object[] { favoriteAvatarList };
            updateAvatarListMethod.Invoke(favList, parameters);
        }
        */
        private void ToggleAvatarFavorite()
        {
            ApiAvatar currentApiAvatar = pageAvatar.avatar.apiAvatar;
            //Check if the current avatar is favorited, and ask to remove it from list if so
            foreach (string avatarId in favoriteAvatarList)
            {
                if (avatarId == currentApiAvatar.id)
                {
                    favButton.GetComponent<Button>().interactable = false;
                    ShowRemoveAvatarConfirmPopup(avatarId);
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
