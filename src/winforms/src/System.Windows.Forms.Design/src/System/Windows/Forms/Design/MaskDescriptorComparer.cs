﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Design;

/// <summary>
///  Implements the manual sorting of items by columns in the mask descriptor table.
///  Used by the MaskDesignerDialog to sort the items in the mask descriptors list.
/// </summary>
internal class MaskDescriptorComparer : IComparer<MaskDescriptor>
{
    private readonly SortOrder _sortOrder;
    private readonly SortType _sortType;

    public enum SortType
    {
        ByName,
        BySample,
        ByValidatingTypeName
    }

    public MaskDescriptorComparer(SortType sortType, SortOrder sortOrder)
    {
        _sortType = sortType;
        _sortOrder = sortOrder;
    }

    public int Compare(MaskDescriptor? maskDescriptorA, MaskDescriptor? maskDescriptorB)
    {
        if (maskDescriptorA is null || maskDescriptorB is null)
        {
            // Since this is an internal class we cannot throw here, the user cannot do anything about this.
            Debug.Fail("One or more parameters invalid");
            return 0;
        }

        string? textA;
        string? textB;

        switch (_sortType)
        {
            default:
                Debug.Fail("Invalid SortType, defaulting to SortType.ByName");
                goto case SortType.ByName;

            case SortType.ByName:
                textA = maskDescriptorA.Name;
                textB = maskDescriptorB.Name;
                break;

            case SortType.BySample:
                textA = maskDescriptorA.Sample;
                textB = maskDescriptorB.Sample;
                break;

            case SortType.ByValidatingTypeName:
                textA = maskDescriptorA.ValidatingType is null ? SR.MaskDescriptorValidatingTypeNone : maskDescriptorA.ValidatingType.Name;
                textB = maskDescriptorB.ValidatingType is null ? SR.MaskDescriptorValidatingTypeNone : maskDescriptorB.ValidatingType.Name;
                break;
        }

        int retVal = string.Compare(textA, textB, StringComparison.CurrentCulture);

        return _sortOrder == SortOrder.Descending ? -retVal : retVal;
    }

    public static int GetHashCode(MaskDescriptor maskDescriptor)
    {
        if (maskDescriptor is not null)
        {
            return maskDescriptor.GetHashCode();
        }

        Debug.Fail("Null maskDescriptor passed.");
        return 0;
    }

    public static bool Equals(MaskDescriptor? maskDescriptorA, MaskDescriptor? maskDescriptorB)
    {
        if (!MaskDescriptor.IsValidMaskDescriptor(maskDescriptorA) || !MaskDescriptor.IsValidMaskDescriptor(maskDescriptorB))
        {
            return maskDescriptorA == maskDescriptorB; // shallow comparison.
        }

        return maskDescriptorA.Equals(maskDescriptorB);
    }
}
