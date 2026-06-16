using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void RunAnalysisAndEnterStageOne()
        {
            _analysisError = string.Empty;
            FbxReplacementComponentWorkflow.DisposeState(_stageFourState);
            _stageFourState = null;
            FbxReplacementMaterialWorkflow.DisposeState(_stageThreeState);
            _stageThreeState = null;
            FbxReplacementStructureWorkflow.DisposeState(_stageTwoState);
            _stageTwoState = null;
            FbxReplacementWorkspaceService.DisposeState(_stageOneState);
            _stageOneState = null;
            _sessionState = null;

            try
            {
                _analysisResult = FbxReplacementAnalyzer.Analyze(_referenceObject, _targetObject);
                _stageOneState = FbxReplacementWorkspaceService.CreateState(_analysisResult, _referenceObject, _targetObject);
                _sessionState = _stageOneState.Session;
                _stageTwoState = FbxReplacementStructureWorkflow.CreateState(_sessionState);
                RefreshOverallProgressPlan();
                if (_stageTwoState != null)
                {
                    FbxReplacementStructureWorkflow.RevealCurrentObjects(_stageTwoState);
                }

                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                FbxReplacementStructureWorkflow.DisposeState(_stageTwoState);
                _stageTwoState = null;
                FbxReplacementWorkspaceService.DisposeState(_stageOneState);
                _analysisResult = null;
                _sessionState = null;
                _stageOneState = null;
                _analysisError = exception.Message;
                FbxReplacementHierarchyHighlighter.Deactivate(this);
            }
        }

        private void ConfirmStructureAlignmentMatch()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementStructureWorkflow.ConfirmCurrentMatch(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void KeepStructureTarget()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementStructureWorkflow.KeepCurrentTarget(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void RemoveStructureTarget()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementStructureWorkflow.RemoveCurrentTarget(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void KeepStructureSupplement()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementStructureWorkflow.KeepCurrentSupplement(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void RemoveStructureSupplement()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementStructureWorkflow.RemoveCurrentSupplement(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void EnterStageThree()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementComponentWorkflow.DisposeState(_stageFourState);
                _stageFourState = null;
                FbxReplacementMaterialWorkflow.DisposeState(_stageThreeState);
                _stageThreeState = FbxReplacementMaterialWorkflow.CreateState(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void ConfirmTargetMaterials()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementMaterialWorkflow.ConfirmCurrentTargetMaterials(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void SkipTargetMaterials()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementMaterialWorkflow.SkipCurrentTargetMaterials(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void ChooseSupplementMaterialReuse()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementMaterialWorkflow.ChooseSupplementReuse(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void ChooseSupplementMaterialAdjust()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementMaterialWorkflow.ChooseSupplementAdjust(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void ConfirmSupplementMaterials()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementMaterialWorkflow.ConfirmCurrentSupplementMaterials(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void StepBackStageThree()
        {
            _analysisError = string.Empty;
            try
            {
                if (_stageThreeState == null)
                {
                    return;
                }

                if (FbxReplacementMaterialWorkflow.StepBack(_stageThreeState))
                {
                    SyncStageHighlights();
                    return;
                }

                FbxReplacementMaterialWorkflow.RevertAllToBaseline(_stageThreeState);
                FbxReplacementMaterialWorkflow.DisposeState(_stageThreeState);
                _stageThreeState = null;
                if (_stageTwoState != null)
                {
                    FbxReplacementStructureWorkflow.StepBack(_stageTwoState);
                }
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void EnterStageFour()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementComponentWorkflow.DisposeState(_stageFourState);
                _stageFourState = FbxReplacementComponentWorkflow.CreateState(_stageThreeState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void ConfirmStageFourComponent()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementComponentWorkflow.ConfirmCurrentComponent(_stageFourState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void KeepStageFourComponent()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementComponentWorkflow.KeepCurrentComponent(_stageFourState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void RemoveStageFourComponent()
        {
            _analysisError = string.Empty;
            try
            {
                FbxReplacementComponentWorkflow.RemoveCurrentComponent(_stageFourState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void StepBackStageFour()
        {
            _analysisError = string.Empty;
            try
            {
                if (_stageFourState == null)
                {
                    return;
                }

                if (FbxReplacementComponentWorkflow.StepBack(_stageFourState))
                {
                    SyncStageHighlights();
                    return;
                }

                FbxReplacementComponentWorkflow.RevertAllToBaseline(_stageFourState);
                FbxReplacementComponentWorkflow.DisposeState(_stageFourState);
                _stageFourState = null;
                if (_stageThreeState != null)
                {
                    FbxReplacementMaterialWorkflow.StepBack(_stageThreeState);
                }
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void StepBackStructureAlignment()
        {
            _analysisError = string.Empty;
            try
            {
                if (_stageTwoState == null)
                {
                    return;
                }

                if (_stageTwoState.History.Count == 0)
                {
                    return;
                }

                FbxReplacementStructureWorkflow.StepBack(_stageTwoState);
                SyncStageHighlights();
            }
            catch (System.Exception exception)
            {
                _analysisError = exception.Message;
            }
        }

        private void SyncStageHighlights()
        {
            if (_stageFourState != null)
            {
                ActivateOrUpdateHighlights(FbxReplacementComponentWorkflow.BuildHighlightMap(_stageFourState));
                return;
            }

            if (_stageThreeState != null)
            {
                ActivateOrUpdateHighlights(FbxReplacementMaterialWorkflow.BuildHighlightMap(_stageThreeState));
                return;
            }

            if (_stageTwoState != null)
            {
                ActivateOrUpdateHighlights(FbxReplacementStructureWorkflow.BuildHighlightMap(_stageTwoState));
                return;
            }

            if (_sessionState == null)
            {
                FbxReplacementHierarchyHighlighter.Deactivate(this);
                return;
            }

            FbxReplacementHierarchyHighlighter.Deactivate(this);
        }

        private void ActivateOrUpdateHighlights(System.Collections.Generic.IDictionary<int, Color> highlightedColors)
        {
            if (FbxReplacementHierarchyHighlighter.IsActiveFor(this))
            {
                FbxReplacementHierarchyHighlighter.Update(this, highlightedColors);
                return;
            }

            FbxReplacementHierarchyHighlighter.Activate(this, highlightedColors);
        }

        private void ResetWorkflowState()
        {
            FbxReplacementHierarchyHighlighter.Deactivate(this);
            FbxReplacementComponentWorkflow.DisposeState(_stageFourState);
            _stageFourState = null;
            FbxReplacementMaterialWorkflow.DisposeState(_stageThreeState);
            _stageThreeState = null;
            FbxReplacementStructureWorkflow.DisposeState(_stageTwoState);
            _stageTwoState = null;
            FbxReplacementWorkspaceService.DisposeState(_stageOneState);
            _sessionState = null;
            _stageOneState = null;
            _overallPlannedTotalStepCount = 0;
            _analysisResult = null;
            _analysisError = string.Empty;
        }
    }
}