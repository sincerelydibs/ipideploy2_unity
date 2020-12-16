using System;
using System.Collections.Generic;
using UnityEngine;

namespace iPiMocap
{
    public class CharacterAnimator : MonoBehaviour
    {
        public MocapStudioClient MocapData;

        private Transform rootJoint;
        private IDictionary<string, Transform> jointMap = new Dictionary<string, Transform>(StringComparer.InvariantCultureIgnoreCase);

        // Update is called once per frame
        void Update()
        {
            if (MocapData != null)
            {
                var pose = MocapData.FetchLatestPose();
                if (pose != null)
                {
                    UpdateRootJoint(pose.RootName);

                    if (rootJoint != null)
                    {
                        // Pose.RootPosition corresponds to a root joint not a character
                        // Thus do some transforms to transfer position to the character
                        var worldToParentLocal = transform.parent?.worldToLocalMatrix ?? Matrix4x4.identity;
                        var positionOfRootInParentSpace = worldToParentLocal.MultiplyPoint(rootJoint.TransformPoint(Vector3.zero));
                        var targetPositionOfRootInParentSpace = pose.RootPosition;
                        transform.localPosition += targetPositionOfRootInParentSpace - positionOfRootInParentSpace;

                        foreach (var entry in pose.Rotations)
                        {
                            if (!jointMap.TryGetValue(entry.Key, out var t))
                            {
                                t = FindJointRecursive(rootJoint, entry.Key);
                                jointMap.Add(entry.Key, t);
                            }
                            if (t != null)
                            {
                                t.localRotation = entry.Value;
                            }
                        }
                    }
                }
            }
        }

        private void UpdateRootJoint(string rootName)
        {
            if (string.IsNullOrEmpty(rootName))
                rootJoint = null;
            else if (!string.Equals(rootJoint?.name, rootName))
                rootJoint = FindJointRecursive(transform, rootName, excludeSelf: true);
            else
                return;

            jointMap.Clear();
        }

        private static Transform FindJointRecursive(Transform start, string name, bool excludeSelf = false)
        {
            if (!excludeSelf && string.Equals(start.name, name, StringComparison.InvariantCultureIgnoreCase))
                return start;

            foreach (Transform child in start)
            {
                var result = FindJointRecursive(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}