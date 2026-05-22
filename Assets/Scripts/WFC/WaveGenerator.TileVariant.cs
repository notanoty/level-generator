using System.Collections.Generic;

namespace WFC
{
    public partial class WaveGenerator
    {
        public class TileVariant
        {
            public Tile Tile;
            public int Rotation;
            public Direction RotatedConnections;
            public Dictionary<Direction, List<TileVariant>> PossibleByDirection = new Dictionary<Direction, List<TileVariant>>();
            public Dictionary<Direction, List<TileVariant>> ImpossibleByDirection = new Dictionary<Direction, List<TileVariant>>();
        }
    }
}