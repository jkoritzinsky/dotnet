// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//
// This file was generated, please do not edit it directly.
//
// Please see MilCodeGen.html for more information.
//


namespace System.Windows.Media
{
    /// <summary>
    ///     BitmapScalingMode - Enum which describes the manner in which we scale the images.
    /// </summary>
    public enum BitmapScalingMode
    {
        /// <summary>
        ///     Unspecified - Rendering engine will chose the optimal algorithm
        /// </summary>
        Unspecified = 0,

        /// <summary>
        ///     LowQuality - Rendering engine will use the fastest mode to scale the images. This 
        ///     may mean a low quality image
        /// </summary>
        LowQuality = 1,

        /// <summary>
        ///     HighQuality - Rendering engine will use the mode which produces a most quality 
        ///     image
        /// </summary>
        HighQuality = 2,

        /// <summary>
        ///     Linear - Rendering engine will use linear interpolation.
        /// </summary>
        Linear = 1,

        /// <summary>
        ///     Fant - Rendering engine will use fant interpolation.
        /// </summary>
        Fant = 2,

        /// <summary>
        ///     NearestNeighbor - Rendering engine will use nearest-neighbor interpolation.
        /// </summary>
        NearestNeighbor = 3,
    }
}
