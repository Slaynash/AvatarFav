using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRCModLoader;
using VRCTools;

namespace AvatarFav
{
    public static class AvatarPageHelper
    {
        internal static FieldInfo fieldCachedSpecificList;

        public static UiAvatarList AddNewList(string title, int index)
        {
            UiAvatarList[] uiAvatarLists = Resources.FindObjectsOfTypeAll<UiAvatarList>();
            
            if(uiAvatarLists.Length == 0)
            {
                VRCModLogger.LogError("[AvatarFav] No UiAvatarList found !");
                return null;
            }
            
            UiAvatarList gameFavList = null;
            foreach(UiAvatarList list in uiAvatarLists)
            {
                if(((int)AvatarFavMod.categoryField.GetValue(list)) != 0)
                {
                    gameFavList = list;
                    break;
                }
            }
            if(gameFavList == null)
            {
                VRCModLogger.LogError("[AvatarFav] No UiAvatarList of category other than 0 found !");
                return null;
            }
            UiAvatarList newList = GameObject.Instantiate(gameFavList, gameFavList.transform.parent);


            newList.GetComponentInChildren<Button>(true).GetComponentInChildren<Text>().text = title;
            newList.gameObject.SetActive(true);
            
            newList.transform.SetSiblingIndex(index);
            
            typeof(UiAvatarList).GetField("category", BindingFlags.Public | BindingFlags.Instance).SetValue(newList, 4); // set the category to SpecificList

            return newList;
        }

        public static void ClearSpecificList(this UiAvatarList list)
        {
            if(fieldCachedSpecificList == null)
            {
                FieldInfo[] npInstFields = typeof(UiAvatarList).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach(FieldInfo fi in npInstFields)
                {
                    if(fi.FieldType == typeof(Dictionary<string, ApiAvatar>))
                    {
                        fieldCachedSpecificList = fi;
                        VRCModLogger.Log("fieldCachedSpecificList: " + fieldCachedSpecificList);
                        break;
                    }
                }
                if (fieldCachedSpecificList == null)
                {
                    VRCModLogger.LogError("[AvatarFav] No CachedSpecificList field found in UiAvatarList !");
                    return;
                }
            }
            ((Dictionary<string, ApiAvatar>)fieldCachedSpecificList.GetValue(list)).Clear();
            list.ClearAll();

            VRCModLogger.Log("Number of elements in list after clear: " + ((Dictionary<string, ApiAvatar>)fieldCachedSpecificList.GetValue(list)).Count);
        }
    }
}
