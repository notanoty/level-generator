using System.Collections.Generic;
using UnityEngine;

namespace WFC
{
    [System.Serializable]
    public class ConnectedTile
    {
        [Tooltip("The tile that can be connected to this tile.")]
        public Tile tile;

        [Tooltip("The direction on this tile where the connection is allowed.")]
        public Direction direction;
    }

    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class Tile : MonoBehaviour
    {
        [Tooltip("Texture2D used to represent this tile in the editor and at runtime.")]
        public Texture2D texture;

        [Tooltip("Directional connections for this tile. Use the flags to pick multiple directions.")]
        public Direction connections;

        [Tooltip("Optional rotation (in 90-degree steps). This rotation is applied to the connection mask when querying.")]
        [Range(0, 3)]
        public int rotation;

        [Tooltip("Tiles that this tile is allowed to connect to, including the direction where each connection is allowed. Leave empty to allow any tile that matches the directions.")]
        public List<ConnectedTile> connectedTiles;

        [Header("Grid Snapping")]
        [Tooltip("Grid size (width, height) in world units used for snapping.")]
        public Vector2 gridSize = new Vector2(100f, 100f);

        public enum SnapPlane { XY, XZ }
        [Tooltip("Plane in which to snap positions.")]
        public SnapPlane snapPlane = SnapPlane.XZ;

        [Tooltip("Whether to snap the tile automatically while editing in the Scene view.")]
        public bool snapInEditor = true;

        [Tooltip("Whether to snap the tile automatically during Play mode.")]
        public bool snapInPlayMode = false;

        [Tooltip("Minimum distance to grid before snapping is applied (prevents tiny adjustments).")]
        public float snapThreshold = 0.001f;

        /// <summary>
        /// Returns the connections after applying the configured rotation (rotation is in 90-degree clockwise steps).
        /// </summary>
        public Direction GetRotatedConnections()
        {
            return DirectionUtils.Rotate(connections, rotation);
        }

        /// <summary>
        /// True if this tile (with its rotation) has the given direction bit(s).
        /// </summary>
        public bool ConnectsTo(Direction dir)
        {
            return DirectionUtils.Has(GetRotatedConnections(), dir);
        }

        /// <summary>
        /// True if this tile explicitly allows connecting to the given tile.
        /// If the list is empty, any tile is allowed and compatibility falls back to direction rules.
        /// </summary>
        public bool CanConnectTo(Tile other, Direction dir)
        {
            if (other == null) return false;
            if (connectedTiles == null || connectedTiles.Count == 0) return true;

            foreach (var connection in connectedTiles)
            {
                if (connection == null || connection.tile == null) continue;
                if (connection.tile != other) continue;
                if (DirectionUtils.Has(connection.direction, dir)) return true;
            }

            return false;
        }

        /// <summary>
        /// Quick check whether this tile is compatible with another tile when this tile faces the given direction.
        /// It checks this tile has 'dir', the other tile has the opposite direction, and the other tile is allowed in the connected tiles list.
        /// </summary>
        public bool IsCompatibleWith(Tile other, Direction dir)
        {
            if (other == null) return false;
            if (!CanConnectTo(other, dir)) return false;

            Direction thisCon = GetRotatedConnections();
            Direction otherCon = other.GetRotatedConnections();
            return DirectionUtils.Has(thisCon, dir) && DirectionUtils.Has(otherCon, DirectionUtils.Opposite(dir));
        }

        void Update()
        {
            // Decide whether snapping should run right now
            if (!Application.isPlaying && !snapInEditor) return;
            if (Application.isPlaying && !snapInPlayMode) return;

            SnapToGridIfNeeded();
        }

        [ContextMenu("Snap To Grid")]
        public void SnapToGrid()
        {
            SnapToGridIfNeeded(force: true);
        }

        private void SnapToGridIfNeeded(bool force = false)
        {
            if (gridSize.x <= 0f || gridSize.y <= 0f) return;

            Vector3 pos = transform.position;
            Vector3 snapped = pos;

            if (snapPlane == SnapPlane.XZ)
            {
                snapped.x = Mathf.Round(pos.x / gridSize.x) * gridSize.x;
                snapped.z = Mathf.Round(pos.z / gridSize.y) * gridSize.y;
            }
            else // XY
            {
                snapped.x = Mathf.Round(pos.x / gridSize.x) * gridSize.x;
                snapped.y = Mathf.Round(pos.y / gridSize.y) * gridSize.y;
            }

            if (force || Vector3.Distance(pos, snapped) > snapThreshold)
            {
#if UNITY_EDITOR
                // Record for undo when used from the editor
                if (!Application.isPlaying)
                    UnityEditor.Undo.RecordObject(transform, "Snap To Grid");
#endif
                transform.position = snapped;
            }
        }
    }
}

