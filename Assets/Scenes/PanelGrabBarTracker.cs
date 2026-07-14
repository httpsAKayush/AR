using UnityEngine;
using UnityEngine.UI;

namespace MetaXR.LofiStudy.ARFoundation
{
    // Runs for the first several frames after a panel is created, re-positioning
    // the grab bar (visual + collider) to sit just below the panel's ACTUAL
    // height once VerticalLayoutGroup + ContentSizeFitter have finished
    // shrinking it to fit content. Then disables itself — panel content is
    // static at runtime, so no need to keep tracking every frame forever.
    public class PanelGrabBarTracker : MonoBehaviour
    {
        public RectTransform panelRect;
        public RectTransform barRect;
        public BoxCollider   grabBox;
        public float gapWorld;
        public float barHeightWorld;

        int m_FramesLeft = 5;   // layout can take a couple frames to settle

        void LateUpdate()
        {
            if (m_FramesLeft <= 0) { enabled = false; return; }
            m_FramesLeft--;

            if (panelRect == null || barRect == null || grabBox == null) { enabled = false; return; }

            // Real current panel height, in world meters (RectTransform units
            // are in "1 unit = 1mm" canvas space here, so /1000).
            float panelHeightWorld = panelRect.rect.height / 1000f;

            float barCenterYWorld = -(panelHeightWorld / 2f) - gapWorld - (barHeightWorld / 2f);
            grabBox.center = new Vector3(0f, barCenterYWorld, 0f);

            // barRect uses anchoredPosition in UI units (mm-space), already
            // anchored to bottom-center — its offset only needs the gap, not
            // the panel height (anchors already sit at the real bottom edge).
        }
    }
}