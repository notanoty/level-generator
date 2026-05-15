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
    }
}

