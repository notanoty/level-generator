namespace WFC
{
    [System.Flags]
    public enum ConnectionTypeMask
    {
        None = 0,
        Default = 1 << 0,
        Forest = 1 << 1,
        Water = 1 << 2,
        Lava = 1 << 3,
        All = Default | Forest | Water | Lava
    }
}