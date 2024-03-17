using UnityEditor;
using UnityEngine;

namespace WorldGeneration
{
    [CustomEditor(typeof(UpdatableData), true)]
    public class UpdatableDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            UpdatableData data = (UpdatableData) target;

            if (GUILayout.Button("Update"))
            {
                data.NotifyOfUpdatedValues();
            }
        }
    }
}
