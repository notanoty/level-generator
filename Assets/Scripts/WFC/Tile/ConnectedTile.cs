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
}