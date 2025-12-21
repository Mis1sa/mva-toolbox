using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    /// <summary>
    /// 参数扫描服务
    /// 从 ContactReceiver / PhysBone 等组件扫描参数候选
    /// </summary>
    public static class ParameterScanService
    {
        /// <summary>
        /// 参数信息
        /// </summary>
        public class ParameterInfo
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

        /// <summary>
        /// PhysBone 参数组
        /// </summary>
        public class PhysBoneParameterGroup
        {
            public string BaseName;
            public List<ParameterInfo> Parameters;
        }

        /// <summary>
        /// 扫描结果
        /// </summary>
        public class ScanResult
        {
            public List<ParameterInfo> AllParameters = new List<ParameterInfo>();
            public List<ParameterInfo> ContactReceiverParameters = new List<ParameterInfo>();
            public List<PhysBoneParameterGroup> PhysBoneGroups = new List<PhysBoneParameterGroup>();
        }

        /// <summary>
        /// 执行参数扫描
        /// </summary>
        /// <param name="targetRoot">目标根物体</param>
        /// <returns>扫描结果</returns>
        public static ScanResult Execute(GameObject targetRoot)
        {
            var result = new ScanResult();
            
            if (targetRoot == null)
                return result;

            var allComponents = targetRoot.GetComponentsInChildren<Component>(true);
            var paramDict = new Dictionary<string, ParameterInfo>();
            var physBoneParamDict = new Dictionary<string, List<ParameterInfo>>();

            // 扫描所有组件
            for (int i = 0; i < allComponents.Length; i++)
            {
                var component = allComponents[i];
                if (component == null) continue;

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

            // 整理扫描结果
            foreach (var param in paramDict.Values)
            {
                result.AllParameters.Add(param);
                
                if (param.IsFromPhysBone && !string.IsNullOrEmpty(param.PhysBoneBaseName))
                {
                    if (!physBoneParamDict.TryGetValue(param.PhysBoneBaseName, out var list))
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

            // 构建 PhysBone 参数组
            foreach (var kv in physBoneParamDict.OrderBy(x => x.Key))
            {
                result.PhysBoneGroups.Add(new PhysBoneParameterGroup
                {
                    BaseName = kv.Key,
                    Parameters = kv.Value.OrderBy(p => p.Name).ToList()
                });
            }

            // 对 ContactReceiver 参数排序
            result.ContactReceiverParameters.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));

            return result;
        }

        /// <summary>
        /// 扫描 ContactReceiver 组件
        /// </summary>
        private static void ScanContactReceiver(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            var parameterProp = so.FindProperty("parameter");
            var receiverTypeProp = so.FindProperty("receiverType");
            
            if (parameterProp == null || receiverTypeProp == null) 
                return;

            string paramName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(paramName)) 
                return;

            int receiverType = receiverTypeProp.intValue;
            var paramType = receiverType == 2
                ? AnimatorControllerParameterType.Float
                : AnimatorControllerParameterType.Bool;

            // 如果参数已存在，合并信息
            if (paramDict.TryGetValue(paramName, out var existing))
            {
                if (existing.Type == AnimatorControllerParameterType.Float && paramType == AnimatorControllerParameterType.Bool)
                {
                    existing.Type = AnimatorControllerParameterType.Bool;
                    existing.SourceComponent += $", {component.GetType().Name}";
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

        /// <summary>
        /// 扫描 PhysBone 组件
        /// </summary>
        private static void ScanPhysBone(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            var parameterProp = so.FindProperty("parameter");
            
            if (parameterProp == null) 
                return;

            string baseParamName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(baseParamName)) 
                return;

            // PhysBone 的标准后缀
            string[] suffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
            
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                string paramName = baseParamName + suffix;
                
                if (paramDict.ContainsKey(paramName))
                    continue;

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
