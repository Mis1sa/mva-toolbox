using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Shared
{
    /// <summary>
    /// 共享上下文：统一管理目标对象、控制器列表、选中的控制器与层
    /// 复用 ToolboxUtils 中的方法
    /// </summary>
    public sealed class QuickAnimatorEditContext
    {
        // 目标对象（Avatar / GameObject / AnimatorController 资产）
        private Object _targetObject;

        // 从目标解析出的组件（可空）
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;

        // 控制器列表与显示名
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();

        private readonly Dictionary<AnimatorController, Dictionary<string, (bool saved, bool synced)>> _maParameterDefaults
            = new Dictionary<AnimatorController, Dictionary<string, (bool saved, bool synced)>>();

        private bool _includeMAParametersControllers;

        // 选中索引
        private int _selectedControllerIndex;
        private int _selectedLayerIndex;

        #region 属性

        public Object TargetObject => _targetObject;
        public VRCAvatarDescriptor AvatarDescriptor => _avatarDescriptor;
        public Animator Animator => _animator;
        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;

        public int SelectedControllerIndex
        {
            get => _selectedControllerIndex;
            set => _selectedControllerIndex = Mathf.Clamp(value, 0, Mathf.Max(0, _controllers.Count - 1));
        }

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set
            {
                var controller = SelectedController;
                int maxLayer = controller != null && controller.layers != null ? controller.layers.Length - 1 : 0;
                _selectedLayerIndex = Mathf.Clamp(value, 0, Mathf.Max(0, maxLayer));
            }
        }

        public AnimatorController SelectedController
        {
            get
            {
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                    return null;
                return _controllers[_selectedControllerIndex];
            }
        }

        public bool TryGetMAParameterDefaults(AnimatorController controller, out Dictionary<string, (bool saved, bool synced)> defaults)
        {
            return _maParameterDefaults.TryGetValue(controller, out defaults);
        }

        public AnimatorControllerLayer SelectedLayer
        {
            get
            {
                var controller = SelectedController;
                if (controller == null || controller.layers == null || controller.layers.Length == 0)
                    return null;
                int index = Mathf.Clamp(_selectedLayerIndex, 0, controller.layers.Length - 1);
                return controller.layers[index];
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置目标对象并刷新控制器列表
        /// </summary>
        public void SetTarget(Object target)
        {
            _targetObject = target;
            RefreshComponents();
            RefreshControllers();
        }

        public void SetIncludeMAParametersControllers(bool enabled)
        {
            if (_includeMAParametersControllers == enabled)
            {
                return;
            }

            _includeMAParametersControllers = enabled;
            RefreshControllers();
        }

        /// <summary>
        /// 绘制目标选择 UI（供 Window 调用）
        /// </summary>
        public bool DrawTargetSelectionUI()
        {
            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField(
                "Avatar / Animator / 控制器",
                _targetObject,
                typeof(Object),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                SetTarget(newTarget);
                return true;
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入 VRChat Avatar、带 Animator 组件的物体，或直接拖入动画控制器。", MessageType.Info);
            }
            else if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在当前目标中未找到任何 AnimatorController。", MessageType.Warning);
            }

            return false;
        }

        /// <summary>
        /// 绘制控制器与层级选择 UI（供 Window 调用）
        /// </summary>
        /// <param name="enableLayerSelection">是否启用层级选择</param>
        public bool DrawControllerAndLayerSelectionUI(bool enableLayerSelection = true)
        {
            if (_controllers.Count == 0)
                return false;

            bool changed = false;

            // 控制器选择
            string[] controllerDisplayNames = new string[_controllerNames.Count];
            for (int i = 0; i < _controllerNames.Count; i++)
            {
                controllerDisplayNames[i] = _controllerNames[i];
            }

            EditorGUI.BeginChangeCheck();
            int newControllerIndex = EditorGUILayout.Popup("控制器", _selectedControllerIndex, controllerDisplayNames);
            if (EditorGUI.EndChangeCheck())
            {
                SelectedControllerIndex = newControllerIndex;
                _selectedLayerIndex = 0;
                changed = true;
            }

            // 层级选择
            var controller = SelectedController;
            if (controller != null && controller.layers != null && controller.layers.Length > 0)
            {
                var layers = controller.layers;
                string[] layerNames = new string[layers.Length];
                for (int i = 0; i < layers.Length; i++)
                {
                    layerNames[i] = string.IsNullOrEmpty(layers[i].name) ? $"Layer {i}" : layers[i].name;
                }

                EditorGUI.BeginDisabledGroup(!enableLayerSelection);
                EditorGUI.BeginChangeCheck();
                int displayLayerIndex = enableLayerSelection ? _selectedLayerIndex : 0;
                int newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerNames);
                if (EditorGUI.EndChangeCheck() && enableLayerSelection)
                {
                    SelectedLayerIndex = newLayerIndex;
                    changed = true;
                }
                EditorGUI.EndDisabledGroup();
            }

            return changed;
        }

        #endregion

        #region 私有方法

        private void RefreshComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetObject is GameObject go)
            {
                _avatarDescriptor = ToolboxUtils.GetAvatarDescriptor(go);
                _animator = go.GetComponent<Animator>();
            }
        }

        private void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _maParameterDefaults.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = 0;

            if (_targetObject == null)
                return;

            // 如果目标是 AnimatorController 资产
            if (_targetObject is AnimatorController controllerAsset)
            {
                _controllers.Add(controllerAsset);
                _controllerNames.Add(controllerAsset.name);
                return;
            }

            // 如果目标是 GameObject，使用 ToolboxUtils 收集控制器
            if (_targetObject is GameObject root)
            {
                _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(root, includeSpecialLayers: true));

                if (_includeMAParametersControllers)
                {
                    AppendMAParametersControllers(root);
                }

                if (_controllers.Count > 0)
                {
                    _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));

                    // Avatar 目标时优先选择 FX 控制器
                    if (_avatarDescriptor != null)
                    {
                        var fxController = ToolboxUtils.GetExistingFXController(_avatarDescriptor);
                        if (fxController != null)
                        {
                            int fxIndex = _controllers.FindIndex(c => c == fxController);
                            if (fxIndex >= 0)
                            {
                                _selectedControllerIndex = fxIndex;
                            }
                        }
                    }
                }
            }
        }

        private void AppendMAParametersControllers(GameObject root)
        {
            if (root == null) return;

            Type maParamsType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && maParamsType == null; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                maParamsType = asm.GetType("nadena.dev.modular_avatar.core.ModularAvatarParameters");
            }
            if (maParamsType == null) return;

            var comps = root.GetComponentsInChildren<Component>(true);
            if (comps == null || comps.Length == 0) return;

            var maComponents = new List<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                if (c.GetType() == maParamsType)
                {
                    maComponents.Add(c);
                }
            }

            if (maComponents.Count == 0) return;

            var parametersField = maParamsType.GetField("parameters", BindingFlags.Public | BindingFlags.Instance);
            if (parametersField == null) return;

            for (int i = 0; i < maComponents.Count; i++)
            {
                var component = maComponents[i];
                if (component == null) continue;

                if (!TryBuildMAParametersController(component, parametersField, i + 1, out var controller, out var defaults))
                {
                    continue;
                }

                if (controller == null) continue;

                _controllers.Add(controller);
                if (defaults != null)
                {
                    _maParameterDefaults[controller] = defaults;
                }
            }
        }

        private static bool TryBuildMAParametersController(
            Component component,
            FieldInfo parametersField,
            int index,
            out AnimatorController controller,
            out Dictionary<string, (bool saved, bool synced)> defaults)
        {
            controller = null;
            defaults = null;

            if (component == null || parametersField == null) return false;

            object listObj;
            try
            {
                listObj = parametersField.GetValue(component);
            }
            catch
            {
                return false;
            }

            if (listObj is not System.Collections.IEnumerable enumerable)
            {
                return false;
            }

            var name = component.gameObject != null ? component.gameObject.name : "(GameObject)";
            controller = new AnimatorController
            {
                name = $"[MA Parameters] {name}[{index}]"
            };

            defaults = new Dictionary<string, (bool saved, bool synced)>();

            foreach (var entry in enumerable)
            {
                if (entry == null) continue;
                var entryType = entry.GetType();

                string nameOrPrefix = GetFieldString(entry, entryType, "nameOrPrefix");
                if (string.IsNullOrEmpty(nameOrPrefix)) continue;

                bool isPrefix = GetFieldBool(entry, entryType, "isPrefix");
                if (isPrefix) continue;

                bool internalParameter = GetFieldBool(entry, entryType, "internalParameter");
                if (internalParameter) continue;

                string remapTo = GetFieldString(entry, entryType, "remapTo");
                string finalName = !string.IsNullOrEmpty(remapTo) ? remapTo : nameOrPrefix;
                if (string.IsNullOrEmpty(finalName)) continue;

                float defaultValue = GetFieldFloat(entry, entryType, "defaultValue");
                bool saved = GetFieldBool(entry, entryType, "saved");
                bool localOnly = GetFieldBool(entry, entryType, "localOnly");
                bool synced = !localOnly;

                int syncTypeValue = GetFieldInt(entry, entryType, "syncType");
                var paramType = AnimatorControllerParameterType.Float;
                if (syncTypeValue == 3) paramType = AnimatorControllerParameterType.Bool;
                else if (syncTypeValue == 1) paramType = AnimatorControllerParameterType.Int;
                else if (syncTypeValue == 2) paramType = AnimatorControllerParameterType.Float;
                else
                {
                    // NotSynced
                    paramType = AnimatorControllerParameterType.Float;
                    synced = false;
                }

                if (controller.parameters.Any(p => p != null && p.name == finalName))
                {
                    continue;
                }

                var p = new AnimatorControllerParameter
                {
                    name = finalName,
                    type = paramType
                };

                switch (paramType)
                {
                    case AnimatorControllerParameterType.Bool:
                        p.defaultBool = defaultValue >= 0.5f;
                        break;
                    case AnimatorControllerParameterType.Int:
                        p.defaultInt = Mathf.RoundToInt(defaultValue);
                        break;
                    default:
                        p.defaultFloat = defaultValue;
                        break;
                }

                controller.AddParameter(p);
                defaults[finalName] = (saved, synced);
            }

            return true;
        }

        private static string GetFieldString(object obj, Type type, string fieldName)
        {
            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return f != null ? f.GetValue(obj) as string : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool GetFieldBool(object obj, Type type, string fieldName)
        {
            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return f != null && f.GetValue(obj) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static float GetFieldFloat(object obj, Type type, string fieldName)
        {
            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return 0f;
                var v = f.GetValue(obj);
                return v is float fv ? fv : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static int GetFieldInt(object obj, Type type, string fieldName)
        {
            try
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return 0;
                var v = f.GetValue(obj);
                if (v is int iv) return iv;
                if (v is Enum ev) return Convert.ToInt32(ev);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        #endregion
    }
}
