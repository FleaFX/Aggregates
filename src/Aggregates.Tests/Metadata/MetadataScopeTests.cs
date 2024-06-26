﻿using FluentAssertions;

namespace Aggregates.Metadata;

public class MetadataScopeTests {
    [Fact]
    public void AddedMetadataCanBeReadBack() {
        using var scope = new MetadataScope();
        scope.Add("Test", 68463);

        var dict = scope.ToDictionary();
        dict.TryGetValue("Test", out var read).Should().BeTrue();
        read.Should().Be(68463);
    }

    [Fact]
    public void SameKeyAddedMultipleTimesResultsInArrayValue() {
        using var scope = new MetadataScope();
        scope.Add("Test", 68463, MetadataMultiplicity.Multiple);
        scope.Add("Test", true, MetadataMultiplicity.Multiple);
        scope.Add("Test", "Some text", MetadataMultiplicity.Multiple);

        var dict = scope.ToDictionary();
        dict.TryGetValue("Test", out var read).Should().BeTrue();

        read.Should().BeEquivalentTo(new object[] { 68463, true, "Some text" });
    }

    [Fact]
    public void ExistingArrayValueIsOverwrittenWhenAddingWithSingleMultiplicity() {
        using var scope = new MetadataScope();
        scope.Add("Test", 68463, MetadataMultiplicity.Multiple);
        scope.Add("Test", true, MetadataMultiplicity.Multiple);
        scope.Add("Test", "Some text");

        var dict = scope.ToDictionary();
        dict.TryGetValue("Test", out var read).Should().BeTrue();

        read.Should().Be("Some text");
    }
}