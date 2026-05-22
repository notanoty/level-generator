using UnityEngine;

namespace WFC
{
    [System.Flags]
    public enum Direction
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
        // NorthEast = 1 << 4,
        // SouthEast = 1 << 5,
        // SouthWest = 1 << 6,
        // NorthWest = 1 << 7,
        All = North | East | South | West 
              // | NorthEast | SouthEast | SouthWest | NorthWest
    }
}

