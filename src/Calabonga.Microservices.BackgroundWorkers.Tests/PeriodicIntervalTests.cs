using Calabonga.Microservices.BackgroundWorkers.Exceptions;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// Seeded <see cref="Random"/> makes the draws deterministic, so range checks never flake.
/// </summary>
public sealed class PeriodicIntervalTests
{
    private const int Draws = 1000;
    private const int MaxHours = (int.MaxValue - 1) / 60;

    [Fact]
    public void Next_Should_ReturnWholeMinutesWithinInclusiveRange_When_PeriodTypeHours()
    {
        // Arrange
        var random = new Random(12345);

        // Act
        var minutes = Enumerable.Range(0, Draws)
            .Select(_ => PeriodicInterval.Next(random, PeriodType.Hours, 3, 4).TotalMinutes)
            .ToList();

        // Assert
        Assert.All(minutes, x => Assert.InRange(x, 180, 240));
        Assert.All(minutes, x => Assert.Equal(Math.Floor(x), x));
        Assert.Contains(180, minutes);
        Assert.Contains(240, minutes);
    }

    [Fact]
    public void Next_Should_ReturnEveryValueOfInclusiveRange_When_PeriodTypeMinutes()
    {
        // Arrange
        var random = new Random(12345);

        // Act
        var minutes = Enumerable.Range(0, Draws)
            .Select(_ => (int)PeriodicInterval.Next(random, PeriodType.Minutes, 10, 15).TotalMinutes)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // Assert
        Assert.Equal(new[] { 10, 11, 12, 13, 14, 15 }, minutes);
    }

    [Fact]
    public void Next_Should_ReturnFixedInterval_When_MinValueEqualsMaxValue()
    {
        // Act
        var interval = PeriodicInterval.Next(new Random(12345), PeriodType.Hours, 2, 2);

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), interval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Should_ThrowWorkerArgumentOutOfRangeException_When_MinValueLessThanOne(int minValue)
    {
        Assert.Throws<WorkerArgumentOutOfRangeException>(() => PeriodicInterval.Validate(PeriodType.Minutes, minValue, 10));
    }

    [Fact]
    public void Validate_Should_ThrowWorkerArgumentOutOfRangeException_When_MaxValueLessThanMinValue()
    {
        Assert.Throws<WorkerArgumentOutOfRangeException>(() => PeriodicInterval.Validate(PeriodType.Minutes, 15, 10));
    }

    [Fact]
    public void Validate_Should_ThrowWorkerArgumentOutOfRangeException_When_PeriodTypeUnknown()
    {
        Assert.Throws<WorkerArgumentOutOfRangeException>(() => PeriodicInterval.Validate((PeriodType)42, 1, 2));
    }

    [Fact]
    public void Validate_Should_ThrowWorkerArgumentOutOfRangeException_When_MaxValueOverflowsMinutes()
    {
        Assert.Throws<WorkerArgumentOutOfRangeException>(() => PeriodicInterval.Validate(PeriodType.Hours, 1, MaxHours + 1));
    }

    [Fact]
    public void Next_ShouldNot_Overflow_When_MaxValueIsLargestAllowed()
    {
        // Arrange
        PeriodicInterval.Validate(PeriodType.Hours, MaxHours, MaxHours);

        // Act
        var interval = PeriodicInterval.Next(new Random(12345), PeriodType.Hours, MaxHours, MaxHours);

        // Assert
        Assert.Equal(TimeSpan.FromHours(MaxHours), interval);
    }

    [Fact]
    public void Validate_ShouldNot_Throw_When_MinValueEqualsMaxValue()
    {
        var exception = Record.Exception(() => PeriodicInterval.Validate(PeriodType.Hours, 3, 3));

        Assert.Null(exception);
    }
}
