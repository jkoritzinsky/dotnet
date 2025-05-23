﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal.KnownBoxes;

namespace System.Windows
{
    /////////////////////////////////////////////////////////////////////////

    internal class StylusCaptureWithinProperty : ReverseInheritProperty
    {
        /////////////////////////////////////////////////////////////////////

        internal StylusCaptureWithinProperty() : base(
            UIElement.IsStylusCaptureWithinPropertyKey,
            CoreFlags.IsStylusCaptureWithinCache,
            CoreFlags.IsStylusCaptureWithinChanged)
        {
        }

        /////////////////////////////////////////////////////////////////////

        internal override void FireNotifications(UIElement uie, ContentElement ce, UIElement3D uie3D, bool oldValue)
        {
            DependencyPropertyChangedEventArgs args = 
                    new DependencyPropertyChangedEventArgs(
                        UIElement.IsStylusCaptureWithinProperty, 
                        BooleanBoxes.Box(oldValue), 
                        BooleanBoxes.Box(!oldValue));
            
            if (uie != null)
            {
                uie.RaiseIsStylusCaptureWithinChanged(args);
            }
            else if (ce != null)
            {
                ce.RaiseIsStylusCaptureWithinChanged(args);
            }
            else
            {
                uie3D?.RaiseIsStylusCaptureWithinChanged(args);
            }
        }
    }
}

