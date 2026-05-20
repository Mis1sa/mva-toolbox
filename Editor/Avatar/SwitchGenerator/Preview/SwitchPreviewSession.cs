using MVA.Toolbox.SwitchGenerator.Spec;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Preview
{
    internal sealed class SwitchPreviewSession
    {
        private readonly PreviewSnapshotStore _snapshot = new PreviewSnapshotStore();

        public bool IsPreviewing { get; private set; }

        public void Start(GameObject root)
        {
            if (IsPreviewing)
            {
                return;
            }

            _snapshot.Capture(root);
            IsPreviewing = true;
        }

        public void Stop()
        {
            if (!IsPreviewing)
            {
                return;
            }

            _snapshot.Restore();
            IsPreviewing = false;
        }

        public void Apply(SwitchLayerSpec layer, float previewValue)
        {
            if (!IsPreviewing)
            {
                return;
            }

            _snapshot.Restore();
            PreviewStateApplier.Apply(layer, previewValue);
        }
    }
}
