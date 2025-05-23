// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/***************************************************************************\
*
*
* This object includes a Storyboard reference.  When triggered, the Storyboard
*  resumes.
*
*
\***************************************************************************/
namespace System.Windows.Media.Animation
{
    /// <summary>
    /// ResumeStoryboard will call resume on its Storyboard reference when
    ///  it is triggered.
    /// </summary>
    public sealed class ResumeStoryboard : ControllableStoryboardAction
{
    /// <summary>
    ///     Called when it's time to execute this storyboard action
    /// </summary>
    internal override void Invoke( FrameworkElement containingFE, FrameworkContentElement containingFCE, Storyboard storyboard )
    {
        Debug.Assert( containingFE != null || containingFCE != null,
            "Caller of internal function failed to verify that we have a FE or FCE - we have neither." );

        if( containingFE != null )
        {
            storyboard.Resume(containingFE);
        }
        else
        {
            storyboard.Resume(containingFCE);
        }
    }
}
}
