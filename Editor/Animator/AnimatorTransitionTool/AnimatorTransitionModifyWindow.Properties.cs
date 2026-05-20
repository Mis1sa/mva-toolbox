using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionModifyWindow
    {
        private void DrawModifyProperties()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Has Exit Time", GUILayout.Width(150f));
            _modifyHasExitTimeValue = EditorGUILayout.Toggle(_modifyHasExitTimeValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Exit Time", GUILayout.Width(150f));
            _modifyExitTimeValue = EditorGUILayout.FloatField(_modifyExitTimeValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Fixed Duration", GUILayout.Width(150f));
            _modifyHasFixedDurationValue = EditorGUILayout.Toggle(_modifyHasFixedDurationValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Transition Duration (s)", GUILayout.Width(150f));
            _modifyDurationValue = EditorGUILayout.FloatField(_modifyDurationValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Transition Offset", GUILayout.Width(150f));
            _modifyOffsetValue = EditorGUILayout.FloatField(_modifyOffsetValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }
    }
}
