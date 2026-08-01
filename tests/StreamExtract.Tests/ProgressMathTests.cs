namespace StreamExtract.Tests;

public class ProgressMathTests
{
    [Fact]
    public void MaxEqualsMin_ReturnsZero()
    {
        Assert.Equal(0f, ProgressMath.Percent(50, 50, 50));
    }

    [Fact]
    public void ValueBelowMin_ClampsToZero()
    {
        Assert.Equal(0f, ProgressMath.Percent(-10, 0, 100));
    }

    [Fact]
    public void ValueAboveMax_ClampsToOne()
    {
        Assert.Equal(1f, ProgressMath.Percent(150, 0, 100));
    }

    [Fact]
    public void MidValue_ReturnsFraction()
    {
        Assert.Equal(0.5f, ProgressMath.Percent(50, 0, 100));
    }

    [Fact]
    public void MinMaxNormalized()
    {
        Assert.Equal(0.25f, ProgressMath.Percent(50, 0, 200));
    }
}
