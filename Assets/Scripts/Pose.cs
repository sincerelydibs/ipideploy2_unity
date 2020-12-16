using System.Collections.Generic;
using UnityEngine;

namespace iPiMocap
{
    public class Pose
    {
        public string RootName;
        public Vector3 RootPosition;
        public readonly IDictionary<string, Quaternion> Rotations = new Dictionary<string, Quaternion>();
    }
}