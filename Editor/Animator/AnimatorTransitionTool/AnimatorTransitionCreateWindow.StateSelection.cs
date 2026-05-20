using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionCreateWindow
    {
        private void DrawCreateStateSelection()
        {
            List<string> sourceOptions = new List<string> { "Any State" };
            sourceOptions.AddRange(_owner.GetDisplayPathOptions());

            int sourceIndex = sourceOptions.IndexOf(_createSourceState);
            if (sourceIndex < 0)
            {
                sourceIndex = 0;
            }

            sourceIndex = EditorGUILayout.Popup("源状态", sourceIndex, sourceOptions.ToArray());
            _createSourceState = sourceOptions[Mathf.Clamp(sourceIndex, 0, sourceOptions.Count - 1)];

            List<string> destOptions = new List<string>(_owner.GetDisplayPathOptions());
            if (_createSourceState != "Any State")
            {
                destOptions.Add("Exit");
            }

            if (destOptions.Count == 0)
            {
                _createDestState = string.Empty;
                return;
            }

            int destIndex = destOptions.IndexOf(_createDestState);
            if (destIndex < 0)
            {
                destIndex = 0;
            }

            destIndex = EditorGUILayout.Popup("目标状态", destIndex, destOptions.ToArray());
            _createDestState = destOptions[Mathf.Clamp(destIndex, 0, destOptions.Count - 1)];
        }
    }
}
