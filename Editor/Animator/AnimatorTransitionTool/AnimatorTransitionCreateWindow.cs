using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionCreateWindow
    {
        private sealed class CreateTransitionItem
        {
            public bool overrideHasExitTime;
            public bool hasExitTime;
            public bool overrideExitTime;
            public float exitTime;
            public bool overrideHasFixedDuration;
            public bool hasFixedDuration;
            public bool overrideDuration;
            public float duration;
            public bool overrideOffset;
            public float offset;
            public bool overrideCanTransitionToSelf;
            public bool canTransitionToSelf;
            public readonly List<ConditionUI> conditions = new List<ConditionUI>();
        }

        private sealed class ConditionUI
        {
            public string parameterName;
            public AnimatorControllerParameterType parameterType;
            public float floatValue;
            public int intValue;
            public bool boolValue;
            public AnimatorConditionMode mode;
            public bool isGlobalOverride;
            public string globalGuid;
        }

        private readonly AnimatorTransitionToolWindow _owner;
        private string _createSourceState = "Any State";
        private string _createDestState = string.Empty;
        private bool _defaultHasExitTime = true;
        private float _defaultExitTime = 0.75f;
        private bool _defaultHasFixedDuration = true;
        private float _defaultDuration = 0.25f;
        private float _defaultOffset;
        private bool _defaultCanTransitionToSelf = true;
        private bool _overrideHasExitTime;
        private bool _overrideExitTime;
        private bool _overrideHasFixedDuration;
        private bool _overrideDuration;
        private bool _overrideOffset;
        private bool _overrideCanTransitionToSelf;
        private readonly List<CreateTransitionItem> _createItems = new List<CreateTransitionItem>();
        private readonly List<ConditionUI> _globalConditions = new List<ConditionUI>();

        internal AnimatorTransitionCreateWindow(AnimatorTransitionToolWindow owner)
        {
            _owner = owner;
        }

        internal void Reset()
        {
            _createSourceState = "Any State";
            _createDestState = string.Empty;
            _defaultHasExitTime = true;
            _defaultExitTime = 0.75f;
            _defaultHasFixedDuration = true;
            _defaultDuration = 0.25f;
            _defaultOffset = 0f;
            _defaultCanTransitionToSelf = true;
            _overrideHasExitTime = false;
            _overrideExitTime = false;
            _overrideHasFixedDuration = false;
            _overrideDuration = false;
            _overrideOffset = false;
            _overrideCanTransitionToSelf = false;
            _createItems.Clear();
            _globalConditions.Clear();
        }

        internal void OnGUI()
        {
            if (_owner.SelectedController == null)
            {
                return;
            }

            DrawCreateStateSelection();
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("默认过渡设置", EditorStyles.boldLabel);
            DrawDefaultSettings();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("全局条件", EditorStyles.boldLabel);
            DrawGlobalConditions();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("待创建过渡列表", EditorStyles.boldLabel);
            SyncGlobalConditionsToTransitions();
            DrawCreateItemsList();

            EditorGUILayout.Space(4f);
            bool canCreate = _createItems.Count > 0 && !string.IsNullOrEmpty(_createSourceState) && !string.IsNullOrEmpty(_createDestState);
            EditorGUI.BeginDisabledGroup(!canCreate);
            if (GUILayout.Button("创建过渡", GUILayout.Height(32f)))
            {
                CreateTransitions();
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
