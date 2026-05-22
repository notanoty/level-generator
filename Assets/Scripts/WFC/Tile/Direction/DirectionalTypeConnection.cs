using UnityEngine;

namespace WFC
{
    [System.Serializable]
    public class DirectionalTypeConnection
    {
        [Tooltip("Direction(s) on this tile where the listed connection types are allowed.")]
        public Direction direction;

        [Tooltip("Allowed neighbor types for the selected direction(s).")]
        public ConnectionTypeMask allowedTypes = ConnectionTypeMask.All;
    }
}