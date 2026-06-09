using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WFC
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class Tile : MonoBehaviour
    {
        [Tooltip("Road connections for this tile. Use the flags to pick multiple directions.")]
        public Direction connections;

        [Tooltip("Optional rotation (in 90-degree steps). This rotation is applied to the connection mask when querying.")]
        [Range(0, 3)]
        public int rotation;

        [Header("Rotation Variants")]
        [Tooltip("If true, the generator can use rotated variants of this tile.")]
        public bool allowRotationVariants = true;

        [Tooltip("Allowed rotation steps (0-3) added on top of the base rotation. If empty, defaults to 0,1,2,3 when rotation variants are enabled.")]
        public List<int> allowedRotationSteps = new List<int>();

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
        public bool snapInPlayMode;

        [Tooltip("Minimum distance to grid before snapping is applied (prevents tiny adjustments).")]
        public float snapThreshold = 0.001f;

        [SerializeField] public Dictionary<Direction, List<Tile>> PossibleTilesByDirection = new Dictionary<Direction, List<Tile>>();

        public Dictionary<Direction, List<Tile>> ImpossibleTilesByDirection = new Dictionary<Direction, List<Tile>>();

        [Header("Typed Connections")]
        [Tooltip("The surface/type of this tile. Used when matching typed connections.")]
        public TileSurfaceType tileType = TileSurfaceType.Default;

        [Tooltip("Allowed neighbor types per direction. Multiple entries and flags are allowed.")]
        public List<DirectionalTypeConnection> typeConnections = new List<DirectionalTypeConnection>();
        
        [Tooltip("Object texture for later reference")]
        public Texture2D texture;

        public Direction GetRotatedConnections()
        {
            return GetConnectionsForRotation(rotation);
        }

        public Direction GetConnectionsForRotation(int rotationSteps)
        {
            return DirectionUtils.Rotate(connections, rotationSteps);
        }

        public bool ConnectsTo(Direction dir)
        {
            return DirectionUtils.Has(GetRotatedConnections(), dir);
        }

        public bool CanConnectTo(Tile other, Direction dir)
        {
            if (other == null) return false;
            if (connectedTiles == null || connectedTiles.Count == 0) return false;

            foreach (var connection in connectedTiles)
            {
                if (connection == null || connection.tile == null) continue;
                if (connection.tile != other) continue;

                Direction rotatedDirection = DirectionUtils.Rotate(connection.direction, rotation);
                if (DirectionUtils.Has(rotatedDirection, dir)) return true;
            }

            return false;
        }

        public ConnectionTypeMask GetAllowedTypesForDirection(Direction dir)
        {
            return GetAllowedTypesForRotation(dir, rotation);
        }

        public ConnectionTypeMask GetAllowedTypesForRotation(Direction dir, int rotationSteps)
        {
            if (typeConnections == null || typeConnections.Count == 0)
            {
                return ConnectionTypeMask.All;
            }

            ConnectionTypeMask result = ConnectionTypeMask.None;
            bool found = false;
            foreach (var entry in typeConnections)
            {
                if (entry == null) continue;
                Direction rotatedDir = DirectionUtils.Rotate(entry.direction, rotationSteps);
                if (!DirectionUtils.Has(rotatedDir, dir)) continue;

                result |= entry.allowedTypes;
                found = true;
            }

            return found ? result : ConnectionTypeMask.None;
        }

        public static ConnectionTypeMask MaskForTileType(TileSurfaceType type)
        {
            switch (type)
            {
                case TileSurfaceType.Forest:
                    return ConnectionTypeMask.Forest;
                case TileSurfaceType.Water:
                    return ConnectionTypeMask.Water;
                case TileSurfaceType.Lava:
                    return ConnectionTypeMask.Lava;
                case TileSurfaceType.Default:
                default:
                    return ConnectionTypeMask.Default;
            }
        }

        public bool IsCompatibleWith(Tile other, Direction dir)
        {
            if (other == null) return false;

            Direction thisCon = GetRotatedConnections();
            Direction otherCon = other.GetRotatedConnections();
            bool byDirection = DirectionUtils.Has(thisCon, dir) && DirectionUtils.Has(otherCon, DirectionUtils.Opposite(dir));

            ConnectionTypeMask allowedFromThis = GetAllowedTypesForDirection(dir);
            ConnectionTypeMask allowedFromOther = other.GetAllowedTypesForDirection(DirectionUtils.Opposite(dir));
            ConnectionTypeMask otherType = MaskForTileType(other.tileType);
            ConnectionTypeMask thisType = MaskForTileType(tileType);
            bool byType = (allowedFromThis & otherType) != 0 && (allowedFromOther & thisType) != 0;

            bool byExplicitRule = CanConnectTo(other, dir);
            return byExplicitRule || (byDirection && byType);
        }


        public void CalculatePossibleTiles(List<Tile> allTiles)
        {
            PossibleTilesByDirection.Clear();
            ImpossibleTilesByDirection.Clear();

            foreach (Direction direction in System.Enum.GetValues(typeof(Direction)))
            {
                PossibleTilesByDirection[direction] = new List<Tile>();
                ImpossibleTilesByDirection[direction] = new List<Tile>();

                foreach (Tile tile in allTiles)
                {
                    if (IsCompatibleWith(tile, direction))
                    {
                        PossibleTilesByDirection[direction].Add(tile);
                    }
                    else
                    {
                        ImpossibleTilesByDirection[direction].Add(tile);
                    }
                }

                // Check connectedTiles for additional constraints
                if (connectedTiles != null && connectedTiles.Count > 0)
                {
                    foreach (var connectedTile in connectedTiles)
                    {
                        if (connectedTile == null || connectedTile.tile == null)
                        {
                            continue;
                        }

                        Direction rotatedDirection = DirectionUtils.Rotate(connectedTile.direction, rotation);
                        if (!DirectionUtils.Has(rotatedDirection, direction))
                        {
                            continue;
                        }

                        if (!PossibleTilesByDirection[direction].Contains(connectedTile.tile))
                        {
                            PossibleTilesByDirection[direction].Add(connectedTile.tile);
                        }

                        if (ImpossibleTilesByDirection[direction].Contains(connectedTile.tile))
                        {
                            ImpossibleTilesByDirection[direction].Remove(connectedTile.tile);
                        }
                    }
                }
            }
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

#if UNITY_EDITOR
        // Draw visual indicators in the Scene view to show which directions are set on this tile
        private void OnDrawGizmosSelected()
        {
            // Use handles for nicer arrows
            var con = GetRotatedConnections();
            Vector3 center = transform.position;

            float cellX = Mathf.Max(1f, gridSize.x);
            float cellY = Mathf.Max(1f, gridSize.y);
            float len = Mathf.Max(cellX, cellY) * 0.5f;
            float headSize = len * 0.25f;

            // Draw background circle
            Handles.color = new Color(0f, 0f, 0f, 0.25f);
            Handles.DrawSolidDisc(center, Vector3.up, Mathf.Min(len * 0.6f, 1.0f));

            // Direction arrows
            if (DirectionUtils.Has(con, Direction.North))
            {
                DrawArrow(center, Vector3.forward, len, headSize, Color.green);
            }
            if (DirectionUtils.Has(con, Direction.East))
            {
                DrawArrow(center, Vector3.right, len, headSize, Color.green);
            }
            if (DirectionUtils.Has(con, Direction.South))
            {
                DrawArrow(center, Vector3.back, len, headSize, Color.green);
            }
            if (DirectionUtils.Has(con, Direction.West))
            {
                DrawArrow(center, Vector3.left, len, headSize, Color.green);
            }

            // Draw connectedTiles relations (if any)
            if (connectedTiles != null)
            {
                Handles.color = Color.cyan;
                foreach (var ct in connectedTiles)
                {
                    if (ct == null || ct.tile == null) continue;
                    // draw line to referenced tile
                    var otherPos = ct.tile.transform.position;
                    Handles.DrawDottedLine(center, otherPos, 4f);
                    // also draw an arrow on this tile in the specified direction
                    var dirVec = DirectionToVector3(ct.direction);
                    if (dirVec != Vector3.zero)
                        DrawArrow(center, dirVec, len * 0.5f, headSize * 0.5f, Color.cyan);
                }
            }
        }

        private static Vector3 DirectionToVector3(Direction d)
        {
            if (DirectionUtils.Has(d, Direction.North)) return Vector3.forward;
            if (DirectionUtils.Has(d, Direction.East)) return Vector3.right;
            if (DirectionUtils.Has(d, Direction.South)) return Vector3.back;
            if (DirectionUtils.Has(d, Direction.West)) return Vector3.left;
            return Vector3.zero;
        }

        private static void DrawArrow(Vector3 origin, Vector3 dir, float length, float headSize, Color color)
        {
            Handles.color = color;
            Vector3 end = origin + dir.normalized * length;
            Handles.DrawLine(origin, end);
            // draw cone for head
            Quaternion rot = Quaternion.LookRotation(dir);
            Handles.ConeHandleCap(0, end, rot, headSize, EventType.Repaint);
        }
#endif
    }
}
