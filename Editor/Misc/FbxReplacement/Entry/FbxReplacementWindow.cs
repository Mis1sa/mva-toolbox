using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private GameObject _referenceObject;
        private GameObject _targetObject;
        private FbxReplacementAnalysisResult _analysisResult;
        private FbxReplacementSessionState _sessionState;
        private FbxReplacementStageOneState _stageOneState;
        private FbxReplacementStageTwoState _stageTwoState;
        private FbxReplacementStageThreeState _stageThreeState;
        private FbxReplacementStageFourState _stageFourState;
        private int _overallPlannedTotalStepCount;
        private const float BottomActionButtonHeight = 36f;
        private const float BottomActionButtonSpacing = 4f;
        private string _analysisError;

        private string _exportFolderRelative = "Assets/MVA Toolbox/Migrated";
        private string _exportPrefabName = string.Empty;

        internal static void Open()
        {
            var window = GetWindow<FbxReplacementWindow>(false, "FBX替换");
            window.minSize = new Vector2(480f, 500f);
            window.Show();
        }

        private void OnDisable()
        {
            ResetWorkflowState();
        }

        private void OnGUI()
        {
            DrawInputSection();

            GUILayout.Space(6f);

            if (_referenceObject == null || _targetObject == null)
            {
                FbxReplacementHierarchyHighlighter.Deactivate(this);
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
                EditorGUILayout.HelpBox("请分别拖入场景或资产中您需要对齐的物体(目标物体)和参考物体。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            DrawContentSection();
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6f);
            DrawBottomActionBar();
        }

        private void DrawContentSection()
        {
            if (_sessionState == null)
            {
                GUILayout.Space(4f);
                return;
            }

            if (_stageFourState != null)
            {
                DrawStageFourWorkflow();
                return;
            }

            if (_stageThreeState != null)
            {
                DrawStageThreeWorkflow();
                return;
            }

            if (_stageTwoState != null)
            {
                DrawStageTwoWorkflow();
                return;
            }
        }

    }
}








