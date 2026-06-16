using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawOverallProgressBar()
        {
            GetOverallProgress(out int processed, out int total);
            Rect rect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
            float progress = total > 0 ? Mathf.Clamp01((float)processed / total) : 0f;
            string label = total > 0 ? $"{processed} / {total}" : "0 / 0";
            EditorGUI.ProgressBar(rect, progress, label);
        }

        private bool ShouldShowOverallProgressBar()
        {
            return _stageTwoState != null && _overallPlannedTotalStepCount > 0;
        }

        private void GetOverallProgress(out int processed, out int total)
        {
            if (!ShouldShowOverallProgressBar())
            {
                processed = 0;
                total = 0;
                return;
            }

            total = _overallPlannedTotalStepCount;
            int pending = GetTotalPendingSteps();
            processed = Mathf.Clamp(total - pending, 0, total);
        }

        private int GetTotalPendingSteps()
        {
            int pendingStructure = GetPendingStructureStepCount();
            int pendingMaterial = GetPendingMaterialStepCount();
            int pendingComponent = GetPendingComponentStepCount();
            return pendingStructure + pendingMaterial + pendingComponent;
        }

        private int GetPendingStructureStepCount()
        {
            if (_stageTwoState == null)
            {
                return 0;
            }

            int pendingCount = 0;
            for (int i = 0; i < _stageTwoState.TargetEntries.Count; i++)
            {
                if (!IsTargetStructureDecided(_stageTwoState, _stageTwoState.TargetEntries[i].Key))
                {
                    pendingCount++;
                }
            }

            for (int i = 0; i < _stageTwoState.ReferenceEntries.Count; i++)
            {
                string refKey = _stageTwoState.ReferenceEntries[i].Key;
                if (_stageTwoState.MatchedReferenceKeys.Contains(refKey))
                {
                    continue;
                }

                bool isDecidedSupplement = false;
                if (_stageTwoState.ReferenceSupplementsInitialized)
                {
                    for (int j = 0; j < _stageTwoState.SupplementEntries.Count; j++)
                    {
                        var entry = _stageTwoState.SupplementEntries[j];
                        if (entry.ReferenceKey == refKey)
                        {
                            if (_stageTwoState.KeptSupplementKeys.Contains(entry.Key) ||
                                _stageTwoState.RemovedSupplementKeys.Contains(entry.Key))
                            {
                                isDecidedSupplement = true;
                            }
                            break;
                        }
                    }
                }

                if (!isDecidedSupplement)
                {
                    pendingCount++;
                }
            }

            return pendingCount;
        }

        private int GetPendingMaterialStepCount()
        {
            if (_stageTwoState == null)
            {
                return 0;
            }

            int pendingCount = 0;

            for (int i = 0; i < _stageTwoState.TargetEntries.Count; i++)
            {
                string targetKey = _stageTwoState.TargetEntries[i].Key;
                if (_stageTwoState.RemovedTargetKeys.Contains(targetKey))
                {
                    continue;
                }

                if (_stageTwoState.CurrentTargetObjectsByKey.TryGetValue(targetKey, out GameObject targetObj) &&
                    HasMaterialReviewCandidate(targetObj))
                {
                    if (_stageThreeState != null)
                    {
                        if (!_stageThreeState.ProcessedTargetEntryKeys.Contains("target-material/" + targetKey) &&
                            !_stageThreeState.SkippedTargetEntryKeys.Contains("target-material/" + targetKey) &&
                            !_stageThreeState.ReplacedTargetEntryKeys.Contains("target-material/" + targetKey))
                        {
                            pendingCount++;
                        }
                    }
                    else
                    {
                        pendingCount++;
                    }
                }
            }

            for (int i = 0; i < _stageTwoState.ReferenceEntries.Count; i++)
            {
                string refKey = _stageTwoState.ReferenceEntries[i].Key;
                if (_stageTwoState.MatchedReferenceKeys.Contains(refKey))
                {
                    continue;
                }

                bool isRemovedSupplement = false;
                string supplementKey = null;
                if (_stageTwoState.ReferenceSupplementsInitialized)
                {
                    for (int j = 0; j < _stageTwoState.SupplementEntries.Count; j++)
                    {
                        var entry = _stageTwoState.SupplementEntries[j];
                        if (entry.ReferenceKey == refKey)
                        {
                            supplementKey = entry.Key;
                            if (_stageTwoState.RemovedSupplementKeys.Contains(supplementKey))
                            {
                                isRemovedSupplement = true;
                            }
                            break;
                        }
                    }
                }

                if (isRemovedSupplement)
                {
                    continue;
                }

                if (_stageTwoState.CurrentReferenceObjectsByKey.TryGetValue(refKey, out GameObject refObj) &&
                    HasMaterialReviewCandidate(refObj))
                {
                    if (_stageThreeState != null && supplementKey != null)
                    {
                        if (!_stageThreeState.SupplementReuseChosen && 
                            !_stageThreeState.ProcessedSupplementEntryKeys.Contains("supplement-material/" + supplementKey))
                        {
                            pendingCount++;
                        }
                    }
                    else
                    {
                        pendingCount++;
                    }
                }
            }

            return pendingCount;
        }

        private int GetPendingComponentStepCount()
        {
            if (_stageFourState != null)
            {
                int totalComponents = FbxReplacementComponentWorkflow.GetTotalComponentCount(_stageFourState);
                int processedComponents = FbxReplacementComponentWorkflow.GetProcessedComponentCount(_stageFourState);
                return totalComponents - processedComponents;
            }

            if (_stageTwoState == null)
            {
                return 0;
            }

            int pendingCount = 0;

            for (int i = 0; i < _stageTwoState.ReferenceEntries.Count; i++)
            {
                string refKey = _stageTwoState.ReferenceEntries[i].Key;

                bool isRemovedSupplement = false;
                if (_stageTwoState.ReferenceSupplementsInitialized)
                {
                    for (int j = 0; j < _stageTwoState.SupplementEntries.Count; j++)
                    {
                        var entry = _stageTwoState.SupplementEntries[j];
                        if (entry.ReferenceKey == refKey && _stageTwoState.RemovedSupplementKeys.Contains(entry.Key))
                        {
                            isRemovedSupplement = true;
                            break;
                        }
                    }
                }

                if (isRemovedSupplement)
                {
                    continue;
                }

                if (_stageTwoState.CurrentReferenceObjectsByKey.TryGetValue(refKey, out GameObject refObj))
                {
                    pendingCount += CountMigratableComponents(refObj);
                }
            }

            return pendingCount;
        }

        private bool IsTargetStructureDecided(FbxReplacementStageTwoState state, string targetKey)
        {
            return state != null
                && targetKey != null
                && (state.MatchedTargetKeys.Contains(targetKey)
                    || state.PreservedTargetKeys.Contains(targetKey)
                    || state.RemovedTargetKeys.Contains(targetKey));
        }

        private bool IsSupplementStructureDecided(FbxReplacementStageTwoState state, string supplementKey)
        {
            return state != null
                && !string.IsNullOrEmpty(supplementKey)
                && (state.KeptSupplementKeys.Contains(supplementKey)
                    || state.RemovedSupplementKeys.Contains(supplementKey));
        }

        private int CountMigratableComponents(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return 0;
            }

            int count = 0;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || IsSkippedStageFourComponentType(component.GetType()))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private bool HasMaterialReviewCandidate(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                return true;
            }

            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            return meshRenderer != null && meshFilter != null && meshFilter.sharedMesh != null;
        }

        private bool IsSkippedStageFourComponentType(System.Type componentType)
        {
            return componentType != null
                && (typeof(Transform).IsAssignableFrom(componentType)
                    || typeof(MeshFilter).IsAssignableFrom(componentType)
                    || typeof(MeshRenderer).IsAssignableFrom(componentType)
                    || typeof(SkinnedMeshRenderer).IsAssignableFrom(componentType));
        }

        private void RefreshOverallProgressPlan()
        {
            if (_analysisResult == null || _stageTwoState == null)
            {
                _overallPlannedTotalStepCount = 0;
                return;
            }

            int structureSteps = _stageTwoState.TargetEntries.Count + _stageTwoState.ReferenceEntries.Count;
            int materialSteps = 0;
            int componentSteps = 0;

            for (int i = 0; i < _stageTwoState.TargetEntries.Count; i++)
            {
                FbxReplacementStructureTargetEntry targetEntry = _stageTwoState.TargetEntries[i];
                if (targetEntry != null && _stageTwoState.CurrentTargetObjectsByKey.TryGetValue(targetEntry.Key, out GameObject targetObj))
                {
                    if (HasMaterialReviewCandidate(targetObj))
                    {
                        materialSteps++;
                    }
                }
            }

            for (int i = 0; i < _stageTwoState.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry refEntry = _stageTwoState.ReferenceEntries[i];
                if (refEntry != null && _stageTwoState.CurrentReferenceObjectsByKey.TryGetValue(refEntry.Key, out GameObject refObj))
                {
                    if (HasMaterialReviewCandidate(refObj))
                    {
                        materialSteps++;
                    }
                    componentSteps += CountMigratableComponents(refObj);
                }
            }

            _overallPlannedTotalStepCount = structureSteps + materialSteps + componentSteps;
        }
    }
}