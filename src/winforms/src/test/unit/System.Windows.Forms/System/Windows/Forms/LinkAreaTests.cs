﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.ComponentModel;

namespace System.Windows.Forms.Tests;

// NB: doesn't require thread affinity
public class LinkAreaTests
{
    [Fact]
    public void LinkArea_Ctor_Default()
    {
        LinkArea area = default;
        Assert.Equal(0, area.Start);
        Assert.Equal(0, area.Length);
        Assert.True(area.IsEmpty);
    }

    [Theory]
    [InlineData(-1, -2, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 2, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    public void LinkArea_Ctor_Int_Int(int start, int length, bool expectedIsEmpty)
    {
        LinkArea area = new(start, length);
        Assert.Equal(start, area.Start);
        Assert.Equal(length, area.Length);
        Assert.Equal(expectedIsEmpty, area.IsEmpty);
    }

    [Theory]
    [IntegerData<int>]
    public void LinkArea_Start_Set_GetReturnsExpected(int value)
    {
        LinkArea area = new()
        {
            Start = value
        };
        Assert.Equal(value, area.Start);

        // Set same.
        area.Start = value;
        Assert.Equal(value, area.Start);
    }

    [Theory]
    [IntegerData<int>]
    public void LinkArea_Length_Set_GetReturnsExpected(int value)
    {
        LinkArea area = new()
        {
            Length = value
        };
        Assert.Equal(value, area.Length);

        // Set same.
        area.Length = value;
        Assert.Equal(value, area.Length);
    }

    public static IEnumerable<object[]> Equals_TestData()
    {
        yield return new object[] { new LinkArea(1, 2), new LinkArea(1, 2), true };
        yield return new object[] { new LinkArea(1, 2), new LinkArea(2, 2), false };
        yield return new object[] { new LinkArea(1, 2), new LinkArea(1, 3), false };

        yield return new object[] { new LinkArea(1, 2), new(), false };
        yield return new object[] { new LinkArea(1, 2), null, false };
    }

    [Theory]
    [MemberData(nameof(Equals_TestData))]
    public void LinkArea_Equals_Invoke_ReturnsExpected(LinkArea area, object other, bool expected)
    {
        if (other is LinkArea otherArea)
        {
            Assert.Equal(expected, area == otherArea);
            Assert.Equal(!expected, area != otherArea);
            Assert.Equal(expected, area.GetHashCode().Equals(otherArea.GetHashCode()));
        }

        Assert.Equal(expected, area.Equals(other));
    }

    [Fact]
    public void LinkArea_ToString_Invoke_ReturnsExpected()
    {
        LinkArea area = new(1, 2);
        Assert.Equal("{Start=1, Length=2}", area.ToString());
    }

    [Fact]
    public void LinkArea_TypeConverter_Get_ReturnsLinkAreaConverter()
    {
        LinkArea area = new(1, 2);
        Assert.IsType<LinkArea.LinkAreaConverter>(TypeDescriptor.GetConverter(area));
    }
}
