using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
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
using VRCTools;
using VRCTools.networking;

namespace AvatarFav
{
    [VRCModInfo("AvatarFav", "1.0", "Slaynash")]
    public class AvatarFavMod : VRCMod
    {


        public static AvatarFavMod instance;


        private static MethodInfo updateAvatarListMethod;

        private static List<ApiAvatar> favoriteAvatarList = new List<ApiAvatar>();
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



                VRCModLogger.Log("[AvatarFav] Adding button to UI - Duplicating Button");
                favButton = DuplicateButton(changeButton, "Favorite", new Vector2(0, 80));
                favButton.name = "ToggleFavorite";
                favButton.gameObject.SetActive(false);
                favButtonText = favButton.Find("Text").GetComponent<Text>();
                favButton.GetComponent<Button>().interactable = false;

                favButton.GetComponent<Button>().onClick.AddListener(ToggleAvatarFavorite);

                VRCModLogger.Log("[AvatarFav] Storing default AvatarModel position");
                avatarModel = pageAvatar.transform.Find("AvatarModel");
                baseAvatarModelPosition = avatarModel.localPosition;



                VRCModLogger.Log("[AvatarFav] Looking up for dev avatar list");
                UiAvatarList[] uiAvatarLists = Resources.FindObjectsOfTypeAll<UiAvatarList>();

                VRCModLogger.Log("[AvatarFav] Found " + uiAvatarLists.Length + " UiAvatarList");

                // Get "developper" list as favList
                FieldInfo categoryField = typeof(UiAvatarList).GetField("category", BindingFlags.Public | BindingFlags.Instance);
                favList = uiAvatarLists.First((list) => (int)categoryField.GetValue(list) == 0);

                VRCModLogger.Log("[AvatarFav] Updating list name and activating");
                // Enable list and change name
                favList.GetComponentInChildren<Button>(true).GetComponentInChildren<Text>().text = "Favorite";
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
                VRCModLogger.Log("[AvatarFav] Looking up for the real UpdateAvatar method (Found " + tmp1.ToList().Count + " mathching methods)");
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
                        if(favList.pickers.Count == 0 || (favList.pickers.Count == lastPickerCound && lastPickerCound != favoriteAvatarList.Count))
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

                        foreach (ApiAvatar avatar in favoriteAvatarList)
                        {
                            if (avatar.id.Equals(currentUiAvatarId))
                            {
                                favButtonText.text = "Unfavorite";
                                return;
                            }
                        }
                        favButtonText.text = "Favorite";
                    }
                }

                //Show returned error if exists
                if(addError != null)
                {
                    VRCUiPopupManagerUtils.ShowPopup("Error", addError, "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    addError = null;
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
            using (WWW avtrRequest = new WWW(API.GetApiUrl() + "avatars/" + pageAvatar.avatar.apiAvatar.id + "?apiKey=" + API.ApiKey))
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
                            VRCModLogger.Log("[AvatarFav] Invoking baseChooseEvent (" + baseChooseEvent + ")");
                            baseChooseEvent.Invoke();
                        }
                        else
                        {
                            VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
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
                    VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                }
            }
        }

        private void UpdateFavList()
        {
            object[] parameters = new object[] { favoriteAvatarList };
            updateAvatarListMethod.Invoke(favList, parameters);


            // Add API request before picking an avatar (avoid picking a private avatar)
            for (int i = 0; i < favList.pickers.Count; i++)
            {
                int index = i;

                VRCUiContentButton picker = favList.pickers[i];
                Action baseAction = applyAvatarField.GetValue(picker) as Action;

                ApiDictContainer responseContainer = new ApiDictContainer(new string[] { "releaseStatus" })
                {
                    OnSuccess = (c) =>
                    {
                        if (((ApiDictContainer)c).ResponseDictionary.TryGetValue("releaseStatus", out object releaseStatus))
                        {
                            VRCModLogger.Log("Server returned releaseStatus: " + releaseStatus);
                            if ("public".Equals((string)releaseStatus))
                            {
                                baseAction();
                            }
                            else
                            {
                                VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to pick avatar: This avatar is now private", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                            }
                        }
                    },
                    OnError = (c) =>
                    {
                        VRCModLogger.Log("Unable to fetch avatar releaseStatus from API");
                        VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to fetch favorited avatar from VRChat API", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    }
                };

                Action newAction = () =>
                {
                    VRCModLogger.Log("Fetching avatar releaseStatus for " + favoriteAvatarList[index].name + " (" + favoriteAvatarList[index].id + ")");
                    API.SendRequest("avatars/" + favoriteAvatarList[index].id, VRC.Core.BestHTTP.HTTPMethods.Get, responseContainer, null, true, true, true);
                };

                applyAvatarField.SetValue(picker, newAction);
            }
        }

        private void ToggleAvatarFavorite()
        {
            ApiAvatar currentApiAvatar = pageAvatar.avatar.apiAvatar;
            foreach (ApiAvatar avatar in favoriteAvatarList)
            {
                if (avatar.id == currentApiAvatar.id)
                {
                    favButton.GetComponent<Button>().interactable = false;
                    ShowRemoveAvatarConfirmPopup(avatar.id);
                    return;
                }
            }

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

        public static Transform DuplicateButton(Transform baseButton, string buttonText, Vector2 posDelta)
        {
            GameObject buttonGO = new GameObject("DuplicatedButton", new Type[] {
                typeof(Button),
                typeof(Image)
            });

            RectTransform rtO = baseButton.GetComponent<RectTransform>();
            RectTransform rtT = buttonGO.GetComponent<RectTransform>();

            buttonGO.transform.SetParent(baseButton.parent);
            buttonGO.GetComponent<Image>().sprite = baseButton.GetComponent<Image>().sprite;
            buttonGO.GetComponent<Image>().type = baseButton.GetComponent<Image>().type;
            buttonGO.GetComponent<Image>().fillCenter = baseButton.GetComponent<Image>().fillCenter;
            buttonGO.GetComponent<Button>().targetGraphic = buttonGO.GetComponent<Image>();

            rtT.localScale = rtO.localScale;

            rtT.anchoredPosition = rtO.anchoredPosition;
            rtT.sizeDelta = rtO.sizeDelta;

            rtT.localPosition = rtO.localPosition + new Vector3(posDelta.x, posDelta.y, 0);
            rtT.localRotation = rtO.localRotation;

            GameObject textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(buttonGO.transform);

            RectTransform rtO2 = baseButton.Find("Text").GetComponent<RectTransform>();
            RectTransform rtT2 = textGO.GetComponent<RectTransform>();
            rtT2.localScale = rtO2.localScale;

            rtT2.anchorMin = rtO2.anchorMin;
            rtT2.anchorMax = rtO2.anchorMax;
            rtT2.anchoredPosition = rtO2.anchoredPosition;
            rtT2.sizeDelta = rtO2.sizeDelta;

            rtT2.localPosition = rtO2.localPosition;
            rtT2.localRotation = rtO2.localRotation;

            Text tO = baseButton.Find("Text").GetComponent<Text>();
            Text tT = textGO.GetComponent<Text>();
            tT.text = buttonText;
            tT.font = tO.font;
            tT.fontSize = tO.fontSize;
            tT.fontStyle = tO.fontStyle;
            tT.alignment = tO.alignment;
            tT.color = tO.color;

            return buttonGO.transform;
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


        // DEBUG

        private void PrintHierarchy(Transform transform, int depth)
        {
            String s = "";
            for (int i = 0; i < depth; i++) s += "\t";
            s += transform.name + " [";

            MonoBehaviour[] mbs = transform.GetComponents<MonoBehaviour>();
            for (int i = 0; i < mbs.Length; i++)
            {
                if (mbs[i] == null) continue;
                if (i == 0)
                    s += mbs[i].GetType();
                else
                    s += ", " + mbs[i].GetType();
            }

            s += "]";
            VRCModLogger.Log(s);
            foreach (Transform t in transform)
            {
                if (t != null) PrintHierarchy(t, depth + 1);
            }
        }
    }
}
