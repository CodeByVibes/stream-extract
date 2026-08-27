namespace StreamExtract;

public static class ProgressMath
{
    public static float Percent(int value, int min, int max)
        => Math.Clamp((value - min) / Math.Max(1f, (float)max - min), 0f, 1f);
}
