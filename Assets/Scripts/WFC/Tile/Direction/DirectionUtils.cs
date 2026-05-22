namespace WFC
{
    public static class DirectionUtils
    {
        public static Direction Opposite(Direction d)
        {
            Direction res = Direction.None;
            if ((d & Direction.North) != 0) res |= Direction.South;
            if ((d & Direction.South) != 0) res |= Direction.North;
            if ((d & Direction.East) != 0) res |= Direction.West;
            if ((d & Direction.West) != 0) res |= Direction.East;

            // if ((d & Direction.NorthEast) != 0) res |= Direction.SouthWest;
            // if ((d & Direction.SouthWest) != 0) res |= Direction.NorthEast;
            // if ((d & Direction.NorthWest) != 0) res |= Direction.SouthEast;
            // if ((d & Direction.SouthEast) != 0) res |= Direction.NorthWest;

            return res;
        }

        public static bool Has(Direction container, Direction check)
        {
            return (container & check) == check;
        }

        public static Direction Rotate90(Direction d)
        {
            Direction res = Direction.None;
            if ((d & Direction.North) != 0) res |= Direction.East;
            if ((d & Direction.East) != 0) res |= Direction.South;
            if ((d & Direction.South) != 0) res |= Direction.West;
            if ((d & Direction.West) != 0) res |= Direction.North;

            // if ((d & Direction.NorthEast) != 0) res |= Direction.SouthEast;
            // if ((d & Direction.SouthEast) != 0) res |= Direction.SouthWest;
            // if ((d & Direction.SouthWest) != 0) res |= Direction.NorthWest;
            // if ((d & Direction.NorthWest) != 0) res |= Direction.NorthEast;

            return res;
        }

        public static Direction Rotate(Direction d, int steps90)
        {
            int s = ((steps90 % 4) + 4) % 4; // normalize
            Direction res = d;
            for (int i = 0; i < s; i++) res = Rotate90(res);
            return res;
        }
    }
}