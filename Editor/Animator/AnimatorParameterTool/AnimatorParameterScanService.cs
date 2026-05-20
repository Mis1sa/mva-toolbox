using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal static class AnimatorParameterScanService
    {
        internal sealed class ParameterInfo
        {
            public string Name;
            public AnimatorControllerParameterType Type;
            public bool IsSelected;
            public bool DefaultBool;
            public float DefaultFloat;
            public int DefaultInt;
            public string SourceComponent;
            public bool IsFromPhysBone;
            public string PhysBoneSuffix;
            public string PhysBoneBaseName;
        }

        internal sealed class PhysBoneParameterGroup
        {
            public string BaseName;
            public List<ParameterInfo> Parameters;
        }

        internal sealed class ScanResult
        {
            public List<ParameterInfo> AllParameters = new List<ParameterInfo>();
            public List<ParameterInfo> ContactReceiverParameters = new List<ParameterInfo>();
            public List<PhysBoneParameterGroup> PhysBoneGroups = new List<PhysBoneParameterGroup>();
        }

        internal static ScanResult Execute(GameObject targetRoot)
        {
            var result = new ScanResult();
            if (targetRoot == null)
            {
                return result;
            }

            Component[] allComponents = targetRoot.GetComponentsInChildren<Component>(true);
            var paramDict = new Dictionary<string, ParameterInfo>();
            var physBoneParamDict = new Dictionary<string, List<ParameterInfo>>();

            for (int i = 0; i < allComponents.Length; i++)
            {
                Component component = allComponents[i];
                if (component == null)
                {
                    continue;
                }

                string componentTypeName = component.GetType().FullName;
                if (componentTypeName.Contains("VRCContactReceiver") || componentTypeName.Contains("ContactReceiver"))
                {
                    ScanContactReceiver(component, paramDict);
                }
                else if (componentTypeName.Contains("VRCPhysBone") || componentTypeName.Contains("PhysBone"))
                {
                    ScanPhysBone(component, paramDict);
                }
            }

            foreach (ParameterInfo param in paramDict.Values)
            {
                result.AllParameters.Add(param);
                if (param.IsFromPhysBone && !string.IsNullOrEmpty(param.PhysBoneBaseName))
                {
                    if (!physBoneParamDict.TryGetValue(param.PhysBoneBaseName, out List<ParameterInfo> list))
                    {
                        list = new List<ParameterInfo>();
                        physBoneParamDict[param.PhysBoneBaseName] = list;
                    }

                    list.Add(param);
                }
                else
                {
                    result.ContactReceiverParameters.Add(param);
                }
            }

            foreach (KeyValuePair<string, List<ParameterInfo>> kv in physBoneParamDict.OrderBy(x => x.Key))
            {
                result.PhysBoneGroups.Add(new PhysBoneParameterGroup
                {
                    BaseName = kv.Key,
                    Parameters = kv.Value.OrderBy(p => p.Name).ToList()
                });
            }

            result.ContactReceiverParameters.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            return result;
        }

        private static void ScanContactReceiver(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            SerializedProperty parameterProp = so.FindProperty("parameter");
            SerializedProperty receiverTypeProp = so.FindProperty("receiverType");
            if (parameterProp == null || receiverTypeProp == null)
            {
                return;
            }

            string paramName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(paramName))
            {
                return;
            }

            int receiverType = receiverTypeProp.intValue;
            AnimatorControllerParameterType paramType = receiverType == 2
                ? AnimatorControllerParameterType.Float
                : AnimatorControllerParameterType.Bool;

            if (paramDict.TryGetValue(paramName, out ParameterInfo existing))
            {
                if (existing.Type == AnimatorControllerParameterType.Float && paramType == AnimatorControllerParameterType.Bool)
                {
                    existing.Type = AnimatorControllerParameterType.Bool;
                    existing.SourceComponent += ", " + component.GetType().Name;
                }

                return;
            }

            paramDict[paramName] = new ParameterInfo
            {
                Name = paramName,
                Type = paramType,
                IsSelected = false,
                DefaultBool = false,
                DefaultFloat = 0f,
                DefaultInt = 0,
                SourceComponent = component.GetType().Name,
                IsFromPhysBone = false,
                PhysBoneSuffix = string.Empty,
                PhysBoneBaseName = string.Empty
            };
        }

        private static void ScanPhysBone(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            SerializedProperty parameterProp = so.FindProperty("parameter");
            if (parameterProp == null)
            {
                return;
            }

            string baseParamName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(baseParamName))
            {
                return;
            }

            string[] suffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                string paramName = baseParamName + suffix;
                if (paramDict.ContainsKey(paramName))
                {
                    continue;
                }

                paramDict[paramName] = new ParameterInfo
                {
                    Name = paramName,
                    Type = AnimatorControllerParameterType.Float,
                    IsSelected = false,
                    DefaultBool = false,
                    DefaultFloat = 0f,
                    DefaultInt = 0,
                    SourceComponent = component.GetType().Name,
                    IsFromPhysBone = true,
                    PhysBoneSuffix = suffix,
                    PhysBoneBaseName = baseParamName
                };
            }
        }
    }
}
