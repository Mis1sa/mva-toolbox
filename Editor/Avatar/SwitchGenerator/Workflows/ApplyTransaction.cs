using System;
using UnityEditor;

namespace MVA.Toolbox.SwitchGenerator.Workflows
{
    internal sealed class ApplyTransaction : IDisposable
    {
        private readonly int _group;
        private bool _completed;

        public ApplyTransaction(string title)
        {
            Undo.IncrementCurrentGroup();
            _group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(title);
        }

        public void Complete()
        {
            _completed = true;
            Undo.CollapseUndoOperations(_group);
        }

        public void Dispose()
        {
            if (_completed)
            {
                return;
            }

            Undo.RevertAllDownToGroup(_group);
        }
    }
}
